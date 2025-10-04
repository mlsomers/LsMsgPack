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
Serializing classes by creating name-value dictionaries of their properties is not an official standard, and to my surprise I found than a majority of MsgPack implementations do not, instead they simply string up a list of values. This is indeed efficient and will work well for the first version, however migrating to a new version may pose some compatibility challenges when introducing new properties over time.

For this reason I have submitted a [pull request]( https://github.com/msgpack/msgpack/pull/334/commits/c6a4935b9e0e38818cc1ef878db72621143bfcd7) to the official MsgPack specification, including a more standardized choice of solutions and in addition a standard way to support polymorphic class-hierarchies.

While using dictionaries diminishes the small size of a MsgPack message, it does help bring up the compatibility level with other serializers (XML / JSON) so that it can be used as a drop-in replacement. I hope to deal with part of the problem later by adding a schema, but let’s first just bring it up to speed with other serializers for now.

Polymorphic class-hierarchy support
-----------------------------------

One of my frustrations with other serializers is that they do not handle class-hierarchies very well. For example, the `System.Xml.Serialization` classes had a solution where you could add `XmlInclude` attributes to a base class or alternatively add `XmlArrayItem` attributes to a property holding a list of derived classes. In this case one would have to add an attribute for each and every possible derived type (and not forget when adding new types). Other serializers had other solutions but almost always needed extra coding. I decided to go an extra mile and add basic support for class hierarchies out of the box.

So if you have an interface IPet with classes Cat, Dog and Fish that implement IPet. You can have a class containing an array (or other collection) of pets and have it serialize and deserialize correctly without adding any extra code.

There are limits and edge cases to this rule though. For example:
```csharp
public IEnumerable<object> Pets { get; set; } = new HashSet<IPet> { new Cat(), new Dog() };
```

May require you to call `MsgPackSerializer.CacheAssemblyTypes(typeof(IPet));` once somewhere before trying to deserialize it. If the property is changed to `IEnumerable<IPet>` instead of `IEnumerable<object>` it will work without preregistering because the deserializer will know how to find the assembly where IPet is defined. Pre-caching may also be required when not all derived IPet implementations are in the same assembly as the IPet interface. There are multiple tier caches; the fastest is the “used names” cache where only previously used types are cached for fast lookup the next time the type is encountered. A 2nd tier has names of all types in cached assemblies. Not all assemblies will be cached by default (waste of memory) but you can opt into caching all loaded assemblies by using an included `WildGooseChaseResolver` which will scan all loaded assemblies when looking for a type (and cache the searched assemblies until it finds the wanted type).


Fiddler Integration
-------------------

In order to use this tool as a Fiddler plugin, copy the following files to the Fiddler Inspectors directory (usually C:\Program Files\Fiddler2\Inspectors):

- MsgPackExplorer.exe
- LsMsgPackFiddlerInspector.dll
- LsMsgPack.dll

Restart fiddler and you should see a MsgPack option in the Inspectors list.

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
