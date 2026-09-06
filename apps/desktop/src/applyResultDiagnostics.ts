/* SPDX-License-Identifier: GPL-3.0-only */

type DiagnosticShape = {
  code?: string | null;
  domain?: string | null;
  expected?: string | null;
  field?: string | null;
  file?: string | null;
  message: string;
  severity: 'info' | 'warning' | 'error';
};

// Apply responses echo the reviewed plan diagnostics. Once an apply succeeds,
// remove only those known entries so newly produced apply diagnostics remain visible.
export function completeSuccessfulApplyResult<
  TDiagnostic extends DiagnosticShape,
  TResult extends { diagnostics: TDiagnostic[] }
>(
  applyResult: TResult,
  reviewedPlan: { diagnostics: readonly DiagnosticShape[] }
): TResult {
  if (applyResult.diagnostics.some((diagnostic) => diagnostic.severity === 'error')) {
    return applyResult;
  }

  const diagnostics = applyResult.diagnostics.filter((diagnostic) =>
    diagnostic.severity !== 'info' || !reviewedPlan.diagnostics.some((reviewedDiagnostic) =>
      areDiagnosticsEquivalent(diagnostic, reviewedDiagnostic)
    )
  );

  return diagnostics.length === applyResult.diagnostics.length
    ? applyResult
    : ({
        ...applyResult,
        diagnostics
      } as TResult);
}

function areDiagnosticsEquivalent(left: DiagnosticShape, right: DiagnosticShape) {
  return (
    left.severity === right.severity &&
    left.message === right.message &&
    (left.code ?? null) === (right.code ?? null) &&
    (left.domain ?? null) === (right.domain ?? null) &&
    (left.expected ?? null) === (right.expected ?? null) &&
    (left.field ?? null) === (right.field ?? null) &&
    (left.file ?? null) === (right.file ?? null)
  );
}
