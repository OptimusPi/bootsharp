using System.Text;

namespace Bootsharp.Publish;

internal sealed class BindingGenerator (Preferences prefs)
{
    private record Binding (MethodMeta? Method, Type? Enum, string Namespace);

    private readonly StringBuilder builder = new();
    private readonly BindingClassGenerator classGenerator = new();
    private IReadOnlyCollection<InterfaceMeta> instanced = [];
    private IReadOnlyCollection<SerializedMeta> serialized = [];

    private Binding binding => bindings[index];
    private Binding? prevBinding => index == 0 ? null : bindings[index - 1];
    private Binding? nextBinding => index == bindings.Length - 1 ? null : bindings[index + 1];

    private Binding[] bindings = null!;
    private int index, level;

    public string Generate (SolutionInspection inspection)
    {
        instanced = inspection.InstancedInterfaces;
        serialized = inspection.SerializedTypes;
        var methods = inspection.StaticMethods
            .Concat(inspection.StaticInterfaces.SelectMany(i => i.Methods))
            .Concat(inspection.InstancedInterfaces.SelectMany(i => i.Methods))
            .ToArray();
        bindings = methods
            .Select(m => new Binding(m, null, m.JSSpace))
            .Concat(inspection.Crawled.Where(t => t.IsEnum)
                .Select(t => new Binding(null, t, BuildJSSpace(t, prefs))))
            .OrderBy(m => m.Namespace).ToArray();
        if (bindings.Length == 0) return "";
        EmitImports();
        builder.Append('\n');
        EmitSerializer();
        builder.Append("\n\n");
        if (inspection.InstancedInterfaces.Count > 0)
            builder.Append(classGenerator.Generate(inspection.InstancedInterfaces));
        for (index = 0; index < bindings.Length; index++)
            EmitBinding();
        return builder.ToString();
    }

    private void EmitImports ()
    {
        builder.Append(
            """
            import { exports } from "./exports";
            import { Event } from "./event";
            import { registerInstance, getInstance, disposeOnFinalize } from "./instances";
            import { serialize, deserialize, binary, types } from "./serialization";

            function getExports() { if (exports == null) throw Error("Boot the runtime before invoking C# APIs."); return exports; }
            function getImport(handler, serializedHandler, name) { if (typeof handler !== "function") throw Error(`Failed to invoke '${name}' from C#. Make sure to assign the function in JavaScript.`); return serializedHandler; }
            """
        );
    }

    private void EmitSerializer ()
    {
        if (serialized.Count == 0) return;
        foreach (var meta in serialized)
            builder.Append($"\nconst {meta.Type.Id} = {EmitFactory(meta)};");
        foreach (var meta in serialized)
        {
            if (meta is not SerializedObjectMeta obj) continue;
            builder.Append("\n\n").Append(EmitObjectWrite(obj));
            builder.Append("\n\n").Append(EmitObjectRead(obj));
        }

        static string EmitFactory (SerializedMeta meta)
        {
            return meta switch {
                SerializedNullableMeta nullable => $"types.Nullable({nullable.ValueType.Id})",
                SerializedEnumMeta => "types.Int32",
                SerializedArrayMeta arr => $"types.Array({arr.ElementType.Id})",
                SerializedListMeta list => $"types.List({list.ElementType.Id})",
                SerializedDictionaryMeta dic => $"types.Dictionary({dic.KeyType.Id}, {dic.ValueType.Id})",
                SerializedObjectMeta => $"binary(write_{meta.Type.Id}, read_{meta.Type.Id})",
                _ => ResolvePrimitive(meta.Type.Clr)
            };

            static string ResolvePrimitive (Type type)
            {
                if (IsNullable(type)) return $"types.Nullable({BuildId(GetNullableUnderlyingType(type))})";
                if (type.FullName == typeof(DateTimeOffset).FullName) return "types.DateTimeOffset";
                if (type.FullName == typeof(nint).FullName) return "types.IntPtr";
                return $"types.{Type.GetTypeCode(type)}";
            }
        }

        static string EmitObjectWrite (SerializedObjectMeta obj)
        {
            var body = new StringBuilder();
            body.AppendLine($"function write_{obj.Type.Id}(writer, value) {{");
            if (!obj.Type.Clr.IsValueType)
            {
                body.AppendLine("    writer.writeBool(value != null);");
                body.AppendLine("    if (value == null) return;");
            }
            foreach (var property in obj.Properties)
            {
                var access = $"value.{property.JSName}";
                if (property.OmitWhenNull)
                {
                    body.AppendLine($"    writer.writeBool({access} != null);");
                    body.AppendLine($"    if ({access} != null) {property.Type.Id}.write(writer, {access});");
                }
                else body.AppendLine($"    {property.Type.Id}.write(writer, {access});");
            }
            body.Append('}');
            return body.ToString();
        }

        static string EmitObjectRead (SerializedObjectMeta obj)
        {
            var body = new StringBuilder();
            body.AppendLine($"function read_{obj.Type.Id}(reader) {{");
            if (!obj.Type.Clr.IsValueType)
                body.AppendLine("    if (!reader.readBool()) return null;");
            body.AppendLine("    const value = {};");
            foreach (var property in obj.Properties)
            {
                if (property.OmitWhenNull)
                    body.AppendLine($"    if (reader.readBool()) value.{property.JSName} = {property.Type.Id}.read(reader);");
                else body.AppendLine($"    value.{property.JSName} = {property.Type.Id}.read(reader);");
            }
            body.AppendLine("    return value;");
            body.Append('}');
            return body.ToString();
        }
    }

    private void EmitBinding ()
    {
        if (ShouldOpenNamespace()) OpenNamespace();
        if (binding.Method != null) EmitMethod(binding.Method);
        else EmitEnum(binding.Enum!);
        if (ShouldCloseNamespace()) CloseNamespace();
    }

    private bool ShouldOpenNamespace ()
    {
        if (prevBinding is null) return true;
        return prevBinding.Namespace != binding.Namespace;
    }

    private void OpenNamespace ()
    {
        level = 0;
        var prevParts = prevBinding?.Namespace.Split('.') ?? [];
        var parts = binding.Namespace.Split('.');
        while (prevParts.ElementAtOrDefault(level) == parts[level]) level++;
        for (var i = level; i < parts.Length; level = i, i++)
            if (i == 0) builder.Append($"\nexport const {parts[i]} = {{");
            else builder.Append($"{Comma()}\n{Pad(i)}{parts[i]}: {{");
    }

    private bool ShouldCloseNamespace ()
    {
        if (nextBinding is null) return true;
        return nextBinding.Namespace != binding.Namespace;
    }

    private void CloseNamespace ()
    {
        var target = GetCloseLevel();
        for (; level >= target; level--)
            if (level == 0) builder.Append("\n};");
            else builder.Append($"\n{Pad(level)}}}");

        int GetCloseLevel ()
        {
            if (nextBinding is null) return 0;
            var closeLevel = 0;
            var parts = binding.Namespace.Split('.');
            var nextParts = nextBinding.Namespace.Split('.');
            for (var i = 0; i < parts.Length; i++)
                if (parts[i] == nextParts[i]) closeLevel++;
                else break;
            return closeLevel;
        }
    }

    private void EmitMethod (MethodMeta method)
    {
        if (method.Kind == MethodKind.Invokable) EmitInvokable(method);
        else if (method.Kind == MethodKind.Function) EmitFunction(method);
        else EmitEvent(method);
    }

    private void EmitInvokable (MethodMeta method)
    {
        var instanced = IsInstanced(method);
        var wait = ShouldWait(method);
        var endpoint = $"getExports().{method.Space.Replace('.', '_')}_{method.Name}";
        var funcArgs = string.Join(", ", method.Arguments.Select(a => a.JSName));
        if (instanced) funcArgs = PrependInstanceIdArgName(funcArgs);
        var invArgs = string.Join(", ", method.Arguments.Select(BuildInvArg));
        if (instanced) invArgs = PrependInstanceIdArgName(invArgs);
        var body = $"{(wait ? "await " : "")}{endpoint}({invArgs})";
        if (method.ReturnValue.InstanceType is { } itp) body = $"new {BuildInstanceClassName(itp)}({body})";
        else if (method.ReturnValue.Serialized) body = $"deserialize({body}, {method.ReturnValue.Type.Id})";
        var func = $"{(wait ? "async " : "")}({funcArgs}) => {body}";
        builder.Append($"{Break()}{method.JSName}: {func}");

        string BuildInvArg (ArgumentMeta arg)
        {
            if (arg.Value.Instance) return $"registerInstance({arg.JSName})";
            if (arg.Value.Serialized) return $"serialize({arg.JSName}, {arg.Value.Type.Id})";
            return arg.JSName;
        }
    }

    private void EmitFunction (MethodMeta method)
    {
        var instanced = IsInstanced(method);
        var wait = ShouldWait(method);
        var name = method.JSName;
        var funcArgs = string.Join(", ", method.Arguments.Select(a => a.JSName));
        if (instanced) funcArgs = PrependInstanceIdArgName(funcArgs);
        var invArgs = string.Join(", ", method.Arguments.Select(BuildInvArg));
        var handler = instanced ? $"getInstance(_id).{name}" : $"this.{name}Handler";
        var body = $"{(wait ? "await " : "")}{handler}({invArgs})";
        if (method.ReturnValue.Instance) body = $"registerInstance({body})";
        else if (method.ReturnValue.Serialized) body = $"serialize({body}, {method.ReturnValue.Type.Id})";
        var serdeHandler = $"{(wait ? "async " : "")}({funcArgs}) => {body}";
        if (instanced) builder.Append($"{Break()}{name}Serialized: {serdeHandler}");
        else
        {
            var set = $"{handler} = handler; this.{name}SerializedHandler = {serdeHandler};";
            var serde = $"return getImport({handler}, this.{name}SerializedHandler, \"{binding.Namespace}.{name}\");";
            builder.Append($"{Break()}get {name}() {{ return {handler}; }}");
            builder.Append($"{Break()}set {name}(handler) {{ {set} }}");
            builder.Append($"{Break()}get {name}Serialized() {{ {serde} }}");
        }

        string BuildInvArg (ArgumentMeta arg)
        {
            if (arg.Value.Instance) return $"new {BuildInstanceClassName(arg.Value.InstanceType)}({arg.JSName})";
            if (arg.Value.Serialized) return $"deserialize({arg.JSName}, {arg.Value.Type.Id})";
            return arg.JSName;
        }
    }

    private void EmitEvent (MethodMeta method)
    {
        var instanced = IsInstanced(method);
        var name = method.JSName;
        if (!instanced) builder.Append($"{Break()}{name}: new Event()");
        var funcArgs = string.Join(", ", method.Arguments.Select(a => a.JSName));
        if (instanced) funcArgs = PrependInstanceIdArgName(funcArgs);
        var evtArgs = string.Join(", ", method.Arguments.Select(BuildEvtArg));
        var handler = instanced ? "getInstance(_id)" : method.JSSpace;
        builder.Append($"{Break()}{name}Serialized: ({funcArgs}) => {handler}.{name}.broadcast({evtArgs})");

        string BuildEvtArg (ArgumentMeta arg)
        {
            if (!arg.Value.Serialized) return arg.JSName;
            // By default, we use 'null' for missing collection items, but here the event args array
            // represents args specified to the event's 'broadcast' function, so user expects 'undefined'.
            var toUndefined = arg.Value.Optional ? " ?? undefined" : "";
            return $"deserialize({arg.JSName}, {arg.Value.Type.Id}){toUndefined}";
        }
    }

    private void EmitEnum (Type @enum)
    {
        var values = Enum.GetValuesAsUnderlyingType(@enum).Cast<object>().ToArray();
        var fields = string.Join(", ", values
            .Select(v => $"\"{v}\": \"{Enum.GetName(@enum, v)}\"")
            .Concat(values.Select(v => $"\"{Enum.GetName(@enum, v)}\": {v}")));
        builder.Append($"{Break()}{@enum.Name}: {{ {fields} }}");
    }

    private bool ShouldWait (MethodMeta method)
    {
        if (!method.Async) return false;
        return method.Arguments.Any(a => a.Value.Serialized || a.Value.Instance) ||
               method.ReturnValue.Serialized || method.ReturnValue.Instance;
    }

    private string Break () => $"{Comma()}\n{Pad(level + 1)}";
    private string Pad (int level) => new(' ', level * 4);
    private string Comma () => builder[^1] == '{' ? "" : ",";

    private string BuildInstanceClassName (Type instanceType)
    {
        var instance = instanced.First(i => i.Type == instanceType);
        return BuildJSInteropInstanceClassName(instance);
    }

    private bool IsInstanced (MethodMeta method)
    {
        return instanced.Any(i => i.Methods.Contains(method));
    }
}
