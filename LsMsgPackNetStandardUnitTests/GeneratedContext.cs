using LtMsgPack;

// The source generator writes LsMsgPackUnitTests.TestMsgPackContext for all classes of this assembly, the Gen* tests (Serializers.Gen) use it.
// Not registered for the process: the Ls* and Lt* tests keep running on the runtime's own plans and accessors.
[assembly: LtMsgPackGenerate(Include = LtMsgPackInclude.AllTypes, ContextName = "LsMsgPackUnitTests.TestMsgPackContext", AutoRegister = false)]
