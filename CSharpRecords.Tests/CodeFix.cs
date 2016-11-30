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
        public void AssertCodeFixTransformsTo ( string preCodeFix, string expectedPostCodeFix, Func<ClassDeclarationSyntax, ClassDeclarationSyntax> fixCode )
        {
            var workspace = MSBuildWorkspace.Create();

            SyntaxTree astPreCodeFix = CSharpSyntaxTree.ParseText( preCodeFix );
            var rootPreCodeFix = ( CompilationUnitSyntax )astPreCodeFix.GetRoot();
            var classPreCodeFix = rootPreCodeFix.ChildNodes().OfType<ClassDeclarationSyntax>().First();

            var classPostCodeFix = fixCode( classPreCodeFix );
            var astPostCodeFixFormattedStr = Formatter.Format( classPostCodeFix, workspace ).ToFullString();

            SyntaxTree astExpectedPostCodeFix = CSharpSyntaxTree.ParseText( expectedPostCodeFix );
            var rootExpectedPostCodeFix = ( CompilationUnitSyntax )astExpectedPostCodeFix.GetRoot();
            var expectedClassPostCodeFix = rootExpectedPostCodeFix.ChildNodes().OfType<ClassDeclarationSyntax>().First();
            var astExpectedPostCodeFixFormattedStr = Formatter.Format( expectedClassPostCodeFix, workspace ).ToFullString();

            Assert.AreEqual( astExpectedPostCodeFixFormattedStr, astPostCodeFixFormattedStr );
        }

        public void AssertUpdateConstructorAndWithMethodTransformsTo ( string preCodeFix, string expectedPostCodeFix )
        {
            AssertCodeFixTransformsTo( preCodeFix, expectedPostCodeFix, CSharpRecordsCodeFixProvider.UpdateConstructorAndWithMethod );
        }

        public void AssertUpdateConstructorTransformsTo ( string preCodeFix, string expectedPostCodeFix )
        {
            AssertCodeFixTransformsTo( preCodeFix, expectedPostCodeFix, CSharpRecordsCodeFixProvider.UpdateConstructor );
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
            AssertUpdateConstructorAndWithMethodTransformsTo( before, after );
        }

        [TestMethod]
        public void SimpleSinglePropertyConstructorOnly ()
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
}
";
            AssertUpdateConstructorTransformsTo( before, after );
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
            AssertUpdateConstructorAndWithMethodTransformsTo( before, after );
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
            AssertUpdateConstructorAndWithMethodTransformsTo( before, after );
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
            AssertUpdateConstructorAndWithMethodTransformsTo( before, after );
        }

        [TestMethod]
        public void GetWithBodyIsIgnored()
        {
            var before =
@"
internal class Foo
{
    public readonly int A;
    public int DeuxA { get { return 2 * A; } }
}
";

            var after =
@"
internal class Foo
{
    public readonly int A;
    public int DeuxA { get { return 2 * A; } }

    public Foo(int A)
    {
        this.A = A;
    }

    public Foo With(int? A = null)
    {
        return new Foo(A ?? this.A);
    }
}
";
            AssertUpdateConstructorAndWithMethodTransformsTo( before, after );
        }

        [TestMethod]
        public void ExpressionBodiedNotation()
        {
            var before =
@"
internal class Foo
{
    public readonly int A;
    public int DeuxA => 2 * A;
}
";

            var after =
@"
internal class Foo
{
    public readonly int A;
    public int DeuxA => 2 * A;

    public Foo(int A)
    {
        this.A = A;
    }

    public Foo With(int? A = null)
    {
        return new Foo(A ?? this.A);
    }
}
";
            AssertUpdateConstructorAndWithMethodTransformsTo( before, after );
        }
    }
}
