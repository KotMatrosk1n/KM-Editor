/* SPDX-License-Identifier: GPL-3.0-only */

type DiagnosticShape = {
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

  const remainingReviewedDiagnostics = [...reviewedPlan.diagnostics];
  const diagnostics = applyResult.diagnostics.filter((diagnostic) => {
    const reviewedIndex = remainingReviewedDiagnostics.findIndex((reviewedDiagnostic) =>
      areDiagnosticsEquivalent(diagnostic, reviewedDiagnostic)
    );
    if (reviewedIndex < 0) {
      return true;
    }

    remainingReviewedDiagnostics.splice(reviewedIndex, 1);
    return false;
  });

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
    (left.domain ?? null) === (right.domain ?? null) &&
    (left.expected ?? null) === (right.expected ?? null) &&
    (left.field ?? null) === (right.field ?? null) &&
    (left.file ?? null) === (right.file ?? null)
  );
}
