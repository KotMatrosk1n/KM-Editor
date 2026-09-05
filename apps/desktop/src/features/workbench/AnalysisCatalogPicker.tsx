/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useRef, useState } from 'react';
import { SearchableOptionInput, type SearchableOptionInputOption } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';

export type AnalysisCatalogPage = {
  options: SearchableOptionInputOption[];
  nextCursor: string | null;
  total: number;
};

export function AnalysisCatalogPicker({ label, search, onSelect }: {
  label: string;
  search: (query: string, cursor?: string) => Promise<AnalysisCatalogPage>;
  onSelect: (id: string, isCurrent: () => boolean) => Promise<void>;
}) {
  const { t } = useLocalization();
  const [query, setQuery] = useState<string | null>(null);
  const [page, setPage] = useState<AnalysisCatalogPage>({ options: [], nextCursor: null, total: 0 });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [selecting, setSelecting] = useState(false);
  const generation = useRef(0);
  const inFlight = useRef(false);
  const selectionInFlight = useRef(false);
  const searchRef = useRef(search);
  searchRef.current = search;

  useEffect(() => {
    const epoch = ++generation.current;
    inFlight.current = false;
    setPage({ options: [], nextCursor: null, total: 0 });
    setError(false);
    setBusy(query !== null);
    if (query === null) return;
    const timer = window.setTimeout(() => {
      inFlight.current = true;
      void searchRef.current(query).then((result) => {
        if (generation.current === epoch) setPage(result);
      }).catch(() => {
        if (generation.current === epoch) setError(true);
      }).finally(() => {
        if (generation.current === epoch) { inFlight.current = false; setBusy(false); }
      });
    }, 200);
    return () => { window.clearTimeout(timer); generation.current++; };
  }, [query, attempt]);

  const changeQuery = useCallback((value: string) => setQuery(value.slice(0, 256)), []);
  const more = useCallback(() => {
    if (inFlight.current || busy || !page.nextCursor || query === null || error) return;
    const epoch = generation.current;
    inFlight.current = true;
    setBusy(true);
    void searchRef.current(query, page.nextCursor).then((result) => {
      if (generation.current !== epoch) return;
      if (result.nextCursor === page.nextCursor) throw new Error('The catalog cursor did not advance.');
      setPage((previous) => ({ ...result, options: [...new Map([...previous.options, ...result.options]
        .map((option) => [option.value, option])).values()] }));
    }).catch(() => {
      if (generation.current === epoch) setError(true);
    }).finally(() => {
      if (generation.current === epoch) { inFlight.current = false; setBusy(false); }
    });
  }, [busy, error, page.nextCursor, query]);

  return <div className="km-searchable-select-field">
    <SearchableOptionInput
      ariaLabel={label}
      data-km-source-site="analysis-catalog-record"
      disabled={selecting}
      emptyOptionDisabled
      emptyOptionLabel={t('analysisCatalog.choose')}
      isFiniteCatalog
      localizeOptions={false}
      maximumVisibleOptions={undefined}
      noOptionsLabel={t(busy ? 'analysisCatalog.loading' : 'analysisCatalog.empty')}
      onCatalogEndReached={more}
      onChange={(id) => {
        if (selectionInFlight.current) return;
        const epoch = generation.current;
        selectionInFlight.current = true;
        setSelecting(true);
        setError(false);
        void onSelect(id, () => generation.current === epoch).catch(() => {
          if (generation.current === epoch) setError(true);
        }).finally(() => {
          selectionInFlight.current = false;
          if (generation.current === epoch) setSelecting(false);
        });
      }}
      onSearchQueryChange={changeQuery}
      options={page.options}
      value=""
    />
    <small aria-live="polite">{t(error ? 'analysisCatalog.error' : busy || selecting
      ? 'analysisCatalog.loading' : 'analysisCatalog.hint')}</small>
    {query !== null && !busy && !error ? <small>{t('analysisCatalog.count', { count: page.total })}</small> : null}
    {error ? <button className="secondary-button compact-button" onClick={() => setAttempt((value) => value + 1)} type="button">
      {t('analysisCatalog.retry')}
    </button> : null}
  </div>;
}
