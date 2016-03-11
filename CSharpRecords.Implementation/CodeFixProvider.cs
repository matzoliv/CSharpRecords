using System;
using System.Composition;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeActions;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpRecords
{
    public class Field
    {
        public string Name { get; }
        public TypeSyntax Type { get; }

        public Field( string Name, TypeSyntax Type )
        {
            this.Name = Name;
            this.Type = Type;
        }

        public Field With( string Name = null, TypeSyntax Type = null )
        {
            return new Field( Name ?? this.Name, Type ?? this.Type );
        }

        public static Field MaybeConvertFromMember( MemberDeclarationSyntax member )
        {
            var fieldDeclarationSyntax = member as FieldDeclarationSyntax;
            if ( fieldDeclarationSyntax != null )
            {
                if ( fieldDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.ReadOnlyKeyword ) &&
                     fieldDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) &&
                     fieldDeclarationSyntax.Declaration.Variables.Any() )
                {
                    return new Field(
                        fieldDeclarationSyntax.Declaration.Variables.First().Identifier.ValueText,
                        fieldDeclarationSyntax.Declaration.Type
                    );
                }
            }

            var propertyDeclarationSyntax = member as PropertyDeclarationSyntax;
            if ( propertyDeclarationSyntax != null )
            {
                if ( propertyDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) )
                {
                    return new Field(
                        propertyDeclarationSyntax.Identifier.ValueText,
                        propertyDeclarationSyntax.Type
                    );
                }
            }

            return null;
        }
    }

    [ExportCodeFixProvider( LanguageNames.CSharp, Name = nameof( CSharpRecordsCodeFixProvider ) ), Shared]
    public class CSharpRecordsCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create( CSharpRecordsDiagnosticsAnalyzer.ImmutableRecordUpdateDiagnostic.Id );
            }
        }

        public override async Task RegisterCodeFixesAsync( CodeFixContext context )
        {
            var root = await context.Document.GetSyntaxRootAsync( context.CancellationToken ).ConfigureAwait( false );
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var declaration =
                root.FindToken( diagnosticSpan.Start )
                    .Parent.AncestorsAndSelf()
                    .OfType<ClassDeclarationSyntax>()
                    .First();

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Update immutable record constructor and modifier method",
                    c => TransformToImmutableRecord( context.Document, declaration, c ) ),
                diagnostic );
        }

        private async Task<Document> TransformToImmutableRecord( Document document, ClassDeclarationSyntax typeDeclaration, CancellationToken cancellationToken )
        {
            var applicableFields =
                typeDeclaration.Members
                    .Select( member => Field.MaybeConvertFromMember( member ) )
                    .Where( x => x != null );

            var constructor = MakeConstructor( typeDeclaration.Identifier.Text, applicableFields );

            var maybePreviousWithMethod =
                typeDeclaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where( m => m.Identifier.ValueText == "With" )
                    .FirstOrDefault();

            var knownNullableTypeParameterNames =
                maybePreviousWithMethod
                    ?.ParameterList.Parameters
                    .Where( param => param.Type is NullableTypeSyntax )
                    .Select( param => param.Identifier.ValueText )
                    .ToImmutableHashSet()
                    ?? ImmutableHashSet<string>.Empty;

            var withMethod = MakeWithMethod( typeDeclaration.Identifier.Text, applicableFields, knownNullableTypeParameterNames );

            var root = await document.GetSyntaxRootAsync( cancellationToken ).ConfigureAwait( false ) as CompilationUnitSyntax;

            var maybePreviousConstructor =
                typeDeclaration.Members
                    .OfType<ConstructorDeclarationSyntax>()
                    .FirstOrDefault();

            var typeDeclarationWithConstructor =
                maybePreviousConstructor == null ?
                typeDeclaration.AddMembers( constructor ) :
                typeDeclaration.ReplaceNode( maybePreviousConstructor, constructor );

            maybePreviousWithMethod =
                typeDeclarationWithConstructor.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where( m => m.Identifier.ValueText == "With" )
                    .FirstOrDefault();

            var newTypeDeclaration =
                maybePreviousWithMethod == null ?
                typeDeclarationWithConstructor.AddMembers( withMethod ) :
                typeDeclarationWithConstructor.ReplaceNode( maybePreviousWithMethod, withMethod );

            var newRoot = root.ReplaceNode( typeDeclaration, newTypeDeclaration );
            document = document.WithSyntaxRoot( newRoot );
            return document;
        }

        private static HashSet<SyntaxKind> NonNullablePredefinedTypes =
            new HashSet<SyntaxKind>( new[] { SyntaxKind.SByteKeyword, SyntaxKind.ShortKeyword, SyntaxKind.IntKeyword, SyntaxKind.ByteKeyword,
                                             SyntaxKind.UShortKeyword, SyntaxKind.UIntKeyword, SyntaxKind.ULongKeyword, SyntaxKind.FloatKeyword,
                                             SyntaxKind.DoubleKeyword, SyntaxKind.BoolKeyword, SyntaxKind.CharKeyword, SyntaxKind.DecimalKeyword } );

        private static HashSet<string> NonNullableTypeName =
            new HashSet<string>( new[] { "Guid", "System.Guid", "DateTime", "System.DateTime" } );

        private static bool IsNonNullable( TypeSyntax type )
        {
            var predefinedTypeSyntax = type as PredefinedTypeSyntax;

            if ( predefinedTypeSyntax != null )
            {
                return NonNullablePredefinedTypes.Contains( predefinedTypeSyntax.Keyword.Kind() );
            }

            var typeIdentifierNameSyntax = type as IdentifierNameSyntax;
            
            if ( typeIdentifierNameSyntax != null )
            {
                return NonNullableTypeName.Contains( typeIdentifierNameSyntax.Identifier.ValueText );
            }

            return false;
        }

        private MethodDeclarationSyntax MakeWithMethod ( string className, IEnumerable<Field> fields, ImmutableHashSet<string> knownNullableTypeParameterNames )
        {
            var withMethodParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        fields.Select(
                            field =>
                                SF.Parameter( SF.Identifier( field.Name ) )
                                    .WithType(
                                        knownNullableTypeParameterNames.Contains( field.Name ) || IsNonNullable( field.Type ) ?
                                        SF.NullableType( field.Type ) :
                                        field.Type
                                    )
                                    .WithDefault(
                                        SF.EqualsValueClause(
                                            SF.Token( SyntaxKind.EqualsToken ),
                                            SF.LiteralExpression( SyntaxKind.NullLiteralExpression ) ) ) ) ) );

            var withMethodBodyStatements =
                fields.Select(
                    field =>
                        SF.Argument(
                            SF.BinaryExpression(
                                SyntaxKind.CoalesceExpression,
                                SF.IdentifierName( field.Name ),
                                SF.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SF.ThisExpression(),
                                    SF.IdentifierName( field.Name ) ) ) ) );

            return
                SF.MethodDeclaration(
                    SF.ParseTypeName( className ),
                    "With"
                )
                .WithModifiers( SF.TokenList( new[] { SF.Token( SyntaxKind.PublicKeyword ) } ) )
                .WithParameterList( withMethodParameters )
                .WithBody(
                    SF.Block(
                        SF.ReturnStatement(
                            SF.ObjectCreationExpression(
                                SF.IdentifierName( className ),
                                SF.ArgumentList( SF.SeparatedList( withMethodBodyStatements ) ),
                                null ) ) ) );
        }

        private ConstructorDeclarationSyntax MakeConstructor ( string className, IEnumerable<Field> fields )
        {
            var constructorParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        fields.Select( field =>
                            SF.Parameter( SF.Identifier( field.Name ) )
                                .WithType( field.Type ) ) ) );

            var constructorBodyStatements =
                fields.Select(
                    field =>
                        SF.ExpressionStatement(
                            SF.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SF.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SF.ThisExpression(),
                                    SF.IdentifierName( field.Name )
                                ),
                                SF.IdentifierName( field.Name ) ) ) );

            return
                SF.ConstructorDeclaration( className )
                    .WithModifiers( SF.TokenList( new[] { SF.Token( SyntaxKind.PublicKeyword ) } ) )
                    .WithParameterList( constructorParameters )
                    .WithBody( SF.Block( constructorBodyStatements ) );
        }
    }
}
