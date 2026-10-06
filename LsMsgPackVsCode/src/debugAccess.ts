// The debug requests of the session and stack frame selected in VS Code, for reading bytes (debugBytes.ts).

import * as vscode from 'vscode';
import { DebugAccess, DebugVariable, EvaluateResult } from './debugBytes';

/**
 * The debug requests in the stack frame selected in the Call Stack view (where the Variables view is).
 */
export function createDebugAccess(): DebugAccess & { sessionName: string } {
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
