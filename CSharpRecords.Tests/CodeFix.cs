using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;

namespace CSharpRecords.Tests
{
    [TestClass]
    public class CodeFix
    {
        public void AssertCodeFixTransformsTo ( string preCodeFix, string expectedPostCodeFix )
        {
            var workspace = MSBuildWorkspace.Create();

            SyntaxTree astPreCodeFix = CSharpSyntaxTree.ParseText( preCodeFix );
            var rootPreCodeFix = ( CompilationUnitSyntax )astPreCodeFix.GetRoot();
            var classPreCodeFix = rootPreCodeFix.ChildNodes().OfType<ClassDeclarationSyntax>().First();

            var classPostCodeFix = CSharpRecordsCodeFixProvider.ApplyCodeFix( classPreCodeFix );
            var astPreCodeFixFormattedStr = Formatter.Format( classPostCodeFix, workspace ).ToFullString();

            SyntaxTree astExpectedPostCodeFix = CSharpSyntaxTree.ParseText( expectedPostCodeFix );
            var rootExpectedPostCodeFix = ( CompilationUnitSyntax )astExpectedPostCodeFix.GetRoot();
            var expectedClassPostCodeFix = rootExpectedPostCodeFix.ChildNodes().OfType<ClassDeclarationSyntax>().First();
            var astPostCodeFixFormattedStr = Formatter.Format( expectedClassPostCodeFix, workspace ).ToFullString();

            Assert.AreEqual( astPostCodeFixFormattedStr, astPreCodeFixFormattedStr );
        }

        [TestMethod]
        public void SimpleSinglePropertyCodeFix ()
        {
            var before =
@"
public class Foo
{
    public string Bar { get; }
}
";

            var after =
@"
public class Foo
{
    public string Bar { get; }

    public Foo(string Bar)
    {
        this.Bar = Bar;
    }

    public Foo With(string Bar = null)
    {
        return new Foo(Bar ?? this.Bar);
    }
}
";
            AssertCodeFixTransformsTo( before, after );
        }

        [TestMethod]
        public void MultiplesPropertiesWithNonNullableType ()
        {
            var before =
@"
public class Foo
{
    public string Bar { get; }
    public DateTime Something { get; }
    public int N { get; }
}
";

            var after =
@"
public class Foo
{
    public string Bar { get; }
    public DateTime Something { get; }
    public int N { get; }

    public Foo(string Bar, DateTime Something, int N)
    {
        this.Bar = Bar;
        this.Something = Something;
        this.N = N;
    }

    public Foo With(string Bar = null, DateTime? Something = null, int? N = null)
    {
        return new Foo(Bar ?? this.Bar, Something ?? this.Something, N ?? this.N);
    }
}
";
            AssertCodeFixTransformsTo( before, after );
        }

        [TestMethod]
        public void PreviouslyNonNullableAreKept ()
        {
            var before =
@"
public class Foo
{
    public string Bar { get; }
    public SomeStruct Something { get; }
    public int N { get; }

    public Foo(string Bar, SomeStruct Something)
    {
        this.Bar = Bar;
        this.Something = Something;
    }

    public Foo With(string Bar = null, SomeStruct? Something = null)
    {
        return new Foo(Bar ?? this.Bar, Something ?? this.Something);
    }
}
";

            var after =
@"
public class Foo
{
    public string Bar { get; }
    public SomeStruct Something { get; }
    public int N { get; }

    public Foo(string Bar, SomeStruct Something, int N)
    {
        this.Bar = Bar;
        this.Something = Something;
        this.N = N;
    }

    public Foo With(string Bar = null, SomeStruct? Something = null, int? N = null)
    {
        return new Foo(Bar ?? this.Bar, Something ?? this.Something, N ?? this.N);
    }
}
";
            AssertCodeFixTransformsTo( before, after );
        }

        [TestMethod]
        public void StaticFieldsAndMethodsAreLeftUntouched ()
        {
            var before =
@"
public class Foo
{
    public string Bar { get; }
    public SomeStruct Something { get; }
    public int N { get; }
    public static Foo Empty = new Foo("""", SomeStruct.Empty);
    public static int Test { get { return 10; } }
    public static MakeEmpty()
    {
        return new Foo("""", SomeStruct.Empty);
    }

    public Foo(string Bar, SomeStruct Something)
    {
        this.Bar = Bar;
        this.Something = Something;
    }

    public Foo With(string Bar = null, SomeStruct? Something = null)
    {
        return new Foo(Bar ?? this.Bar, Something ?? this.Something);
    }
}
";

            var after =
@"
public class Foo
{
    public string Bar { get; }
    public SomeStruct Something { get; }
    public int N { get; }
    public static Foo Empty = new Foo("""", SomeStruct.Empty);
    public static int Test { get { return 10; } }
    public static MakeEmpty()
    {
        return new Foo("""", SomeStruct.Empty);
    }

    public Foo(string Bar, SomeStruct Something, int N)
    {
        this.Bar = Bar;
        this.Something = Something;
        this.N = N;
    }

    public Foo With(string Bar = null, SomeStruct? Something = null, int? N = null)
    {
        return new Foo(Bar ?? this.Bar, Something ?? this.Something, N ?? this.N);
    }
}
";
            AssertCodeFixTransformsTo( before, after );
        }
    }
}
