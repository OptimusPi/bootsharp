using System.Reflection;

namespace Bootsharp.Publish;

internal sealed class SerializedInspector (TypeConverter converter)
{
    private record RefMeta : SerializedMeta;

    public IReadOnlyList<SerializedMeta> Inspect (IEnumerable<Type> types)
    {
        var result = new Dictionary<Type, SerializedMeta>();
        var cycle = new HashSet<Type>();
        foreach (var type in types.Where(t => ShouldSerialize(t) && !t.ContainsGenericParameters).Distinct())
            Inspect(type);
        return OrderForInitialization(result.Values);

        SerializedMeta Inspect (Type type)
        {
            if (result.TryGetValue(type, out var existing)) return existing;
            if (!cycle.Add(type)) return new RefMeta { Type = BuildTypeMeta(type) }; // break self-ref cycle
            var meta = Build(type, Inspect);
            result[type] = meta;
            cycle.Remove(type);
            return meta;
        }
    }

    private SerializedMeta Build (Type type, Func<Type, SerializedMeta> inspect)
    {
        var original = type;
        if (IsTaskWithResult(type, out var result))
        {
            original = result;
            type = result;
        }
        var nullable = IsNullable(type);
        if (nullable) type = GetNullableUnderlyingType(type);
        if (nullable && (type.IsEnum || !IsPrimitive(type) && !type.IsArray && !IsDictionary(type) && !IsList(type) && !IsCollection(type)))
            return new SerializedNullableMeta {
                Type = BuildTypeMeta(original),
                ValueType = inspect(type).Type
            };
        var typeMeta = BuildTypeMeta(original);
        if (type.IsEnum) return new SerializedEnumMeta { Type = typeMeta };
        if (IsPrimitive(type)) return new SerializedPrimitiveMeta { Type = typeMeta };
        if (type.IsArray)
            return new SerializedArrayMeta {
                Type = typeMeta,
                ElementType = inspect(type.GetElementType()!).Type
            };
        if (IsList(type) || IsCollection(type))
            return new SerializedListMeta {
                Type = typeMeta,
                ElementType = inspect(GetListElementType(type)).Type
            };
        if (IsDictionary(type))
            return new SerializedDictionaryMeta {
                Type = typeMeta,
                KeyType = inspect(type.GenericTypeArguments[0]).Type,
                ValueType = inspect(type.GenericTypeArguments[1]).Type
            };
        return BuildObject(type, typeMeta, inspect);

        static bool IsPrimitive (Type type) =>
            Type.GetTypeCode(type) != TypeCode.Object ||
            type.FullName == typeof(DateTimeOffset).FullName ||
            type.FullName == typeof(nint).FullName;
    }

    private SerializedMeta BuildObject (Type type, TypeMeta typeMeta, Func<Type, SerializedMeta> inspect)
    {
        var ctor = ResolveConstructor(type);
        var ctorParams = ctor?.GetParameters() ?? [];
        var parameterOrders = ctorParams
            .Select((p, i) => (p.Name!, i))
            .ToDictionary(p => p.Item1, p => p.i, StringComparer.OrdinalIgnoreCase);
        var properties = GetSerializableProperties(type)
            .OrderBy(p => parameterOrders.GetValueOrDefault(p.Name, int.MaxValue))
            .Select(p => BuildProperty(p, parameterOrders.ContainsKey(p.Name), inspect))
            .ToArray();
        return new SerializedObjectMeta { Type = typeMeta, Properties = properties };
    }

    private SerializedPropertyMeta BuildProperty (PropertyInfo property, bool ctorParameter,
        Func<Type, SerializedMeta> inspect)
    {
        var value = inspect(property.PropertyType);
        var getter = property.GetMethod!;
        var setter = property.SetMethod;
        var initOnly = setter?.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == typeof(System.Runtime.CompilerServices.IsExternalInit).FullName) == true;
        var required = property.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute");
        var canInit = setter != null && setter.IsPublic && initOnly;
        var canWrite = setter != null && setter.IsPublic && !initOnly;
        var canSetField = !canInit && !canWrite && IsAutoProperty(property) && getter.IsPublic;
        var setKind = canInit ? PropertySetKind.Init :
            canWrite ? PropertySetKind.Write :
            canSetField ? PropertySetKind.Field : PropertySetKind.None;
        return new SerializedPropertyMeta {
            Name = property.Name,
            JSName = ToFirstLower(property.Name),
            Type = value.Type,
            OmitWhenNull = CanBeNull(property.PropertyType),
            Required = required,
            ConstructorParameter = ctorParameter,
            SetKind = setKind,
            FieldAccessorName = canSetField ? $"Set_{BuildId(property.DeclaringType!)}_{property.Name}" : null
        };

        static bool CanBeNull (Type type)
        {
            return !type.IsValueType || IsNullable(type);
        }
    }

    private TypeMeta BuildTypeMeta (Type type) => new() {
        Clr = type,
        Syntax = BuildSyntax(type),
        TSSyntax = converter.ToTypeScript(type, null),
        Id = BuildId(type)
    };

    private static ConstructorInfo? ResolveConstructor (Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        if (constructors.Length == 0) return null;
        var parameterless = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
        if (parameterless != null) return parameterless;
        var matching = constructors.Where(c => HasMatchingParameters(c, type)).ToArray();
        if (matching.Length == 1) return matching[0];
        return null;

        static bool HasMatchingParameters (ConstructorInfo ctor, Type declaringType)
        {
            var properties = declaringType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in ctor.GetParameters())
            {
                if (!properties.TryGetValue(parameter.Name!, out var property)) return false;
                if (property.PropertyType != parameter.ParameterType) return false;
            }
            return true;
        }
    }

    private static IEnumerable<PropertyInfo> GetSerializableProperties (Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        return type.GetProperties(flags).Where(p => p.GetMethod != null && (IsAutoProperty(p) || type.IsInterface));
    }

    private static IReadOnlyList<SerializedMeta> OrderForInitialization (IEnumerable<SerializedMeta> types)
    {
        var metas = types.DistinctBy(m => m.Type.Id).ToDictionary(m => m.Type.Id);
        var pending = metas.ToDictionary(
            m => m.Key,
            m => GetInitDependencies(m.Value).Where(metas.ContainsKey).ToHashSet(StringComparer.Ordinal));
        var dependents = metas.Keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (id, dependencies) in pending)
        foreach (var dependency in dependencies)
            dependents[dependency].Add(id);

        var queue = new PriorityQueue<SerializedMeta, (int, string)>();
        foreach (var meta in metas.Values.Where(m => pending[m.Type.Id].Count == 0))
            queue.Enqueue(meta, (GetInitOrder(meta), meta.Type.Id));

        var ordered = new List<SerializedMeta>(metas.Count);
        while (queue.TryDequeue(out var meta, out _))
        {
            if (!pending.Remove(meta.Type.Id)) continue;
            ordered.Add(meta);
            foreach (var dependent in dependents[meta.Type.Id])
                if (pending.TryGetValue(dependent, out var deps))
                {
                    deps.Remove(meta.Type.Id);
                    if (deps.Count == 0)
                        queue.Enqueue(metas[dependent], (GetInitOrder(metas[dependent]), dependent));
                }
        }

        return ordered;

        static int GetInitOrder (SerializedMeta meta) => meta switch {
            SerializedPrimitiveMeta or SerializedEnumMeta => 0,
            SerializedObjectMeta => 2,
            _ => 3
        };

        static IEnumerable<string> GetInitDependencies (SerializedMeta meta)
        {
            switch (meta)
            {
                case SerializedNullableMeta nullable:
                    yield return nullable.ValueType.Id;
                    yield break;
                case SerializedListMeta list:
                    yield return list.ElementType.Id;
                    yield break;
                case SerializedDictionaryMeta dic:
                    yield return dic.KeyType.Id;
                    yield return dic.ValueType.Id;
                    yield break;
            }
        }
    }
}
