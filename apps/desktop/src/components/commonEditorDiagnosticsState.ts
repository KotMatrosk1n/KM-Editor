/* SPDX-License-Identifier: GPL-3.0-only */

type CommonEditorDiagnostic = Readonly<{
  code?: string | null;
  domain?: string | null;
  expected?: string | null;
  field?: string | null;
  file?: string | null;
  message: string;
  severity: string;
}>;

export type LocalEditorValidationIssue = Readonly<{
  field: string;
  label?: string;
  message?: string;
}>;

export type LocalEditorValidationDiagnostic = Readonly<{
  domain: string;
  expected: string;
  field: string;
  message: string;
  severity: 'error';
}>;

export function createLocalEditorValidationDiagnostics(
  domain: string,
  issues: readonly LocalEditorValidationIssue[]
): LocalEditorValidationDiagnostic[] {
  return issues.map((issue) => ({
    domain,
    expected: 'Correct the draft value before staging.',
    field: issue.field,
    message:
      issue.message ??
      `${issue.label?.trim() || issue.field} has an invalid draft value.`,
    severity: 'error'
  }));
}

export function mergeEditorDiagnostics<T extends CommonEditorDiagnostic>(
  ...diagnosticLists: (readonly T[])[]
): T[] {
  const seen = new Set<string>();
  const merged: T[] = [];

  for (const diagnostics of diagnosticLists) {
    for (const diagnostic of diagnostics) {
      const identity = diagnosticIdentity(diagnostic);
      if (seen.has(identity)) {
        continue;
      }
      seen.add(identity);
      merged.push(diagnostic);
    }
  }

  return merged;
}

export function diagnosticListFingerprint(diagnostics: readonly CommonEditorDiagnostic[]) {
  return JSON.stringify(diagnostics.map(diagnosticIdentity));
}

export function areDiagnosticListsEqual(
  left: readonly CommonEditorDiagnostic[] | undefined,
  right: readonly CommonEditorDiagnostic[]
) {
  if (!left || left.length !== right.length) {
    return left === undefined && right.length === 0;
  }
  return left.every(
    (diagnostic, index) => diagnosticIdentity(diagnostic) === diagnosticIdentity(right[index]!)
  );
}

export function updateEditorDiagnosticsSource<T extends CommonEditorDiagnostic>(
  current: ReadonlyMap<string, readonly T[]>,
  sourceId: string,
  diagnostics: readonly T[]
): ReadonlyMap<string, readonly T[]> {
  if (areDiagnosticListsEqual(current.get(sourceId), diagnostics)) {
    return current;
  }

  const next = new Map(current);
  if (diagnostics.length === 0) {
    next.delete(sourceId);
  } else {
    next.set(sourceId, [...diagnostics]);
  }
  return next;
}

function diagnosticIdentity(diagnostic: CommonEditorDiagnostic) {
  return JSON.stringify([
    diagnostic.code ?? null,
    diagnostic.severity,
    diagnostic.message,
    diagnostic.domain ?? null,
    diagnostic.file ?? null,
    diagnostic.field ?? null,
    diagnostic.expected ?? null
  ]);
}
