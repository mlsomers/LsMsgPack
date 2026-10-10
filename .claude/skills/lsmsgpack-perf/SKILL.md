---
name: lsmsgpack-perf
description: Measure and verify performance changes to the LsMsgPack and LtMsgPack serializers (and LsMsgPack.Core and the web formatters they share). Use before and after optimizing or refactoring anything in LsMsgPackNetStandard, LtMsgPack, LsMsgPackCore or the formatters (packing, unpacking, the indexed schema, property access, caches), to profile with perf, to A/B benchmark against master and to prove the serialized bytes and round trips are unchanged.
---

# Performance work on LsMsgPack

Two questions for every change: **is it faster** (A/B against a base build, same machine, same session) and **is the output unchanged** (byte and round trip equivalence against the base). The scripts in `scripts/` build both harnesses from the working tree ("new") and a git ref ("base").

## Setup (once per base)

```bash
.claude/skills/lsmsgpack-perf/scripts/setup.sh <work dir> [base ref, default origin/master]
```

Use a work dir **outside the repository** (e.g. the session scratchpad). It extracts the base library with `git archive` into `<work dir>/baseline` and builds four console projects: `bench-new`, `bench-base`, `equiv-new`, `equiv-base`. Run setup again when the base should change (e.g. after master moved), and delete `<work dir>/base.txt` so the base output is regenerated.

## Equivalence (must stay identical)

```bash
.claude/skills/lsmsgpack-perf/scripts/compare.sh <work dir>   # ~35 s, rebuilds equiv-new
```

`Equiv.cs` serializes ~90 edge case values (all integer sizes and boundaries, strings up to 70000 chars, bin, floats, NaN, decimals, DateTimes before/after the timestamp ranges, enums of several underlying types, arrays/maps over 65535 entries, nested objects, a custom property id resolver) for all `EndianAction` × `DynamicallyCompact` × `UseInexedSchema` settings, and prints the bytes (hashes for large ones), the typed round trip and the raw `MsgPackItem` tree. It warms up first, so the recorded pass uses the bound property delegates (see `PropertyAccessor`).

An intended format change shows up as a diff: check that only the expected lines differ, and mention it in the commit message. Add a case to `Equiv.cs` when a change touches something the corpus does not cover (and rerun setup, the base must run the same program).

## Benchmark

```bash
.claude/skills/lsmsgpack-perf/scripts/bench.sh <work dir> [seconds per run=8] [repeats=2]
```

Alternates base and new runs of the invoice model (same model and data as the `BenchmarkInvoices` unit test) and prints the fastest write and read of 20 rounds × 100 invoices, for the indexed schema and property names. Direct use:

```bash
dotnet <work dir>/bench-new/bin/Release/net8.0/bench-new.dll both 10 indexed          # write|read|both, seconds, indexed|named
dotnet <work dir>/bench-new/bin/Release/net8.0/bench-new.dll both 10 named nocompile  # without bound property delegates (A/B in one build)
dotnet <work dir>/bench-new/bin/Release/net8.0/bench-new.dll phases 1                 # tree build vs ToBytes vs raw Unpack
```

It also counts first chance exceptions, which should stay 0.

The numbers for the user come from the unit test (their table format):

```bash
dotnet test LsMsgPackNetStandardUnitTests/LsMsgPackUnitTests.csproj -c Release --filter "TestCategory=Benchmark" --logger "console;verbosity=detailed"
```

## LtMsgPack, Core and the formatters (`scripts/lt/`)

The same approach for LtMsgPack, with System.Text.Json as the baseline. A change to Core needs both harnesses (LsMsgPack's above and these).

```bash
.claude/skills/lsmsgpack-perf/scripts/lt/setup.sh <work dir> [base ref]     # bench-, equiv- and web- new/base (the base gets LtMsgPack, Core, LsMsgPack and LsMsgPackFormatters)
.claude/skills/lsmsgpack-perf/scripts/lt/compare.sh <work dir>              # ~10 s, 71k lines: must stay identical
.claude/skills/lsmsgpack-perf/scripts/lt/bench.sh <work dir> write 5 2 "lt-default lt-map-named"   # alternating base/new
dotnet <work dir>/bench-new/bin/Release/net8.0/bench-new.dll table 3        # all candidates vs System.Text.Json (invoices)
dotnet <work dir>/bench-new/bin/Release/net8.0/bench-new.dll small 1.5      # per call cost: one Address
dotnet <work dir>/web-new/bin/Release/net8.0/web-new.dll 3                  # end to end: ASP.NET Core TestServer, GET/POST an invoice, JSON vs the formatters
```

- `LtEquiv.cs` writes and reads ~195 values (the corpus of `Equiv.cs`, non-ASCII strings around the string header sizes, polymorphic and `[DefaultValue]` objects, invoices) in 61 configurations: every `EndianAction` × `DynamicallyCompact` × layout × names/inline/reference (× `TrimTrailingNulls`), the presets, `AddTypeIdOption.Always`, a custom property id resolver, streams, and the HTTP serializer with and without negotiation. Add a case when a change touches something it does not cover, and run setup again (the base must run the same program).
- `LtBench.cs` candidates: `stj`, `lt-default`, `lt-map-indexed`, `lt-map-reference`, `lt-map-named`, `lt-arr-indexed`, `lt-arr-reference`, `lt-arr-noschema`, `lt-mpcsharp`, `http-lsmsgpack`, `http-lsmsgpack-ref`, `http-plain`, and reading with an `out ReadDifferences` (`lt-report-default`, `lt-report-map-indexed`, `lt-report-map-reference`, `lt-report-map-named`: the cost of the reporting path on matching data, left out when the base has no such overload). Modes `write|read|both <candidate> <s>`, `small1 <candidate> <s>`, `table <s> [prefix]`, `small <s> [prefix]`.
- The `table` mode runs all candidates in one process, their numbers differ a bit from single candidate runs (shared JIT profile): compare base and new the same way.
- Shared caches: also check a change with several threads on one serializer and alternating root types (a scratch program, compare with the single threaded bytes and values).

## Pitfalls

- **Noise.** The same run varies 5-10% on this kind of machine. Alternate base/new, repeat, compare ranges, and do not claim a gain smaller than the noise. The System.Text.Json row in the unit test output shows how noisy a run was.
- **Warm-up.** Discard the first rounds (tiered JIT, caches, type resolution). The harness already skips 2.
- **Retention distorts.** Keeping thousands of results alive (e.g. collecting all trees in an array) adds gen2 GC cost that the real workload does not have. Discard results inside timing loops.
- **Microbenchmarks** (a small console project referencing the library, `Stopwatch` over 1-3 million calls after a warm-up) are the reliable way to find out what a single piece costs. Internal members can be reached by reflection (`typeof(MsgPackSerializer).GetMethod("WithSchema", BindingFlags.NonPublic | BindingFlags.Static)`), `IndexedSchemaTypeResolver.GetComplex/Pack/Unpack` are public.
- **One-time costs matter** for a library that is often used for small messages: check the per call overhead too (e.g. serialize an object with one int), and weigh setup costs (compiling, reflection) against the per call gain. Expression.Compile costs ~300 µs per delegate, `Delegate.CreateDelegate` ~6 µs, a reflection property get ~30-60 ns.
- **Types in a harness** that are deserialized as elements of a collection need `MsgPackSerializer.CacheAssemblyTypes(typeof(T))`, and their short names must be unique among the cached assemblies (see CLAUDE.md).

## Profiling

- `dotnet-trace` can be installed without touching the user's global tools: `dotnet tool install dotnet-trace --tool-path <scratch>/tools`, then `DOTNET_ROOT=/usr/lib/dotnet <scratch>/tools/dotnet-trace collect --format Speedscope --profile dotnet-sampled-thread-time -- dotnet <harness.dll> read 12` (on Linux `cpu-sampling` only works with `collect-linux`).
- On Linux its sampled stacks were **unreliable at the leaf** (time attributed to frames like `RuntimeType.GetGenericArguments`, `List..ctor` or `MpInt.SetEnumVal` that microbenchmarks proved cheap), also with `DOTNET_TieredCompilation=0`. Use it for a rough picture only, and confirm every hotspot with a microbenchmark or an A/B run before optimizing.
- **`perf` is the tool** (accurate, used for the October 2026 LtMsgPack round on a physical machine): `scripts/lt/prof.sh <work dir> <out> <bench args>`. It needs `DOTNET_PerfMapEnabled=1` **and** `DOTNET_EnableWriteXorExecute=0`: with W^X (the default since .NET 7) the JIT code runs from a double mapping (`memfd:doublemapper`) that the perf map does not cover, so every frame shows up as a bare address. Self time is attributed to the caller of whatever the JIT inlined (guarded devirtualization inlines virtual calls), so a big self time can be its callees: confirm with a microbenchmark.
- `perf` needs `kernel.perf_event_paranoid` ≤ 1. Only the user can change that (`sudo sysctl kernel.perf_event_paranoid=1`, it resets on reboot), ask instead of working around it.
