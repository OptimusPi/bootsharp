# Preferences

Use the `[Preferences]` assembly attribute to customize Bootsharp behaviour at build time when the interop code is emitted. Each property accepts an array of `(pattern, replacement)` strings that are fed to [Regex.Replace](https://docs.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.replace?view=net-6.0#system-text-regularexpressions-regex-replace(system-string-system-string-system-string)) when generating the associated code. Pairs are tested in order; on first match the result replaces the default.

## Space

Customizes how C# type namespaces map to JavaScript module names. The patterns are matched against the type's C# namespace, or an empty string when the type is global; the result is slugified into the module path. Types whose resolved space ends up empty land in the default `index` module.

By default, all generated bindings and TypeScript declarations are grouped under their C# namespace. To rewrite every type declared under `Foo.Bar` so its binding ends up in the `baz` module:

```cs
[assembly: Preferences(
    Space = ["^Foo\.Bar$", "Baz"]
)]
```

## Name

Customizes how C# type names map to the emitted JavaScript object names. The patterns are matched against the C# type name as reflected. To rename every exported type whose name ends with `Service` so the suffix is dropped on the JavaScript side:

```cs
[assembly: Preferences(
    Name = ["^(\S+)Service$", "$1"]
)]
```

## Method

Customizes how C# method names map to JavaScript function names. The patterns are matched against the reflected method name. By default, Bootsharp camel-cases the C# name; this pref overrides the result. To force every method starting with `Get` to keep its original casing:

```cs
[assembly: Preferences(
    Method = ["^Get(\S+)$", "Get$1"]
)]
```

## Property

Customizes how C# property names map to JavaScript property names. The patterns are matched against the reflected property name and behave the same way as `Method`, but apply to both exported and imported properties.

```cs
[assembly: Preferences(
    Property = ["^Is(\S+)$", "has$1"]
)]
```

## Event

Customizes how C# event names map to JavaScript event names. The patterns are matched against the reflected event name. Applies to both exported and imported events.
```cs
[assembly: Preferences(
    Event = ["^On(\S+)$", "$1Changed"]
)]
```

## Combining Preferences

Multiple properties can be set on the same attribute, and each property accepts more than one pair — earlier pairs take precedence over later ones on the same input:

```cs
[assembly: Preferences(
    Space = ["^Foo\.Bar$", "Baz"],
    Name = [
        "^I([A-Z].*)UI$", "$1",
        "^I([A-Z].*)$", "$1"
    ],
    Method = ["^Get(\S+)$", "fetch$1"]
)]
```

The example preferences above will strip the leading `I` from interface names (so `IService` is emitted as `Service`), additionally drop a trailing `UI` suffix when present (so `IMainUI` becomes `Main`) — the more specific pair is listed first so it wins over the generic `I` strip on the same input — move every type declared in the C# `Foo.Bar` namespace into the `baz` JavaScript module, and rename every C# method starting with `Get` so the JavaScript binding uses a `fetch` prefix instead (so `GetUser` is exposed as `fetchUser`).

