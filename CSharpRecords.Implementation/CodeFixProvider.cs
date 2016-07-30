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
        public bool IsNonNullable { get; }

        public Field( string Name, TypeSyntax Type, bool IsNonNullable )
        {
            this.Name = Name;
            this.Type = Type;
            this.IsNonNullable = IsNonNullable;
        }

        public Field With( string Name = null, TypeSyntax Type = null, bool? IsNonNullable = null )
        {
            return new Field( Name ?? this.Name, Type ?? this.Type, IsNonNullable ?? this.IsNonNullable );
        }


        private static HashSet<SyntaxKind> NonNullablePredefinedTypes =
            new HashSet<SyntaxKind>( new[] { SyntaxKind.SByteKeyword, SyntaxKind.ShortKeyword, SyntaxKind.IntKeyword, SyntaxKind.ByteKeyword,
                                             SyntaxKind.UShortKeyword, SyntaxKind.UIntKeyword, SyntaxKind.ULongKeyword, SyntaxKind.FloatKeyword,
                                             SyntaxKind.DoubleKeyword, SyntaxKind.BoolKeyword, SyntaxKind.CharKeyword, SyntaxKind.DecimalKeyword } );

        private static HashSet<string> NonNullableTypeName =
            new HashSet<string>( new[] { "Guid", "System.Guid", "DateTime", "System.DateTime" } );

        public static bool IsTypeSyntaxNonNullable ( TypeSyntax type )
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

        public static Field MaybeConvertFromMember( MemberDeclarationSyntax member )
        {
            var fieldDeclarationSyntax = member as FieldDeclarationSyntax;
            if ( fieldDeclarationSyntax != null )
            {
                if ( !fieldDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.StaticKeyword ) &&
                     fieldDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.ReadOnlyKeyword ) &&
                     fieldDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) &&
                     fieldDeclarationSyntax.Declaration.Variables.Any() )
                {
                    return new Field(
                        fieldDeclarationSyntax.Declaration.Variables.First().Identifier.ValueText,
                        fieldDeclarationSyntax.Declaration.Type,
                        IsTypeSyntaxNonNullable( fieldDeclarationSyntax.Declaration.Type )
                    );
                }
            }

            var propertyDeclarationSyntax = member as PropertyDeclarationSyntax;
            if ( propertyDeclarationSyntax != null )
            {
                if ( propertyDeclarationSyntax.AccessorList != null && // Expression Bodied properties
                     propertyDeclarationSyntax.AccessorList.Accessors.All( x => x.Body == null ) &&
                     !propertyDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.StaticKeyword ) &&
                     propertyDeclarationSyntax.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) )
                {
                    return new Field(
                        propertyDeclarationSyntax.Identifier.ValueText,
                        propertyDeclarationSyntax.Type,
                        IsTypeSyntaxNonNullable( propertyDeclarationSyntax.Type )
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

        public override async Task RegisterCodeFixesAsync ( CodeFixContext context )
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

        public static MethodDeclarationSyntax MakeWithMethod ( string className, IEnumerable<Field> fields )
        {
            var withMethodParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        fields.Select(
                            field =>
                                SF.Parameter( SF.Identifier( field.Name ) )
                                    .WithType(
                                        field.IsNonNullable ? SF.NullableType( field.Type ) : field.Type
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

        public static ConstructorDeclarationSyntax MakeConstructor ( string className, IEnumerable<Field> fields )
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

        public static ClassDeclarationSyntax UpdateOrAddConstructor ( ClassDeclarationSyntax classDeclaration, IEnumerable<Field> fields )
        {
            var constructor = MakeConstructor( classDeclaration.Identifier.Text, fields );

            var maybePreviousConstructor =
                classDeclaration.Members
                    .OfType<ConstructorDeclarationSyntax>()
                    .FirstOrDefault();

            return
                maybePreviousConstructor == null ?
                classDeclaration.AddMembers( constructor ) :
                classDeclaration.ReplaceNode( maybePreviousConstructor, constructor );
        }

        public static ClassDeclarationSyntax UpdateOrAddWithMethod ( ClassDeclarationSyntax classDeclaration, IEnumerable<Field> fields )
        {
            var withMethod = MakeWithMethod( classDeclaration.Identifier.Text, fields );

            var maybePreviousWithMethod =
                classDeclaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where( m => m.Identifier.ValueText == "With" )
                    .FirstOrDefault();

            return
                maybePreviousWithMethod == null ?
                classDeclaration.AddMembers( withMethod ) :
                classDeclaration.ReplaceNode( maybePreviousWithMethod, withMethod );
        }

        public static IEnumerable<Field> GetApplicableFields ( ClassDeclarationSyntax classDeclaration )
        {
            var maybePreviousWithMethod =
                classDeclaration.Members
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

            return
                classDeclaration.Members
                    .Select( member => Field.MaybeConvertFromMember( member ) )
                    .Where( x => x != null )
                    .Select( field => knownNullableTypeParameterNames.Contains( field.Name ) ? field.With( IsNonNullable: true ) : field )
                    .ToList();
        }

        public static ClassDeclarationSyntax ApplyCodeFix( ClassDeclarationSyntax classDeclaration )
        {
            var applicableFields = GetApplicableFields( classDeclaration );
            return UpdateOrAddWithMethod( UpdateOrAddConstructor( classDeclaration, applicableFields ), applicableFields );
        }

        private async Task<Document> TransformToImmutableRecord ( Document document, ClassDeclarationSyntax classDeclaration, CancellationToken cancellationToken )
        {
            var root = await document.GetSyntaxRootAsync( cancellationToken ).ConfigureAwait( false ) as CompilationUnitSyntax;
            var newRoot = root.ReplaceNode( classDeclaration, ApplyCodeFix( classDeclaration ) );
            document = document.WithSyntaxRoot( newRoot );
            return document;
        }
    }
}
