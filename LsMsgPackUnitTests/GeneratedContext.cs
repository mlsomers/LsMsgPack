using LsMsgPack;

// The source generator writes LsMsgPackUnitTests.TestMsgPackContext for all classes of this assembly, the Gen* tests (Serializers.Gen) use it.
// Not registered for the process: the Debug* and Ls* tests keep running on the runtime's own plans and accessors.
// NonPublicProperties.All, Fields All: the generated code covers every non-public property and every field (non-public ones through UnsafeAccessor), the options decide which are serialized.
[assembly: LsMsgPackGenerate(Include = LsMsgPackInclude.AllTypes, ContextName = "LsMsgPackUnitTests.TestMsgPackContext", AutoRegister = false, NonPublicProperties = LsMsgPack.TypeResolving.Filters.NonPublicProperties.All,
  Fields = LsMsgPack.TypeResolving.Filters.SerializedFields.All)]
