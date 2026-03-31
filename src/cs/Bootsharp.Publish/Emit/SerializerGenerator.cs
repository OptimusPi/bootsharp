using System.Text;

namespace Bootsharp.Publish;

internal sealed class SerializerGenerator
{
    public string Generate (IReadOnlyCollection<SerializedMeta> types)
    {
        if (types.Count == 0) return "";
        return $$"""
                 using System.Runtime.CompilerServices;

                 namespace Bootsharp.Generated;

                 internal static class SerializerContext
                 {
                     {{JoinLines(types.Select(EmitType))}}

                     {{JoinLines(types.OfType<SerializedObjectMeta>().SelectMany(EmitObject), separator: "\n\n")}}
                 }
                 """;
    }

    private static string EmitType (SerializedMeta meta)
    {
        return $"internal static readonly Binary<{meta.Type.Syntax}> {meta.Type.Id} = {EmitFactory(meta)};";

        static string EmitFactory (SerializedMeta meta) => meta switch {
            SerializedEnumMeta => $"Serializer.Enum<{meta.Type.Syntax}>()",
            SerializedNullableMeta nullable => $"Serializer.Nullable({nullable.ValueType.Id})",
            SerializedArrayMeta arr => $"Serializer.Array({arr.ElementType.Id})",
            SerializedListMeta list => $"Serializer.{GetGeneric(list)}({list.ElementType.Id})",
            SerializedDictionaryMeta dic => $"Serializer.{GetGeneric(dic)}({dic.KeyType.Id}, {dic.ValueType.Id})",
            SerializedObjectMeta => $"new(Write_{meta.Type.Id}, Read_{meta.Type.Id})",
            _ => EmitPrimitive(meta.Type.Clr)
        };

        static string GetGeneric (SerializedMeta meta)
        {
            var name = meta.Type.Clr.Name;
            return name[..name.IndexOf('`')];
        }

        static string EmitPrimitive (Type type)
        {
            if (IsNullable(type)) return $"Serializer.Nullable({EmitPrimitive(GetNullableUnderlyingType(type))})";
            if (type.FullName == typeof(nint).FullName) return "Serializer.IntPtr";
            if (type.FullName == typeof(DateTimeOffset).FullName) return "Serializer.DateTimeOffset";
            return $"Serializer.{Type.GetTypeCode(type)}";
        }
    }

    private static IEnumerable<string> EmitObject (SerializedObjectMeta obj)
    {
        yield return EmitObjectWriter(obj);
        yield return EmitObjectReader(obj);
        foreach (var property in obj.Properties.Where(p => p.SetKind == PropertySetKind.Field))
            yield return EmitFieldAccessor(obj, property);
    }

    private static string EmitObjectWriter (SerializedObjectMeta obj)
    {
        var body = new StringBuilder();
        body.AppendLine($"private static void Write_{obj.Type.Id} (ref Writer writer, {obj.Type.Syntax} value)");
        body.AppendLine("{");
        if (!obj.Type.Clr.IsValueType)
        {
            body.AppendLine("    writer.WriteBool(value is not null);");
            body.AppendLine("    if (value is null) return;");
        }
        foreach (var property in obj.Properties)
        {
            if (property.OmitWhenNull)
            {
                body.AppendLine($"    writer.WriteBool(value.{property.Name} is not null);");
                body.AppendLine($"    if (value.{property.Name} is not null) {property.Type.Id}.Write(ref writer, value.{property.Name});");
            }
            else body.AppendLine($"    {property.Type.Id}.Write(ref writer, value.{property.Name});");
        }
        body.Append('}');
        return body.ToString();
    }

    private static string EmitObjectReader (SerializedObjectMeta obj)
    {
        var body = new StringBuilder();
        body.AppendLine($"private static {obj.Type.Syntax} Read_{obj.Type.Id} (ref Reader reader)");
        body.AppendLine("{");
        if (!obj.Type.Clr.IsValueType)
        {
            body.AppendLine("    if (!reader.ReadBool()) return null!;");
        }
        foreach (var prop in obj.Properties)
        {
            var local = MangleLocal(prop.Name);
            if (prop.OmitWhenNull)
                body.AppendLine($"    var {local} = reader.ReadBool() ? {prop.Type.Id}.Read(ref reader) : default;");
            else body.AppendLine($"    var {local} = {prop.Type.Id}.Read(ref reader);");
        }
        body.AppendLine($"    var _value_ = {BuildObjectConstruction(obj)};");
        foreach (var property in obj.Properties.Where(p => !p.ConstructorParameter && !ShouldInitializeInConstruction(p)))
        {
            var local = MangleLocal(property.Name);
            if (property.SetKind == PropertySetKind.Write) body.AppendLine($"    _value_.{property.Name} = {local};");
            else if (property.SetKind == PropertySetKind.Field)
                body.AppendLine(obj.Type.Clr.IsValueType
                    ? $"    {property.FieldAccessorName}(ref _value_) = {local};"
                    : $"    {property.FieldAccessorName}(_value_) = {local};");
        }
        body.AppendLine("    return _value_;");
        body.Append('}');
        return body.ToString();
    }

    private static string BuildObjectConstruction (SerializedObjectMeta obj)
    {
        var ctorArgs = obj.Properties.Where(p => p.ConstructorParameter);
        var ctor = $"new {obj.Type.Syntax}({string.Join(", ", ctorArgs.Select(p => MangleLocal(p.Name)))})";
        var props = obj.Properties
            .Where(p => !p.ConstructorParameter && ShouldInitializeInConstruction(p))
            .Select(p => $"{p.Name} = {MangleLocal(p.Name)}").ToArray();
        if (props.Length == 0) return ctor;
        return $"{ctor} {{ {string.Join(", ", props)} }}";
    }

    private static string EmitFieldAccessor (SerializedObjectMeta obj, SerializedPropertyMeta property)
    {
        var receiver = obj.Type.Clr.IsValueType ? $"ref {obj.Type.Syntax} value" : $"{obj.Type.Syntax} value";
        return $"""
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<{property.Name}>k__BackingField")]
                private static extern ref {property.Type.Syntax} {property.FieldAccessorName} ({receiver});
                """;
    }

    private static bool ShouldInitializeInConstruction (SerializedPropertyMeta property)
    {
        return property.SetKind == PropertySetKind.Init || property.Required && property.SetKind == PropertySetKind.Write;
    }

    private static string MangleLocal (string name)
    {
        return $"@{ToFirstLower(name)}";
    }
}
