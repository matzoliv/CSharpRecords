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

            Assert.AreEqual( eligible, CSharpRecordsDiagnosticsAnalyzer.IsClassEligible( root.ChildNodes().OfType<ClassDeclarationSyntax>().First() ) );
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

        [TestMethod]
        public void StaticFieldsOrPropertiesIsEligible ()
        {
            var ast =
@"
public class Foo
{
    public string Bar { get; }
    public int Dummy { get; }

    public static int Static1;
    public static int StaticTest2;
}";

            AssertEligible( eligible: true, ast: ast );
        }

        [TestMethod]
        public void PropertiesNoModifiers()
        {
            var ast =
@"
public class Foo
{
    string Bar { get; }
}";

            AssertEligible( eligible: false, ast: ast );
        }

        [TestMethod]
        public void PrivateFieldWithInitialValue()
        {
            var ast =
@"
public class Foo
{
    private int m_timesEntityQueriesBeforeInitialized = 0;
}";

            AssertEligible( eligible: false, ast: ast );
        }

        [TestMethod]
        public void CSharp6GetBodyShortcutSyntax()
        {
            var ast =
@"
public class Foo
{
    public IEnumerable<Guid> Doors => m_externalEntityStateSource.AllEntityGuids;
}";

            AssertEligible( eligible: true, ast: ast );
        }

        [TestMethod]
        public void PropertiesGetWithBody()
        {
            var ast =
@"
public class Foo
{
    public string Bar { get { return ""foobar""; } }
}";

            AssertEligible( eligible: true, ast: ast );
        }
    }
}
