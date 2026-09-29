# LsMsgPack
MsgPack debugging and validation tool also usable as Fiddler plugin

More info about this application (and screenshots) can be found at:
http://www.infotopie.nl/open-source/msgpack-explorer

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/yellow_img.png)](https://www.buymeacoffee.com/mlsomers)

[![.NET](https://github.com/mlsomers/LsMsgPack/actions/workflows/dotnet.yml/badge.svg)](https://github.com/mlsomers/LsMsgPack/actions/workflows/dotnet.yml)

Library Usage Example
---------------------
Although the original was optimised for debugging and analysing, some compiler directives have been added to exclude keeping track of all offsets and other overhead needed for debugging. It has been expanded to support serialization and deserialization of .Net classes (using the properties) similar to other xml and json serializers.

Add LsMsgPackL.dll as a reference.

```csharp
public class MyClass
{
    public string Name { get; set; }
    public int Quantity { get; set; }
    public List<object> Anything { get; set; }
}

public void Test()
{
    MyClass message = new MyClass()
    {
        Name = "Test message",
        Quantity = 100,
        Anything = new List<object>(new object[] { "first", 2, false, null, 4.2d, "last" })
    };
    
    // Serialize
    byte[] buffer = MsgPackSerializer.Serialize(message);
    
    // Deserialize
    MyClass returnMsg = MsgPackSerializer.Deserialize<MyClass>(buffer);
}
```

Compatibility with other implementations
----------------------------------------
Serializing classes by creating name-value dictionaries of their properties is not an official standard, and to my surprise I found than a many MsgPack implementations do not. Some just string a list of values into an array. This is indeed efficient and will work well for the first version, however migrating to a new version may pose some compatibility challenges when introducing new properties over time.

For this reason I have submitted a [pull request]( https://github.com/msgpack/msgpack/pull/334/commits/c6a4935b9e0e38818cc1ef878db72621143bfcd7) to the official MsgPack specification, including a more standardized choice of solutions and in addition a standard way to support polymorphic class-hierarchies.

Which settings to use to exchange data with MessagePack-CSharp, Nerdbank.MessagePack, Python, JavaScript and others is described in [docs/Compatibility.md](docs/Compatibility.md).

While using dictionaries diminishes the small size of a MsgPack message, it does help bring up the compatibility level with other serializers (XML / JSON) so that it can be used as a drop-in replacement.

Polymorphic class-hierarchy support
-----------------------------------

One of my frustrations with other serializers is that they do not handle class-hierarchies very well. For example, the `System.Xml.Serialization` classes had a solution where you could add `XmlInclude` attributes to a base class or alternatively add `XmlArrayItem` attributes to a property holding a list of derived classes. In this case one would have to add an attribute for each and every possible derived type (and not forget when adding new types). Other serializers had other solutions but almost always needed extra coding. I decided to go an extra mile and add basic support for class hierarchies out of the box.

So if you have an interface IPet with classes Cat, Dog and Fish that implement IPet. You can have a class containing an array (or other collection) of pets and have it serialize and deserialize correctly without adding any extra code.

How the type is found again: a value whose type differs from the declared type gets a type id, the short name of its type by default (`Cat`). When reading, the name is looked up in this order:

1. Names resolved before (the fastest), and types that `Type.GetType` finds by themselves.
2. The assembly of the declared type (and of its generic arguments): all its types are cached by name the first time. With the indexed schema (the default) the assembly of the type you deserialize is cached as well.
3. The names of all types in the assemblies cached so far.

So the implementations of `IPet` are found without any registration when they are in the assembly of `IPet` (declared as `IPet`, `List<IPet>`, `IPet[]`...), or in the assembly of the class you deserialize. Register an assembly yourself when that is not the case, once, before deserializing:

```csharp
// The property is declared as object, so the reader has no assembly to look in:
public IEnumerable<object> Pets { get; set; } = new HashSet<IPet> { new Cat(), new Dog() };

MsgPackSerializer.CacheAssemblyTypes(typeof(IPet));    // LsMsgPack
LtMsgPackSerializer.CacheAssemblyTypes(typeof(IPet));  // LtMsgPack (the same cache, either call will do)
```

The same applies when implementations of `IPet` live in other assemblies than `IPet` itself (e.g. plugins): register each of them.

- **Short names must be unique** among the cached assemblies. Two cached classes called `Cat` (in different namespaces) make reading throw "Type assignment dilamma". Use `AddTypeIdOptions = AddTypeIdOption.FullName` (bigger payloads) or your own `IMsgPackTypeResolver` in `TypeResolvers`.
- **Let it search**: `TypeResolvers = new IMsgPackTypeResolver[] { new WildGooseChaseResolver() }` searches all assemblies loaded in the AppDomain for a name it cannot find otherwise (and caches the assemblies it searched). Convenient, but slower the first time and it keeps more names in memory.
- **Your own mapping**: implement `IMsgPackTypeResolver` to choose the ids and the types (e.g. a fixed table of names, or `XmlRootAttributeTypeResolver` to use the names of `[XmlRoot]`).

Fiddler Integration
-------------------

In order to use this tool as a Fiddler plugin, copy the following files to the Fiddler Inspectors directory (usually C:\Program Files\Fiddler2\Inspectors):

- MsgPackExplorer.exe
- LsMsgPackFiddlerInspector.dll
- LsMsgPack.dll

Restart fiddler and you should see a MsgPack option in the Inspectors list.

Visual Studio Integration
-------------------------

The tool can also be used as a debugging Visualizer in Visual Studio. It can be installed via the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=mlsomers.V2025102900).

Source documentation
--------------------

### Modules

#### LsMsgPack.dll
This module contains the "parser" and generator of MsgPack Packages. It breaks down the binary file into a hirarchical structure, keeping track of offsets and errors. And it can also be used to generate MsgPack files.

#### MsgPackExplorer.exe
The main winforms executable, containing a MsgPackExplorer UserControl (so it can easily be integrated into other tools such as Fiddler).

#### LsMsgPackFiddlerInspector.dll
A tiny wrapper enabling the use of MsgPack Explorer as a Fiddler Inspector.

#### LsMsgPackUnitTests.dll
Some unit tests on the core LsMsgPack.dll. No full coverage yet, but at least it's a start.

#### LsMsgPackNetStandard.dll & LsMsgPackNetStandardUnitTests.dll

A light version of the serializer. The parsing and generating methods are almost identical to the LsMsgPack lib, but with allot of overhead removed that comes with keeping track of offsets, original types and other debugging info. I'm planning to use this version in my projects that use the MsgPack format.

### Architecture

#### Object-model

![Hierarchy](https://github.com/mlsomers/LsMsgPack/blob/master/Hierarchy.png)

Each class can serialize/deserialize the associated MsgPack type. Types that have a variable length inherit from MsgPackVarLen.

#### Worker classes (or services)

![Hierarchy](https://github.com/mlsomers/LsMsgPack/blob/master/Services.png)

The MsgPackSerializer and MsgPackSettings are the ones that end-users are supposed to use (entry points).
