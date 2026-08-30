/* SPDX-License-Identifier: GPL-3.0-only */

import { resolveRowClipboardAdapterRegistration } from './rowClipboardAdapters';
import { RowClipboardController } from './rowClipboardController';
import type {
  RowClipboardEditorSchemaRef,
  RowClipboardScope
} from './rowClipboardTypes';

export function createScopedRowClipboardController(
  editor: RowClipboardEditorSchemaRef,
  scope: RowClipboardScope
) {
  // Bridge copy preparation returns the validated envelope scope DTO, which also
  // carries gameFamily. The controller deliberately accepts only its three-key
  // internal scope contract, so project the DTO before strict normalization.
  const controllerScope: RowClipboardScope = Object.freeze({
    game: scope.game,
    profileId: scope.profileId,
    projectId: scope.projectId
  });
  return new RowClipboardController({
    registrations: [resolveRowClipboardAdapterRegistration(editor, controllerScope)],
    scope: controllerScope
  });
}
