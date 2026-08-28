/* SPDX-License-Identifier: GPL-3.0-only */

/**
 * Retains revoked source identities only while an overlapping request or the
 * visible controller snapshot can still refer to them.
 */
export class ResearchSourceRevocationTracker {
  private readonly activeReferences = new Map<string, number>();
  private readonly revokedSourceIds = new Set<string>();

  public begin(sourceId: string | null) {
    if (sourceId === null) return;
    this.activeReferences.set(sourceId, (this.activeReferences.get(sourceId) ?? 0) + 1);
  }

  public clear() {
    this.activeReferences.clear();
    this.revokedSourceIds.clear();
  }

  public end(sourceId: string | null, visibleSourceIds: readonly string[]) {
    if (sourceId !== null) {
      const referenceCount = this.activeReferences.get(sourceId) ?? 0;
      if (referenceCount <= 1) {
        this.activeReferences.delete(sourceId);
      } else {
        this.activeReferences.set(sourceId, referenceCount - 1);
      }
    }
    this.prune(visibleSourceIds);
  }

  public isRevoked(sourceId: string) {
    return this.revokedSourceIds.has(sourceId);
  }

  public revoke(sourceId: string, visibleSourceIds: readonly string[]) {
    this.revokedSourceIds.add(sourceId);
    this.prune(visibleSourceIds);
  }

  private prune(visibleSourceIds: readonly string[]) {
    const visible = new Set(visibleSourceIds);
    for (const sourceId of this.revokedSourceIds) {
      if (!this.activeReferences.has(sourceId) && !visible.has(sourceId)) {
        this.revokedSourceIds.delete(sourceId);
      }
    }
  }
}
