using System;
using System.Composition;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private async Task<Document> TransformToImmutableRecord ( Document document, ClassDeclarationSyntax typeDeclaration, CancellationToken cancellationToken )
        {
            var publicReadonlyFields =
                typeDeclaration.Members
                    .OfType<FieldDeclarationSyntax>()
                    .Where( field => field.Modifiers.Any( m => m.Kind() == SyntaxKind.ReadOnlyKeyword ) )
                    .Where( field => field.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) )
                    .Where( field => field.Declaration.Variables.Any() );

            var constructor = MakeConstructor( typeDeclaration.Identifier.Text, publicReadonlyFields );
            var withMethod = MakeWithMethod( typeDeclaration.Identifier.Text, publicReadonlyFields );
            var root = await document.GetSyntaxRootAsync( cancellationToken ).ConfigureAwait( false ) as CompilationUnitSyntax;

            var newTypeDeclaration =
                typeDeclaration.WithMembers(
                    SF.List(
                        typeDeclaration.Members
                            .Where(
                                m =>
                                    !( m is ConstructorDeclarationSyntax ) &&
                                    ( !( m is MethodDeclarationSyntax ) ||
                                      ( m as MethodDeclarationSyntax ).Identifier.ValueText != "With" )
                            )
                        .Concat( new MemberDeclarationSyntax[] { withMethod, constructor } )
                    )
                );

            var newRoot = root.ReplaceNode( typeDeclaration, newTypeDeclaration );
            document = document.WithSyntaxRoot( newRoot );
            return document;
        }

        private MethodDeclarationSyntax MakeWithMethod ( string className, IEnumerable<FieldDeclarationSyntax> fields )
        {
            var withMethodParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        fields.Select(
                            field =>
                                SF.Parameter( field.Declaration.Variables.First().Identifier )
                                    .WithType(
                                        field.Declaration.Type is PredefinedTypeSyntax ?
                                        SF.NullableType( field.Declaration.Type ) :
                                        field.Declaration.Type 
                                    )
                                    .WithDefault(
                                        SF.EqualsValueClause(
                                            SF.Token( SyntaxKind.EqualsToken ),
                                            SF.LiteralExpression( SyntaxKind.NullLiteralExpression ) ) ) ) ) );

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
                                SF.ArgumentList(
                                    SF.SeparatedList(
                                        fields.Select(
                                            field =>
                                                SF.Argument(
                                                    SF.BinaryExpression(
                                                        SyntaxKind.CoalesceExpression,
                                                        SF.IdentifierName( field.Declaration.Variables.First().Identifier.ValueText ),
                                                        SF.MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            SF.ThisExpression(),
                                                            SF.IdentifierName( field.Declaration.Variables.First().Identifier.ValueText ) ) ) ) ) ) ),
                                null ) ) ) );

        }

        private ConstructorDeclarationSyntax MakeConstructor ( string className, IEnumerable<FieldDeclarationSyntax> fields )
        {
            var constructorParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        fields.Select( field =>
                            SF.Parameter( field.Declaration.Variables.First().Identifier )
                                .WithType( field.Declaration.Type ) ) ) );

            return
                SF.ConstructorDeclaration( className )
                    .WithModifiers( SF.TokenList( new[] { SF.Token( SyntaxKind.PublicKeyword ) } ) )
                    .WithParameterList( constructorParameters )
                    .WithBody(
                        SF.Block(
                            fields.Select(
                                field =>
                                    SF.ExpressionStatement(
                                        SF.AssignmentExpression(
                                            SyntaxKind.SimpleAssignmentExpression,
                                            SF.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SF.ThisExpression(),
                                                SF.IdentifierName( field.Declaration.Variables.First().Identifier.ValueText )
                                            ),
                                            SF.IdentifierName( field.Declaration.Variables.First().Identifier.ValueText ) ) ) ) ) );

        }
    }
}
