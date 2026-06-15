/* SPDX-License-Identifier: GPL-3.0-only */

export function formatBagHookStatus(status: string) {
  switch (status.toLocaleLowerCase()) {
    case 'available':
      return 'Available';
    case 'blocked':
      return 'Blocked';
    case 'conflict':
      return 'Conflict';
    case 'empty':
      return 'Empty';
    case 'foreign':
      return 'Foreign';
    case 'installed':
      return 'Installed';
    case 'legacy':
      return 'Legacy';
    case 'occupied':
      return 'Occupied';
    case 'repairable':
      return 'Repair needed';
    case 'readonly':
    case 'read-only':
      return 'Read-only';
    case 'unavailable':
      return 'Unavailable';
    default:
      return status.length > 0 ? `${status[0]!.toLocaleUpperCase()}${status.slice(1)}` : status;
  }
}

export function formatSourceLayer(layer: string) {
  switch (layer) {
    case 'base':
      return 'Base';
    case 'generated':
      return 'Generated';
    case 'layered':
      return 'LayeredFS';
    case 'pending':
      return 'Pending';
    default:
      return layer;
  }
}

export function formatFileState(state: string) {
  switch (state) {
    case 'baseOnly':
      return 'Base only';
    case 'layeredOnly':
      return 'Layered only';
    case 'layeredOverride':
      return 'Layered override';
    default:
      return state;
  }
}
