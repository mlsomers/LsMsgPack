// A minimal hand-written reader (same code quality for both formats): generic maps by name vs
// x-lsmsgpack bodies read straight into objects with the schema's names (what a LtMsgPack-style port would do).
import { encode } from "@msgpack/msgpack";

const td = new TextDecoder();
class Reader {
  constructor(b) { this.b = b; this.v = new DataView(b.buffer, b.byteOffset, b.byteLength); this.p = 0; this.keys = new Map(); }
  str(n) {
    const b = this.b, s = this.p; this.p += n;
    if (n < 24) { let r = ""; for (let i = s; i < s + n; i++) { const c = b[i]; if (c > 127) return td.decode(b.subarray(s, s + n)); r += String.fromCharCode(c); } return r; }
    return td.decode(b.subarray(s, s + n));
  }
  key(n) { // cache map keys by their bytes (like @msgpack/msgpack does)
    const b = this.b, s = this.p; let h = n; for (let i = s; i < s + n; i++) h = (h * 31 + b[i]) | 0;
    let e = this.keys.get(h); if (e === undefined) { e = this.str(n); this.keys.set(h, e); return e; } this.p += n; return e;
  }
  len(t) { // header byte -> [kind, length]
    const v = this.v;
    if (t <= 0x7f) return t; if (t >= 0xe0) return t - 256;
    switch (t) { case 0xcc: return this.b[this.p++]; case 0xcd: { const r = v.getUint16(this.p); this.p += 2; return r; } case 0xce: { const r = v.getUint32(this.p); this.p += 4; return r; }
      case 0xd0: return v.getInt8(this.p++); case 0xd1: { const r = v.getInt16(this.p); this.p += 2; return r; } case 0xd2: { const r = v.getInt32(this.p); this.p += 4; return r; } }
    throw new Error("int " + t.toString(16));
  }
  any() {
    const t = this.b[this.p++];
    if (t <= 0x7f || t >= 0xe0) return t >= 0xe0 ? t - 256 : t;
    if (t >= 0xa0 && t <= 0xbf) return this.str(t & 31);
    if (t >= 0x90 && t <= 0x9f) return this.arr(t & 15);
    if (t >= 0x80 && t <= 0x8f) return this.map(t & 15);
    const v = this.v;
    switch (t) {
      case 0xc0: return null; case 0xc2: return false; case 0xc3: return true;
      case 0xd9: return this.str(this.b[this.p++]); case 0xda: { const n = v.getUint16(this.p); this.p += 2; return this.str(n); }
      case 0xc4: { const n = this.b[this.p++]; const r = this.b.slice(this.p, this.p + n); this.p += n; return r; }
      case 0xdc: { const n = v.getUint16(this.p); this.p += 2; return this.arr(n); }
      case 0xde: { const n = v.getUint16(this.p); this.p += 2; return this.map(n); }
      case 0xd6: { this.p++; const s = v.getUint32(this.p); this.p += 4; return new Date(s * 1000); }
      case 0xd7: { this.p++; const hi = v.getUint32(this.p), lo = v.getUint32(this.p + 4); this.p += 8; return new Date(((hi & 3) * 4294967296 + lo) * 1000 + (hi >>> 2) / 1e6); }
      case 0xcb: { const r = v.getFloat64(this.p); this.p += 8; return r; }
    }
    return this.len(t);
  }
  arr(n) { const r = new Array(n); for (let i = 0; i < n; i++) r[i] = this.any(); return r; }
  map(n) {
    const r = {};
    for (let i = 0; i < n; i++) { const t = this.b[this.p++]; let k; if (t >= 0xa0 && t <= 0xbf) k = this.key(t & 31); else { this.p--; k = this.any(); } r[k] = this.any(); }
    return r;
  }
  count() { const t = this.b[this.p++]; if (t >= 0x90 && t <= 0x9f) return t & 15; if (t === 0xdc) { const n = this.v.getUint16(this.p); this.p += 2; return n; } if (t === 0xc0) return -1; throw new Error("array " + t.toString(16)); }
  // an object of a known type: an array of values, the names from the schema
  obj(plan) {
    const n = this.count(); if (n < 0) return null;
    const r = new plan.ctor(); const names = plan.names, sub = plan.sub;
    for (let k = 0; k < n; k++) {
      if (this.b[this.p] === 0xc0) { this.p++; continue; }
      const s = sub[k];
      if (s === undefined) r[names[k]] = this.any();
      else if (s.list) { const c = this.count(); const a = new Array(c); for (let i = 0; i < c; i++) a[i] = this.obj(s.list); r[names[k]] = a; }
      else r[names[k]] = this.obj(s);
    }
    return r;
  }
}

// plans: per type the schema's names and the plans of the nested objects (built once per schema, like LtMsgPack's BoundSchema)
function plan(schema, desc, cache = new Map()) {
  if (cache.has(desc.name)) return cache.get(desc.name);
  const names = schema[desc.name];
  // a constructor with the properties in a fixed order gives V8 one hidden class (an object literal shape)
  const ctor = new Function(names.map((n) => `this[${JSON.stringify(n)}]=undefined;`).join(""));
  const p = { names, ctor, sub: [] }; cache.set(desc.name, p);
  names.forEach((n, k) => { const st = desc.types[n]; if (st) p.sub[k] = st.list ? { list: plan(schema, st.list, cache) } : plan(schema, st, cache); });
  return p;
}

const { T, schema, named, positional } = await import("./data.mjs");
const plain = encode(named), body = encode(positional);
const pl = plan(schema, T.Invoice);
function time(label, fn) {
  for (let i = 0; i < 300; i++) fn();
  const runs = 3000; const t0 = process.hrtime.bigint(); for (let i = 0; i < runs; i++) fn(); const t = Number(process.hrtime.bigint() - t0) / runs / 1000;
  console.log(label.padEnd(52), t.toFixed(1).padStart(8), "µs");
}
const jsonText = JSON.stringify(named);
time("JSON.parse (string)", () => JSON.parse(jsonText));
time("hand reader, msgpack names (generic maps)", () => new Reader(plain).any());
time("hand reader, x-lsmsgpack body -> objects (plans)", () => { const r = new Reader(body); const n = r.count(); const a = new Array(n); for (let i = 0; i < n; i++) a[i] = r.obj(pl); return a; });
// check: same objects
const r = new Reader(body); const n = r.count(); const a = []; for (let i = 0; i < n; i++) a.push(r.obj(pl));
const strip = (o) => JSON.parse(JSON.stringify(o));
console.log("same result:", JSON.stringify(strip(a)) === JSON.stringify(strip(new Reader(plain).any())));
