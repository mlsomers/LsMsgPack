# Unity

LsMsgPack runs in Unity 6, in the editor and in players built with either scripting backend (Mono or IL2CPP), with the same bytes and values as on .NET. It is tested with Unity 6000.6 on Linux: in the editor, a Mono player, and IL2CPP players at the Minimal and High managed stripping levels (`LsMsgPackUnitySmokeTest` in the repository). Android, iOS, WebGL and consoles aren't tested yet.

## Setup

Unity doesn't install NuGet packages itself. Take the `netstandard2.1` builds from the packages, or use [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity):

1. Copy `LsMsgPack.dll` (package `LsMsgPack`) and `LsMsgPack.Core.dll` (package `LsMsgPack.Core`), from `lib/netstandard2.1`, into `Assets/Plugins`. Add `LsMsgPackDebug.dll` only if you use its item-tree serializer.
2. Leave out `System.Memory` and `System.Runtime.CompilerServices.Unsafe`: Unity has its own.
3. Optional, recommended for IL2CPP: the [source generator](SourceGenerator.md). Copy `analyzers/dotnet/cs/LsMsgPack.Generator.dll` from the `LsMsgPack` package into `Assets` (for example `Assets/Plugins/LsMsgPack/Analyzers`). In the inspector of the DLL, clear every platform under *Select platforms for plugin*, and add the asset label `RoslynAnalyzer`. Unity then runs it when it compiles your scripts.

```csharp
using LsMsgPack;

[LsMsgPackSerializable(typeof(SaveGame))]
public partial class GameMsgPackContext : LsMsgPackContext { }

LsMsgPackSerializer serializer = new LsMsgPackSerializer(GameMsgPackContext.Default, new LsMsgPackOptions());
byte[] bytes = serializer.Serialize(save);
SaveGame read = serializer.Deserialize<SaveGame>(bytes);
```

Unity compiles C# 9 against .NET Standard 2.1, which has no `UnsafeAccessor`: properties with an `init` setter are set by reflection (the generator reports them as info LSMP104), and there are no record structs. Everything else the generator writes is used.

## Unity's types

Unity's structs whose state is public fields are written by those fields, as System.Numerics' are, whatever the fields mode: `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Color`, `Matrix4x4`. Their computed properties (`normalized`, `magnitude`, `eulerAngles`...) are left out. In the array layout without the schema, `Vector3` is `[x, y, z]`: the bytes of `System.Numerics.Vector3`, and of MessagePack-CSharp's Unity resolver.

Unity's structs with private fields are written by their settable properties: `Vector2Int` is its `x` and `y`. Some have many settable properties that overlap (`Rect`: `x`, `xMin`, `center`, `size`...; `Bounds`): a [converter](Converters.md) writes them more compactly.

LsMsgPack recognizes Unity's engine by the names of its assemblies (`UnityEngine`, `UnityEngine.*`): it doesn't reference Unity.

Write your own data classes, not `MonoBehaviour`s or `ScriptableObject`s: Unity creates those itself.

### Short type names

Type names in the schema and type ids are short by default (`Vector3`, not `UnityEngine.Vector3`), and a process resolves a short name to one type. A reader that declares `UnityEngine.Vector3` where the schema says `Vector3` reads it as such, also in a process that resolved `Vector3` to `System.Numerics.Vector3` (see [type names](schema.md#polymorphic-class-hierarchy-support)). Two cases still need full names (`AddTypeIdOptions = AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName`), or one `Vector3`:

- one payload with both `Vector3`s: its schema has two entries with the same name;
- a type id of a `Vector3` in a member declared as `object` or an interface: the reader has no declared `Vector3` to pair it with.

## Fields

Unity's own serializer writes public fields and the private fields marked `[SerializeField]`. LsMsgPack writes properties, and fields only when they opt in:

- `[SerializeField]` opts a field in, like `[DataMember]`.
- Public fields need `SerializedFields.Public` in the static filters, as Unity writes them:

```csharp
LsMsgPackOptions options = new LsMsgPackOptions()
{
  StaticFilters = new IMsgPackPropertyIncludeStatically[]
  {
    new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, SerializedFields.Public),
    new FilterIgnoredAttribute(),
    new FilterStatic()
  }
};
```

`[NonSerialized]` leaves a field out (`IgnoreAttributes.NonSerialized`, see [schema.md](schema.md)). See [schema.md](schema.md#fields) for the fields modes.

## IL2CPP

IL2CPP compiles ahead of time, so LsMsgPack can't compile its accessors and plans at runtime and uses reflection, about 2.8x slower than with the generated code. Per invoice of the smoke test (2 KB, Unity 6000.6, Linux x64):

| | Write | Read |
|---|---:|---:|
| Mono player | 49 µs | 86 µs |
| IL2CPP, without the generator | 126 µs | 252 µs |
| IL2CPP, with the generator | 45 µs | 89 µs |

## Managed code stripping

Unity's stripper removes the members nothing calls, and LsMsgPack reaches your classes by reflection:

- With the generator, the generated code calls the members it describes, so they are kept: the smoke test's model gives the same bytes at High stripping without a `link.xml`.
- Without it, Minimal stripping keeps your own assemblies; at Medium and High, keep your model with a `link.xml` in `Assets`:

```xml
<linker>
  <assembly fullname="Assembly-CSharp">
    <namespace fullname="MyGame.Saves" preserve="all" />
  </assembly>
</linker>
```

- Unity strips its own engine assemblies at every level. The structs of public fields keep their fields, but `Vector2Int`'s `x` and `y`, reached only by reflection, are removed: it's then written as an empty object. Use the generator, or keep the type:

```xml
  <assembly fullname="UnityEngine.CoreModule">
    <type fullname="UnityEngine.Vector2Int" preserve="all" />
  </assembly>
```

The attributes LsMsgPack reads from your classes (`[DataMember]`, `[EnumMember]`, `[IgnoreDataMember]`, `[XmlIgnore]`, `[DefaultValue]`, its own) are kept: their types are named in its code, which is what Unity's stripper looks for. Other libraries' attributes (System.Text.Json's, Json.NET's) are recognized by name, so keep their types in the `link.xml` when you use them at High stripping.
