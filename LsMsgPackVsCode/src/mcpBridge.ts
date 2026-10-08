// The bridge between the MCP server (LsMsgPackMcpServer, .NET) and the debugger of VS Code: a server on 127.0.0.1 that answers
// one JSON line per connection ({"token", "method", "params"} → {"result"} or {"error"}), and a lock file that tells MCP servers
// started by other clients (Claude Code, Cursor...) where it is. The Visual Studio package speaks the same protocol.
// No dependency on the vscode module, so it can be tested on its own.

import * as crypto from 'crypto';
import * as fs from 'fs';
import * as net from 'net';
import * as os from 'os';
import * as path from 'path';

export interface FrameInfo {
  name: string;
  source?: string;
  line?: number;
  session?: string;
}

export interface SessionInfo {
  id: string;
  name: string;
  type: string;
  active: boolean;
}

export interface StatusResult {
  ide: string;
  name: string;
  workspaceFolders: string[];
  sessions: SessionInfo[];
  frame?: FrameInfo;
  /** "paths": only variable and member paths are read, "any": any expression */
  expressions: 'paths' | 'any';
}

export interface VariableInfo {
  scope?: string;
  name: string;
  type?: string;
  value?: string;
  evaluateName?: string;
}

export interface LocalsResult {
  frame?: FrameInfo;
  variables: VariableInfo[];
  truncated?: boolean;
}

export interface ReadRequest {
  expression: string;
  /** Read the schema with this id from the SchemaStore that the expression refers to */
  schemaId?: string;
  maxBytes?: number;
}

export interface ReadResponse {
  base64: string;
  description: string;
  length: number;
  session: string;
  warning?: string;
}

/**
 * What the IDE does for the requests (the extension implements it with the debug API).
 */
export interface BridgeHandler {
  status(): Promise<StatusResult>;
  locals(maxVariables: number): Promise<LocalsResult>;
  read(request: ReadRequest): Promise<ReadResponse>;
}

/** Where the lock files are: ~/.lsmsgpack/ide, or LSMSGPACK_IDE_DIR */
export function lockDirectory(): string {
  return process.env.LSMSGPACK_IDE_DIR || path.join(os.homedir(), '.lsmsgpack', 'ide');
}

/**
 * The command of the .NET tool (dotnet tool install -g LsMsgPack.Mcp) when it is installed: a configuration using it stays valid when the
 * extension is updated, the path of the server that comes with the extension has the extension's version in it.
 * "lsmsgpack-mcp" when it is on the PATH, otherwise its path in the folder of global tools (~/.dotnet/tools, not always on the PATH of VS Code).
 */
export function findMcpTool(env: NodeJS.ProcessEnv = process.env, platform: string = process.platform, exists: (file: string) => boolean = fs.existsSync): string | undefined {
  const file = platform === 'win32' ? 'lsmsgpack-mcp.exe' : 'lsmsgpack-mcp';
  const pathVariable = env.PATH ?? env.Path ?? '';
  const pathPath = platform === 'win32' ? path.win32 : path.posix;
  for (const folder of pathVariable.split(platform === 'win32' ? ';' : ':')) {
    if (folder && exists(pathPath.join(folder, file))) {
      return 'lsmsgpack-mcp';
    }
  }
  const home = env.DOTNET_CLI_HOME || (platform === 'win32' ? env.USERPROFILE : env.HOME) || os.homedir();
  const tool = pathPath.join(home, '.dotnet', 'tools', file);
  return exists(tool) ? tool : undefined;
}

export interface LockInfo {
  ide: string;
  name: string;
  pid: number;
  workspaceFolders: string[];
}

/** Longer requests are refused (they only hold an expression) */
const maxRequestLength = 64 * 1024;

export class BridgeServer {
  private readonly server: net.Server;
  private lockFile: string | undefined;
  private info: LockInfo | undefined;
  readonly token = crypto.randomBytes(24).toString('hex');

  constructor(private readonly handler: BridgeHandler) {
    this.server = net.createServer((socket) => this.serve(socket));
  }

  /** Listens on a free port of 127.0.0.1 */
  start(): Promise<number> {
    return new Promise((resolve, reject) => {
      this.server.once('error', reject);
      this.server.listen(0, '127.0.0.1', () => {
        this.server.off('error', reject);
        resolve(this.port);
      });
    });
  }

  get port(): number {
    const address = this.server.address();
    return address && typeof address === 'object' ? address.port : 0;
  }

  /**
   * Writes (or rewrites, e.g. when the workspace folders change) the lock file, readable by the user only.
   * Lock files of processes that are gone are removed.
   */
  writeLockFile(info: LockInfo): string {
    this.info = info;
    const dir = lockDirectory();
    fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
    removeStaleLockFiles(dir);
    const file = this.lockFile ?? path.join(dir, `${info.pid}-${this.port}.json`);
    const content = JSON.stringify({ ide: info.ide, name: info.name, pid: info.pid, port: this.port, token: this.token, workspaceFolders: info.workspaceFolders });
    // Written aside and renamed, so a reader never sees half a file
    const temp = `${file}.${process.pid}.tmp`;
    fs.writeFileSync(temp, content, { encoding: 'utf8', mode: 0o600 });
    fs.renameSync(temp, file);
    this.lockFile = file;
    return file;
  }

  updateWorkspaceFolders(folders: string[]): void {
    if (this.info && this.lockFile) {
      this.writeLockFile({ ...this.info, workspaceFolders: folders });
    }
  }

  dispose(): void {
    this.server.close();
    if (this.lockFile) {
      try {
        fs.unlinkSync(this.lockFile);
      } catch {
        // already gone
      }
      this.lockFile = undefined;
    }
  }

  private serve(socket: net.Socket): void {
    let buffer = '';
    let answered = false;
    socket.setEncoding('utf8');
    socket.setTimeout(10000, () => socket.destroy()); // until the request arrives
    const answer = (response: object): void => {
      if (!answered) {
        answered = true;
        socket.end(JSON.stringify(response) + '\n');
      }
    };
    socket.on('error', () => undefined);
    socket.on('data', (text: string) => {
      if (answered) {
        return;
      }
      buffer += text;
      if (buffer.length > maxRequestLength) {
        answer({ error: 'Request too long.' });
        return;
      }
      const newline = buffer.indexOf('\n');
      if (newline < 0) {
        return;
      }
      socket.setTimeout(0); // reading a large value may take a while
      this.handle(buffer.substring(0, newline)).then(answer, (error) => answer({ error: messageOf(error) }));
    });
  }

  /** Handles one request line (public for tests). */
  async handle(line: string): Promise<object> {
    let request: { token?: unknown; method?: unknown; params?: Record<string, unknown> };
    try {
      request = JSON.parse(line);
    } catch {
      return { error: 'Not JSON.' };
    }
    if (typeof request.token !== 'string' || !sameToken(request.token, this.token)) {
      return { error: 'Wrong token.' };
    }
    const params = request.params ?? {};
    try {
      switch (request.method) {
        case 'status':
          return { result: await this.handler.status() };
        case 'locals':
          return { result: await this.handler.locals(typeof params.maxVariables === 'number' ? params.maxVariables : 300) };
        case 'read': {
          if (typeof params.expression !== 'string' || params.expression.trim().length === 0) {
            return { error: 'The expression is missing.' };
          }
          return {
            result: await this.handler.read({
              expression: params.expression,
              schemaId: typeof params.schemaId === 'string' ? params.schemaId : undefined,
              maxBytes: typeof params.maxBytes === 'number' ? params.maxBytes : undefined
            })
          };
        }
        default:
          return { error: `Unknown method: ${String(request.method)}` };
      }
    } catch (error) {
      return { error: messageOf(error) };
    }
  }
}

function sameToken(given: string, expected: string): boolean {
  const a = Buffer.from(given, 'utf8');
  const b = Buffer.from(expected, 'utf8');
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function removeStaleLockFiles(dir: string): void {
  let files: string[];
  try {
    files = fs.readdirSync(dir);
  } catch {
    return;
  }
  for (const file of files) {
    if (!file.endsWith('.json')) {
      continue;
    }
    const full = path.join(dir, file);
    try {
      const pid = JSON.parse(fs.readFileSync(full, 'utf8')).pid;
      if (typeof pid === 'number' && pid > 0 && !isRunning(pid)) {
        fs.unlinkSync(full);
      }
    } catch {
      // being written by another window, or not ours
    }
  }
}

function isRunning(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EPERM'; // runs, as another user
  }
}

/**
 * A variable or member path, as an agent may read it without approval: names joined by dots, indexes with numbers or string literals
 * (e.g. buffer, this._payload, response.Content, items[2].Data, map["key"]). No calls, operators or assignments, so the evaluation
 * has no side effects other than property getters.
 */
export function isSimplePath(expression: string): boolean {
  const name = '[A-Za-z_$@][\\w$]*';
  const index = '\\[\\s*(?:\\d+|"[^"\\\\]*"|\'[^\'\\\\]*\')\\s*\\]';
  // C#'s null-forgiving ! may follow any part (x!.y, x![0])
  const pattern = new RegExp(`^\\s*${name}(?:\\s*!?\\s*(?:\\??\\.\\s*${name}|\\??${index}))*\\s*!?\\s*$`);
  return pattern.test(expression);
}

/**
 * The expression that evaluates to the bytes of a schema in a SchemaStore of LsMsgPack (.NET only).
 */
export function schemaExpression(store: string, schemaId: string): string {
  if (!/^[0-9a-fA-F]{32}$/.test(schemaId)) {
    throw new Error(`Not a schema id: ${schemaId}`);
  }
  return `((LsMsgPack.SchemaStore)(${store})).GetSchema(LsMsgPack.SchemaId.Parse("${schemaId}"))`;
}
