namespace Bootsharp.Publish;

/// <summary>
/// Describes a CLR type that crosses the interop boundary.
/// </summary>
internal sealed record TypeMeta
{
    /// <summary>
    /// The described CLR type.
    /// </summary>
    public required Type Clr { get; init; }
    /// <summary>
    /// Unique identifier of the type sanitized from symbols not allowed in source code identifiers.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// C# syntax of the type, as specified in the source code.
    /// </summary>
    public required string Syntax { get; init; }
    /// <summary>
    /// TypeScript syntax of the type, as specified in the source code.
    /// </summary>
    public required string TSSyntax { get; init; }
}
