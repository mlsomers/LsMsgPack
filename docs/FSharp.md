# F# types

F# records, options, unions and collections are written and read without attributes or setup, by both serializers (LsMsgPackDebug's `MsgPackSerializer` and LsMsgPack's `LsMsgPackSerializer`), with the same bytes. They are recognized by the names of FSharp.Core's types and of the `[CompilationMapping]` attribute the F# compiler puts on them, so LsMsgPack doesn't reference FSharp.Core.

| F# | Written as | Example (plain map layout) |
|---|---|---|
| Record, struct record, `[<CLIMutable>]` record | an object of its fields | `{ "Street": "Main", "Number": 5 }` |
| `'T option`, `'T voption` | the value, `None` as nil | `"annie"`, `nil` |
| Union case with fields | an object of its fields, the case name as the type id | `{ "": "Circle", "radius": 2.0 }` |
| Fieldless union case | its name | `"Empty"` |
| Union of fieldless cases (`Red \| Green \| Blue`) | the case's name | `"Blue"` |
| Union of one case with one field (`Email of string`) | the field | `"a@b.nl"` |
| Union of one case with several fields | an object of its fields, no type id | `{ "left": 1, "right": "x" }` |
| `list`, `Set` | an array | `[1, 2, 3]` |
| `Map` | a map | `{ "one": 1, "two": 2 }` |
| Tuples, struct tuples | an array of their items | `[7, "seven"]` |

The default layout writes objects as arrays and names through the [indexed schema](schema.md), as it does for C# classes: a `Circle 2.0` is then `{ "": 0, "@": [2.0] }` with a schema entry `Circle: ["radius"]`. A union is like a C# class hierarchy whose classes are its cases.

## Options

`Some x` is written as `x` and `None` as nil, so an `int option` property reads like a C# `int?`, and readers in other languages see the plain value. `ValueSome`/`ValueNone` are written the same way. These two cases can't be told apart on the wire and read back as `None`:

- `Some None` (an option of an option),
- `Some null` (an option of a class holding null).

System.Text.Json and MessagePack-CSharp's F# extensions behave the same way.

## Unions

```fsharp
type Shape =
  | Circle of radius: float
  | Rect of width: float * height: float
  | Empty
```

- **A case with fields** is an object of its fields with the case name as its type id, the way LsMsgPack writes a class of a hierarchy. Fields without a name are `Item`, or `Item1`, `Item2`... The computed members F# adds (`Tag`, `IsCircle`) are not written.
- **A fieldless case** is its name, as System.Text.Json writes it. It's read by its name, exactly or ignoring case. A name that isn't a fieldless case of the union is an error (a `MsgPackException`).
- **A union of one case with one field** is that field: `type Email = Email of string` is written as the string. A value object stays a plain value for readers in other languages, as with FSharp.SystemTextJson's default.
- **Case names only need to be unique within their union.** A type id is looked up among the cases of the declared union first, so `Ok` of one union and `Ok` of another are each read as their own case.
- The case of a union is part of its value, so its type id is written also with `AddTypeIdOption.Never` (plain MsgPack, `MsgPackMediaTypes.ToPlain`, `application/msgpack`), as System.Text.Json writes the `$type` of a union. With `AddTypeIdOption.FullName` the type id is the full name of the case's class.
- **Declared as `object`**, a case with fields is found by its name among the assemblies the reader knows (see [type ids](schema.md#type-ids); a name that several classes have is ambiguous, and the cases of a generic union aren't found). A fieldless case declared as `object` is read back as its name (a string), as an enum written by name is.

**Struct unions with fields in several cases** (`[<Struct>]` unions, and FSharp.Core's `Result<'T, 'TError>`) are not supported yet. Their cases share one type, so a type id has no class to name. Writing or reading one throws a `MsgPackException` that says so; nothing is written without its fields. Make the union a reference union, or register a [converter](Converters.md) for it. Struct unions of fieldless cases, and of one case, work.

### Compared with other libraries

| Library | `Circle 2.0` | `Empty` | `Some 5` |
|---|---|---|---|
| LsMsgPack (plain map layout) | `{ "": "Circle", "radius": 2.0 }` | `"Empty"` | `5` |
| System.Text.Json (.NET 11) | `{ "$type": "Circle", "radius": 2.0 }` | `"Empty"` | `5` |
| FSharp.SystemTextJson (default) | `{ "Case": "Circle", "Fields": [2.0] }` | `{ "Case": "Empty" }` | `5` |
| MessagePack-CSharp's MessagePack.FSharpExtensions | `[0, { "radius": 2.0 }]` | `[2, {}]` | `5` |

LsMsgPack's unions are not compatible with MessagePack.FSharpExtensions: it writes the tag number, which changes when the cases are reordered. A converter can write that format where a peer needs it.

## Source generator, trimming and NativeAOT

F# projects can't run C# source generators, so F# types are written and read by reflection, as System.Text.Json's F# support does. A C# project's [generated context](SourceGenerator.md) that reaches F# types describes them the way the runtime writes them, and leaves the options and unions to the runtime. F# types under NativeAOT are not supported.

## Schemas and TypeScript

[Schema version 2](schema.md#version-2-the-declared-types) describes options as their values, unions written as names or as their field as those, and the classes of the cases as entries of their own. [lsmsgpack-ts](TypeScriptModels.md) writes the TypeScript models as the server writes the values:

```ts
export class Holder {
  shapes: (Shape | 'Empty')[] | null = null;   // a union: its classes and the names of its fieldless cases
  colour: 'Red' | 'Green' | 'Blue' | null = null;
  maybe: Shape | 'Empty' | null = null;        // an option of it
}

export abstract class Shape {
  declare readonly $type: 'Circle' | 'Rect';
}
export class Circle extends Shape {
  declare readonly $type: 'Circle';
  radius: number = 0;
}
```

Point `--assembly` at the application's build output: an F# library's own `bin` folder doesn't have FSharp.Core.dll, and the types that use it can't be loaded from there.

The [TypeScript client](TypeScriptClient.md) finds the class of a type id by its name among all the models. Two unions with cases of the same name (`Ok`) get two TypeScript classes (`Ok`, `Ok_2`), and the client reads both as the one registered last: rename one of the cases when a TypeScript client reads both.
