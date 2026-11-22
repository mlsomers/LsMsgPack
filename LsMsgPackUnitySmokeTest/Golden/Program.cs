using System;
using System.IO;
using LsMsgPackUnitySmoke;

string path = args.Length > 0 ? args[0] : "golden.txt";
File.WriteAllText(path, SmokeChecks.Golden(Console.WriteLine));
int failures = SmokeChecks.Check(File.ReadAllText(path), Console.WriteLine); // the same runtime reads its own data back
Console.WriteLine(failures == 0 ? "Written " + path : $"FAILED: {failures}");
return failures;
