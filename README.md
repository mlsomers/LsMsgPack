# LsMsgPack
MsgPack debugging and validation tool also usable as Fiddler plugin

More info about this application (and screenshots) can be found at:
http://www.infotopie.nl/open-source/msgpack-explorer

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

#### LsMsgPackL.dll & LightUnitTests.dll
A light version of the "parser". The parsing and generating methods are almost identical to the LsMsgPack lib, but with allot of overhead removed that comes with keeping track of offsets, original types and other debugging info. I'm planning to use this version in my projects that use the MsgPack format.
The LightUnitTests are the same as LsMsgPackUnitTests with some tests omitted (preserving original types is not needed for non-debugging purposes).
