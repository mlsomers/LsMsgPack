// Runs the inspector (Server/LsMsgPackInspector, .NET) and talks to it: one JSON request per line on stdin, one response per line on stdout.
// No dependency on the vscode module, so it can be tested on its own.

import { ChildProcess, spawn } from 'child_process';

export type EndianAction = 'SwapIfCurrentSystemIsLittleEndian' | 'NeverSwap' | 'AlwaysSwap';
export type ObjectsMode = 'auto' | 'show' | 'hide';
/** How sure the inspector must be that the data was written from objects to show them in the 'auto' mode */
export type ObjectConfidence = 'certain' | 'high' | 'medium' | 'low';

export interface LoadSettings {
  continueOnError: boolean;
  endian: EndianAction;
  /** 0: no limit */
  displayLimit: number;
  objects: ObjectsMode;
  /** Default 'high' */
  showObjectsAt?: ObjectConfidence;
}

interface Pending {
  resolve(value: unknown): void;
  reject(error: Error): void;
}

export class InspectorClient {
  private process: ChildProcess | undefined;
  private buffer = '';
  private stderr = '';
  private nextId = 1;
  private started = 0;
  private readonly pending = new Map<number, Pending>();

  /**
   * @param dotnet The dotnet executable
   * @param assembly LsMsgPackInspector.dll
   */
  constructor(private readonly dotnet: string, private readonly assembly: string) {
  }

  /**
   * Changes when the process is started again (after it stopped): the documents it had are gone, they have to be loaded with their data again.
   * 0 while it is not running.
   */
  get generation(): number {
    return this.process ? this.started : 0;
  }

  /** Reads the data (base64, or undefined to read the document's data again with other settings). */
  load(doc: string, data: string | undefined, settings: LoadSettings): Promise<unknown> {
    return this.request({ method: 'load', doc, data, ...settings });
  }

  search(doc: string, text: string, matchCase: boolean): Promise<unknown> {
    return this.request({ method: 'search', doc, text, matchCase });
  }

  close(doc: string): Promise<unknown> {
    if (!this.process) {
      return Promise.resolve(true); // nothing to forget
    }
    return this.request({ method: 'close', doc });
  }

  version(): Promise<string> {
    return this.request({ method: 'version' }) as Promise<string>;
  }

  dispose(): void {
    const process = this.process;
    this.process = undefined;
    if (process) {
      process.stdin?.end();
      process.kill();
    }
    this.failAll(new Error('The inspector was stopped.'));
  }

  private request(body: object): Promise<unknown> {
    const process = this.start();
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      process.stdin!.write(JSON.stringify({ id, ...body }) + '\n', (error) => {
        if (error) {
          this.pending.delete(id);
          reject(error);
        }
      });
    });
  }

  private start(): ChildProcess {
    if (this.process) {
      return this.process;
    }
    this.buffer = '';
    this.stderr = '';
    const process = spawn(this.dotnet, [this.assembly], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.process = process;
    this.started++;
    process.stdout!.setEncoding('utf8');
    process.stdout!.on('data', (text: string) => {
      if (this.process === process) {
        this.receive(text);
      }
    });
    process.stderr!.setEncoding('utf8');
    process.stderr!.on('data', (text: string) => {
      this.stderr = (this.stderr + text).slice(-4000);
    });
    // The requests belong to the running process: one that was stopped (dispose) already failed them
    process.on('error', (error: NodeJS.ErrnoException) => {
      if (this.process !== process) {
        return;
      }
      this.process = undefined;
      const message = error.code === 'ENOENT'
        ? `Could not start "${this.dotnet}": the MsgPack inspector needs the .NET runtime (8 or later). Install it, or set "lsmsgpack.dotnetPath".`
        : `Could not start the MsgPack inspector: ${error.message}`;
      this.failAll(new Error(message));
    });
    process.on('exit', (code) => {
      if (this.process !== process) {
        return;
      }
      this.process = undefined;
      // The next request starts it again (the documents are loaded again by the panels)
      this.failAll(new Error(`The MsgPack inspector stopped (exit code ${code}).${this.stderr ? '\n' + this.stderr.trim() : ''}`));
    });
    return process;
  }

  private receive(text: string): void {
    this.buffer += text;
    let newline: number;
    while ((newline = this.buffer.indexOf('\n')) >= 0) {
      const line = this.buffer.substring(0, newline).trim();
      this.buffer = this.buffer.substring(newline + 1);
      if (line.length === 0) {
        continue;
      }
      let response: { id?: number; result?: unknown; error?: string };
      try {
        response = JSON.parse(line);
      } catch {
        continue; // not a response (e.g. something the runtime wrote)
      }
      if (typeof response.id !== 'number') {
        continue;
      }
      const pending = this.pending.get(response.id);
      if (!pending) {
        continue;
      }
      this.pending.delete(response.id);
      if (response.error !== undefined) {
        pending.reject(new Error(response.error));
      } else {
        pending.resolve(response.result);
      }
    }
  }

  private failAll(error: Error): void {
    const pending = Array.from(this.pending.values());
    this.pending.clear();
    for (const p of pending) {
      p.reject(error);
    }
  }
}
