# C# Records

This Visual Studio extension will propose a code fix on classes that use public readonly fields or properties. Applying this code fix will generate or update both the constructor, and a F# inspired "With" update method.

```C#
public class Foo
{
    public string First { get; }
    public int Second { get; }
}
```

would be updated to

```C#
public class Foo
{
    public string First { get; }
    public int Second { get; }

    public Foo(string First, int Second)
    {
        this.First = First;
        this.Second = Second;
    }

    public Foo With(string First = null, int? Second)
    {
        return new Foo(First ?? this.First, Second ?? this.Second);
    }
}
```

### Non nullable types and the With method

When generating the With method, the extension will automatically use the nullable type syntax on applicable predefined types like `int` or `bool`, and on widely used struct types like `System.Guid` and `System.DateTime`. Unfortunately, a basic AST level analysis can't reveal all struct types: you may need to manually and the `?` at the end of a parameter type. All subsequent use of the code fix will use the hint that this parameter type is non nullable.

### Known limitations

The implementation of the With method implies that a field cannot be set to null using it : it will default to the value of the previous instance.
