# Interop tests

Checks what MessagePack-CSharp and Nerdbank.MessagePack read of LsMsgPack's output and the other way around (the invoice model with property names, byte equality per type, and the known differences: Guid, decimal, `DateTimeKind.Unspecified`, `DateTimeOffset`, polymorphism, omitted defaults, the indexed schema). `PresetTests` checks `LtMsgPackPresets` against those libraries' defaults (MessagePack-CSharp: the same bytes), `IgnoreAttributeTests` the `Like<Library>` presets of `FilterIgnoredAttribute`.

- A format change can make these fail: update the test when the change is intended, and mention it.
- `docs/Compatibility.md` documents the settings per library (from these tests): keep it in sync.
- The shared test classes and adapters work as in the unit tests (see `../LsMsgPackNetStandardUnitTests/CLAUDE.md`); this project adds the Nerdbank Guid to `LtSerializer.ExtensionEquivalents`.
