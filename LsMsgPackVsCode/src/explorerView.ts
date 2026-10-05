// Connects a webview (media/explorer.js) with the inspector process and the source of the bytes.

import * as os from 'os';
import * as vscode from 'vscode';
import { InspectorClient, LoadSettings } from './inspectorClient';

export interface ByteSource {
  /** Shown in the title bar of the explorer */
  title: string;
  /** Where the bytes came from, e.g. "byte[] buffer (debugger)" */
  description: string;
  bytes: Uint8Array;
  /** A warning to show with the data (e.g. read up to the maximum length) */
  warning?: string;
  /** Reads the bytes again (e.g. evaluates the expression again in the debugger), undefined when that is not possible */
  refresh?(): Promise<ByteSource>;
}

let documentCounter = 0;

export class ExplorerView implements vscode.Disposable {
  private readonly doc = `doc${++documentCounter}`;
  private loadedGeneration = 0;
  private readonly disposables: vscode.Disposable[] = [];
  private disposed = false;

  constructor(
    private readonly webview: vscode.Webview,
    private readonly extensionUri: vscode.Uri,
    private readonly client: () => InspectorClient,
    private source: ByteSource,
    private readonly onTitle?: (title: string) => void
  ) {
    webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(extensionUri, 'media')]
    };
    webview.html = this.html();
    this.disposables.push(webview.onDidReceiveMessage((message) => this.receive(message)));
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    for (const disposable of this.disposables) {
      disposable.dispose();
    }
    this.client().close(this.doc).catch(() => undefined);
  }

  /** Shows other bytes (e.g. the file changed on disk). */
  setSource(source: ByteSource): void {
    this.source = source;
    this.loadedGeneration = 0;
    this.sendInit();
  }

  private post(message: object): void {
    if (!this.disposed) {
      this.webview.postMessage(message);
    }
  }

  private sendInit(): void {
    if (this.onTitle) {
      this.onTitle(this.source.title);
    }
    const config = vscode.workspace.getConfiguration('lsmsgpack');
    this.post({
      type: 'init',
      title: this.source.title,
      description: this.source.description,
      warning: this.source.warning,
      data: Buffer.from(this.source.bytes).toString('base64'),
      canRefresh: !!this.source.refresh,
      displayLimit: config.get<number>('displayLimit', 1000)
    });
  }

  private async receive(message: { type: string;[key: string]: unknown }): Promise<void> {
    try {
      switch (message.type) {
        case 'ready':
          this.sendInit();
          break;
        case 'load':
          this.post({ type: 'model', seq: message.seq, result: await this.load(message.settings as LoadSettings) });
          break;
        case 'search':
          this.post({ type: 'searchResult', seq: message.seq, result: await this.search(message.text as string, !!message.matchCase, message.settings as LoadSettings) });
          break;
        case 'refresh':
          await this.refresh();
          break;
        case 'save':
          await this.save();
          break;
        case 'copy':
          await this.copy(message.format as string);
          break;
        case 'about':
          await this.about();
          break;
        case 'help':
          await vscode.env.openExternal(vscode.Uri.parse('https://github.com/mlsomers/LsMsgPack/tree/master/LsMsgPackVsCode#readme'));
          break;
      }
    } catch (error) {
      this.post({ type: 'error', seq: message.seq, request: message.type, message: error instanceof Error ? error.message : String(error) });
    }
  }

  private async load(settings: LoadSettings): Promise<unknown> {
    const client = this.client();
    const generation = client.generation;
    const known = generation !== 0 && generation === this.loadedGeneration;
    try {
      const result = await client.load(this.doc, known ? undefined : Buffer.from(this.source.bytes).toString('base64'), settings);
      this.loadedGeneration = client.generation;
      return result;
    } catch (error) {
      // The inspector was started again in the meantime, it does not have the data any more
      if (known && error instanceof Error && error.message.startsWith('Unknown document')) {
        this.loadedGeneration = 0;
        return this.load(settings);
      }
      throw error;
    }
  }

  private async search(text: string, matchCase: boolean, settings: LoadSettings): Promise<unknown> {
    const client = this.client();
    if (client.generation === 0 || client.generation !== this.loadedGeneration) {
      await this.load(settings); // the same ids as the tree the webview shows (same data and settings)
    }
    return client.search(this.doc, text, matchCase);
  }

  private async refresh(): Promise<void> {
    if (!this.source.refresh) {
      return;
    }
    this.source = await this.source.refresh();
    this.loadedGeneration = 0;
    this.sendInit();
  }

  private async save(): Promise<void> {
    const name = this.source.title.replace(/[^\w.-]+/g, '_').replace(/^_+|_+$/g, '') || 'data';
    const uri = await vscode.window.showSaveDialog({
      defaultUri: vscode.Uri.joinPath(vscode.workspace.workspaceFolders?.[0]?.uri ?? vscode.Uri.file(os.homedir()), name.endsWith('.msgpack') ? name : `${name}.msgpack`),
      filters: { 'MsgPack': ['msgpack', 'MsgPack', 'mpk'], 'All files': ['*'] }
    });
    if (uri) {
      await vscode.workspace.fs.writeFile(uri, this.source.bytes);
    }
  }

  private async copy(format: string): Promise<void> {
    const buffer = Buffer.from(this.source.bytes);
    const text = format === 'base64' ? buffer.toString('base64') : buffer.toString('hex').toUpperCase().replace(/(..)(?!$)/g, '$1 ');
    await vscode.env.clipboard.writeText(text);
    vscode.window.setStatusBarMessage(`Copied ${buffer.length} bytes as ${format === 'base64' ? 'base64' : 'hex'}.`, 3000);
  }

  private async about(): Promise<void> {
    const extension = vscode.extensions.all.find((e) => e.extensionUri.toString() === this.extensionUri.toString());
    let inspector: string;
    try {
      inspector = await this.client().version();
    } catch (error) {
      inspector = `not available (${error instanceof Error ? error.message : String(error)})`;
    }
    const choice = await vscode.window.showInformationMessage(
      `MsgPack Explorer ${extension?.packageJSON?.version ?? ''}`,
      { modal: true, detail: `Inspector (LsMsgPack) ${inspector}\nCopyright © Matheu Louis Somers 2015-2026, Apache License 2.0.\nhttps://github.com/mlsomers/LsMsgPack` },
      'Open on GitHub');
    if (choice) {
      await vscode.env.openExternal(vscode.Uri.parse('https://github.com/mlsomers/LsMsgPack'));
    }
  }

  private html(): string {
    const media = (file: string) => this.webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', file));
    const nonce = Array.from({ length: 32 }, () => 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'.charAt(Math.floor(Math.random() * 62))).join('');
    const csp = `default-src 'none'; style-src ${this.webview.cspSource} 'unsafe-inline'; img-src ${this.webview.cspSource} data:; script-src 'nonce-${nonce}';`;
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<link href="${media('explorer.css')}" rel="stylesheet">
<title>MsgPack Explorer</title>
</head>
<body>
<div id="app"></div>
<script nonce="${nonce}" src="${media('explorer.js')}"></script>
</body>
</html>`;
  }
}
