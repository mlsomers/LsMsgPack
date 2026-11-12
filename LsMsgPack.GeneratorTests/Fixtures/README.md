# Generated code of older contract versions

One folder per released version of the generated code (`LsMsgPack.Generated.GeneratedContract`) the runtime still supports:
the model (`Model.cs.txt`) and what the generator of that version wrote for it (`*.g.cs.txt`), never changed after its release (a version that is not released yet may still be rewritten from the snapshot).
`ContractFixtureTests` compiles each against the current runtime and checks that the code is used and gives the same bytes and values as the runtime plans.

When `GeneratedContract.Current` is raised, add a folder for the new version (from the snapshot of the invoice model).
When `MinSupported` is raised, remove the folders below it.
