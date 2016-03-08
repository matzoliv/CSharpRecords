using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CSharpRecords.Tests
{
    class Program
    {
        public static string Source = @"
namespace ConsoleApplication1
{
    public class Program
    {
        static void Main(string[] args)
        {
        }
    }

    public class Record
    {
        public readonly string FieldA;
        public readonly string FieldB;
        public readonly int FieldC;

        public Record(string FieldA, string FieldB)
        {
            this.FieldA = fieldA;
            this.FieldB = fieldB;
        }

        public Record With(string FieldA = null, string FieldB = null, int? FieldC = null)
        {
            return new Record(
                fieldA ?? this.FieldA,
                fieldB ?? this.FieldB
            );
        }

        public 
    }
}";

        static void Main ( string[] args )
        {
            var ast = CSharpSyntaxTree.ParseText( Source );

            var root = ast.GetRoot();
            var recordClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where( @class => @class.Identifier.ValueText == "Record" )
                .First();

            var publicReadonlyFields = recordClass.Members
                .OfType<FieldDeclarationSyntax>()
                .Where( field => field.Modifiers.Any( m => m.Kind() == SyntaxKind.ReadOnlyKeyword ) )
                .Where( field => field.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) )
                .Where( field => field.Declaration.Variables.Any() );

            var withMethodParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        publicReadonlyFields.Select(
                            field =>
                                SF.Parameter( field.Declaration.Variables.First().Identifier )
                                    .WithType( field.Declaration.Type )
                                    .WithDefault(
                                        SF.EqualsValueClause(
                                            SF.Token( SyntaxKind.EqualsToken ),
                                            SF.LiteralExpression( SyntaxKind.NullLiteralExpression ) ) ) ) ) );

            var withMethod =
                SF.MethodDeclaration(
                    SF.ParseTypeName( recordClass.Identifier.ValueText ),
                    "With"
                )
                .WithModifiers( SF.TokenList( new[] { SF.Token( SyntaxKind.PublicKeyword ) } ) )
                .WithParameterList( withMethodParameters )
                .WithBody(
                    SF.Block(
                        SF.ReturnStatement(
                            SF.ObjectCreationExpression(
                                SF.IdentifierName( recordClass.Identifier.ValueText ),
                                SF.ArgumentList(
                                    SF.SeparatedList(
                                        publicReadonlyFields.Select(
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

            var constructorParameters =
                SF.ParameterList(
                    SF.SeparatedList(
                        publicReadonlyFields.Select( field =>
                            SF.Parameter( field.Declaration.Variables.First().Identifier )
                                .WithType( field.Declaration.Type ) ) ) );

            var constructor =
                SF.ConstructorDeclaration( recordClass.Identifier.ValueText )
                    .WithModifiers( SF.TokenList( new[] { SF.Token( SyntaxKind.PublicKeyword ) } ) )
                    .WithParameterList( constructorParameters )
                    .WithBody(
                        SF.Block(
                            publicReadonlyFields.Select(
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

            var str = constructor.ToFullString();
            var str2 = withMethod.ToFullString();

            return;
        }
    }
}
