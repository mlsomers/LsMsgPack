import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { bytesFromText } from './bytesFromText';
import { CancelledError, DebugAccess, DebugVariable, EvaluateResult, readBytes, ReadResult } from './debugBytes';
import { ByteSource, ExplorerView } from './explorerView';
import { InspectorClient } from './inspectorClient';

let client: InspectorClient | undefined;
let clientKey = '';

export function activate(context: vscode.ExtensionContext): void {
  const getClient = (): InspectorClient => {
    const dotnet = vscode.workspace.getConfiguration('lsmsgpack').get<string>('dotnetPath') || 'dotnet';
    const assembly = findInspector(context.extensionUri.fsPath);
    const key = `${dotnet}|${assembly}`;
    if (!client || key !== clientKey) {
      client?.dispose();
      client = new InspectorClient(dotnet, assembly);
      clientKey = key;
    }
    return client;
  };

  const open = (source: ByteSource): void => {
    const panel = vscode.window.createWebviewPanel('lsmsgpack.explorerPanel', `MsgPack: ${source.title}`, { viewColumn: vscode.ViewColumn.Beside, preserveFocus: false }, { retainContextWhenHidden: true });
    panel.iconPath = vscode.Uri.joinPath(context.extensionUri, 'media', 'icon.png');
    const view = new ExplorerView(panel.webview, context.extensionUri, getClient, source, (title) => { panel.title = `MsgPack: ${title}`; });
    panel.onDidDispose(() => view.dispose());
    if (source.warning) {
      vscode.window.showWarningMessage(source.warning);
    }
  };

  context.subscriptions.push(
    vscode.commands.registerCommand('lsmsgpack.viewVariable', async (context?: VariablesContext) => {
      const variable = context?.variable;
      if (!variable) {
        return vscode.commands.executeCommand('lsmsgpack.inspectExpression');
      }
      await showFromDebugger(open, variable.evaluateName, variable);
    }),

    vscode.commands.registerCommand('lsmsgpack.inspectExpression', async () => {
      const expression = await vscode.window.showInputBox({
        title: 'MsgPack: Inspect Expression',
        prompt: 'An expression of the debugged program holding MsgPack bytes (byte[], Stream, List<byte>, Memory<byte>, HttpContent, a base64 string...), evaluated in the selected stack frame.',
        placeHolder: 'e.g. buffer, stream, response.Content'
      });
      if (expression) {
        await showFromDebugger(open, expression, undefined);
      }
    }),

    vscode.commands.registerCommand('lsmsgpack.openFile', async (uri?: vscode.Uri) => {
      if (!uri) {
        const picked = await vscode.window.showOpenDialog({ canSelectMany: false, filters: { 'MsgPack': ['msgpack', 'MsgPack', 'mpk', 'bin'], 'All files': ['*'] } });
        uri = picked?.[0];
      }
      if (uri) {
        await vscode.commands.executeCommand('vscode.openWith', uri, 'lsmsgpack.explorer');
      }
    }),

    vscode.commands.registerCommand('lsmsgpack.fromClipboard', async () => {
      const text = await vscode.env.clipboard.readText();
      showFromText(open, text, 'Clipboard');
    }),

    vscode.commands.registerCommand('lsmsgpack.fromSelection', async () => {
      const editor = vscode.window.activeTextEditor;
      if (!editor) {
        return;
      }
      const text = editor.selections.map((selection) => editor.document.getText(selection)).join('\n');
      showFromText(open, text, `Selection of ${path.basename(editor.document.fileName)}`);
    }),

    vscode.window.registerCustomEditorProvider('lsmsgpack.explorer', new FileEditorProvider(context.extensionUri, getClient), {
      webviewOptions: { retainContextWhenHidden: true },
      supportsMultipleEditorsPerDocument: true
    }),

    { dispose: () => client?.dispose() }
  );
}

export function deactivate(): void {
  client?.dispose();
  client = undefined;
}

/**
 * The published inspector (npm run build:server), or the one built in Server/bin while developing.
 */
function findInspector(extensionPath: string): string {
  const candidates = [
    path.join(extensionPath, 'dist', 'inspector', 'LsMsgPackInspector.dll'),
    path.join(extensionPath, 'Server', 'bin', 'Release', 'net8.0', 'LsMsgPackInspector.dll'),
    path.join(extensionPath, 'Server', 'bin', 'Debug', 'net8.0', 'LsMsgPackInspector.dll')
  ];
  return candidates.find((candidate) => fs.existsSync(candidate)) ?? candidates[0];
}

/**
 * What VS Code passes to a command of the context menu of the Variables view.
 */
interface VariablesContext {
  sessionId?: string;
  variable?: DebugVariable;
}

function showFromText(open: (source: ByteSource) => void, text: string, title: string): void {
  try {
    const bytes = bytesFromText(text);
    if (bytes.length === 0) {
      vscode.window.showWarningMessage('There are no bytes in the text.');
      return;
    }
    open({ title, description: `${title} (${bytes.length} bytes)`, bytes });
  } catch (error) {
    vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
  }
}

async function showFromDebugger(open: (source: ByteSource) => void, expression: string | undefined, variable: DebugVariable | undefined): Promise<void> {
  const name = expression || variable?.name || 'value';
  try {
    const source = await readFromDebugger(name, expression, variable);
    if (source) {
      open(source);
    }
  } catch (error) {
    if (!(error instanceof CancelledError)) {
      vscode.window.showErrorMessage(`Could not read MsgPack bytes from ${name}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
}

/**
 * Reads the bytes in the selected stack frame, the source can read them again (Refresh) while the program is paused.
 */
async function readFromDebugger(name: string, expression: string | undefined, variable: DebugVariable | undefined): Promise<ByteSource | undefined> {
  const access = createDebugAccess();
  const config = vscode.workspace.getConfiguration('lsmsgpack');
  const result = await vscode.window.withProgress({
    location: vscode.ProgressLocation.Notification,
    title: `Reading ${name} for MsgPack Explorer`,
    cancellable: true
  }, (progress, token): Promise<ReadResult> => {
    let reported = 0;
    return readBytes(access, expression, {
      chunkSize: config.get<number>('chunkSize', 49152),
      maxBytes: config.get<number>('maxBytes', 16777216),
      isCancelled: () => token.isCancellationRequested,
      confirmConsume: async (message) => (await vscode.window.showWarningMessage(message, { modal: true }, 'Read anyway')) === 'Read anyway',
      progress: (read, total) => {
        if (total > 0) {
          const percent = Math.floor(read * 100 / total);
          progress.report({ increment: percent - reported, message: `${read} of ${total} bytes` });
          reported = percent;
        } else {
          progress.report({ message: `${read} bytes` });
        }
      }
    }, variable);
  });

  return {
    title: name,
    description: `${result.description} ${name} (${result.bytes.length} bytes, ${access.sessionName})`,
    bytes: result.bytes,
    warning: result.warning,
    // Variables of the Variables view are only valid while the program stays paused, an expression can be evaluated again
    refresh: expression ? () => readFromDebugger(name, expression, undefined).then((source) => {
      if (!source) {
        throw new CancelledError();
      }
      return source;
    }) : undefined
  };
}

/**
 * The debug requests in the stack frame selected in the Call Stack view (where the Variables view is).
 */
function createDebugAccess(): DebugAccess & { sessionName: string } {
  const item = vscode.debug.activeStackItem;
  const session = item?.session ?? vscode.debug.activeDebugSession;
  if (!session) {
    throw new Error('There is no debug session.');
  }
  const frameId = item instanceof vscode.DebugStackFrame ? item.frameId : undefined;
  return {
    sessionType: session.type,
    sessionName: session.name,
    async evaluate(expression: string): Promise<EvaluateResult> {
      const response = await session.customRequest('evaluate', { expression, frameId, context: 'repl' });
      return { result: String(response.result ?? ''), type: response.type, variablesReference: response.variablesReference ?? 0, indexedVariables: response.indexedVariables };
    },
    async variables(variablesReference: number, start?: number, count?: number): Promise<DebugVariable[]> {
      const args: { variablesReference: number; filter?: string; start?: number; count?: number } = { variablesReference };
      if (start !== undefined) {
        args.filter = 'indexed';
        args.start = start;
        args.count = count;
      }
      const response = await session.customRequest('variables', args);
      return response.variables ?? [];
    }
  };
}

/**
 * Opens .msgpack files in the explorer (read only).
 */
class FileEditorProvider implements vscode.CustomReadonlyEditorProvider {
  constructor(private readonly extensionUri: vscode.Uri, private readonly client: () => InspectorClient) {
  }

  openCustomDocument(uri: vscode.Uri): vscode.CustomDocument {
    return { uri, dispose: () => undefined };
  }

  async resolveCustomEditor(document: vscode.CustomDocument, panel: vscode.WebviewPanel): Promise<void> {
    const read = async (): Promise<ByteSource> => {
      const bytes = await vscode.workspace.fs.readFile(document.uri);
      return {
        title: path.basename(document.uri.fsPath),
        description: `${vscode.workspace.asRelativePath(document.uri)} (${bytes.length} bytes)`,
        bytes,
        refresh: read
      };
    };
    const view = new ExplorerView(panel.webview, this.extensionUri, this.client, await read());

    // Show the file again when it changes (e.g. a test writes it again)
    const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.joinPath(document.uri, '..'), path.basename(document.uri.fsPath)));
    let timer: NodeJS.Timeout | undefined;
    const changed = () => {
      clearTimeout(timer);
      timer = setTimeout(() => read().then((source) => view.setSource(source), () => undefined), 300);
    };
    watcher.onDidChange(changed);
    watcher.onDidCreate(changed);
    panel.onDidDispose(() => {
      clearTimeout(timer);
      watcher.dispose();
      view.dispose();
    });
  }
}
