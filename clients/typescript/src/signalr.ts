// A SignalR hub protocol for @microsoft/signalr on this client's reader and writer: the envelope of Microsoft's MessagePack protocol (a VarInt length prefix and an array per message),
// the values (arguments, stream items, results) as LsMsgPack's hub protocol (LsMsgPack.SignalR) writes and reads them. Structurally an IHubProtocol: nothing of @microsoft/signalr is imported.
import { Decoder } from './decoder.js';
import type { DecodeOptions } from './decoder.js';
import { Writer } from './writer.js';
import type { EncodeOptions } from './writer.js';
import { MsgPackReader } from './reader.js';
import { MsgPackError } from './errors.js';
import { Schema } from './schema.js';

/** The message types of SignalR (MessageType of @microsoft/signalr). */
export const HubMessageType = {
  Invocation: 1,
  StreamItem: 2,
  Completion: 3,
  StreamInvocation: 4,
  CancelInvocation: 5,
  Ping: 6,
  Close: 7,
  Ack: 8,
  Sequence: 9,
} as const;

/** The messages as @microsoft/signalr has them (HubMessage): its types are accepted and returned as they are. */
export interface HubMessageLike {
  readonly type: number;
  readonly headers?: Record<string, string>;
  readonly invocationId?: string;
  readonly target?: string;
  readonly arguments?: unknown[];
  readonly streamIds?: string[];
  readonly item?: unknown;
  readonly error?: string | null;
  readonly result?: unknown;
  readonly allowReconnect?: boolean;
  readonly sequenceId?: number;
}

/** ILogger of @microsoft/signalr. */
export interface HubLoggerLike {
  log(logLevel: number, message: string): void;
}

export interface HubProtocolOptions extends Omit<DecodeOptions, 'indexedSchema'>, EncodeOptions {
  /**
   * 'lsmsgpack' (default): the server's AddLsMsgPackProtocol(), LsMsgPack's values (decimals, DateTimes with their kind, type ids). 'lsmsgpack-indexed': the server's
   * AddLsMsgPackIndexedProtocol(), the same with the indexed schema in each value the server writes (smaller for lists of objects; the values this client writes stay plain).
   * 'messagepack': the server's AddLsMsgPackAsMessagePackProtocol() or Microsoft's AddMessagePackProtocol() (the values of MessagePack-CSharp: decimals and Guids as text,
   * dates as timestamps, enums by name).
   */
  name?: 'lsmsgpack' | 'lsmsgpack-indexed' | 'messagepack';
}

const ErrorResult = 1;
const VoidResult = 2;
const NonVoidResult = 3;
const LogInformation = 2; // LogLevel.Information
const TransferFormatBinary = 2; // TransferFormat.Binary
const KeptSchemas = 16; // 'lsmsgpack-indexed': the schemas read last, one instance per schema (its plans are made once)

/**
 * A hub protocol for `new HubConnectionBuilder().withHubProtocol(new LsMsgPackHubProtocol())` of @microsoft/signalr (version 8 and up: protocol version 2).
 * Values are plain MsgPack without a schema (SignalR writes a message once for every connection it goes to): objects have the .NET property names (the server reads names
 * ignoring case, so camelCase objects are fine to send), a type id becomes `$type`, Guids are text, decimals numbers (DecodeOptions.decimal), DateTimes Dates.
 * Each value is read and written on its own (its references, extension type 5, point into it), as the .NET protocol does.
 * 'lsmsgpack-indexed': each value is nil, [schema, body] or [nil, value without a schema]; the server writes the first, this client the last (it writes plain values).
 * Objects keep the .NET names there too (DecodeOptions.propertyNames 'dotnet' unless given) and get the declared types and defaults of the described schema.
 */
export class LsMsgPackHubProtocol {
  readonly name: string;
  readonly version = 2;
  readonly transferFormat = TransferFormatBinary;
  private readonly decoder: Decoder;
  private readonly writer: Writer;
  /** 'lsmsgpack-indexed': the decoder of [schema, body] values. */
  private readonly schemaDecoder: Decoder | null;
  private readonly schemas: Schema[] = [];
  private readonly inlineSchema = (bytes: Uint8Array): Schema => this.schemaOf(bytes);

  constructor(options: HubProtocolOptions = {}) {
    this.name = options.name ?? 'lsmsgpack';
    this.decoder = new Decoder({ ...options, indexedSchema: false });
    // the .NET names by default, as the values without a schema have them: switching the name does not rename the properties
    this.schemaDecoder = this.name === 'lsmsgpack-indexed' ? new Decoder({ propertyNames: 'dotnet', ...options, indexedSchema: true }) : null;
    this.writer = new Writer(options.maxDepth ?? 256, options.models, options.generateCode ?? true, options.references === 'preserve');
  }

  parseMessages(input: string | ArrayBuffer | Uint8Array, logger?: HubLoggerLike | null): HubMessageLike[] {
    if (typeof input === 'string') throw new Error('Invalid input for the LsMsgPack hub protocol: expected an ArrayBuffer.');
    const bytes = input instanceof Uint8Array ? input : new Uint8Array(input);
    const messages: HubMessageLike[] = [];
    let pos = 0;
    while (pos < bytes.length) {
      // the length prefix: 7 bits per byte, the lowest first, at most 5 bytes (2 GB)
      let length = 0, shift = 0, b: number;
      do {
        if (pos >= bytes.length) throw new Error('Cannot read message size.');
        b = bytes[pos++];
        length += (b & 0x7f) * 2 ** shift;
        shift += 7;
      } while ((b & 0x80) !== 0 && shift < 35);
      if ((b & 0x80) !== 0 || length > 0x7fffffff) throw new Error('Messages bigger than 2GB are not supported.');
      if (bytes.length - pos < length) throw new Error('Incomplete message.');
      const message = this.parseMessage(bytes, pos, pos + length, logger);
      if (message !== null) messages.push(message);
      pos += length;
    }
    return messages;
  }

  writeMessage(message: HubMessageLike): ArrayBuffer {
    const w = this.writer;
    w.truncate(0);
    try {
      this.writeBody(message, w);
      const length = w.length;
      let prefix = 1;
      for (let n = length; n > 0x7f; n = Math.floor(n / 128)) prefix++;
      const out = new Uint8Array(prefix + length);
      let n = length;
      for (let t = 0; t < prefix; t++) {
        out[t] = (n & 0x7f) | (t + 1 < prefix ? 0x80 : 0);
        n = Math.floor(n / 128);
      }
      out.set(w.bytesView(), prefix);
      return out.buffer;
    } finally {
      w.truncate(0);
    }
  }

  private writeBody(m: HubMessageLike, w: Writer): void {
    switch (m.type) {
      case HubMessageType.Invocation:
      case HubMessageType.StreamInvocation:
        w.arrayHeader(6);
        w.number(m.type);
        writeHeaders(m.headers, w);
        if (m.invocationId) w.string(m.invocationId); else w.write(null, 0);
        w.string(m.target ?? '');
        this.writeValues(m.arguments ?? [], w);
        writeStrings(m.streamIds, w);
        return;
      case HubMessageType.StreamItem:
        w.arrayHeader(4);
        w.number(m.type);
        writeHeaders(m.headers, w);
        w.string(m.invocationId ?? '');
        this.writeValue(m.item, w);
        return;
      case HubMessageType.Completion: {
        const kind = m.error ? ErrorResult : m.result !== undefined ? NonVoidResult : VoidResult;
        w.arrayHeader(kind === VoidResult ? 4 : 5);
        w.number(m.type);
        writeHeaders(m.headers, w);
        w.string(m.invocationId ?? '');
        w.number(kind);
        if (kind === ErrorResult) w.string(m.error!);
        else if (kind === NonVoidResult) this.writeValue(m.result, w);
        return;
      }
      case HubMessageType.CancelInvocation:
        w.arrayHeader(3);
        w.number(m.type);
        writeHeaders(m.headers, w);
        w.string(m.invocationId ?? '');
        return;
      case HubMessageType.Ping:
        w.arrayHeader(1);
        w.number(m.type);
        return;
      case HubMessageType.Close:
        w.arrayHeader(3);
        w.number(m.type);
        if (m.error) w.string(m.error); else w.write(null, 0);
        w.write(m.allowReconnect === true, 0);
        return;
      case HubMessageType.Ack:
      case HubMessageType.Sequence:
        w.arrayHeader(2);
        w.number(m.type);
        w.number(m.sequenceId ?? 0);
        return;
      default:
        throw new Error('Invalid message type.');
    }
  }

  private writeValues(values: unknown[], w: Writer): void {
    w.arrayHeader(values.length);
    for (let t = 0; t < values.length; t++) this.writeValue(values[t], w);
  }

  /** A value on its own: its references (EncodeOptions.references 'preserve') are paths from it. 'lsmsgpack-indexed': [nil, value] (no schema), null as nil. */
  private writeValue(value: unknown, w: Writer): void {
    if (this.schemaDecoder !== null && value !== null && value !== undefined) {
      w.arrayHeader(2);
      w.write(null, 0);
    }
    w.root(0);
    w.write(value, 0);
  }

  /** One message in bytes[start, end): Microsoft's MessagePack protocol's fields. Null for a message type this protocol does not know (ignored, logged). */
  private parseMessage(bytes: Uint8Array, start: number, end: number, logger?: HubLoggerLike | null): HubMessageLike | null {
    const data = end === bytes.length ? bytes : bytes.subarray(0, end);
    const r = new MsgPackReader(data, undefined, start);
    const count = r.arrayLength();
    if (count < 1) throw new Error('Invalid payload.');
    const type = r.value();
    switch (type) {
      case HubMessageType.Invocation:
      case HubMessageType.StreamInvocation: {
        if (count < 5) throw new Error(`Invalid payload for ${type === HubMessageType.Invocation ? 'Invocation' : 'StreamInvocation'} message.`);
        const headers = readHeaders(r);
        const invocationId = r.value();
        const target = r.value();
        if (typeof target !== 'string' || target === '') throw new Error('Invalid payload: no target.');
        const args = this.readValues(data, r, end);
        const streamIds = count > 5 ? readStrings(r) : [];
        const message: { -readonly [K in keyof HubMessageLike]: HubMessageLike[K] } = { type, headers, target, arguments: args, streamIds };
        if (typeof invocationId === 'string' && invocationId !== '') message.invocationId = invocationId;
        return message;
      }
      case HubMessageType.StreamItem: {
        if (count < 4) throw new Error('Invalid payload for StreamItem message.');
        const headers = readHeaders(r);
        const invocationId = r.value() as string;
        const item = this.readValue(data, r, end);
        return { type, headers, invocationId, item };
      }
      case HubMessageType.Completion: {
        if (count < 4) throw new Error('Invalid payload for Completion message.');
        const headers = readHeaders(r);
        const invocationId = r.value() as string;
        const kind = r.value();
        if (kind !== VoidResult && count < 5) throw new Error('Invalid payload for Completion message.');
        let error: string | undefined;
        let result: unknown;
        if (kind === ErrorResult) error = r.value() as string;
        else if (kind === NonVoidResult) result = this.readValue(data, r, end);
        return { type, headers, invocationId, error, result };
      }
      case HubMessageType.CancelInvocation: {
        const headers = readHeaders(r);
        return { type, headers, invocationId: r.value() as string };
      }
      case HubMessageType.Ping:
        return { type };
      case HubMessageType.Close: {
        if (count < 2) throw new Error('Invalid payload for Close message.');
        const error = r.value() as string | null;
        const allowReconnect = count >= 3 ? r.value() === true : undefined;
        return { type, error: error ?? undefined, allowReconnect };
      }
      case HubMessageType.Ack:
      case HubMessageType.Sequence: {
        if (count < 2) throw new Error(`Invalid payload for ${type === HubMessageType.Ack ? 'Ack' : 'Sequence'} message.`);
        const sequenceId = r.value();
        return { type, sequenceId: typeof sequenceId === 'bigint' ? Number(sequenceId) : sequenceId as number };
      }
      default:
        logger?.log(LogInformation, `Unknown message type '${String(type)}' ignored.`);
        return null;
    }
  }

  private readValues(data: Uint8Array, r: MsgPackReader, end: number): unknown[] {
    const n = r.arrayLength();
    if (n < 0) throw new Error('Invalid payload: the arguments are not an array.');
    const values = new Array<unknown>(n);
    for (let t = 0; t < n; t++) values[t] = this.readValue(data, r, end);
    return values;
  }

  /** A value on its own (its references point into it), read by the decoder from where the reader is; the reader continues after it. */
  private readValue(data: Uint8Array, r: MsgPackReader, end: number): unknown {
    let pos = r.pos;
    if (this.schemaDecoder !== null && pos < end && data[pos] !== 0xc0) {
      if (data[pos] !== 0x92) throw new MsgPackError(`Expected a value of the lsmsgpack-indexed protocol: nil, [schema, value] or [nil, value], found 0x${data[pos].toString(16)}.`, pos);
      pos++;
      if (data[pos] !== 0xc0) {
        const decoded = this.schemaDecoder.decodeAt(data, pos, end, undefined, this.inlineSchema);
        r.pos = decoded.end;
        return decoded.value;
      }
      pos++;
    }
    const read = this.decoder.readValue(data, pos, end, null);
    r.pos = read.end;
    return read.value;
  }

  /** The schema of the bytes: a kept instance when one has them (most messages repeat a few schemas), otherwise a new one (a copy: the message's buffer is not kept). */
  private schemaOf(bytes: Uint8Array): Schema {
    const kept = this.schemas;
    for (let t = kept.length - 1; t >= 0; t--) {
      if (sameBytes(kept[t].bytes, bytes)) {
        if (t !== kept.length - 1) kept.push(kept.splice(t, 1)[0]); // the most recent last
        return kept[kept.length - 1];
      }
    }
    const schema = new Schema(bytes.slice());
    if (kept.length === KeptSchemas) kept.shift();
    kept.push(schema);
    return schema;
  }
}

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let t = a.length - 1; t >= 0; t--) if (a[t] !== b[t]) return false;
  return true;
}

function writeHeaders(headers: Record<string, string> | undefined, w: Writer): void {
  if (headers === undefined || headers === null) return w.mapHeader(0);
  const keys = Object.keys(headers);
  w.mapHeader(keys.length);
  for (let t = 0; t < keys.length; t++) {
    w.string(keys[t]);
    w.string(String(headers[keys[t]]));
  }
}

function writeStrings(values: string[] | undefined, w: Writer): void {
  if (values === undefined || values === null) return w.arrayHeader(0);
  w.arrayHeader(values.length);
  for (let t = 0; t < values.length; t++) w.string(values[t]);
}

function readHeaders(r: MsgPackReader): Record<string, string> {
  const n = r.mapLength();
  if (n < 0) throw new Error('Invalid headers.');
  const headers: Record<string, string> = {};
  for (let t = 0; t < n; t++) {
    const key = r.value();
    const value = r.value();
    if (typeof key !== 'string' || typeof value !== 'string') throw new MsgPackError('Invalid headers.', r.pos);
    headers[key] = value;
  }
  return headers;
}

function readStrings(r: MsgPackReader): string[] {
  const n = r.arrayLength();
  if (n < 0) throw new Error('Invalid payload: the stream ids are not an array.');
  const values = new Array<string>(n);
  for (let t = 0; t < n; t++) values[t] = r.value() as string;
  return values;
}
