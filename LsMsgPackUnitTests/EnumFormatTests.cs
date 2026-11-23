using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  #region Model

  public enum EnColour { Red, Green, Blue }

  [MsgPackEnumFormat(EnumFormat.Name)]
  public enum EnNamed { First, Second, Third }

  [Flags]
  public enum EnFlags : byte { None = 0, A = 1, B = 2, C = 4, AC = A | C }

  public enum EnCustom
  {
    None,
    [EnumMember(Value = "first-one")] First,
    [JsonStringEnumMemberName("second-one")] Second,
    Third
  }

  public enum EnSwapped { [EnumMember(Value = "Y")] X, [EnumMember(Value = "X")] Y } // written names win over .NET names when reading

  public enum EnClash { A, [EnumMember(Value = "A")] B } // two values written as "A": refused

  public enum EnSigned : sbyte { Minus = -1, Zero = 0, Plus = 1 }

  public enum EnWide : long { Smallest = long.MinValue, Largest = long.MaxValue }

  public enum EnAliased { One = 1, Uno = 1, Two = 2 }

  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum EnStj { Left, Right }

  [JsonConverter(typeof(JsonStringEnumConverter<EnStjGeneric>))]
  public enum EnStjGeneric { Up, Down }

  [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
  public enum EnNewtonsoft { North, South }

  public class EnDerivedStringConverter : Newtonsoft.Json.Converters.StringEnumConverter { }

  [Newtonsoft.Json.JsonConverter(typeof(EnDerivedStringConverter))]
  public enum EnNewtonsoftDerived { East, West }

  [MsgPack.Serialization.MessagePackEnum] // MsgPack.Cli's default: by name
  public enum EnCli { Alpha, Beta }

  [MsgPack.Serialization.MessagePackEnum(SerializationMethod = MsgPack.Serialization.EnumSerializationMethod.ByUnderlyingValue)]
  public enum EnCliNumbers { Alpha, Beta }

  public class EnHolder
  {
    public EnColour Colour { get; set; }
    public EnColour? Maybe { get; set; }
    public EnNamed Named { get; set; }
    public EnNamed? MaybeNamed { get; set; }
    [MsgPackEnumFormat(EnumFormat.Number)] public EnNamed NamedAsNumber { get; set; }
    [MsgPackEnumFormat(EnumFormat.Name)] public EnColour ColourByName { get; set; }
    [MsgPackEnumFormat(EnumFormat.Name)] public EnColour? MaybeByName { get; set; }
    public List<EnNamed> NamedList { get; set; }
    public EnColour[] Colours { get; set; }
    public Dictionary<EnNamed, int> NamedKeys { get; set; }
    public EnFlags Flags { get; set; }
    public EnCustom Custom { get; set; }
    public EnSigned Signed { get; set; }
    public EnWide Wide { get; set; }
    public object Boxed { get; set; }
  }

  public class EnForeign
  {
    public EnStj Stj { get; set; }
    public EnStjGeneric StjGeneric { get; set; }
    public EnNewtonsoft Newtonsoft { get; set; }
    public EnNewtonsoftDerived NewtonsoftDerived { get; set; }
    public EnCli Cli { get; set; }
    public EnCliNumbers CliNumbers { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public EnColour StjMember { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))] public EnColour NewtonsoftMember { get; set; }
    [JsonConverter(typeof(JsonNumberEnumConverter<EnStj>))] public EnStj StjAsNumber { get; set; }
    [MsgPack.Serialization.MessagePackEnumMember(SerializationMethod = MsgPack.Serialization.EnumMemberSerializationMethod.ByName)] public EnColour CliMember { get; set; }
    [MsgPack.Serialization.MessagePackEnumMember] public EnCli CliMemberDefault { get; set; } // Default: the enum decides
  }

  /// <summary>
  /// The members of EnHolder as written, read untyped (maps of names without the schema).
  /// </summary>
  public class EnWritten
  {
    public object Colour { get; set; }
    public object Maybe { get; set; }
    public object Named { get; set; }
    public object MaybeNamed { get; set; }
    public object NamedAsNumber { get; set; }
    public object ColourByName { get; set; }
    public object MaybeByName { get; set; }
    public List<object> NamedList { get; set; }
    public List<object> Colours { get; set; }
    public Dictionary<object, object> NamedKeys { get; set; }
    public object Flags { get; set; }
    public object Custom { get; set; }
    public object Signed { get; set; }
    public object Wide { get; set; }
  }

  public class EnStrings
  {
    public string Colour { get; set; }
    public string Named { get; set; }
    public string Flags { get; set; }
    public string Custom { get; set; }
    public string Signed { get; set; }
  }

  public class EnRead
  {
    public EnColour Colour { get; set; }
    public EnNamed Named { get; set; }
    public EnFlags Flags { get; set; }
    public EnCustom Custom { get; set; }
    public EnSigned Signed { get; set; }
  }

  public class EnWithDefault
  {
    [DefaultValue(EnNamed.Second)] public EnNamed Named { get; set; } = EnNamed.Second;
    [DefaultValue(EnColour.Blue), MsgPackEnumFormat(EnumFormat.Name)] public EnColour Colour { get; set; } = EnColour.Blue;
  }

  public struct EnStruct
  {
    public EnColour Plain { get; set; }
    [MsgPackEnumFormat(EnumFormat.Name)] public EnColour ByName { get; set; }
    public EnNamed Named { get; set; }
  }

  public class EnFields // [JsonInclude]: public fields are serialized when an attribute opts them in (SerializedFields.Attributed, the default)
  {
    [JsonInclude] public EnColour Plain;
    [JsonInclude, MsgPackEnumFormat(EnumFormat.Name)] public EnColour ByName;
    [JsonInclude, MsgPackEnumFormat(EnumFormat.Number)] public EnNamed NamedAsNumber;
  }

  #endregion

  /// <summary>
  /// Enums by number or name (docs/Enums.md): MsgPackOptions.EnumFormat, [MsgPackEnumFormat] on enums and members, the attributes of other serializers, custom names; readers take both forms.
  /// </summary>
  public abstract class EnumFormatTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    private static readonly MsgPackSettings Untyped = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map };

    protected static EnHolder Sample()
    {
      return new EnHolder()
      {
        Colour = EnColour.Green,
        Maybe = EnColour.Blue,
        Named = EnNamed.Second,
        MaybeNamed = EnNamed.Third,
        NamedAsNumber = EnNamed.Third,
        ColourByName = EnColour.Blue,
        MaybeByName = EnColour.Red,
        NamedList = new List<EnNamed>() { EnNamed.Third, EnNamed.First },
        Colours = new EnColour[] { EnColour.Red, EnColour.Blue },
        NamedKeys = new Dictionary<EnNamed, int>() { { EnNamed.First, 1 }, { EnNamed.Third, 3 } },
        Flags = EnFlags.A | EnFlags.B,
        Custom = EnCustom.Second,
        Signed = EnSigned.Minus,
        Wide = EnWide.Smallest,
        Boxed = EnNamed.Second
      };
    }

    protected static IEnumerable<MsgPackSettings> AllSettings(EnumFormat format)
    {
      foreach (bool schema in new bool[] { false, true })
      {
        foreach (ObjectLayout layout in new ObjectLayout[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          yield return new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = layout, EnumFormat = format };
          yield return new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = layout, EnumFormat = format, SchemaVersion = SchemaVersion.Described };
        }
      }
    }

    private static void AssertSame(EnHolder expected, EnHolder actual, string message)
    {
      Assert.AreEqual(expected.Colour, actual.Colour, message);
      Assert.AreEqual(expected.Maybe, actual.Maybe, message);
      Assert.AreEqual(expected.Named, actual.Named, message);
      Assert.AreEqual(expected.MaybeNamed, actual.MaybeNamed, message);
      Assert.AreEqual(expected.NamedAsNumber, actual.NamedAsNumber, message);
      Assert.AreEqual(expected.ColourByName, actual.ColourByName, message);
      Assert.AreEqual(expected.MaybeByName, actual.MaybeByName, message);
      CollectionAssert.AreEqual(expected.NamedList, actual.NamedList, message);
      CollectionAssert.AreEqual(expected.Colours, actual.Colours, message);
      CollectionAssert.AreEquivalent(expected.NamedKeys.ToList(), actual.NamedKeys.ToList(), message);
      Assert.AreEqual(expected.Flags, actual.Flags, message);
      Assert.AreEqual(expected.Custom, actual.Custom, message);
      Assert.AreEqual(expected.Signed, actual.Signed, message);
      Assert.AreEqual(expected.Wide, actual.Wide, message);
      Assert.AreEqual(expected.Boxed, actual.Boxed, message);
    }

    [TestMethod]
    [DataRow(EnumFormat.Number)]
    [DataRow(EnumFormat.Name)]
    public void RoundTrip_AllSettings(EnumFormat format)
    {
      EnHolder value = Sample();
      foreach (MsgPackSettings settings in AllSettings(format))
      {
        string message = $"{format}, schema {settings.UseIndexedSchema}, {settings.ObjectLayout}, {settings.SchemaVersion}";
        AssertSame(value, Serializer.Deserialize<EnHolder>(Serializer.Serialize(value, settings), settings), message);
      }
    }

    [TestMethod]
    public void Default_NumbersUnlessTheEnumOrMemberAsks()
    {
      EnWritten written = Serializer.Deserialize<EnWritten>(Serializer.Serialize(Sample(), Untyped), Untyped);
      Assert.AreEqual(1L, Convert.ToInt64(written.Colour));
      Assert.AreEqual(2L, Convert.ToInt64(written.Maybe));
      Assert.AreEqual("Second", written.Named, "the enum's attribute");
      Assert.AreEqual("Third", written.MaybeNamed);
      Assert.AreEqual(2L, Convert.ToInt64(written.NamedAsNumber), "the member's attribute wins over the enum's");
      Assert.AreEqual("Blue", written.ColourByName, "the member's attribute wins over the options");
      Assert.AreEqual("Red", written.MaybeByName);
      CollectionAssert.AreEqual(new object[] { "Third", "First" }, written.NamedList, "elements in the enum's format");
      CollectionAssert.AreEqual(new long[] { 0, 2 }, written.Colours.Select(c => Convert.ToInt64(c)).ToList());
      CollectionAssert.AreEquivalent(new object[] { "First", "Third" }, written.NamedKeys.Keys.ToList(), "dictionary keys too");
      Assert.AreEqual(3L, Convert.ToInt64(written.Flags));
      Assert.AreEqual(-1L, Convert.ToInt64(written.Signed));
    }

    [TestMethod]
    public void NameOption_NamesCustomNamesFlagsAndNumbers()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Name };
      EnHolder value = Sample();
      EnWritten written = Serializer.Deserialize<EnWritten>(Serializer.Serialize(value, settings), Untyped);
      Assert.AreEqual("Green", written.Colour);
      Assert.AreEqual("Blue", written.Maybe);
      Assert.AreEqual(2L, Convert.ToInt64(written.NamedAsNumber), "the member's Number wins over the options");
      CollectionAssert.AreEqual(new object[] { "Red", "Blue" }, written.Colours);
      Assert.AreEqual("A, B", written.Flags, "a combination of flags, as Enum.ToString");
      Assert.AreEqual("second-one", written.Custom, "[JsonStringEnumMemberName]");
      Assert.AreEqual("Minus", written.Signed);
      Assert.AreEqual("Smallest", written.Wide);

      value.Flags = EnFlags.A | EnFlags.C;
      value.Custom = EnCustom.First;
      value.Colour = (EnColour)7;
      value.Signed = (EnSigned)(-5);
      written = Serializer.Deserialize<EnWritten>(Serializer.Serialize(value, settings), Untyped);
      Assert.AreEqual("AC", written.Flags, "a combination with a name of its own");
      Assert.AreEqual("first-one", written.Custom, "[EnumMember(Value)]");
      Assert.AreEqual(7L, Convert.ToInt64(written.Colour), "a value without a name: its number");
      Assert.AreEqual(-5L, Convert.ToInt64(written.Signed));

      value.Flags = (EnFlags)(1 | 8);
      written = Serializer.Deserialize<EnWritten>(Serializer.Serialize(value, settings), Untyped);
      Assert.AreEqual(9L, Convert.ToInt64(written.Flags), "bits no name covers: the number");
      EnHolder back = Serializer.Deserialize<EnHolder>(Serializer.Serialize(value, settings), settings);
      Assert.AreEqual((EnFlags)9, back.Flags);
      Assert.AreEqual((EnColour)7, back.Colour);
      Assert.AreEqual((EnSigned)(-5), back.Signed);
    }

    [TestMethod]
    public void CustomNames_SwappedAndClashing()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, EnumFormat = EnumFormat.Name };
      Assert.AreEqual("Y", Serializer.Deserialize<string>(Serializer.Serialize(EnSwapped.X, settings), settings));
      Assert.AreEqual(EnSwapped.X, Serializer.Deserialize<EnSwapped>(Serializer.Serialize(EnSwapped.X, settings), settings));
      Assert.AreEqual(EnSwapped.Y, Serializer.Deserialize<EnSwapped>(Serializer.Serialize("X", settings), settings), "the written name, not the .NET name");
      MsgPackException clash = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(EnClash.B, settings));
      StringAssert.Contains(clash.Message, "\"A\"");
      Assert.AreEqual(EnClash.B, Serializer.Deserialize<EnClash>(Serializer.Serialize(EnClash.B, new MsgPackSettings() { UseIndexedSchema = false }), settings), "numbers still read");
    }

    [TestMethod]
    public void Aliases_TheFirstDeclaredName()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, EnumFormat = EnumFormat.Name };
      Assert.AreEqual("One", Serializer.Deserialize<string>(Serializer.Serialize(EnAliased.Uno, settings), settings));
      Assert.AreEqual(EnAliased.One, Serializer.Deserialize<EnAliased>(Serializer.Serialize("Uno", settings), settings));
    }

    [TestMethod]
    [DataRow(EnumFormat.Number)]
    [DataRow(EnumFormat.Name)]
    public void Reading_NumbersAndNamesWhateverTheOptions(EnumFormat format)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = format };
      EnStrings[] texts = new EnStrings[]
      {
        new EnStrings() { Colour = "Blue", Named = "Third", Flags = "A, B", Custom = "second-one", Signed = "Minus" },
        new EnStrings() { Colour = "blue", Named = "THIRD", Flags = "b,a", Custom = "Second", Signed = "-1" }, // ignoring case, .NET names, numbers as text
        new EnStrings() { Colour = "2", Named = "2", Flags = "3", Custom = "SECOND-ONE", Signed = " Minus " },
      };
      for (int t = 0; t < texts.Length; t++)
      {
        EnRead read = Serializer.Deserialize<EnRead>(Serializer.Serialize(texts[t], settings), settings);
        Assert.AreEqual(EnColour.Blue, read.Colour, "row " + t);
        Assert.AreEqual(EnNamed.Third, read.Named, "row " + t);
        Assert.AreEqual(EnFlags.A | EnFlags.B, read.Flags, "row " + t);
        Assert.AreEqual(EnCustom.Second, read.Custom, "row " + t);
        Assert.AreEqual(EnSigned.Minus, read.Signed, "row " + t);
      }

      EnHolder numbers = Serializer.Deserialize<EnHolder>(Serializer.Serialize(Sample(), new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Number }), settings);
      AssertSame(Sample(), numbers, "numbers read");
      EnHolder names = Serializer.Deserialize<EnHolder>(Serializer.Serialize(Sample(), new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Name }), settings);
      AssertSame(Sample(), names, "names read");
    }

    [TestMethod]
    public void UnknownName_IsAnError()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map };
      byte[] data = Serializer.Serialize(new EnStrings() { Colour = "Purple", Named = "Second" }, settings);
      Exception error = Assert.ThrowsExactly<ArgumentException>(() => Serializer.Deserialize<EnRead>(data, settings));
      StringAssert.Contains(error.Message, "Purple");

      MsgPackSettings reporting = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, ReadErrors = ReadErrorHandling.ReportAndContinue };
      EnRead read = Serializer.Deserialize<EnRead>(data, reporting, out ReadDifferences differences);
      Assert.AreEqual(EnColour.Red, read.Colour, "skipped");
      Assert.AreEqual(EnNamed.Second, read.Named);
      Assert.IsTrue(differences.Differences.Any(d => d.Kind == DifferenceKind.InvalidValue && d.Name == nameof(EnRead.Colour)), differences.GenerateReport());
    }

    [TestMethod]
    public void OtherSerializersAttributes()
    {
      EnForeign value = new EnForeign()
      {
        Stj = EnStj.Right, StjGeneric = EnStjGeneric.Down, Newtonsoft = EnNewtonsoft.South, NewtonsoftDerived = EnNewtonsoftDerived.West, Cli = EnCli.Beta, CliNumbers = EnCliNumbers.Beta,
        StjMember = EnColour.Blue, NewtonsoftMember = EnColour.Green, StjAsNumber = EnStj.Right, CliMember = EnColour.Blue, CliMemberDefault = EnCli.Beta
      };
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Number };
      byte[] data = Serializer.Serialize(value, settings);
      Dictionary<string, object> written = Serializer.Deserialize<Dictionary<string, object>>(data, Untyped);
      Assert.AreEqual("Right", written[nameof(EnForeign.Stj)]);
      Assert.AreEqual("Down", written[nameof(EnForeign.StjGeneric)]);
      Assert.AreEqual("South", written[nameof(EnForeign.Newtonsoft)]);
      Assert.AreEqual("West", written[nameof(EnForeign.NewtonsoftDerived)], "a converter deriving from StringEnumConverter");
      Assert.AreEqual("Beta", written[nameof(EnForeign.Cli)]);
      Assert.AreEqual(1L, Convert.ToInt64(written[nameof(EnForeign.CliNumbers)]));
      Assert.AreEqual("Blue", written[nameof(EnForeign.StjMember)]);
      Assert.AreEqual("Green", written[nameof(EnForeign.NewtonsoftMember)]);
      Assert.AreEqual(1L, Convert.ToInt64(written[nameof(EnForeign.StjAsNumber)]), "JsonNumberEnumConverter on the member wins over the enum's JsonStringEnumConverter");
      Assert.AreEqual("Blue", written[nameof(EnForeign.CliMember)]);
      Assert.AreEqual("Beta", written[nameof(EnForeign.CliMemberDefault)]);

      EnForeign back = Serializer.Deserialize<EnForeign>(data, settings);
      Assert.AreEqual(EnStj.Right, back.Stj);
      Assert.AreEqual(EnNewtonsoftDerived.West, back.NewtonsoftDerived);
      Assert.AreEqual(EnStj.Right, back.StjAsNumber);
      Assert.AreEqual(EnCli.Beta, back.CliMemberDefault);
    }

    [TestMethod]
    public void DefaultValues_LeftOutAndReadBack()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = true, ObjectLayout = ObjectLayout.Array, DynamicFilters = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeDynamically[] { new LsMsgPack.TypeResolving.Filters.FilterDefaultValues() }, SchemaVersion = SchemaVersion.Described };
      EnWithDefault read = Serializer.Deserialize<EnWithDefault>(Serializer.Serialize(new EnWithDefault(), settings), settings);
      Assert.AreEqual(EnNamed.Second, read.Named);
      Assert.AreEqual(EnColour.Blue, read.Colour);
      read = Serializer.Deserialize<EnWithDefault>(Serializer.Serialize(new EnWithDefault() { Named = EnNamed.First, Colour = EnColour.Red }, settings), settings);
      Assert.AreEqual(EnNamed.First, read.Named);
      Assert.AreEqual(EnColour.Red, read.Colour);
    }

    [TestMethod]
    public void StructsAndFields_TheMembersFormat()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map };
      Dictionary<string, object> written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(new EnStruct() { Plain = EnColour.Blue, ByName = EnColour.Blue, Named = EnNamed.Third }, settings), Untyped);
      Assert.AreEqual(2L, Convert.ToInt64(written["Plain"]));
      Assert.AreEqual("Blue", written["ByName"]);
      Assert.AreEqual("Third", written["Named"]);
      EnStruct back = Serializer.Deserialize<EnStruct>(Serializer.Serialize(new EnStruct() { Plain = EnColour.Blue, ByName = EnColour.Green, Named = EnNamed.Third }, settings), settings);
      Assert.AreEqual(EnColour.Green, back.ByName);
      Assert.AreEqual(EnNamed.Third, back.Named);

      written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(new EnFields() { Plain = EnColour.Blue, ByName = EnColour.Blue, NamedAsNumber = EnNamed.Third }, settings), Untyped);
      Assert.AreEqual(2L, Convert.ToInt64(written["Plain"]));
      Assert.AreEqual("Blue", written["ByName"]);
      Assert.AreEqual(2L, Convert.ToInt64(written["NamedAsNumber"]));
    }

    [TestMethod]
    public void RootAndBoxedValues()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, EnumFormat = EnumFormat.Name };
      Assert.AreEqual("Green", Serializer.Deserialize<string>(Serializer.Serialize(EnColour.Green, settings), settings));
      Assert.AreEqual("Second", Serializer.Deserialize<string>(Serializer.Serialize(EnNamed.Second, new MsgPackSettings() { UseIndexedSchema = false }), settings));
      Assert.AreEqual(EnColour.Green, Serializer.Deserialize<EnColour>(Serializer.Serialize("green", settings), settings));
      Assert.AreEqual(EnColour.Blue, Serializer.Deserialize<EnColour?>(Serializer.Serialize(EnColour.Blue, settings), settings));
      List<object> boxed = new List<object>() { EnColour.Blue, EnNamed.Third };
      List<object> back = Serializer.Deserialize<List<object>>(Serializer.Serialize(boxed, settings), settings);
      CollectionAssert.AreEqual(boxed, back, "with type ids");
    }
  }

  [TestClass]
  public class DebugEnumFormatTests : EnumFormatTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsEnumFormatTests : EnumFormatTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    /// <summary>
    /// The same bytes as LsMsgPackDebug, also with the compiled writers and readers from the first object and with the generated code.
    /// </summary>
    [TestMethod]
    [DataRow(EnumFormat.Number)]
    [DataRow(EnumFormat.Name)]
    public void SameBytesAsDebug_RuntimeCompiledGenerated(EnumFormat format)
    {
      object[] values = new object[]
      {
        null, new EnForeign(), new EnWithDefault(), new EnStruct() { ByName = EnColour.Blue, Named = EnNamed.Third }, new EnFields() { ByName = EnColour.Green },
        new List<EnHolder>() { new EnHolder(), new EnHolder() { Colour = EnColour.Blue, Flags = EnFlags.AC, Custom = EnCustom.First } },
        EnColour.Blue, (EnColour?)EnColour.Red, new EnColour[] { EnColour.Red, (EnColour)9 }
      };
      values[0] = Sample();
      foreach (MsgPackSettings settings in AllSettings(format))
      {
        foreach (bool filter in new bool[] { false, true })
        {
          if (filter)
            settings.DynamicFilters = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeDynamically[] { new LsMsgPack.TypeResolving.Filters.FilterDefaultValues() };
          for (int t = 0; t < values.Length; t++)
          {
            object value = values[t];
            string message = $"{format}, schema {settings.UseIndexedSchema}, {settings.ObjectLayout}, {settings.SchemaVersion}, filter {filter}, {value.GetType().Name}";
            byte[] expected = MsgPackSerializer.Serialize(value, value.GetType(), settings);
            CollectionAssert.AreEqual(expected, Serializers.Ls.Serialize(value, value.GetType(), settings), "runtime: " + message);
            CollectionAssert.AreEqual(expected, Serializers.Gen.Serialize(value, value.GetType(), settings), "generated: " + message);
            foreach (bool generated in new bool[] { false, true })
            {
              LsMsgPackOptions compiled = new LsMsgPackOptions()
              {
                UseIndexedSchema = settings.UseIndexedSchema, ObjectLayout = settings.ObjectLayout, EnumFormat = format, SchemaVersion = settings.SchemaVersion, DynamicFilters = settings.DynamicFilters,
                CompileWritersAfter = 0, CompileReadersAfter = 0, UseRegisteredContexts = false
              };
              if (generated)
                compiled.Contexts = new LsMsgPackContext[] { TestMsgPackContext.Default };
              LsMsgPackSerializer serializer = new LsMsgPackSerializer(compiled);
              for (int round = 0; round < 3; round++)
              {
                byte[] bytes = serializer.Serialize(value, value.GetType());
                CollectionAssert.AreEqual(expected, bytes, $"compiled ({(generated ? "generated" : "runtime")}), round {round}: " + message);
                object back = serializer.Deserialize(value.GetType(), bytes);
                CollectionAssert.AreEqual(expected, MsgPackSerializer.Serialize(back, value.GetType(), settings), $"read back compiled, round {round}: " + message);
              }
            }
          }
        }
      }
    }
  }

  [TestClass]
  public class GenEnumFormatTests : EnumFormatTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}

namespace MsgPack.Serialization
{
  // MsgPack.Cli's attributes, recognized by their full names (the package is not referenced)
  public enum EnumSerializationMethod { ByName = 0, ByUnderlyingValue }
  public enum EnumMemberSerializationMethod { Default = 0, ByName, ByUnderlyingValue }

  [AttributeUsage(AttributeTargets.Enum)]
  public sealed class MessagePackEnumAttribute : Attribute
  {
    public EnumSerializationMethod SerializationMethod { get; set; }
  }

  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
  public sealed class MessagePackEnumMemberAttribute : Attribute
  {
    public EnumMemberSerializationMethod SerializationMethod { get; set; }
  }
}
