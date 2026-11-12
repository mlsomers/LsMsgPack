using LsMsgPack;

// The source generator writes LsMsgPackUnitTests.TestMsgPackContext for all classes of this assembly, the Gen* tests (Serializers.Gen) use it.
// Not registered for the process: the Debug* and Ls* tests keep running on the runtime's own plans and accessors.
[assembly: LsMsgPackGenerate(Include = LsMsgPackInclude.AllTypes, ContextName = "LsMsgPackUnitTests.TestMsgPackContext", AutoRegister = false)]
