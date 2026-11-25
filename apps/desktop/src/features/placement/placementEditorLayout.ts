/* SPDX-License-Identifier: GPL-3.0-only */

import { type PlacementFieldControl } from './placementUi';

const transformRows = [
  { label: 'Position', fields: ['locationX', 'locationY', 'locationZ'], point: ['positionX', 'positionY', 'positionZ'], raw: 'Location' },
  { label: 'Rotation', fields: [null, 'rotationY', null], point: ['rotationPitch', 'rotationYaw', 'rotationRoll'], raw: 'Rotation' },
  { label: 'Scale', fields: [null, null, null], point: [], raw: 'Scale' }
];
export const placementAxes = ['X', 'Y', 'Z'];

type FieldGroup = { label: string; fields: PlacementFieldControl[] };
type EntryArray = {
  path: string;
  label: string;
  count?: PlacementFieldControl;
  entries: { index: string; field: PlacementFieldControl }[];
};

function addField(groups: Map<string, FieldGroup>, label: string, field: PlacementFieldControl) {
  let group = groups.get(label);
  if (!group) {
    group = { label, fields: [] };
    groups.set(label, group);
  }
  group.fields.push(field);
}

export function getPlacementEditorLayout(fields: PlacementFieldControl[]) {
  const byId = new Map(fields.map(field => [field.field, field]));
  const used = new Set<string>();
  // SwSh uses the first transform table found by the archive reader as the primary transform.
  const rawTransform = fields.find(field => field.field.startsWith('raw.') && field.field.endsWith('.HashObjectName'));
  const rawPrefix = rawTransform?.field.slice(0, -'HashObjectName'.length);
  const transform = transformRows.map(row => ({
    label: row.label,
    cells: placementAxes.map((axis, index) => {
      const canonical = row.fields[index];
      const point = row.point[index];
      const field = (canonical ? byId.get(canonical) : undefined) ??
        (point ? byId.get(`point.${point}`) : undefined) ??
        (rawPrefix ? byId.get(`${rawPrefix}${row.raw}${axis}`) : undefined);
      if (!field || (field.isReadOnly && field.value === '')) return undefined;
      used.add(field.field);
      return field;
    })
  })).filter(row => row.cells.some(Boolean));

  const data = new Map<string, FieldGroup>();
  const technical = new Map<string, FieldGroup>();
  const arrays = new Map<string, EntryArray>();
  const identity: PlacementFieldControl[] = [];
  const entryFields: PlacementFieldControl[] = [];
  for (const field of fields) {
    if (used.has(field.field)) continue;
    const entry = /^(raw\..+\.(Flags|Items))(?:\.Count|\[(\d+)\])$/.exec(field.field);
    if (entry) {
      const [, path, label, index] = entry;
      let array = arrays.get(path);
      if (!array) {
        array = { path, label, entries: [] };
        arrays.set(path, array);
      }
      if (index === undefined) array.count = field;
      else array.entries.push({ index, field });
    } else if (field.group === 'Identity' && field.isReadOnly) {
      identity.push(field);
    } else if (field.group === 'References' || field.valueKind === 'hash' || /hash$/i.test(field.field)) {
      addField(technical, 'References', field);
    } else if (field.group === 'Entries') {
      entryFields.push(field);
    } else if (['Bounds / Ranges', 'Advanced / Unknown'].includes(field.group)) {
      addField(technical, field.group, field);
    } else if (field.group === 'Transform' && field.field !== 'point.attachTransformEnable') {
      addField(technical, field.group, field);
    } else {
      addField(data, field.group, field);
    }
  }
  return { transform, data: [...data.values()], identity, technical: [...technical.values()], arrays: [...arrays.values()], entryFields };
}
