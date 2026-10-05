// MsgPack for AI agents: registers the MCP server (LsMsgPackMcpServer) with VS Code, and runs the bridge that lets it (and the
// MCP server started by other clients, e.g. Claude Code) read bytes from the debugger of this window.

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { createDebugAccess } from './debugAccess';
import { CancelledError, languageOf, readBytes } from './debugBytes';
import { BridgeHandler, BridgeServer, FrameInfo, isSimplePath, LocalsResult, ReadRequest, ReadResponse, schemaExpression, SessionInfo, StatusResult, VariableInfo } from './mcpBridge';

const serverLabel = 'MsgPack (LsMsgPack)';

/**
 * The published MCP server (npm run build:mcp), or the one built in the repository while developing.
 */
export function findMcpServer(extensionPath: string): string {
  const candidates = [
    path.join(extensionPath, 'dist', 'mcp', 'LsMsgPackMcp.dll'),
    path.join(extensionPath, '..', 'LsMsgPackMcpServer', 'bin', 'Release', 'net8.0', 'LsMsgPackMcp.dll'),
    path.join(extensionPath, '..', 'LsMsgPackMcpServer', 'bin', 'Debug', 'net8.0', 'LsMsgPackMcp.dll')
  ];
  return candidates.find((candidate) => fs.existsSync(candidate)) ?? candidates[0];
}

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration('lsmsgpack');
}

function dotnetPath(): string {
  return config().get<string>('dotnetPath') || 'dotnet';
}

export function activateMcp(context: vscode.ExtensionContext): void {
  const sessions = new Map<string, vscode.DebugSession>();
  context.subscriptions.push(
    vscode.debug.onDidStartDebugSession((session) => sessions.set(session.id, session)),
    vscode.debug.onDidTerminateDebugSession((session) => sessions.delete(session.id))
  );
  if (vscode.debug.activeDebugSession) {
    sessions.set(vscode.debug.activeDebugSession.id, vscode.debug.activeDebugSession);
  }

  let bridge: BridgeServer | undefined;
  let started: Promise<BridgeServer | undefined> = Promise.resolve(undefined);
  const start = (): void => {
    stop();
    if (!config().get<boolean>('mcp.enabled', true)) {
      return;
    }
    const server = new BridgeServer(new DebuggerHandler(sessions));
    bridge = server;
    started = server.start().then(() => {
      server.writeLockFile({ ide: 'vscode', name: `${vscode.env.appName} ${vscode.version}`, pid: process.pid, workspaceFolders: workspaceFolders() });
      return server;
    }, (error) => {
      vscode.window.showWarningMessage(`MsgPack Explorer could not start the debugger bridge for AI agents: ${error instanceof Error ? error.message : String(error)}`);
      return undefined;
    });
  };
  const stop = (): void => {
    bridge?.dispose();
    bridge = undefined;
    started = Promise.resolve(undefined);
  };

  const changed = new vscode.EventEmitter<void>();
  start();
  context.subscriptions.push(
    changed,
    { dispose: stop },
    vscode.workspace.onDidChangeWorkspaceFolders(() => bridge?.updateWorkspaceFolders(workspaceFolders())),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('lsmsgpack.mcp.enabled')) {
        start();
        changed.fire();
      } else if (event.affectsConfiguration('lsmsgpack.dotnetPath')) {
        changed.fire();
      }
    }),
    vscode.commands.registerCommand('lsmsgpack.copyMcpConfiguration', () => copyConfiguration(context.extensionUri.fsPath))
  );

  // VS Code 1.101 and later: the agent mode of the chat starts the server itself (the API is not in the types of the oldest supported VS Code)
  const lm = vscode.lm as unknown as { registerMcpServerDefinitionProvider?: (id: string, provider: unknown) => vscode.Disposable };
  const definition = (vscode as unknown as { McpStdioServerDefinition?: new (label: string, command: string, args?: string[], env?: Record<string, string>, version?: string) => { cwd?: vscode.Uri } }).McpStdioServerDefinition;
  if (typeof lm.registerMcpServerDefinitionProvider === 'function' && definition) {
    context.subscriptions.push(lm.registerMcpServerDefinitionProvider('lsmsgpack.mcp', {
      onDidChangeMcpServerDefinitions: changed.event,
      provideMcpServerDefinitions: async () => {
        // Without the bridge (lsmsgpack.mcp.enabled off) the server still decodes data and files. Port 0 tells it that this window does not allow
        // reading from the debugger (otherwise it would look for other windows through their lock files)
        const server = await started;
        const env: Record<string, string> = server ? { LSMSGPACK_IDE_PORT: String(server.port), LSMSGPACK_IDE_TOKEN: server.token } : { LSMSGPACK_IDE_PORT: '0' };
        const version = String(context.extension.packageJSON.version ?? '');
        const result = new definition(serverLabel, dotnetPath(), [findMcpServer(context.extensionUri.fsPath)], env, version);
        // Relative file names of the agent are relative to the workspace
        const folder = vscode.workspace.workspaceFolders?.[0];
        if (folder) {
          result.cwd = folder.uri;
        }
        return [result];
      }
    }));
  }
}

function workspaceFolders(): string[] {
  return (vscode.workspace.workspaceFolders ?? []).map((folder) => folder.uri.fsPath);
}

/**
 * Answers the MCP server with the debug API: the selected session and stack frame, as the Variables view shows them.
 */
class DebuggerHandler implements BridgeHandler {
  constructor(private readonly sessions: Map<string, vscode.DebugSession>) {
  }

  async status(): Promise<StatusResult> {
    const active = vscode.debug.activeDebugSession;
    const sessions: SessionInfo[] = Array.from(this.sessions.values()).map((session) => ({ id: session.id, name: session.name, type: session.type, active: session.id === active?.id }));
    if (active && !this.sessions.has(active.id)) {
      sessions.push({ id: active.id, name: active.name, type: active.type, active: true });
    }
    const frame = await selectedFrame();
    return {
      ide: 'vscode',
      name: `${vscode.env.appName} ${vscode.version}`,
      workspaceFolders: workspaceFolders(),
      sessions,
      frame: frame?.info,
      expressions: config().get<boolean>('mcp.allowAnyExpression', false) ? 'any' : 'paths'
    };
  }

  async locals(maxVariables: number): Promise<LocalsResult> {
    const frame = await selectedFrame();
    if (!frame) {
      throw new Error(vscode.debug.activeDebugSession ? 'No stack frame is selected: pause the program (e.g. at a breakpoint) first.' : 'There is no debug session.');
    }
    const variables: VariableInfo[] = [];
    let truncated = false;
    const scopes = (await frame.session.customRequest('scopes', { frameId: frame.frameId })).scopes ?? [];
    for (const scope of scopes) {
      if (scope.expensive || !scope.variablesReference) {
        continue; // e.g. globals
      }
      const response = await frame.session.customRequest('variables', { variablesReference: scope.variablesReference });
      for (const variable of response.variables ?? []) {
        if (variables.length >= maxVariables) {
          truncated = true;
          break;
        }
        variables.push({ scope: scope.name, name: variable.name, type: variable.type, value: variable.value, evaluateName: variable.evaluateName });
      }
    }
    return { frame: frame.info, variables, truncated };
  }

  async read(request: ReadRequest): Promise<ReadResponse> {
    const allowAny = config().get<boolean>('mcp.allowAnyExpression', false);
    if (!allowAny && !isSimplePath(request.expression)) {
      throw new Error(`Only variable and member paths are read for AI agents (e.g. buffer, this._payload, items[2].Data), not "${request.expression}". The setting lsmsgpack.mcp.allowAnyExpression allows any expression.`);
    }
    const access = createDebugAccess();
    let expression = request.expression;
    if (request.schemaId) {
      if (languageOf(access.sessionType) !== 'dotnet') {
        throw new Error('A SchemaStore can only be read from a .NET program.');
      }
      expression = schemaExpression(request.expression, request.schemaId);
    }

    const settings = config();
    const maxBytes = Math.min(request.maxBytes ?? Number.MAX_SAFE_INTEGER, settings.get<number>('maxBytes', 16777216));
    const result = await Promise.resolve(vscode.window.withProgress({
      location: vscode.ProgressLocation.Notification,
      title: `An AI agent reads ${request.expression} for MsgPack`,
      cancellable: true
    }, (progress, token) => readBytes(access, expression, {
      chunkSize: settings.get<number>('chunkSize', 49152),
      maxBytes,
      isCancelled: () => token.isCancellationRequested,
      // A stream that cannot seek is consumed: the user decides, not the agent
      confirmConsume: async (message) => (await vscode.window.showWarningMessage(`An AI agent (MCP) asks to read ${request.expression}. ${message}`, { modal: true }, 'Read anyway')) === 'Read anyway',
      progress: (read, total) => progress.report({ message: total > 0 ? `${read} of ${total} bytes` : `${read} bytes` })
    }))).catch((error: unknown) => {
      throw error instanceof CancelledError ? new Error('The user cancelled reading the value.') : error;
    });

    return {
      base64: Buffer.from(result.bytes).toString('base64'),
      description: result.description,
      length: result.bytes.length,
      session: access.sessionName,
      warning: result.warning
    };
  }
}

interface SelectedFrame {
  session: vscode.DebugSession;
  frameId: number;
  info: FrameInfo;
}

/**
 * The stack frame selected in the Call Stack view (or the top frame of the selected thread), with its name and location.
 */
async function selectedFrame(): Promise<SelectedFrame | undefined> {
  const item = vscode.debug.activeStackItem;
  if (!item) {
    return undefined;
  }
  const session = item.session;
  const wanted = item instanceof vscode.DebugStackFrame ? item.frameId : undefined;
  try {
    const trace = await session.customRequest('stackTrace', { threadId: item.threadId, startFrame: 0, levels: wanted === undefined ? 1 : 100 });
    const frames: { id: number; name: string; line?: number; source?: { path?: string; name?: string } }[] = trace.stackFrames ?? [];
    const frame = frames.find((f) => f.id === wanted) ?? frames[0];
    if (!frame) {
      return undefined;
    }
    return { session, frameId: frame.id, info: { name: frame.name, source: frame.source?.path ?? frame.source?.name, line: frame.line, session: session.name } };
  } catch {
    return wanted === undefined ? undefined : { session, frameId: wanted, info: { name: '(selected frame)', session: session.name } };
  }
}

/**
 * The configuration for other MCP clients: they start the server themselves, it finds this window through the lock file.
 */
async function copyConfiguration(extensionPath: string): Promise<void> {
  const dll = findMcpServer(extensionPath);
  const dotnet = dotnetPath();
  const json = JSON.stringify({ mcpServers: { lsmsgpack: { command: dotnet, args: [dll] } } }, null, 2);
  const visualStudio = JSON.stringify({ servers: { lsmsgpack: { type: 'stdio', command: dotnet, args: [dll] } } }, null, 2);
  const quote = (text: string): string => (/[\s"']/.test(text) ? `"${text.replace(/"/g, '\\"')}"` : text);
  const choices: (vscode.QuickPickItem & { text: string })[] = [
    { label: 'Claude Code', description: 'claude mcp add (run it in a terminal)', text: `claude mcp add lsmsgpack -- ${quote(dotnet)} ${quote(dll)}` },
    { label: 'JSON (mcpServers)', description: 'Claude Desktop, Cursor, Windsurf and others', text: json },
    { label: 'Visual Studio (.mcp.json)', description: 'servers, as Visual Studio and VS Code mcp.json files write it', text: visualStudio },
    { label: 'Command line', description: 'decode a file in a terminal', text: `${quote(dotnet)} ${quote(dll)} decode <file.msgpack>` }
  ];
  const picked = await vscode.window.showQuickPick(choices, { title: 'Copy the MCP server configuration for', placeHolder: 'VS Code itself needs none: the chat agent mode lists the server already' });
  if (!picked) {
    return;
  }
  await vscode.env.clipboard.writeText(picked.text);
  if (!fs.existsSync(dll)) {
    vscode.window.showWarningMessage(`Copied, but ${dll} is not built yet (npm run build:mcp).`);
  } else {
    vscode.window.showInformationMessage('Copied. The path changes when the extension is updated: copy it again after an update.');
  }
}
