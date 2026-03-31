using System.Diagnostics.CodeAnalysis;

namespace Bootsharp.Publish;

/// <summary>
/// Describes a value that passes the interop boundary.
/// </summary>
internal sealed record ValueMeta
{
    /// <summary>
    /// Type of the value.
    /// </summary>
    public required TypeMeta Type { get; init; }
    /// <summary>
    /// Whether the value type has to be serialized to cross the interop boundary.
    /// </summary>
    public required bool Serialized { get; init; }
    /// <summary>
    /// Whether the value can be omitted or is explicitly nullable.
    /// </summary>
    public required bool Optional { get; init; }
    /// <summary>
    /// Whether the value is an interop instance.
    /// </summary>
    [MemberNotNullWhen(true, nameof(InstanceType))]
    public required bool Instance { get; init; }
    /// <summary>
    /// When <see cref="Instance"/> contains a type of the associated interop interface instance.
    /// </summary>
    public required Type? InstanceType { get; init; }
}
