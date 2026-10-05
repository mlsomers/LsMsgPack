// Reads the bytes of a value of the debugged program through the Debug Adapter Protocol (evaluate and variables requests).
// No dependency on the vscode module, so it can be tested with a fake debugger.

import { bytesFromText, fromBase64, unquote } from './bytesFromText';

export interface EvaluateResult {
  result: string;
  type?: string;
  variablesReference: number;
  indexedVariables?: number;
}

export interface DebugVariable {
  name: string;
  value: string;
  type?: string;
  evaluateName?: string;
  variablesReference: number;
  indexedVariables?: number;
}

/**
 * The requests of the debug session that are needed, evaluated in the selected stack frame.
 */
export interface DebugAccess {
  /** The type of the debug session (coreclr, clr, pwa-node, python...) */
  readonly sessionType: string;
  evaluate(expression: string): Promise<EvaluateResult>;
  variables(variablesReference: number, start?: number, count?: number): Promise<DebugVariable[]>;
}

export interface ReadOptions {
  /** Bytes per evaluation (as base64 text), made smaller when the debugger shortens long strings */
  chunkSize: number;
  /** Larger values are read up to this length */
  maxBytes: number;
  /** Asked before a stream is read that cannot be put back (not seekable), false cancels */
  confirmConsume?(message: string): Promise<boolean>;
  progress?(read: number, total: number): void;
  isCancelled?(): boolean;
}

export interface ReadResult {
  bytes: Uint8Array;
  /** What was read, e.g. "byte[]" or "MemoryStream" */
  description: string;
  /** Shown to the user, e.g. when the value was longer than maxBytes */
  warning?: string;
}

export class CancelledError extends Error {
  constructor() {
    super('Cancelled');
  }
}

/**
 * Reads the bytes of an expression (or of a variable when the expression cannot be evaluated).
 * Evaluating is tried first, as the language of the debug session allows it, then the elements the debugger shows.
 */
export async function readBytes(access: DebugAccess, expression: string | undefined, options: ReadOptions, variable?: DebugVariable): Promise<ReadResult> {
  const errors: string[] = [];
  if (expression) {
    const language = languageOf(access.sessionType);
    try {
      if (language === 'dotnet') {
        return await new DotNetReader(access, expression, options).read();
      }
      if (language === 'javascript') {
        return await readJavaScript(access, expression, options);
      }
      if (language === 'python') {
        return await readPython(access, expression, options);
      }
    } catch (error) {
      if (error instanceof CancelledError) {
        throw error;
      }
      errors.push(messageOf(error));
    }
  }

  // The elements as the debugger shows them, for any language (slow for large values)
  try {
    let target: DebugVariable | undefined = variable;
    if (!target && expression) {
      const evaluated = await access.evaluate(expression);
      target = { name: expression, value: evaluated.result, type: evaluated.type, variablesReference: evaluated.variablesReference, indexedVariables: evaluated.indexedVariables };
    }
    if (target) {
      return await readFromVariable(access, target, options);
    }
  } catch (error) {
    if (error instanceof CancelledError) {
      throw error;
    }
    errors.push(messageOf(error));
  }
  throw new Error(errors.length > 0 ? errors.join('\n') : 'Nothing to read.');
}

export type Language = 'dotnet' | 'javascript' | 'python' | 'other';

export function languageOf(sessionType: string): Language {
  const type = sessionType.toLowerCase();
  if (type === 'coreclr' || type === 'clr' || type.includes('dotnet') || type.includes('mono') || type === 'unity' || type === 'vsdbg') {
    return 'dotnet';
  }
  if (type.includes('node') || type.includes('chrome') || type.includes('msedge') || type.includes('extensionhost') || type === 'bun' || type === 'deno') {
    return 'javascript';
  }
  if (type.includes('python') || type === 'debugpy') {
    return 'python';
  }
  return 'other';
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function checkCancelled(options: ReadOptions): void {
  if (options.isCancelled && options.isCancelled()) {
    throw new CancelledError();
  }
}

/**
 * A number as debuggers show it: 400, 0x00000190 (hexadecimal display), 400L...
 */
export function parseInteger(result: string): number {
  const match = /-?0x[0-9a-fA-F]+|-?\d+/.exec(result);
  const value = match ? Number(match[0]) : NaN;
  if (!Number.isSafeInteger(value)) {
    throw new Error(`Not a number: ${result}`);
  }
  return value;
}

/**
 * Reads chunks of base64 text: chunk(offset, length) evaluates to the base64 of that range.
 * A chunk that comes back shorter (the debugger shortened the string) is read again in smaller parts.
 */
async function readChunks(length: number, options: ReadOptions, chunk: (offset: number, count: number) => Promise<string>): Promise<Uint8Array> {
  const bytes = new Uint8Array(length);
  // A multiple of 3, so every chunk is base64 without padding in the middle
  let size = Math.max(3, Math.floor(options.chunkSize / 3) * 3);
  let offset = 0;
  while (offset < length) {
    checkCancelled(options);
    const count = Math.min(size, length - offset);
    const text = await chunk(offset, count);
    let part: Uint8Array | undefined;
    try {
      part = fromBase64(unquote(text));
    } catch {
      part = undefined; // a shortened string usually ends with "..." instead of the quote
    }
    if (!part || part.length !== count) {
      if (size <= 3) {
        throw new Error(`The debugger returned ${part ? `${part.length} bytes` : `"${text.substring(0, 40)}"`} where ${count} bytes were asked at offset ${offset}.`);
      }
      size = Math.max(3, Math.floor(size / 2 / 3) * 3);
      continue;
    }
    bytes.set(part, offset);
    offset += count;
    if (options.progress) {
      options.progress(offset, length);
    }
  }
  return bytes;
}

function limitWarning(length: number, options: ReadOptions): string | undefined {
  return length > options.maxBytes ? `Only the first ${options.maxBytes} of ${length} bytes were read (setting lsmsgpack.maxBytes).` : undefined;
}

/**
 * .NET (vsdbg, the C# debugger of VS Code, and other debuggers that evaluate C#): the same types as the Visual Studio visualizer
 * (byte[], Stream, List<byte>, a base64 string, ByteArrayContent and HttpResponseMessage) and a few more (Memory<byte>, any IEnumerable<byte>, HttpContent).
 */
class DotNetReader {
  private readonly e: string;

  constructor(private readonly access: DebugAccess, expression: string, private readonly options: ReadOptions) {
    this.e = `(${expression})`;
  }

  async read(): Promise<ReadResult> {
    const e = this.e;
    if (await this.isTrue(`${e} == null`)) {
      throw new Error('The value is null.');
    }
    if (await this.is('byte[]')) {
      return this.readArray(e, 'byte[]');
    }
    if (await this.is('System.IO.MemoryStream')) {
      // The whole stream, whatever its position, without moving it
      return this.readArray(`((System.IO.MemoryStream)${e}).ToArray()`, 'MemoryStream');
    }
    if (await this.is('System.IO.Stream')) {
      return this.readStream(`((System.IO.Stream)${e})`);
    }
    if (await this.is('string')) {
      return this.readText(`((string)${e})`);
    }
    if (await this.is('System.Net.Http.HttpResponseMessage')) {
      return this.readArray(`((System.Net.Http.HttpResponseMessage)${e}).Content.ReadAsByteArrayAsync().Result`, 'HttpResponseMessage content');
    }
    if (await this.is('System.Net.Http.HttpContent')) {
      return this.readArray(`((System.Net.Http.HttpContent)${e}).ReadAsByteArrayAsync().Result`, 'HttpContent');
    }
    if (await this.is('System.ReadOnlyMemory<byte>')) {
      return this.readArray(`((System.ReadOnlyMemory<byte>)${e}).ToArray()`, 'ReadOnlyMemory<byte>');
    }
    if (await this.is('System.Memory<byte>')) {
      return this.readArray(`((System.Memory<byte>)${e}).ToArray()`, 'Memory<byte>');
    }
    if (await this.is('System.Collections.Generic.IEnumerable<byte>')) {
      // List<byte>, ArraySegment<byte>, ImmutableArray<byte>...
      return this.readArray(`System.Linq.Enumerable.ToArray((System.Collections.Generic.IEnumerable<byte>)${e})`, 'IEnumerable<byte>');
    }
    if (await this.is('System.Buffers.ReadOnlySequence<byte>')) {
      return this.readArray(`System.Buffers.BuffersExtensions.ToArray((System.Buffers.ReadOnlySequence<byte>)${e})`, 'ReadOnlySequence<byte>');
    }
    throw new Error('Not a byte array, stream, list of bytes, (base64) string or HTTP content.');
  }

  private async is(type: string): Promise<boolean> {
    return this.isTrue(`${this.e} is ${type}`);
  }

  private async isTrue(expression: string): Promise<boolean> {
    try {
      return (await this.access.evaluate(expression)).result.trim().toLowerCase() === 'true';
    } catch {
      return false; // e.g. the type is not loaded in the program
    }
  }

  private async evaluate(expression: string): Promise<string> {
    return (await this.access.evaluate(expression)).result;
  }

  private async readArray(array: string, description: string): Promise<ReadResult> {
    const length = parseInteger(await this.evaluate(`${array}.Length`));
    const count = Math.min(length, this.options.maxBytes);
    const bytes = await readChunks(count, this.options, (offset, n) => this.evaluate(`System.Convert.ToBase64String(${array}, ${offset}, ${n})`));
    return { bytes, description, warning: limitWarning(length, this.options) };
  }

  /**
   * Like the Visual Studio visualizer: a seekable stream is read from the start and put back at its position,
   * any other stream is read from where it is, and the program cannot read those bytes any more.
   */
  private async readStream(stream: string): Promise<ReadResult> {
    if (!await this.isTrue(`${stream}.CanRead`)) {
      throw new Error('The stream cannot be read (CanRead is false).');
    }
    const name = (await this.evaluate(`${stream}.GetType().Name`)).replace(/^"|"$/g, '');
    const read = (n: number) => this.evaluate(`System.Convert.ToBase64String(new System.IO.BinaryReader(${stream}).ReadBytes(${n}))`);

    if (await this.isTrue(`${stream}.CanSeek`)) {
      const position = parseInteger(await this.evaluate(`${stream}.Position`));
      const length = parseInteger(await this.evaluate(`${stream}.Length`));
      const count = Math.min(length, this.options.maxBytes);
      try {
        const bytes = await readChunks(count, this.options, async (offset, n) => {
          await this.evaluate(`${stream}.Seek(${offset}, System.IO.SeekOrigin.Begin)`);
          return read(n);
        });
        return { bytes, description: name, warning: limitWarning(length, this.options) };
      } finally {
        await this.evaluate(`${stream}.Seek(${position}, System.IO.SeekOrigin.Begin)`);
      }
    }

    if (this.options.confirmConsume && !await this.options.confirmConsume(
      `The ${name} cannot seek: its bytes will be read from the current position and the program cannot read them any more.`)) {
      throw new CancelledError();
    }
    // Small chunks: a shortened result cannot be read again
    const size = Math.min(3072, Math.max(3, Math.floor(this.options.chunkSize / 3) * 3));
    const parts: Uint8Array[] = [];
    let total = 0;
    for (; ;) {
      checkCancelled(this.options);
      const n = Math.min(size, this.options.maxBytes - total);
      if (n <= 0) {
        break;
      }
      const part = fromBase64(unquote(await read(n)));
      parts.push(part);
      total += part.length;
      if (this.options.progress) {
        this.options.progress(total, 0);
      }
      if (part.length < n) {
        break;
      }
    }
    return {
      bytes: concat(parts, total),
      description: name,
      warning: total >= this.options.maxBytes ? `Only the first ${total} bytes were read (setting lsmsgpack.maxBytes).` : 'The stream cannot seek, the bytes read are gone for the program.'
    };
  }

  /**
   * A string holding the bytes (base64 like the Visual Studio visualizer, or hex...).
   */
  private async readText(text: string): Promise<ReadResult> {
    const length = parseInteger(await this.evaluate(`${text}.Length`));
    const size = Math.max(1024, this.options.chunkSize);
    let value = '';
    for (let offset = 0; offset < length;) {
      checkCancelled(this.options);
      const n = Math.min(size, length - offset);
      const part = unquote(await this.evaluate(`${text}.Substring(${offset}, ${n})`));
      if (part.length === 0) {
        throw new Error('The debugger returned an empty part of the string.');
      }
      value += part;
      offset += part.length;
    }
    return { bytes: bytesFromText(value), description: 'string' };
  }
}

function concat(parts: Uint8Array[], total: number): Uint8Array {
  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const part of parts) {
    bytes.set(part, offset);
    offset += part.length;
  }
  return bytes;
}

/**
 * JavaScript (js-debug: Node.js, Chrome, Edge): Uint8Array, Buffer, ArrayBuffer, DataView, other typed arrays, arrays of numbers and (base64/hex) strings.
 */
async function readJavaScript(access: DebugAccess, expression: string, options: ReadOptions): Promise<ReadResult> {
  const value = `(${expression})`;
  const kind = unquote((await access.evaluate(`(function(v){return typeof v==='string'?'string':v==null?'null':Object.prototype.toString.call(v).slice(8,-1)})(${value})`)).result);
  if (kind === 'null') {
    throw new Error('The value is null or undefined.');
  }
  if (kind === 'string') {
    return { bytes: bytesFromText(unquote((await access.evaluate(value)).result)), description: 'string' };
  }
  // The bytes as a Uint8Array (works without Buffer, in browsers too)
  const asBytes = `(function(v){return v instanceof ArrayBuffer?new Uint8Array(v):ArrayBuffer.isView(v)?new Uint8Array(v.buffer,v.byteOffset,v.byteLength):Uint8Array.from(v)})(${value})`;
  const length = parseInteger((await access.evaluate(`${asBytes}.length`)).result);
  const count = Math.min(length, options.maxBytes);
  const bytes = await readChunks(count, options, async (offset, n) => (await access.evaluate(
    `(function(u){var s='';for(var i=0;i<u.length;i+=8192){s+=String.fromCharCode.apply(null,u.subarray(i,i+8192))}return btoa(s)})(${asBytes}.subarray(${offset},${offset + n}))`)).result);
  return { bytes, description: kind, warning: limitWarning(length, options) };
}

/**
 * Python (debugpy): bytes, bytearray, memoryview, io.BytesIO (getvalue, the position is kept), lists of ints and (base64/hex) strings.
 */
async function readPython(access: DebugAccess, expression: string, options: ReadOptions): Promise<ReadResult> {
  const value = `(${expression})`;
  const kind = unquote((await access.evaluate(`type(${value}).__name__`)).result);
  if (kind === 'NoneType') {
    throw new Error('The value is None.');
  }
  if (kind === 'str') {
    return { bytes: bytesFromText(unquote((await access.evaluate(value)).result)), description: 'str' };
  }
  const asBytes = `(lambda v: v.getvalue() if hasattr(v, 'getvalue') else bytes(v))(${value})`;
  const length = parseInteger((await access.evaluate(`len(${asBytes})`)).result);
  const count = Math.min(length, options.maxBytes);
  const bytes = await readChunks(count, options, async (offset, n) => (await access.evaluate(
    `__import__('base64').b64encode(${asBytes}[${offset}:${offset + n}]).decode('ascii')`)).result);
  return { bytes, description: kind, warning: limitWarning(length, options) };
}

/**
 * The elements the debugger shows for the value (any language): numbers, also in groups like [0..99], or a string holding the bytes.
 */
export async function readFromVariable(access: DebugAccess, variable: DebugVariable, options: ReadOptions): Promise<ReadResult> {
  if (variable.variablesReference === 0) {
    // No children: a string with the bytes (base64, hex, b'...')
    return { bytes: bytesFromText(unquote(variable.value)), description: variable.type || 'text' };
  }

  const values: number[] = [];
  let warning: string | undefined;
  const add = async (reference: number, indexed: number | undefined): Promise<void> => {
    const pages: DebugVariable[][] = [];
    if (indexed && indexed > 0) {
      const pageSize = 1000;
      for (let start = 0; start < indexed && values.length < options.maxBytes; start += pageSize) {
        checkCancelled(options);
        pages.push(await access.variables(reference, start, Math.min(pageSize, indexed - start)));
      }
    } else {
      pages.push(await access.variables(reference));
    }
    for (const page of pages) {
      for (const child of page) {
        if (values.length >= options.maxBytes) {
          warning = `Only the first ${options.maxBytes} bytes were read (setting lsmsgpack.maxBytes).`;
          return;
        }
        const name = child.name.trim();
        if (/^\[\d+\s*(\.\.|-)\s*\d+\]$/.test(name) && child.variablesReference > 0) {
          await add(child.variablesReference, child.indexedVariables); // a group of elements
        } else if (/^\[?\d+\]?$/.test(name)) {
          values.push(parseByte(child.value, name));
          if (options.progress) {
            options.progress(values.length, 0);
          }
        }
      }
    }
  };
  await add(variable.variablesReference, variable.indexedVariables);
  if (values.length === 0) {
    // e.g. a string with children (its characters), or an object without elements
    try {
      return { bytes: bytesFromText(unquote(variable.value)), description: variable.type || 'text' };
    } catch {
      throw new Error(`No byte elements found in ${variable.name} (${variable.value}).`);
    }
  }
  return { bytes: Uint8Array.from(values), description: variable.type || 'elements', warning };
}

/**
 * An element as debuggers show it: 26, 0x1a, 0x1A '\x1a', 26 '\u001a', (byte)26...
 */
export function parseByte(value: string, name: string): number {
  const match = /^\s*(?:\(\w+\)\s*)?(0x[0-9a-fA-F]+|\d+)/.exec(value);
  const number = match ? Number(match[1]) : NaN;
  if (!Number.isInteger(number) || number < 0 || number > 255) {
    throw new Error(`Element ${name} is not a byte: ${value}`);
  }
  return number;
}
