// Where MsgPackStreamReader reads from: sequential sources (a fetch body, a ReadableStream, an async iterable of chunks, bytes) and random access sources
// (bytes, a Blob or File, a url with HTTP range requests, a Node FileHandle), which the index needs to go to an entry directly.

import { MsgPackError } from './errors.js';

/** Bytes at any position: the reader of a stream with an index goes to an entry without reading what comes before it (readIndexes, seek). */
export interface RandomAccessSource {
  /** The length of the stream in bytes. */
  size(): Promise<number>;
  /** The bytes from `offset`, `length` of them (fewer only at the end of the stream). */
  read(offset: number, length: number): Promise<Uint8Array>;
  /** The bytes from `offset` to the end, in chunks. */
  stream(offset: number): SequentialSource;
}

/** Bytes in order, in chunks of any size. */
export type SequentialSource = ReadableStream<Uint8Array> | AsyncIterable<Uint8Array> | Iterable<Uint8Array>;

/** What MsgPackStreamReader reads: a sequential source, the body of a Response, bytes or a Blob (random access), or any RandomAccessSource. */
export type StreamSource = SequentialSource | Response | Uint8Array | ArrayBuffer | Blob | RandomAccessSource;

/** Pulls the chunks of a sequential source (internal). */
export interface Pull {
  /** The next chunk, null at the end. */
  next(): Promise<Uint8Array | null>;
  /** Stops reading (releases a ReadableStream). */
  close(): Promise<void>;
}

const Block = 64 * 1024;

export function isRandomAccess(source: unknown): source is RandomAccessSource {
  const s = source as RandomAccessSource;
  return typeof s === 'object' && s !== null && typeof s.size === 'function' && typeof s.read === 'function' && typeof s.stream === 'function';
}

/** The random access source of bytes or a Blob, undefined for other sources. */
export function randomAccessOf(source: StreamSource): RandomAccessSource | undefined {
  if (isRandomAccess(source)) return source;
  if (source instanceof Uint8Array) return bytesSource(source);
  if (source instanceof ArrayBuffer) return bytesSource(new Uint8Array(source));
  if (typeof Blob !== 'undefined' && source instanceof Blob) return blobSource(source);
  return undefined;
}

/** Pulls the chunks of a sequential source (or of a random access source from the start). */
export function pullOf(source: StreamSource): Pull {
  const random = randomAccessOf(source);
  if (random !== undefined) return pullOf(random.stream(0));
  if (typeof Response !== 'undefined' && source instanceof Response) {
    if (source.body === null) return pullOf([]);
    return pullOf(source.body);
  }
  if (typeof ReadableStream !== 'undefined' && source instanceof ReadableStream) {
    const reader = source.getReader();
    return {
      async next() {
        for (;;) {
          const { done, value } = await reader.read();
          if (done) return null;
          if (value.length > 0) return value;
        }
      },
      async close() {
        try {
          await reader.cancel();
        } catch {
          // already closed or errored
        }
        reader.releaseLock();
      },
    };
  }
  const s = source as AsyncIterable<Uint8Array> & Iterable<Uint8Array>;
  if (typeof s[Symbol.asyncIterator] === 'function') {
    const it = s[Symbol.asyncIterator]();
    return {
      async next() {
        for (;;) {
          const r = await it.next();
          if (r.done) return null;
          if (r.value.length > 0) return r.value;
        }
      },
      async close() {
        await it.return?.();
      },
    };
  }
  if (typeof s[Symbol.iterator] === 'function') {
    const it = s[Symbol.iterator]();
    return {
      async next() {
        for (;;) {
          const r = it.next();
          if (r.done) return null;
          if (r.value.length > 0) return r.value;
        }
      },
      async close() {
        it.return?.();
      },
    };
  }
  throw new TypeError('The source of a stream is a ReadableStream, an (async) iterable of Uint8Array, a Response, bytes, a Blob or a RandomAccessSource.');
}

/** Bytes in memory as a random access source. */
export function bytesSource(bytes: Uint8Array): RandomAccessSource {
  return {
    size: async () => bytes.length,
    read: async (offset, length) => bytes.subarray(offset, Math.min(bytes.length, offset + length)),
    stream: (offset) => [bytes.subarray(offset)],
  };
}

/** A Blob or File (browsers, Node 18+): read in slices, nothing else is loaded. */
export function blobSource(blob: Blob): RandomAccessSource {
  return {
    size: async () => blob.size,
    read: async (offset, length) => new Uint8Array(await blob.slice(offset, offset + length).arrayBuffer()),
    stream: (offset) => blob.slice(offset).stream() as ReadableStream<Uint8Array>,
  };
}

/** The part of Node's FileHandle (fs/promises open) the file source uses: no import of node:fs here. */
export interface FileHandleLike {
  read(buffer: Uint8Array, offset: number, length: number, position: number): Promise<{ bytesRead: number }>;
  stat(): Promise<{ size: number | bigint }>;
}

/** A file opened with Node's fs/promises open(path): read at positions. The caller closes the handle. */
export function fileSource(handle: FileHandleLike): RandomAccessSource {
  const read = async (offset: number, length: number): Promise<Uint8Array> => {
    const buffer = new Uint8Array(length);
    let done = 0;
    while (done < length) {
      const { bytesRead } = await handle.read(buffer, done, length - done, offset + done);
      if (bytesRead <= 0) break;
      done += bytesRead;
    }
    return done === length ? buffer : buffer.subarray(0, done);
  };
  return {
    size: async () => Number((await handle.stat()).size),
    read,
    stream: (offset) => ({
      async *[Symbol.asyncIterator]() {
        for (let at = offset; ; ) {
          const chunk = await read(at, Block);
          if (chunk.length === 0) return;
          at += chunk.length;
          yield chunk;
          if (chunk.length < Block) return;
        }
      },
    }),
  };
}

export interface HttpSourceOptions {
  /** The fetch to use (default: globalThis.fetch). */
  fetch?: typeof globalThis.fetch;
  /** Headers of every request (Authorization...). */
  headers?: HeadersInit;
}

/**
 * A url read with HTTP range requests (a static file, or an endpoint with range processing, e.g. ASP.NET Core's File(..., enableRangeProcessing: true)):
 * the index from the end, then only the entries that are read. A server that ignores the Range header is refused (it would send the whole stream every time).
 */
export function httpSource(url: string | URL, options: HttpSourceOptions = {}): RandomAccessSource {
  const fetchFn = options.fetch ?? globalThis.fetch.bind(globalThis);
  let known: number | undefined;
  const request = async (range: string): Promise<Response> => {
    const headers = new Headers(options.headers);
    headers.set('Range', range);
    const response = await fetchFn(url, { headers });
    if (response.status === 416) { // the range starts at or after the end
      await response.body?.cancel();
      return response;
    }
    if (response.status !== 206) {
      await response.body?.cancel();
      throw new MsgPackError(`${url} answered a range request with ${response.status} ${response.statusText}, not 206 Partial Content: the server does not read ranges.`);
    }
    const total = /\/(\d+)\s*$/.exec(response.headers.get('Content-Range') ?? '');
    if (total !== null) known = Number(total[1]);
    return response;
  };
  return {
    async size() {
      if (known === undefined) {
        const response = await request('bytes=0-0');
        await response.body?.cancel();
        if (known === undefined) {
          if (response.status === 416) return 0;
          throw new MsgPackError(`${url} answered a range request without the length of the stream (Content-Range).`);
        }
      }
      return known;
    },
    async read(offset, length) {
      if (length <= 0) return new Uint8Array(0);
      const response = await request(`bytes=${offset}-${offset + length - 1}`);
      if (response.status === 416) return new Uint8Array(0);
      return new Uint8Array(await response.arrayBuffer());
    },
    stream(offset) {
      return {
        async *[Symbol.asyncIterator]() {
          const response = await request(`bytes=${offset}-`);
          if (response.status === 416 || response.body === null) return;
          const reader = response.body.getReader();
          try {
            for (;;) {
              const { done, value } = await reader.read();
              if (done) return;
              yield value;
            }
          } finally {
            await reader.cancel().catch(() => undefined);
          }
        },
      };
    },
  };
}
