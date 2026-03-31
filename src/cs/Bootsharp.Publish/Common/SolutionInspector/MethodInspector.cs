using System.Reflection;

namespace Bootsharp.Publish;

internal sealed class MethodInspector (Preferences prefs, TypeConverter converter)
{
    private MethodInfo method = null!;
    private MethodKind kind;

    public MethodMeta Inspect (MethodInfo method, MethodKind kind)
    {
        this.method = method;
        this.kind = kind;
        return CreateMethod();
    }

    private MethodMeta CreateMethod () => new() {
        Kind = kind,
        Assembly = method.DeclaringType!.Assembly.GetName().Name!,
        Space = method.DeclaringType.FullName!,
        Name = method.Name,
        Arguments = method.GetParameters().Select(CreateArgument).ToArray(),
        JSSpace = BuildMethodSpace(),
        JSName = WithPrefs(prefs.Function, method.Name, ToFirstLower(method.Name)),
        ReturnValue = CreateValue(method.ReturnParameter, true),
        Void = IsVoid(method.ReturnParameter.ParameterType),
        Async = IsTaskLike(method.ReturnParameter.ParameterType)
    };

    private ArgumentMeta CreateArgument (ParameterInfo param) => new() {
        Name = param.Name!,
        JSName = param.Name == "function" ? "fn" : param.Name!,
        Value = CreateValue(param, false)
    };

    private ValueMeta CreateValue (ParameterInfo param, bool @return) => new() {
        Type = CreateType(param),
        Optional = @return ? IsNullable(method) : IsNullable(param),
        Serialized = ShouldSerialize(param.ParameterType),
        Instance = IsInstancedInteropInterface(param.ParameterType, out var instanceType),
        InstanceType = instanceType
    };

    private TypeMeta CreateType (ParameterInfo param) => new() {
        Clr = param.ParameterType,
        Id = BuildId(param.ParameterType),
        Syntax = BuildSyntax(param.ParameterType, param),
        TSSyntax = converter.ToTypeScript(param.ParameterType, GetNullability(param))
    };

    private string BuildMethodSpace ()
    {
        var space = method.DeclaringType!.Namespace ?? "";
        var name = BuildJSSpaceName(method.DeclaringType);
        if (method.DeclaringType.IsInterface) name = name[1..];
        var fullname = string.IsNullOrEmpty(space) ? name : $"{space}.{name}";
        return WithPrefs(prefs.Space, fullname, fullname);
    }
}
