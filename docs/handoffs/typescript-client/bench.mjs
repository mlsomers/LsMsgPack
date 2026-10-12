// Compares, in JavaScript, what a client gets from plain msgpack (maps keyed by names), the x-lsmsgpack shape
// (indexed schema + objects as arrays of values in schema order) and the schema reference (body only).
// Port of the BenchmarkInvoices model (seeded PRNG differs from .NET's Random, the shapes and value kinds are the same).
import { encode, decode, Encoder, Decoder } from "@msgpack/msgpack";
import zlib from "node:zlib";
import crypto from "node:crypto";
import { COUNT, T, schema, named, positional, toPositional, fromPositional } from "./data.mjs";

const jsonText = JSON.stringify(named, (k, v) => (v && v.type === "Buffer" ? Buffer.from(v.data).toString("base64") : v));
const json = Buffer.from(jsonText);
const plain = encode(named);
const schemaBytes = encode(schema);
const body = encode(positional);
const inline = Buffer.concat([schemaBytes, body]);
const reference = Buffer.concat([Buffer.from([0xd8, 0x02]), crypto.randomBytes(16), body]);

const gz = (b) => zlib.gzipSync(b, { level: 6 }).length;
const br = (b) => zlib.brotliCompressSync(b, { params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 5 } }).length;
console.log(`${COUNT} invoices          raw      gzip(6)   brotli(5)`);
for (const [n, b] of [["JSON (names)", json], ["msgpack names", plain], ["x-lsmsgpack inline", inline], ["x-lsmsgpack reference", reference]])
  console.log(n.padEnd(22), String(b.length).padStart(8), String(gz(b)).padStart(9), String(br(b)).padStart(10));
console.log("schema alone", schemaBytes.length, "bytes");

function time(label, fn) {
  for (let i = 0; i < 200; i++) fn();
  const runs = 2000; const t0 = process.hrtime.bigint(); for (let i = 0; i < runs; i++) fn(); const t = Number(process.hrtime.bigint() - t0) / runs / 1000;
  console.log(label.padEnd(48), t.toFixed(1).padStart(8), "µs");
}
const dec1 = new Decoder(); const enc1 = new Encoder();
console.log("\nread (bytes -> named JS objects)");
time("JSON.parse", () => JSON.parse(jsonText));
time("JSON.parse from bytes (TextDecoder + parse)", () => JSON.parse(new TextDecoder().decode(json)));
time("msgpack names: decode", () => dec1.decode(plain));
time("x-lsmsgpack ref: decode body (arrays only)", () => dec1.decode(body));
time("x-lsmsgpack ref: decode body + map to names", () => decodeRef());
time("x-lsmsgpack inline: decodeMulti schema+body + map", () => decodeInline());
function decodeRef() { return dec1.decode(body).map((a) => fromPositional(a, T.Invoice, schema)); }
function decodeInline() { const it = dec1.decodeMulti(inline); const s = it.next().value; return it.next().value.map((a) => fromPositional(a, T.Invoice, s)); }
console.log("\nwrite (named JS objects -> bytes)");
time("JSON.stringify", () => JSON.stringify(named));
time("msgpack names: encode", () => enc1.encode(named));
time("x-lsmsgpack ref: to positional + encode", () => enc1.encode(named.map((x) => toPositional(x, T.Invoice))));
