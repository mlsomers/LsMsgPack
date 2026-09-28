---
name: lsmsgpack-perf
description: Measure and verify performance changes to the LsMsgPack serializer. Use before and after optimizing or refactoring anything in LsMsgPackNetStandard (packing, unpacking, the indexed schema, property access, caches), to A/B benchmark against master and to prove the serialized bytes and round trips are unchanged.
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

## Pitfalls

- **Noise.** The same run varies 5-10% on this kind of machine. Alternate base/new, repeat, compare ranges, and do not claim a gain smaller than the noise. Json.NET's row in the unit test output shows how noisy a run was.
- **Warm-up.** Discard the first rounds (tiered JIT, caches, type resolution). The harness already skips 2.
- **Retention distorts.** Keeping thousands of results alive (e.g. collecting all trees in an array) adds gen2 GC cost that the real workload does not have. Discard results inside timing loops.
- **Microbenchmarks** (a small console project referencing the library, `Stopwatch` over 1-3 million calls after a warm-up) are the reliable way to find out what a single piece costs. Internal members can be reached by reflection (`typeof(MsgPackSerializer).GetMethod("WithSchema", BindingFlags.NonPublic | BindingFlags.Static)`), `IndexedSchemaTypeResolver.GetComplex/Pack/Unpack` are public.
- **One-time costs matter** for a library that is often used for small messages: check the per call overhead too (e.g. serialize an object with one int), and weigh setup costs (compiling, reflection) against the per call gain. Expression.Compile costs ~300 µs per delegate, `Delegate.CreateDelegate` ~6 µs, a reflection property get ~30-60 ns.
- **Types in a harness** that are deserialized as elements of a collection need `MsgPackSerializer.CacheAssemblyTypes(typeof(T))`, and their short names must be unique among the cached assemblies (see CLAUDE.md).

## Profiling

- `dotnet-trace` can be installed without touching the user's global tools: `dotnet tool install dotnet-trace --tool-path <scratch>/tools`, then `DOTNET_ROOT=/usr/lib/dotnet <scratch>/tools/dotnet-trace collect --format Speedscope --profile dotnet-sampled-thread-time -- dotnet <harness.dll> read 12` (on Linux `cpu-sampling` only works with `collect-linux`).
- On Linux its sampled stacks were **unreliable at the leaf** (time attributed to frames like `RuntimeType.GetGenericArguments`, `List..ctor` or `MpInt.SetEnumVal` that microbenchmarks proved cheap), also with `DOTNET_TieredCompilation=0`. Use it for a rough picture only, and confirm every hotspot with a microbenchmark or an A/B run before optimizing.
- `perf` gives accurate stacks with `DOTNET_PerfMapEnabled=1`, but needs `kernel.perf_event_paranoid` ≤ 1 (it was 4). Only the user can change that (`sudo sysctl kernel.perf_event_paranoid=1`), ask instead of working around it.
