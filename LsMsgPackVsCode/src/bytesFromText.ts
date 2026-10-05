// Bytes written as text: what MsgPackExplorer accepts from the clipboard (ClipboardSupport.GetBytes), and Python bytes literals.

const supportedFormats =
  'Supported formats are:\n' +
  '  - hex strings (0x1a4f...)\n' +
  '  - base64 encoded\n' +
  '  - delimited hexadecimal values (1A 4F, 0x1a,0x4f...)\n' +
  "  - delimited decimal values (between 0 and 255 inclusive each, e.g. a copied array: [26, 79])\n" +
  "  - Python bytes literals (b'\\x1aO')";

/**
 * Converts text holding bytes to the bytes: hex, base64, delimited hex or decimal values, or a Python bytes literal.
 * @throws Error when the text does not look like any of them.
 */
export function bytesFromText(text: string): Uint8Array {
  const trimmed = text.trim();
  const python = /^[bB](['"])([\s\S]*)\1$/.exec(trimmed);
  if (python) {
    return fromPythonLiteral(python[2]);
  }

  let allHex = true;
  let allNumeric = true;
  let all2chars = true;
  const parts: string[] = [];
  for (let part of trimmed.split(/[\r\n;\t ,.\-|[\]{}()]+/)) {
    part = part.trim();
    if (part.length === 0) {
      continue;
    }
    if (part.startsWith('0x') || part.startsWith('0X')) {
      part = part.substring(2);
    }
    if (allHex) {
      for (const ch of part) {
        if (ch >= '0' && ch <= '9') {
          continue;
        }
        allNumeric = false;
        if (ch >= 'A' && ch <= 'F' || ch >= 'a' && ch <= 'f') {
          continue;
        }
        allHex = false;
      }
    }
    if (all2chars) {
      all2chars = part.length === 2;
    }
    parts.push(part);
  }

  if (parts.length === 0) {
    return new Uint8Array(0);
  }

  if (parts.length === 1) { // One long string, so either hex or base64
    if (allHex) {
      return fromHex(parts[0].length % 2 === 0 ? parts[0] : parts[0] + '0');
    }
    return fromBase64(parts[0]);
  }
  if (all2chars && allHex) { // delimited hex
    return fromHex(parts.join(''));
  }
  if (allNumeric) { // csv, or an array copied from a debugger
    const bytes = new Uint8Array(parts.length);
    for (let t = 0; t < parts.length; t++) {
      const value = Number(parts[t]);
      if (!Number.isInteger(value) || value < 0 || value > 255) {
        throw new Error(`Failure parsing delimited decimal values: ${parts[t]} is not a byte (0..255).`);
      }
      bytes[t] = value;
    }
    return bytes;
  }
  // Base64 broken over several lines
  if (/^[A-Za-z0-9+/=\s]+$/.test(trimmed) && /[\r\n]/.test(trimmed)) {
    return fromBase64(trimmed.replace(/\s+/g, ''));
  }
  throw new Error('The text does not seem to represent a byte array.\n' + supportedFormats);
}

export function fromHex(hex: string): Uint8Array {
  if (hex.length % 2 !== 0 || !/^[0-9a-fA-F]*$/.test(hex)) {
    throw new Error('Failure parsing hex string: not an even number of hex digits.');
  }
  const bytes = new Uint8Array(hex.length / 2);
  for (let t = 0; t < bytes.length; t++) {
    bytes[t] = parseInt(hex.substr(t * 2, 2), 16);
  }
  return bytes;
}

export function fromBase64(text: string): Uint8Array {
  // Base64url too (- and _), padding is optional
  const normalized = text.replace(/-/g, '+').replace(/_/g, '/');
  if (!/^[A-Za-z0-9+/]*={0,2}$/.test(normalized) || normalized.replace(/=+$/, '').length % 4 === 1) {
    throw new Error('Failure parsing base64 encoded string.\n' + supportedFormats);
  }
  return new Uint8Array(Buffer.from(normalized, 'base64'));
}

/**
 * The content of b'...': printable characters as they are, \xNN, and the usual escapes.
 */
function fromPythonLiteral(content: string): Uint8Array {
  const bytes: number[] = [];
  const escapes: { [key: string]: number } = { n: 10, r: 13, t: 9, '\\': 92, "'": 39, '"': 34, a: 7, b: 8, f: 12, v: 11, '0': 0 };
  for (let t = 0; t < content.length; t++) {
    const ch = content[t];
    if (ch !== '\\') {
      const code = ch.charCodeAt(0);
      if (code > 255) {
        throw new Error('A bytes literal holds only ASCII characters and escapes.');
      }
      bytes.push(code);
      continue;
    }
    const next = content[++t];
    if (next === 'x') {
      const hex = content.substr(t + 1, 2);
      if (!/^[0-9a-fA-F]{2}$/.test(hex)) {
        throw new Error('Invalid \\x escape in bytes literal.');
      }
      bytes.push(parseInt(hex, 16));
      t += 2;
    } else if (/[0-7]/.test(next) && /^[0-7]{1,3}/.test(content.substr(t, 3))) {
      const octal = /^[0-7]{1,3}/.exec(content.substr(t, 3))![0];
      bytes.push(parseInt(octal, 8) & 0xff);
      t += octal.length - 1;
    } else if (next !== undefined && next in escapes) {
      bytes.push(escapes[next]);
    } else {
      bytes.push(92); // an unknown escape keeps the backslash
      if (next !== undefined) {
        bytes.push(next.charCodeAt(0));
      }
    }
  }
  return new Uint8Array(bytes);
}

/**
 * The text of a string as a debugger shows it: without the quotes, with the escapes of C#, JavaScript and Python undone.
 */
export function unquote(value: string): string {
  let text = value.trim();
  // C# verbatim strings, and the quotes of C#, JavaScript and Python
  const quoted = /^@?(["'`])([\s\S]*)\1$/.exec(text);
  if (!quoted) {
    return text;
  }
  text = quoted[2];
  if (value.trim().startsWith('@')) {
    return text.replace(/""/g, '"');
  }
  return text.replace(/\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{2}|.)/g, (match, escape: string) => {
    switch (escape[0]) {
      case 'n': return '\n';
      case 'r': return '\r';
      case 't': return '\t';
      case '0': return '\0';
      case 'u':
      case 'x':
        return String.fromCharCode(parseInt(escape.substring(1), 16));
      default: return escape;
    }
  });
}
