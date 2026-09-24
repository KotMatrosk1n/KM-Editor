// SPDX-License-Identifier: GPL-3.0-only
import type { MergePackageCatalog } from './mergeWorkspaceBridge';

export function packageDependencies(catalog: MergePackageCatalog, ids: Iterable<string>): Set<string> {
  const selected = new Set<string>();
  function add(id: string) {
    if (selected.has(id)) return;
    const item = catalog.packages.find(item => item.id === id);
    if (!item) return;
    selected.add(id); item.requires.forEach(add);
  }
  for (const id of ids) add(id);
  return selected;
}

export function requiredPackages(catalog: MergePackageCatalog) {
  return packageDependencies(catalog, catalog.packages.filter(item => item.required).map(item => item.id));
}

export function changePackageSelection(catalog: MergePackageCatalog, current: string[], id: string, include: boolean): string[] | null {
  const next = new Set(current);
  if (include) {
    const additions = packageDependencies(catalog, [id]);
    for (const added of additions) {
      const group = catalog.packages.find(item => item.id === added)?.group;
      if (group) for (const item of catalog.packages) if (item.group === group && !additions.has(item.id)) next.delete(item.id);
      next.add(added);
    }
  } else next.delete(id);
  for (let pass = 0; pass < catalog.packages.length; pass++) {
    const invalid = catalog.packages.filter(item => next.has(item.id) && item.requires.some(dependency => !next.has(dependency)));
    if (invalid.length === 0) break;
    invalid.forEach(item => next.delete(item.id));
  }
  if ([...requiredPackages(catalog)].some(required => !next.has(required)) || include && !next.has(id)) return null;
  return [...next];
}

export function defaultPackageSelection(catalog: MergePackageCatalog): string[] {
  let selected = [...requiredPackages(catalog)];
  const recommendations = catalog.packages.filter(item => item.recommended).map(item => ({ item, closure: packageDependencies(catalog, [item.id]) }));
  for (const { item, closure } of recommendations) {
    const competing = recommendations.some(other => other.item.id !== item.id && catalog.groups.some(group => {
      const ours = catalog.packages.find(member => member.group === group.id && closure.has(member.id));
      return ours && catalog.packages.some(member => member.group === group.id && other.closure.has(member.id) && member.id !== ours.id);
    }));
    if (!competing) selected = changePackageSelection(catalog, selected, item.id, true) ?? selected;
  }
  if (!catalog.requiresSelection && catalog.packages.length === 1) return [catalog.packages[0]!.id];
  return selected;
}

export function packageSelectionValid(catalog: MergePackageCatalog, selected: string[]): boolean {
  const ids = new Set(selected);
  return ids.size > 0 && ids.size === selected.length && selected.every(id => catalog.packages.some(item => item.id === id))
    && [...requiredPackages(catalog)].every(id => ids.has(id))
    && catalog.packages.every(item => !ids.has(item.id) || item.requires.every(id => ids.has(id)))
    && catalog.groups.every(group => {
      const count = catalog.packages.filter(item => item.group === group.id && ids.has(item.id)).length;
      return count <= 1 && (!group.required || count === 1);
    });
}
