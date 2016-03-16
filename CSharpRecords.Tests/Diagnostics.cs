using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace CSharpRecords.Tests
{
    [TestClass]
    public class Diagnostics
    {
        public void AssertEligible ( bool eligible, string ast )
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText( ast );

            var root = ( CompilationUnitSyntax )tree.GetRoot();

            Assert.AreEqual( CSharpRecordsDiagnosticsAnalyzer.IsClassEligible( root.ChildNodes().OfType<ClassDeclarationSyntax>().First() ), eligible );
        }

        [TestMethod]
        public void EmptyClassIsNotEligible ()
        {
            AssertEligible( eligible: false, ast: @"public class Foo { }" );
        }

        [TestMethod]
        public void AnyMutablePropertyIsNotEligible ()
        {
            var ast =
@"
public class Foo
{
    public string Bar { get; set; }
    public int Dummy { get; }
}";
            AssertEligible( eligible: false, ast: ast );
        }

        [TestMethod]
        public void AnyMutablePrivatePropertyIsNotEligible ()
        {
            var ast =
@"
public class Foo
{
    public string Bar { get; private set; }
    public int Dummy { get; }
}";

            AssertEligible( eligible: false, ast: ast );
        }

        [TestMethod]
        public void AnyMutableFieldIsNotEligible ()
        {
            var ast =
@"
public class Foo
{
    public string Bar;
    public readonly int Dummy;
}";

            AssertEligible( eligible: false, ast: ast );
        }

        [TestMethod]
        public void AllPublicReadonlyFieldsIsEligible ()
        {
            var ast =
@"
public class Foo
{
    public readonly string Bar;
    public readonly int Dummy;
}";

            AssertEligible( eligible: true, ast: ast );
        }

        [TestMethod]
        public void AllPublicReadonlyPropertiesIsEligible ()
        {
            var ast =
@"
public class Foo
{
    public string Bar { get; }
    public int Dummy { get; }
}";

            AssertEligible( eligible: true, ast: ast );
        }

        [TestMethod]
        public void MixOfFieldsAndPropertiesIsEligible ()
        {
            var ast =
@"
public class Foo
{
    public string Bar { get; }
    public readonly int Dummy;
}";

            AssertEligible( eligible: true, ast: ast );
        }
    }
}
