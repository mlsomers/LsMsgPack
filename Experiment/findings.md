I'll start by reading the relevant parts of the codebase (the indexed schema, the session setup, the benchmark), then measure where time actually goes before answering.

I've covered the schema resolver, session setup, and pack/unpack paths. Next I'll check the item types and formatters, then build a small harness to measure the potential savings from schema caching.

.NET isn't installed in this container, so I'll install the SDK before measuring anything.

I'll skip the proxy check and just try the normal .NET install script.

The .NET download host is blocked by the network policy. Let me check what the environment allows before trying other routes.

Ubuntu's own package repository has the .NET 8 SDK, so I'll install it from there.

.NET 8 is installed. Now I'll check that NuGet restore works and build the solution.

The build works. Now the baseline: the unit test benchmark (the maintainer's table format) on this machine.

Baseline numbers match CLAUDE.md's notes (indexed write 0.52x, read 0.74x; names write 0.70x, read 0.83x vs Json.NET). Now I'll prototype a cached-schema path in a scratch harness, outside the repo, to see what savings caching could actually offer.

I'll run a first experiment: benchmarking a cached schema approach against the existing pipeline, breaking down where time goes (tree build, bytes, unpack, convert), and comparing against Json.NET, System.Text.Json, and MessagePack-CSharp as reference points.

Packages restore fine. I'll switch the MessagePack-CSharp reference to the current 3.x version (it's only a benchmark reference) and write the harness.

Now the harness itself. It reaches the private `WithSchema` and `ConvertDeserializeValue` through delegates, as the perf skill suggests, so the library stays untouched.

The prototype checks out: cached bodies match exactly across all 100 invoices, and payload size drops from 226,527 to 187,227 bytes by replacing the 403-byte schema with a 10-byte reference. Now checking the timing.

Caching cuts per-invoice time by about 10 µs both ways (write 64.7→43.9 ms, read 68.8→48.4 ms), putting reads on par with Json.NET (1.02x) but writes still at 0.73x. I'll rerun for noise, then measure small messages and phase-split timings.

Found a bug: the write phase split produced a negative ToBytes because I took the minimum of per-pass differences instead of the difference of minimums. I'll fix that and re-run, along with the small-message test.

The cached-schema prototype is measured and shows even bigger gains for small messages, but the phase split reveals the remaining bottleneck is the intermediate MsgPackItem tree, not the schema itself. Next I'll test a tree-less writer/reader against Json.NET, starting by reading the integer, bin, and array encodings to match byte output exactly.

The encodings follow standard MsgPack with LsMsgPack-specific choices—unsigned formats for non-negative ints, 5-bit negative fixint down to -31, decimal as fixext16 type 1, and Guid as bin8. I'll check the remaining timestamp details next, then write the tree-less prototype.

Now I'll build the tree-less prototype, keeping it generic (runtime reflection, no hand-written invoice code), free of code generation (only `Delegate.CreateDelegate`/`MakeGenericType`, as the library already does), and byte-for-byte consistent with the current serializer's filters, property order, and schema.

Now I'll wire the prototype into the harness: verification (byte-identical to the current serializer in both modes, plus round trips) and two new candidates.

The tree-less prototype writes exactly the same bytes as the current serializer in both modes (100/100). Its reader round-trips, and the current reader accepts its output. Now the timing:

The tree-less prototype beats Json.NET by 5-8x and even edges out MessagePack-CSharp. Next I'll test caching the parsed schema in-process with today's wire format to see if that captures most of the CPU gain on its own—starting by verifying the tree-less writer reproduces today's full indexed payload byte for byte.

The inline variant is byte-identical to today's indexed payloads (schema + body) and reads them correctly, so it needs no format change at all. Now the full comparison, run twice for noise:

Consistent across both runs. Now the small messages, where per-call overhead matters most:

Small messages confirm the trend: tree-less writes/reads run 0.13–0.25 µs versus 0.67/1.26 µs for Json.NET and 2.41/3.02 µs for indexed mode. Next I'll measure cold-start (first-call) cost for each serializer in a fresh process, since source generators mainly benefit startup/AOT rather than steady-state speed.

My harness parses the second argument as seconds before the mode is checked. A one-line fix:

Cold start numbers hold up: the tree-less prototype's write/read times stay on par with LsMsgPack and far ahead of Json.NET, so source generators aren't needed for startup here either—only for NativeAOT/trimming scenarios. I also spotted that type resolution takes a global lock on every read (`TypeResolver.SyncRoot`), which could hurt throughput under concurrency, so I'll test how each mode scales across threads.

Two findings: indexed mode already scales 3.4x on 4 threads (same as names mode, so the global lock isn't the bottleneck), but my prototype's names reader actually scales negatively (0.58x) due to a shared "expected next property" field causing cache-line contention across cores. I'll fix this by moving that state into a local variable instead.

The names reader now scales 3.4x on 4 threads (751K messages/s vs 126K for Json.NET), while indexed read scaling was inconclusive at invoice size (2.7x vs 3.4x across runs). Checking small message performance next, since that's where a per-message lock would be most visible.

Small messages confirm the pattern: indexed reads scale only 2.4–2.5x on 4 threads versus 3.2–3.3x for names mode, while a tree-less version with a process-local schema cache scales 3.6x—supporting the case for caching the bound schema. Now I'll measure compressed sizes per message, since gzip/brotli tends to shrink JSON's size advantage on the wire.

With compression, today's inline schema loses its size edge (even larger than JSON for invoices), while a cached schema cuts size ~20-40%—though compression adds 70-110 µs per message, 25-35x the prototype's serialization cost. I'll now check the final state of the prototype file before sharing it.

The measurements are done; I'm wrapping up. A quick sanity check that the prototype file has no duplicated sections, then I'll share the files and write up the answer.

No duplicates, and the repository is untouched. I'll attach the harness so it outlives this container.

### Short answer

- **Caching the schema alone won't beat Json.NET.** It saves about 10 µs per message in each direction, roughly 30%. Indexed mode moves from ~0.5x write / 0.75x read of Json.NET to ~0.75x / 1.05x: level on reads, still slower on writes.
- **The schema isn't the bottleneck; the intermediate `MsgPackItem` tree is.** I built a prototype that goes straight from objects to bytes and back, with no item tree. It produces exactly the same bytes (100/100 invoices), uses no source generator and no `Reflection.Emit`, and runs about 5–6x faster than Json.NET on write and 6–9x on read.
- **With that prototype, a process-local schema cache gets nearly all of the CPU gain** while keeping today's wire format (schema inline, parsed once per distinct schema). The exchange protocol then only buys bytes: −17% for invoices and −48% for single small objects. After compression it's the only format clearly smaller than JSON.

Nothing in the repository was changed.

### Measurements

Setup: the `BenchmarkInvoices` model and data, 100 invoices × 20 rounds, fastest pass, two runs. The container has a 4-core Xeon at 2.1 GHz, .NET 8, and its time zone is UTC. The unit test on this machine gave the ratios in your notes: indexed 0.52x / 0.74x, names 0.70x / 0.83x.

| | Payload | Write | vs Json.NET | Read | vs Json.NET |
|---|---|---|---|---|---|
| Json.NET (UTF-8 bytes) | 338 KB | 31–33 ms | 1.00x | 53–56 ms | 1.00x |
| System.Text.Json (reflection) | 331 KB | 16–17 ms | ~2x | 27 ms | ~2x |
| MessagePack-CSharp, contractless | 279 KB | 10–11 ms | ~3x | 17–19 ms | ~3x |
| LsMsgPack indexed (today) | 227 KB | 60–66 ms | 0.47–0.55x | 70–75 ms | 0.75x |
| LsMsgPack names (today) | 314 KB | 47–52 ms | 0.60–0.70x | 59–66 ms | 0.85–0.90x |
| Today's pipeline + cached schema | 187 KB | 44–46 ms | 0.67–0.76x | 49–54 ms | 1.05–1.08x |
| No item tree, names | 314 KB | 6.7 ms | 4.6–5.0x | 8.1–8.9 ms | 6.3–6.5x |
| No item tree, inline schema + local cache | 227 KB | 5.1–5.2 ms | 6.1–6.4x | 6.2–7.1 ms | 7.9–8.6x |
| No item tree, cached schema | 187 KB | 5.0–5.5 ms | 6.0–6.2x | 5.9–6.5 ms | 8.7–9.0x |

- **Small messages (one `Address` per call):**
  - Today's indexed mode takes 2.41 µs to write and 3.02 µs to read, against 0.88 / 1.14 µs for names and 0.67 / 1.26 µs for Json.NET.
  - The inline schema also makes the payload bigger than names: 112 bytes against 94. With a cached schema it's 58 bytes.
  - Without the item tree it takes 0.13–0.17 µs to write and 0.21–0.25 µs to read.
- **Where today's time goes** (with the schema work already cached): writing is tree building (28–31 ms) plus `ToBytes` (14–15 ms). Reading is `Unpack` into items (26–28 ms) plus converting items into objects (29–31 ms). Each of these phases takes about as long as System.Text.Json needs for the whole job.
- **Verification:** the cached body and both prototype writers produce the same bytes as today for all 100 invoices. The inline variant reproduces today's full indexed payload, schema included. Round trips are equal, and today's reader reads the prototype's bytes.

### Beating Json.NET without source generators: yes, by dropping the item tree

The prototype (`Direct.cs`, attached) is generic. It builds a plan per type by reflection and only uses what `PropertyAccessor` already uses: `Delegate.CreateDelegate` and `MakeGenericType`. What makes it fast:

- It writes into one reusable buffer and reads straight from the byte array into the objects. There's no `MsgPackItem` per value, no `object[]` or `KeyValuePair<object,object>[]`, and no `Dictionary` per object in `ConvertMap`.
- Nothing is boxed. Properties go through typed getter and setter delegates plus a value writer and reader per type, so the default-value filter becomes a typed comparison.
- Keys (index or name) are encoded once per plan and copied. The map header is reserved, then patched after default values are skipped.
- With a schema, finding a property is an array lookup. It's bound by name once per schema, so added or removed properties still work. With names, it compares the UTF-8 bytes against the property expected next, without allocating strings.

Startup is fine too. The first write / first read of one invoice in a fresh process takes 20–33 / 7–12 ms, against 22–25 / 14–50 ms for today's LsMsgPack and 110–138 / 37–39 ms for Json.NET. Source generators would mainly add NativeAOT and trimming support, where `MakeGenericType` over value types needs the reflection fallback `PropertyAccessor` already has.

Not done yet in the prototype:
- `object` and interface-typed properties, dictionaries and other collections, and the `"@"` wrapper.
- Custom extensions, and custom id resolvers and dynamic filters. Those interfaces take a boxed `object`, so they'd need a slower path.
- `DefaultValueAttribute`, struct targets, and non-default `EndianAction` / `DynamicallyCompact`.
- Limits for untrusted input: nesting depth, and `new List<T>(n)` with `n` taken from the payload.

The `MsgPackItem` API would stay for the explorer and KEEPTRACK, so two paths would have to produce the same bytes; the Equiv harness is the guard for that. One behavior change: custom property id resolvers would be asked once per plan instead of on every call.

On MessagePack-CSharp: it writes decimals and Guids as strings by default, so the prototype being ahead of it here isn't like for like.

### Is the cached schema viable? Yes, as a bandwidth feature

It's the same model as Avro's single-object encoding and Confluent's schema registry. Here's what I think is missing or simplified.

**What the hash covers**
- A payload's schema covers the whole object graph (Invoice, Customer, Address, InvoiceLine), and type ids are indexes into that one list. A payload has to reference a set of types (per root type or per service version), not a single class. Property ids are per type and don't depend on type order.
- `ExtractSchemas(assembly)` assumes all types are known up front, which doesn't hold:
  - Closed generics such as `Page<Invoice>`.
  - Property types from other assemblies.
  - Polymorphic runtime types.
  - Anonymous types or records returned by endpoints.
  - Collections whose properties are serialized because of a `[SerializeEnumerable]` on a property. Today `AddProp` adds those lazily.
  - A root `List<Invoice>` whose assembly is CoreLib, so "the assembly of the item" is the wrong fallback.
- Two consequences follow:
  - Discover the types from root types, e.g. the action signatures via ApiExplorer, rather than `[Serializable]` or the Auto heuristic.
  - Allow "base reference + inline extension" in a payload, so an unknown type never blocks writing.
- The schema depends on the settings. Static filters decide the property lists, and custom type resolvers or `FullName` decide the type names. A static global cache has to be keyed per settings.
- A static class can't inherit from `MsgPackSerializer`, which is static itself. An instance class holding the settings and its caches fits better, also for the formatters.

**Hashing and security**
- The hash must be stable across processes (`string.GetHashCode` is randomized per process) and computed over canonical bytes: sorted types plus a format version.
- If a server accepts schemas from clients, use a truncated SHA-256 and verify it. A 64-bit FNV or xxHash collision can be crafted to poison the cache, so one client's values land in another type's properties.
- Bound the cache size, or it becomes a memory denial-of-service risk.
- A known schema set can double as an allow-list of the types a reader may create.
- Content addressing means a cached schema can never be stale, so `GetSchemaHashes()` isn't needed. During a rolling upgrade the two versions are simply two hashes.

**Direction and transport**
- For requests from client to server, the server can't call a browser or mobile app back, and "derived from the request URL" gives the server's own URL. Two options:
  - The client writes with the server's schema. Unknown properties are dropped, as JSON would, or written with string keys, which MsgPack allows alongside integer keys.
  - The client sends its schema inline when the server answers "unknown schema". That check has to run before the action (e.g. in middleware) so resending is safe.
- Behind a load balancer, retries don't reliably converge: sticky sessions or IP hashing keep hitting the old node, and canary deployments run for hours. Negotiation avoids the callback entirely:
  - The client sends the hashes it holds in a request header.
  - The node that writes the response inlines the schema when the client lacks it.
  - This is stateless and works with any topology. HTTP compression dictionary transport (`Available-Dictionary`) follows the same pattern.
  - Add `Vary` on that header. Content-addressed schema URLs can still exist for prefetching, cached as immutable.
- A static `Func<hash, Stream>` callback can't work as is:
  - It blocks thread-pool threads.
  - It can't know which server to ask.
  - It has to deduplicate concurrent misses, because after a deploy every client fetches the new schema at once.
  - It should only call configured hosts, otherwise the server can be made to fetch arbitrary URLs.
- Hash-only payloads need their own media type or parameter. The explorer, Fiddler inspector and VS plugin can't decode them without a schema source.

**Shared state**
- The schema objects are single-threaded today: `IdByName` is built lazily, `GetComplex` and `AddProp` modify the schema, and the session caches are plain dictionaries. A shared schema has to be immutable, with copy-on-write for extensions.
- My first prototype wrote a "next property" guess into a shared field, and its names reader scaled 0.58x on 4 threads until I made it a local variable. Avoid writes to shared state on hot paths.
- For single-`Address` messages, today's indexed read scales only 2.4–2.5x on 4 threads, against 3.2–3.3x for names. That fits the global `TypeResolver` lock taken per message, though I didn't isolate it. A process-local schema cache fixes this without any protocol.

**Compression**
- With brotli per message, 100 invoices come to 98.0 KB for JSON, 95.5 KB for names, 103.4 KB for today's indexed format and 78.2 KB with a cached schema. For one `Address` it's 103, 93, 112 and 62 bytes.
- So on compressed links today's indexed format has no size advantage.
- Compression costs 70–110 µs per invoice message, roughly 20–40x the serialization time without the item tree.

### What I'd do, in this order
1. **A fast path without the item tree** behind `Serialize` / `Deserialize`, verified byte for byte. It's the only step that beats Json.NET. It also makes plain `application/msgpack` 5–6x faster than Json.NET and 2–3x faster than System.Text.Json. System.Text.Json is ASP.NET Core's default, so it's the real bar for the formatters.
2. **Process-local schema caches, no format change.** The writer reuses the packed schema per root type; the reader caches bound schemas by their bytes.
3. **Only if bandwidth matters: the exchange protocol**, designed as negotiation with an inline fallback.

On benchmark targets: your intuition is right, the format isn't the problem. Invoices are a good general target. I'd add System.Text.Json to the table, plus one model heavy on doubles (where JSON float formatting and parsing is expensive) and one heavy on strings, to show the range.

The attached files are the experiment harness. The .NET 8 SDK came from Ubuntu's package repository, because the network policy blocks the dot.net download host. I can turn this into a shareable page if you want to discuss it with others.