/* SPDX-License-Identifier: GPL-3.0-only */

export const editorPortalHostId = 'km-editor-portal-host';

export function getEditorPortalHost(anchor?: Element | null) {
  if (typeof document === 'undefined') {
    return null;
  }

  return anchor?.closest('[role="dialog"]')?.querySelector('[data-editor-portal-host]')
    ?? document.getElementById(editorPortalHostId) ?? document.querySelector('main') ?? document.body;
}
