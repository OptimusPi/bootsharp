namespace Bootsharp.Publish;

/// <summary>
/// Describes a serialized type that passes the interop boundary.
/// </summary>
internal abstract record SerializedMeta
{
    /// <summary>
    /// The serialized type.
    /// </summary>
    public required TypeMeta Type { get; init; }
}

/// <summary>
/// Describes a serialized primitive (string, int, bool, etc).
/// </summary>
internal sealed record SerializedPrimitiveMeta : SerializedMeta;

/// <summary>
/// Describes a serialized <see cref="System.Enum"/>.
/// </summary>
internal sealed record SerializedEnumMeta : SerializedMeta;

/// <summary>
/// Describes a serialized <see cref="System.Nullable"/>.
/// </summary>
internal sealed record SerializedNullableMeta : SerializedMeta
{
    /// <summary>
    /// The underlying nullable value type.
    /// </summary>
    public required TypeMeta ValueType { get; init; }
}

/// <summary>
/// Describes a serialized <see cref="System.Array"/>.
/// </summary>
internal sealed record SerializedArrayMeta : SerializedMeta
{
    /// <summary>
    /// The array element type.
    /// </summary>
    public required TypeMeta ElementType { get; init; }
}

/// <summary>
/// Describes a serialized linear collection type, such as generic lists and single-argument generic collections.
/// </summary>
internal sealed record SerializedListMeta : SerializedMeta
{
    /// <summary>
    /// The collection element type.
    /// </summary>
    public required TypeMeta ElementType { get; init; }
}

/// <summary>
/// Describes a serialized generic key-value type, such as generic dictionaries.
/// </summary>
internal sealed record SerializedDictionaryMeta : SerializedMeta
{
    /// <summary>
    /// The dictionary key type.
    /// </summary>
    public required TypeMeta KeyType { get; init; }
    /// <summary>
    /// The dictionary value type.
    /// </summary>
    public required TypeMeta ValueType { get; init; }
}

/// <summary>
/// Describes a serialized user-defined object, such as a record or a struct.
/// </summary>
internal sealed record SerializedObjectMeta : SerializedMeta
{
    /// <summary>
    /// The properties of the object, pre-ordered for serialization:
    /// constructor-parameter properties first (in parameter order), then the rest.
    /// </summary>
    public required IReadOnlyList<SerializedPropertyMeta> Properties { get; init; }
}

/// <summary>
/// Describes a serializable property of a <see cref="SerializedObjectMeta"/>.
/// </summary>
internal sealed record SerializedPropertyMeta
{
    /// <summary>
    /// The name of the property.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Corresponding JavaScript property name.
    /// </summary>
    public required string JSName { get; init; }
    /// <summary>
    /// The property type.
    /// </summary>
    public required TypeMeta Type { get; init; }
    /// <summary>
    /// How the property can be assigned during deserialization.
    /// </summary>
    public required PropertySetKind SetKind { get; init; }
    /// <summary>
    /// Whether the property has the 'required' modifier.
    /// </summary>
    public required bool Required { get; init; }
    /// <summary>
    /// Whether the property should be omitted from serialization output when null.
    /// </summary>
    public required bool OmitWhenNull { get; init; }
    /// <summary>
    /// Whether the property is bound to a constructor parameter.
    /// </summary>
    public required bool ConstructorParameter { get; init; }
    /// <summary>
    /// Name of the generated unsafe field accessor method when <see cref="PropertySetKind.Field"/>.
    /// </summary>
    public required string? FieldAccessorName { get; init; }
}

/// <summary>
/// How a serialized property can be assigned during deserialization.
/// </summary>
internal enum PropertySetKind
{
    /// <summary>
    /// Property cannot be set (read-only, no accessible setter or backing field).
    /// </summary>
    None,
    /// <summary>
    /// Property has a regular public setter.
    /// </summary>
    Write,
    /// <summary>
    /// Property has an init-only setter.
    /// </summary>
    Init,
    /// <summary>
    /// Property is set via an unsafe accessor to the compiler-generated backing field.
    /// </summary>
    Field
}
