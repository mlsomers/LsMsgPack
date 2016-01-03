using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("MsgPackExplorer")]
[assembly: AssemblyDescription(@"Explorer and debugger for MsgPack formatted files.

Released under the Apache License Version 2.0.
The source can be found at:
https://github.com/mlsomers/LsMsgPack

More info about this application can be found at:
http://www.infotopie.nl/open-source/msgpack-explorer

-------------------
Fiddler Integration
-------------------

In order to use this tool as a Fiddler plugin, copy the following files to the Fiddler Inspectors directory (usually C:\Program Files\Fiddler2\Inspectors):

MsgPackExplorer.exe
LsMsgPackFiddlerInspector.dll
LsMsgPack.dll

Restart fiddler and you should see a MsgPack option in the Inspectors list.
")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: InternalsVisibleTo("LsMsgPackFiddlerInspector")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("a43ed63b-ca9c-4fdf-b02b-8e7f958903a2")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers 
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
