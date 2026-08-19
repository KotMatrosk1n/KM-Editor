/* SPDX-License-Identifier: GPL-3.0-only */

import {
  BookmarkPlus,
  CheckSquare,
  Filter,
  Search,
  X
} from 'lucide-react';
import type { ReactNode } from 'react';
import { useLocalization } from '../../localization';

export type WorkspaceBrowserToolbarProps = {
  activeFilterCount: number;
  filterControls?: ReactNode;
  isSelectionMode: boolean;
  onClearSearch: () => void;
  onOpenFilters?: () => void;
  onSaveView?: () => void;
  onSearchChange: (value: string) => void;
  onToggleSelectionMode?: () => void;
  searchValue: string;
};

export function WorkspaceBrowserToolbar({
  activeFilterCount,
  filterControls,
  isSelectionMode,
  onClearSearch,
  onOpenFilters,
  onSaveView,
  onSearchChange,
  onToggleSelectionMode,
  searchValue
}: WorkspaceBrowserToolbarProps) {
  const { t } = useLocalization();
  return (
    <div className="km-browser-toolbar">
      <label className="km-command-palette-search km-browser-toolbar-search">
        <Search aria-hidden="true" size={16} />
        <span className="km-workbench-visually-hidden">
          {t('workbench.browser.searchLabel')}
        </span>
        <input
          onChange={(event) => onSearchChange(event.target.value)}
          placeholder={t('workbench.browser.searchPlaceholder')}
          type="search"
          value={searchValue}
        />
        {searchValue ? (
          <button
            aria-label={t('workbench.browser.clearSearch')}
            className="km-browser-toolbar-clear"
            onClick={onClearSearch}
            type="button"
          >
            <X aria-hidden="true" size={14} />
          </button>
        ) : null}
      </label>
      {filterControls}
      {onOpenFilters ? (
        <button className="secondary-button compact-button" onClick={onOpenFilters} type="button">
          <Filter aria-hidden="true" size={15} />
          <span>{t('workbench.browser.filters')}</span>
          {activeFilterCount > 0 ? <small>{activeFilterCount}</small> : null}
        </button>
      ) : null}
      {onSaveView ? (
        <button className="secondary-button compact-button" onClick={onSaveView} type="button">
          <BookmarkPlus aria-hidden="true" size={15} />
          <span>{t('workbench.browser.saveView')}</span>
        </button>
      ) : null}
      {onToggleSelectionMode ? (
        <button
          aria-pressed={isSelectionMode}
          className="secondary-button compact-button"
          onClick={onToggleSelectionMode}
          type="button"
        >
          <CheckSquare aria-hidden="true" size={15} />
          <span>{t(isSelectionMode ? 'workbench.browser.finishSelection' : 'workbench.browser.select')}</span>
        </button>
      ) : null}
    </div>
  );
}
