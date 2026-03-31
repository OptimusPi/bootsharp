namespace Bootsharp.Publish.Test;

public class SerializerTest : EmitTest
{
    protected override string TestedContent => GeneratedSerializer;

    [Fact]
    public void WhenNothingInspectedIsEmpty ()
    {
        Execute();
        Assert.Empty(TestedContent);
    }

    [Fact]
    public void WhenNoSerializableTypesIsEmpty ()
    {
        AddAssembly(
            WithClass("[JSInvokable] public static bool? Foo (int a, char b, DateTime c, DateTimeOffset d) => default;")
        );
        Execute();
        DoesNotContain("Binary<");
    }

    [Fact]
    public void SerializesCommonTypes ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public struct Structure;
            public enum Enumeration { A, B }

            public record Node(
                bool Boolean,
                byte Byte,
                sbyte SByte,
                short Int16,
                ushort UInt16,
                uint UInt32,
                long Int64,
                ulong UInt64,
                float Single,
                decimal Decimal,
                char Char,
                string String,
                DateTime DateTime,
                DateTimeOffset DateTimeOffset,
                nint NInt,
                int? NullableInt,
                Structure Struct,
                Structure? NullableStruct,
                Enumeration Enum,
                Enumeration? NullableEnum);

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("Binary<global::System.Boolean>");
        Contains("Binary<global::System.Byte>");
        Contains("Binary<global::System.SByte>");
        Contains("Binary<global::System.Int16>");
        Contains("Binary<global::System.UInt16>");
        Contains("Binary<global::System.UInt32>");
        Contains("Binary<global::System.Int64>");
        Contains("Binary<global::System.UInt64>");
        Contains("Binary<global::System.Single>");
        Contains("Binary<global::System.Decimal>");
        Contains("Binary<global::System.Char>");
        Contains("Binary<global::System.String>");
        Contains("Binary<global::System.DateTime>");
        Contains("Binary<global::System.DateTimeOffset>");
        Contains("Binary<global::System.IntPtr>");
        Contains("Binary<global::System.Int32?>");
        Contains("Binary<global::Space.Structure>");
        Contains("Binary<global::Space.Structure?>");
        Contains("Binary<global::Space.Enumeration>");
        Contains("Binary<global::Space.Enumeration?>");
    }

    [Fact]
    public void DoesntGenerateSpecialSerializerForNullableReferenceTypes ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public record Record;

            public record Node(
                Record Rec,
                Record? NullableRec,
                object Object,
                object? NullableObject);

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("Binary<global::Space.Record>");
        Contains("Binary<global::System.Object>");
        DoesNotContain("Binary<global::Space.Record?>");
        DoesNotContain("Binary<global::System.Object?>");
    }

    [Fact]
    public void HandlesSelfReferencedTypes ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public record Node (string Id, Node? Parent, Node? Child);

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("Binary<global::Space.Node>");
    }

    [Fact]
    public void SerializesCollections ()
    {
        AddAssembly(With(
            """
            using System.Collections.Generic;

            namespace Space;

            public record Node(
                List<int> ListItems,
                IList<int> ListInterfaceItems,
                IReadOnlyList<int> ReadOnlyListItems,
                ICollection<int> CollectionItems,
                IReadOnlyCollection<int> ReadOnlyCollectionItems,
                Dictionary<string, DateTime> Map,
                IDictionary<string, DateTimeOffset> MapInterface,
                IReadOnlyDictionary<string, nint> ReadOnlyMap);

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("Serializer.List(");
        Contains("Serializer.IList(");
        Contains("Serializer.IReadOnlyList(");
        Contains("Serializer.ICollection(");
        Contains("Serializer.IReadOnlyCollection(");
        Contains("Serializer.Dictionary(");
        Contains("Serializer.IDictionary(");
        Contains("Serializer.IReadOnlyDictionary(");
    }

    [Fact]
    public void InitializesNestedCollectionSerializersAfterElementSerializers ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public class Item
            {
                public string? Value { get; init; }
            }

            public record Info(Item?[]?[]? Items);

            public class Class
            {
                [JSInvokable] public static Info Echo (Info info) => info;
            }
            """));
        Execute();
        var itemIdx = TestedContent.IndexOf("Space_Item", StringComparison.Ordinal);
        var innerArrayIdx = TestedContent.IndexOf("Space_ItemArray", StringComparison.Ordinal);
        var outerArrayIdx = TestedContent.IndexOf("Space_ItemArrayArray", StringComparison.Ordinal);
        Assert.True(itemIdx >= 0, "Expected item serializer.");
        Assert.True(innerArrayIdx > itemIdx, "Expected inner array serializer after item serializer.");
        Assert.True(outerArrayIdx > innerArrayIdx, "Expected outer array serializer after inner array serializer.");
    }

    [Fact]
    public void OrdersIndependentObjectInitializersDeterministically ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public record Alpha(string Value);
            public record Beta(string Value);

            public class Class
            {
                [JSInvokable] public static Alpha EchoA (Alpha value) => value;
                [JSInvokable] public static Beta EchoB (Beta value) => value;
            }
            """));
        Execute();
        var alphaIdx = TestedContent.IndexOf("Space_Alpha", StringComparison.Ordinal);
        var betaIdx = TestedContent.IndexOf("Space_Beta", StringComparison.Ordinal);
        Assert.True(alphaIdx >= 0 && betaIdx >= 0, "Expected both serializers to be initialized.");
        Assert.True(alphaIdx < betaIdx, "Expected deterministic initializer ordering by TypeInfo.");
    }

    [Fact]
    public void UsesParameterizedConstructorForGetterOnlyProperties ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public class Node
            {
                public Node (string id) => Id = id;
                public string Id { get; }
            }

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("new global::Space.Node(@id);");
    }

    [Fact]
    public void UsesParameterlessConstructorForWritablePropertiesWhenAvailable ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public class Node
            {
                public Node () { }
                public Node (string id) => Id = id;
                public string Id { get; set; } = string.Empty;
            }

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("var _value_ = new global::Space.Node();");
        Contains("_value_.Id = @id;");
    }

    [Fact]
    public void UsesObjectInitializerForPublicInitOnlyProperties ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public class Node
            {
                public string Id { get; init; } = string.Empty;
            }

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("new global::Space.Node() { Id = @id };");
    }

    [Fact]
    public void UsesObjectInitializerForRequiredMembers ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public readonly record struct CompletionItem
            {
                public required string Label { get; init; }
                public string? Detail { get; init; }
            }

            public class Class
            {
                [JSInvokable] public static CompletionItem Echo (CompletionItem item) => item;
            }
            """));
        Execute();
        Contains("new global::Space.CompletionItem() { Label = @label, Detail = @detail };");
    }

    [Fact]
    public void AssignsRequiredWritableMembersAfterConstruction ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public class CompletionItem
            {
                public CompletionItem () { }
                public required string Label { get; set; }
            }

            public class Class
            {
                [JSInvokable] public static CompletionItem Echo (CompletionItem item) => item;
            }
            """));
        Execute();
        Contains("new global::Space.CompletionItem() { Label = @label }");
    }

    [Fact]
    public void UsesBackingFieldAssignmentWhenConstructorParameterNameDoesNotMatchProperty ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public struct Node
            {
                public Node (string other) => Id = other;
                public string Id { get; }
            }

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("<Id>k__BackingField");
    }

    [Fact]
    public void UsesBackingFieldAssignmentWhenConstructorParameterTypeDoesNotMatchProperty ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public struct Node
            {
                public Node (int id) => Id = id.ToString();
                public string Id { get; }
            }

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        Contains("<Id>k__BackingField");
    }

    [Fact]
    public void IgnoresWriteOnlyAndComputedProperties ()
    {
        AddAssembly(With(
            """
            namespace Space;

            public class Node
            {
                public string Id { get; set; } = string.Empty;
                public string Computed => string.Empty;
                public string WriteOnly { set { } }
            }

            public class Class
            {
                [JSInvokable] public static Node Echo (Node node) => node;
            }
            """));
        Execute();
        DoesNotContain("Computed");
        DoesNotContain("WriteOnly");
    }

    [Fact]
    public void SerializesTypesFromInteropMethods ()
    {
        AddAssembly(With(
            """
            public record RecordA;
            public record RecordB;
            public record RecordC;

            public class Class
            {
                [JSInvokable] public static Task<RecordA[]> A (RecordC c) => default;
                [JSFunction] public static RecordB[] B (RecordC[] c) => default;
            }
            """));
        Execute();
        Contains("Binary<global::RecordA> ");
        Contains("Binary<global::RecordB> ");
        Contains("Binary<global::RecordC> ");
        Contains("Binary<global::RecordA[]> ");
        Contains("Binary<global::RecordB[]> ");
        Contains("Binary<global::RecordC[]> ");
    }

    [Fact]
    public void SerializesTypesFromInteropInterfaces ()
    {
        AddAssembly(With(
            """
            public record RecordA;
            public record RecordB;
            public record RecordC;
            public interface IExported { void Inv (RecordA a); }
            public interface IImported { void Fun (RecordB b); void NotifyEvt(RecordC c); }

            public class Class
            {
                [JSFunction] public static Task<IImported> GetImported (IExported arg) => default;
            }
            """));
        Execute();
        Contains("Binary<global::RecordA>");
        Contains("Binary<global::RecordB>");
        Contains("Binary<global::RecordC>");
    }

    [Fact]
    public void DoesntSerializeInstancedInteropInterfacesThemselves ()
    {
        AddAssembly(With(
            """
            namespace Space
            {
                public interface IExported { void Inv (); }
                public interface IImported { void Fun (); void NotifyEvt(); }
            }

            public interface IExported { void Inv (); }
            public interface IImported { void Fun (); void NotifyEvt(); }

            public class Class
            {
                [JSInvokable] public static Space.IExported GetExported (Space.IImported arg) => default;
                [JSFunction] public static Task<IImported> GetImported (IExported arg) => default;
            }
            """));
        Execute();
        DoesNotContain("Binary<");
    }

    [Fact]
    public void SerializesAllTheCrawledSerializableTypes ()
    {
        AddAssembly(
            With("y", "public enum Enum { A, B }"),
            With("y", "public record Struct (double A, ReadonlyStruct[]? B);"),
            With("y", "public record ReadonlyStruct (Enum e);"),
            With("n", "public struct Struct { public y.Struct S { get; set; } public ReadonlyStruct[]? A { get; set; } }"),
            With("n", "public readonly struct ReadonlyStruct { public double A { get; init; } }"),
            With("n", "public readonly record struct ReadonlyRecordStruct(double A);"),
            With("n", "public record class RecordClass(double A);"),
            With("n", "public enum Enum { A, B }"),
            With("n", "public class Foo { public Struct S { get; } public ReadonlyStruct Rs { get; } }"),
            WithClass("n", "public class Bar : Foo { public ReadonlyRecordStruct Rrs { get; } public RecordClass Rc { get; } }"),
            With("n", "public class Baz { public List<Class.Bar?> Bars { get; } }"),
            WithClass("n", "[JSInvokable] public static Task<Baz?> GetBaz (Enum e) => default;"));
        Execute();
        Contains("Binary<global::y.Enum> ");
        Contains("Binary<global::n.Enum> ");
        Contains("Binary<global::y.Struct> ");
        Contains("Binary<global::n.Struct> ");
        Contains("Binary<global::n.ReadonlyStruct> ");
        Contains("Binary<global::y.ReadonlyStruct> ");
        Contains("Binary<global::n.ReadonlyStruct[]> ");
        Contains("Binary<global::y.ReadonlyStruct[]> ");
    }
}
