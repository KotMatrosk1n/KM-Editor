/* SPDX-License-Identifier: GPL-3.0-only */

export const editorPortalHostId = 'km-editor-portal-host';

export function getEditorPortalHost() {
  if (typeof document === 'undefined') {
    return null;
  }

  return document.getElementById(editorPortalHostId) ?? document.querySelector('main') ?? document.body;
}
