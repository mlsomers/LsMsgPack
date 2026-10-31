// CRC-32 (IEEE, the polynomial of zip and PNG) of the index of a stream, as LsMsgPack.Core's Crc32.

const table = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

export function crc32(data: Uint8Array, start = 0, end = data.length): number {
  let crc = 0xffffffff;
  for (let i = start; i < end; i++) crc = table[(crc ^ data[i]) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}
