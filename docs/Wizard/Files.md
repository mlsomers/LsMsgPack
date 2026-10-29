[Setup wizard](README.md) › Saved

# Question 2: who will read these files?

A file written by your .NET app can be read back by your app, or by something else entirely. This decides the *format* of the file, so it's the most important choice for storage.

LsMsgPack's default format is made for .NET readers: it starts with a small schema (the property names of each class, written once), followed by the values. That makes files small and keeps them readable when your classes change, and it supports class hierarchies (a list of shapes holding circles and squares) without any attributes. But a general MsgPack library in Python or Go doesn't know about that schema: it would only see the schema and stop.

### Only my own .NET application (or other .NET apps using these packages)

The files are written and read by your program, maybe by several versions of it, maybe by a companion tool you also write in .NET with LtMsgPack or LsMsgPack.

Examples:
- Documents of a desktop app (WinForms, WPF, MAUI, Avalonia).
- Save games, settings snapshots, undo history.
- Cached results of a web service, written and read by the same service.

**→ [Only my own app reads them](FilesOwnApp.md)**

### Other programs too

Something you don't control, or something in another language, also opens the files.

Examples:
- A Python data-science script reads the exported measurements.
- A web page (JavaScript) shows a file the user uploads.
- A partner's .NET program reads them with MessagePack-CSharp.

**→ [Other programs read them too](FilesOtherPrograms.md)**

### Not sure?

If there's a chance that "someone might want to read these in Python one day", you can still start with *only my own app*: the format can be converted later by reading the files with the old settings and writing them with new ones. Choosing *other programs* now costs some size and loses automatic support for class hierarchies.
