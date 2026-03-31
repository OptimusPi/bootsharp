global using static Bootsharp.Publish.GlobalSerialization;
using System.Collections.Frozen;

namespace Bootsharp.Publish;

internal static class GlobalSerialization
{
    private static readonly FrozenSet<string> native = new[] {
        typeof(string).FullName!, typeof(bool).FullName!, typeof(byte).FullName!,
        typeof(char).FullName!, typeof(short).FullName!, typeof(long).FullName!,
        typeof(int).FullName!, typeof(float).FullName!, typeof(double).FullName!,
        typeof(nint).FullName!, typeof(Task).FullName!, typeof(DateTime).FullName!,
        typeof(DateTimeOffset).FullName!, typeof(Exception).FullName!
    }.ToFrozenSet();

    // https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/import-export-interop
    public static bool ShouldSerialize (Type type)
    {
        if (IsVoid(type)) return false;
        if (IsInstancedInteropInterface(type, out _)) return false;
        if (IsTaskWithResult(type, out var result)) return ShouldSerialize(result);
        if (IsNullable(type)) return ShouldSerialize(GetNullableUnderlyingType(type));
        return !native.Contains(type.FullName!);
    }
}
