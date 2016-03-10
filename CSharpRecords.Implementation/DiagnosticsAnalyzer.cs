using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpRecords
{
    [DiagnosticAnalyzer( LanguageNames.CSharp )]
    public class CSharpRecordsDiagnosticsAnalyzer : DiagnosticAnalyzer
    {
        internal static DiagnosticDescriptor ImmutableRecordUpdateDiagnostic =
            new DiagnosticDescriptor(
                "ImmutableRecordUpdate",
                "Records constructor and modifiers can be updated",
                "Records constructor and modifiers can be updated",
                "Refactoring",
                DiagnosticSeverity.Info,
                true
            );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create( ImmutableRecordUpdateDiagnostic ); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSymbolAction( Analyze, SymbolKind.NamedType );
        }

        private void Analyze(SymbolAnalysisContext context)
        {
            var classDeclaration =
                ( context.Symbol as ITypeSymbol )
                ?.DeclaringSyntaxReferences
                .FirstOrDefault()
                ?.GetSyntax() as ClassDeclarationSyntax;

            if ( classDeclaration == null )
                return;

            var hasAnyNonPublicNonReadonlyFields =
                classDeclaration.Members
                    .OfType<FieldDeclarationSyntax>()
                    .Where(
                        field =>
                            !( field.Modifiers.Any( m => m.Kind() == SyntaxKind.ReadOnlyKeyword ) &&
                               field.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) )
                    )
                    .Any();

            var hasAnyProperties =
                classDeclaration.Members
                    .OfType<PropertyDeclarationSyntax>()
                    .Any();

            if ( !hasAnyNonPublicNonReadonlyFields && !hasAnyProperties )
            {
                context.ReportDiagnostic( Diagnostic.Create( ImmutableRecordUpdateDiagnostic, classDeclaration.GetLocation() ) );
            }
        }
    }
}
