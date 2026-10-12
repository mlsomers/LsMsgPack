// What a type id on every object (AddTypeIdOption.Always) would cost: it makes x-lsmsgpack readable without type descriptors.
import { encode } from "@msgpack/msgpack";
import zlib from "node:zlib";
import { T, schema, named, positional } from "./data.mjs";

const idx = Object.fromEntries(Object.keys(schema).map((k, i) => [k, i]));
const isDefault = (v) => v === null || v === undefined || v === 0 || v === "0.00";
// array layout with a type id: { "": typeIndex, "@": [values] }, nil for a left out value
function wrapArr(o, t) {
  return { "": idx[t.name], "@": t.props.map((p) => { const v = o[p]; if (isDefault(v)) return null; const st = t.types[p]; return st ? (st.list ? v.map((e) => wrapArr(e, st.list)) : wrapArr(v, st)) : v; }) };
}
// ObjectLayout.Map with a type id: { "": typeIndex, 0: v, 3: v ... }. Written with string keys "k<n>", then the bytes
// that integer keys save are subtracted (fixstr "k<n>" = 1 + length, fixint = 1)
let saved = 0;
function wrapMap(o, t) {
  const m = { "": idx[t.name] };
  t.props.forEach((p, k) => { let v = o[p]; if (isDefault(v)) return; const st = t.types[p]; if (st) v = st.list ? v.map((e) => wrapMap(e, st.list)) : wrapMap(v, st); m["k" + k] = v; saved += ("k" + k).length; });
  return m;
}

const s = encode(schema);
const gz = (b) => zlib.gzipSync(b).length;
const show = (n, raw, gzip) => console.log(n.padEnd(44), String(raw).padStart(8), String(gzip).padStart(8));
console.log("".padEnd(44), "     raw", "    gzip");
const def = Buffer.concat([s, encode(positional)]);
show("x-lsmsgpack inline, IfAmbiguious (default)", def.length, gz(def));
const always = Buffer.concat([s, encode(named.map((x) => wrapArr(x, T.Invoice)))]);
show("x-lsmsgpack inline, Always, arrays", always.length, gz(always));
const map = encode(named.map((x) => wrapMap(x, T.Invoice)));
show("x-lsmsgpack inline, Always, Map (raw only)", s.length + map.length - saved, "");
const names = encode(named);
show("msgpack names", names.length, gz(names));
