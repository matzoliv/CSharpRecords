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
                "CSharpRecordsUpdateNecessary",
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
            var property = context.Symbol as ITypeSymbol;

            if ( property == null )
                return;

            var classDeclaration = property.DeclaringSyntaxReferences.First().GetSyntax() as ClassDeclarationSyntax;

            if ( classDeclaration == null )
                return;

            var fields = classDeclaration.Members.OfType<FieldDeclarationSyntax>();
            var properties = classDeclaration.Members.OfType<PropertyDeclarationSyntax>();

            var allReadonlyFields =
                fields
                    .All(
                        field =>
                            field.Modifiers.Any( m => m.Kind() == SyntaxKind.ReadOnlyKeyword ) &&
                            field.Modifiers.Any( m => m.Kind() == SyntaxKind.PublicKeyword ) );

            if ( allReadonlyFields && !properties.Any() )
            {
                context.ReportDiagnostic( Diagnostic.Create( ImmutableRecordUpdateDiagnostic, classDeclaration.GetLocation() ) );
            }
        }
    }
}
