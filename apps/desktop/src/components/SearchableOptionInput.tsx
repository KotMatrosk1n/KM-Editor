/* SPDX-License-Identifier: GPL-3.0-only */

import { ChevronDown } from 'lucide-react';
import {
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode
} from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../localization';
import { getEditorPortalHost } from './editorPortal';
import { HoverTooltip } from './HoverTooltip';
import {
  formatSearchableOptionValue,
  getSmartOptionMatches,
  transitionSearchableOptionInteraction
} from './searchableOptionInputState';
import { useCoalescedTextInputState } from './useCoalescedTextInputState';

export type SearchableOptionInputOption = Readonly<{
  disabled?: boolean;
  groupLabel?: string;
  inputLabel?: string;
  label: string;
  searchAliases?: readonly string[];
  value: string | number;
}>;

export type SearchableOptionInputProps = Readonly<{
  ariaDescribedBy?: string;
  ariaInvalid?: boolean | 'false' | 'true';
  ariaLabel: string;
  className?: string;
  'data-localization-ignore'?: 'true';
  'data-km-source-site'?: string;
  disabled: boolean;
  emptyOptionDisabled?: boolean;
  emptyOptionLabel?: string;
  id?: string;
  isFiniteCatalog?: boolean;
  localizeOptions?: boolean;
  maximumVisibleOptions?: number;
  menuMinimumWidth?: number;
  name?: string;
  noOptionsLabel?: string;
  onChange: (value: string) => void;
  onFocus?: () => void;
  onSearchQueryChange?: (query: string) => void;
  onCatalogEndReached?: () => void;
  options: readonly SearchableOptionInputOption[];
  portalMenu?: boolean;
  required?: boolean;
  tooltipContent?: ReactNode;
  value: string;
}>;

type MenuItem =
  | Readonly<{ disabled: boolean; key: 'empty'; kind: 'empty'; label: string }>
  | Readonly<{
      key: string;
      kind: 'option';
      label: string;
      option: SearchableOptionInputOption;
    }>;

export function SearchableOptionInput({
  ariaLabel,
  ariaDescribedBy,
  ariaInvalid,
  className,
  'data-localization-ignore': localizationIgnore,
  'data-km-source-site': sourceSite,
  disabled,
  emptyOptionDisabled = false,
  emptyOptionLabel,
  id,
  isFiniteCatalog = false,
  localizeOptions = true,
  maximumVisibleOptions,
  menuMinimumWidth,
  name,
  noOptionsLabel,
  onChange,
  onFocus,
  onSearchQueryChange,
  onCatalogEndReached,
  options,
  portalMenu = true,
  required,
  tooltipContent,
  value
}: SearchableOptionInputProps) {
  const { translateLiteral } = useLocalization();
  const containerRef = useRef<HTMLDivElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const generatedId = `searchable-option-${useId().replace(/:/g, '')}`;
  const inputId = id ?? generatedId;
  const listboxId = `${inputId}-listbox`;
  const [isOpen, setIsOpen] = useState(false);
  const [activeOptionIndex, setActiveOptionIndex] = useState(-1);
  const [catalogScrollTop, setCatalogScrollTop] = useState(0);
  const [portalMenuStyle, setPortalMenuStyle] = useState<CSSProperties | undefined>();
  const effectiveLocalizationIgnore =
    localizationIgnore ?? (localizeOptions ? undefined : 'true');
  const localizedAriaLabel = translateLiteral(ariaLabel);
  const localizedEmptyOptionLabel =
    emptyOptionLabel !== undefined ? translateLiteral(emptyOptionLabel) : undefined;
  const localizedNoOptionsLabel =
    noOptionsLabel !== undefined ? translateLiteral(noOptionsLabel) : undefined;
  const localizedOptions = useMemo(
    () => options.map((option) => ({
      ...option,
      groupLabel:
        option.groupLabel === undefined || !localizeOptions
          ? option.groupLabel
          : translateLiteral(option.groupLabel),
      inputLabel:
        option.inputLabel === undefined || !localizeOptions
          ? option.inputLabel
          : translateLiteral(option.inputLabel),
      label: localizeOptions ? translateLiteral(option.label) : option.label
    })),
    [localizeOptions, options, translateLiteral]
  );
  const formattedValue = useMemo(
    () => formatSearchableOptionValue(value, localizedOptions, localizedEmptyOptionLabel),
    [localizedEmptyOptionLabel, localizedOptions, value]
  );
  const [query, setQuery] = useCoalescedTextInputState(formattedValue);
  const [hasUserQuery, setHasUserQuery] = useState(false);
  const inputTooltipText = hasUserQuery
    ? undefined
    : tooltipContent ?? (formattedValue || undefined);
  const optionQuery = hasUserQuery ? query : '';
  const trimmedOptionQuery = optionQuery.trim().toLocaleLowerCase();
  const hasEmptyOption = localizedEmptyOptionLabel !== undefined;
  const emptyOptionMatches =
    hasEmptyOption &&
    (trimmedOptionQuery.length === 0 ||
      localizedEmptyOptionLabel.toLocaleLowerCase().includes(trimmedOptionQuery));
  const filteredOptions = useMemo(() => {
    const matches = onSearchQueryChange ? localizedOptions : getSmartOptionMatches(optionQuery, localizedOptions);
    if (
      maximumVisibleOptions === undefined
      || !Number.isInteger(maximumVisibleOptions)
      || maximumVisibleOptions < 1
      || matches.length <= maximumVisibleOptions
    ) {
      return matches;
    }

    const visibleMatches = matches.slice(0, maximumVisibleOptions);
    if (!hasUserQuery) {
      const selectedOption = matches.find(
        (option) => option.value.toString() === value.trim()
      );
      if (selectedOption && !visibleMatches.includes(selectedOption)) {
        visibleMatches[visibleMatches.length - 1] = selectedOption;
      }
    }
    return visibleMatches;
  }, [hasUserQuery, localizedOptions, maximumVisibleOptions, onSearchQueryChange, optionQuery, value]);
  useEffect(() => {
    if (isOpen) onSearchQueryChange?.(optionQuery);
    if (onSearchQueryChange) {
      if (menuRef.current) menuRef.current.scrollTop = 0;
      setCatalogScrollTop(0);
    }
  }, [isOpen, onSearchQueryChange, optionQuery]);
  const menuItems = useMemo<MenuItem[]>(
    () => [
      ...(emptyOptionMatches
        ? [
            {
              disabled: emptyOptionDisabled,
              key: 'empty' as const,
              kind: 'empty' as const,
              label: localizedEmptyOptionLabel ?? ''
            }
          ]
        : []),
      ...filteredOptions.map((option) => ({
        key: `option-${option.value}`,
        kind: 'option' as const,
        label: option.label,
        option
      }))
    ],
    [emptyOptionDisabled, emptyOptionMatches, filteredOptions, localizedEmptyOptionLabel]
  );
  const hasMenu =
    isOpen &&
    !disabled &&
    (menuItems.length > 0 || localizedNoOptionsLabel !== undefined);

  useEffect(() => {
    if (!isOpen) {
      setQuery(formattedValue);
      setHasUserQuery(false);
    }
  }, [formattedValue, isOpen, setQuery]);

  useEffect(() => {
    if (disabled) {
      setIsOpen(false);
      setActiveOptionIndex(-1);
    }
  }, [disabled]);

  useEffect(() => {
    if (!hasMenu) {
      setActiveOptionIndex(-1);
      return;
    }

    setActiveOptionIndex((currentIndex) =>
      isEnabledMenuItem(menuItems[currentIndex]) ? currentIndex : -1
    );
  }, [hasMenu, menuItems]);

  useLayoutEffect(() => {
    if (!hasMenu || activeOptionIndex < 0) {
      return;
    }

    if (onSearchQueryChange && menuRef.current) {
      const menu = menuRef.current;
      const top = activeOptionIndex * 48;
      if (top < menu.scrollTop) menu.scrollTop = top;
      else if (top + 48 > menu.scrollTop + menu.clientHeight) menu.scrollTop = top + 48 - menu.clientHeight;
      setCatalogScrollTop(menu.scrollTop);
      if (activeOptionIndex >= menuItems.length - 3) onCatalogEndReached?.();
      return;
    }
    const activeOption = menuRef.current?.querySelector<HTMLElement>(
      `[data-option-index="${activeOptionIndex}"]`
    );
    activeOption?.scrollIntoView({ block: 'nearest' });
  }, [activeOptionIndex, hasMenu, menuItems.length, onCatalogEndReached, onSearchQueryChange]);

  useLayoutEffect(() => {
    if (!hasMenu || !portalMenu) {
      setPortalMenuStyle(undefined);
      return undefined;
    }

    const updatePosition = () => {
      const anchor = containerRef.current;
      if (!anchor) {
        return;
      }

      const rect = anchor.getBoundingClientRect();
      const viewportGap = 12;
      const menuGap = 6;
      const maximumHeight = 240;
      const availableWidth = Math.max(0, window.innerWidth - viewportGap * 2);
      const width = Math.min(
        Math.max(rect.width, menuMinimumWidth ?? rect.width),
        availableWidth
      );
      const left = Math.min(
        Math.max(rect.left, viewportGap),
        Math.max(viewportGap, window.innerWidth - viewportGap - width)
      );
      const belowSpace = Math.max(0, window.innerHeight - rect.bottom - menuGap - viewportGap);
      const aboveSpace = Math.max(0, rect.top - menuGap - viewportGap);
      const placeBelow = belowSpace >= Math.min(maximumHeight, 120) || belowSpace >= aboveSpace;
      const availableHeight = placeBelow ? belowSpace : aboveSpace;

      setPortalMenuStyle({
        bottom: placeBelow ? 'auto' : window.innerHeight - rect.top + menuGap,
        left,
        maxHeight: Math.max(0, Math.min(maximumHeight, availableHeight)),
        position: 'fixed',
        top: placeBelow ? rect.bottom + menuGap : 'auto',
        visibility: availableHeight > 0 && width > 0 ? 'visible' : 'hidden',
        width
      });
    };

    updatePosition();
    window.addEventListener('resize', updatePosition);
    window.addEventListener('scroll', updatePosition, true);
    return () => {
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition, true);
    };
  }, [hasMenu, menuMinimumWidth, portalMenu]);

  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    const handlePointerDown = (event: MouseEvent) => {
      const target = event.target as Node;
      if (
        !containerRef.current?.contains(target) &&
        !menuRef.current?.contains(target)
      ) {
        setIsOpen(false);
        setActiveOptionIndex(-1);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    return () => document.removeEventListener('mousedown', handlePointerDown);
  }, [isOpen]);

  const selectOption = (option: SearchableOptionInputOption) => {
    if (option.disabled) {
      return;
    }

    const selectedValue = option.value.toString();
    if (selectedValue !== value.trim()) {
      onChange(selectedValue);
    }
    setQuery(option.inputLabel ?? option.label);
    setHasUserQuery(false);
    setIsOpen(false);
  };

  const selectEmptyOption = () => {
    if (emptyOptionDisabled) {
      return;
    }

    if (value.trim().length > 0) {
      onChange('');
    }
    setQuery(localizedEmptyOptionLabel ?? '');
    setHasUserQuery(false);
    setIsOpen(false);
  };

  const applyInteractionResult = (
    result: ReturnType<typeof transitionSearchableOptionInteraction>
  ) => {
    setQuery(result.state.query);
    setHasUserQuery(result.state.hasUserQuery);
    setIsOpen(result.state.isOpen);
  };

  const restoreCommittedValue = () => {
    applyInteractionResult(
      transitionSearchableOptionInteraction(
        { hasUserQuery, isOpen, query },
        { formattedValue, type: 'restore' }
      )
    );
    setActiveOptionIndex(-1);
  };

  const selectMenuItem = (index: number) => {
    const item = menuItems[index];
    if (!item || !isEnabledMenuItem(item)) {
      return;
    }

    if (item.kind === 'empty') {
      selectEmptyOption();
    } else {
      selectOption(item.option);
    }
  };

  const commitTypedOption = () => {
    const enabledOptions = localizedOptions.filter((option) => !option.disabled);
    const result = transitionSearchableOptionInteraction(
      { hasUserQuery, isOpen, query },
      {
        committedValue: value,
        emptyOptionLabel: emptyOptionDisabled ? undefined : localizedEmptyOptionLabel,
        formattedValue,
        isFiniteCatalog,
        options: enabledOptions,
        type: 'commit'
      }
    );
    if (result.sourceCommit !== null) {
      onChange(result.sourceCommit.value);
    }
    applyInteractionResult(result);
    setActiveOptionIndex(-1);
  };

  const handleInputChange = (nextValue: string) => {
    applyInteractionResult(
      transitionSearchableOptionInteraction(
        { hasUserQuery, isOpen, query },
        { query: nextValue, type: 'input' }
      )
    );
    setActiveOptionIndex(-1);
  };

  const firstVisible = onSearchQueryChange ? Math.max(0, Math.floor(catalogScrollTop / 48) - 5) : 0;
  const lastVisible = onSearchQueryChange ? firstVisible + 20 : menuItems.length;
  const visibleRows = menuItems.slice(firstVisible, lastVisible).map((item, offset) => {
    const index = firstVisible + offset;
    const isSelected =
      item.kind === 'empty'
        ? value.trim().length === 0
        : item.option.value.toString() === value.trim();
    const isDisabled = !isEnabledMenuItem(item);

    return (
      <button
        aria-disabled={isDisabled || undefined}
        aria-selected={isSelected}
        aria-posinset={onSearchQueryChange ? index + 1 : undefined}
        aria-setsize={onSearchQueryChange ? menuItems.length : undefined}
        className={`searchable-option-row ${
          activeOptionIndex === index ? 'is-active' : ''
        }`.trim()}
        data-option-index={index}
        data-value={item.kind === 'empty' ? '' : item.option.value.toString()}
        disabled={isDisabled}
        id={`${listboxId}-option-${index}`}
        key={`${ariaLabel}:${item.key}`}
        onMouseDown={(event) => {
          event.preventDefault();
        }}
        onClick={() => {
          selectMenuItem(index);
        }}
        onPointerMove={() => {
          if (!isDisabled) {
            setActiveOptionIndex(index);
          }
        }}
        role="option"
        tabIndex={-1}
        type="button"
      >
        <span>{item.label}</span>
        {item.kind === 'option' && item.option.groupLabel ? (
          <small>{item.option.groupLabel}</small>
        ) : null}
      </button>
    );
  });
  const optionRows = <>
    {firstVisible > 0 ? <div aria-hidden="true" style={{ height: firstVisible * 48 }} /> : null}
    {visibleRows}
    {lastVisible < menuItems.length ? <div aria-hidden="true" style={{ height: (menuItems.length - lastVisible) * 48 }} /> : null}
  </>;
  const noOptionsStatus =
    menuItems.length === 0 && localizedNoOptionsLabel !== undefined ? (
      <div className="searchable-option-empty" role="status">
        {localizedNoOptionsLabel}
      </div>
    ) : null;
  const menu = hasMenu ? (
    <div
      className="searchable-option-menu"
      data-localization-ignore={effectiveLocalizationIgnore}
      ref={menuRef}
      onScroll={onSearchQueryChange ? (event) => {
        const menu = event.currentTarget;
        setCatalogScrollTop(menu.scrollTop);
        if (menu.scrollTop + menu.clientHeight >= menu.scrollHeight - 144) onCatalogEndReached?.();
      } : undefined}
      style={portalMenu ? portalMenuStyle ?? { visibility: 'hidden' } : undefined}
    >
      <div
        aria-label={localizedAriaLabel}
        className="searchable-option-listbox"
        data-virtual-catalog={onSearchQueryChange ? 'true' : undefined}
        id={listboxId}
        role="listbox"
      >
        {optionRows}
      </div>
      {noOptionsStatus}
    </div>
  ) : null;
  const portalHost = portalMenu ? getEditorPortalHost() : null;

  return (
    <HoverTooltip
      content={hasMenu ? undefined : inputTooltipText}
      describe={false}
      placement="above"
    >
      <div
        className={[
          'searchable-option-input',
          disabled ? 'searchable-option-disabled' : '',
          className ?? ''
        ].filter(Boolean).join(' ')}
        data-localization-ignore={effectiveLocalizationIgnore}
        ref={containerRef}
      >
        <input
          aria-activedescendant={
            hasMenu && activeOptionIndex >= 0
              ? `${listboxId}-option-${activeOptionIndex}`
              : undefined
          }
          aria-autocomplete="list"
          aria-controls={hasMenu ? listboxId : undefined}
          aria-describedby={ariaDescribedBy}
          aria-expanded={hasMenu}
          aria-label={localizedAriaLabel}
          aria-haspopup="listbox"
          aria-invalid={ariaInvalid}
          autoComplete="off"
          data-km-source-site={sourceSite}
          data-value={value}
          disabled={disabled}
          id={inputId}
          inputMode="search"
          name={name}
          onBlur={commitTypedOption}
          onChange={(event) => handleInputChange(event.target.value)}
          onClick={() => {
            if (!isOpen) {
              applyInteractionResult(
                transitionSearchableOptionInteraction(
                  { hasUserQuery, isOpen, query },
                  { formattedValue, type: 'focus' }
                )
              );
              setActiveOptionIndex(-1);
            }
          }}
          onFocus={() => {
            applyInteractionResult(
              transitionSearchableOptionInteraction(
                { hasUserQuery, isOpen, query },
                { formattedValue, type: 'focus' }
              )
            );
            setActiveOptionIndex(-1);
            onFocus?.();
          }}
          onKeyDown={(event) => {
            if (event.key === 'Escape' && isOpen) {
              event.preventDefault();
              event.stopPropagation();
              restoreCommittedValue();
              return;
            }

            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault();
              setIsOpen(true);
              setActiveOptionIndex((currentIndex) =>
                nextEnabledMenuItemIndex(
                  menuItems,
                  currentIndex,
                  event.key === 'ArrowDown' ? 1 : -1
                )
              );
              return;
            }

            if (event.key === 'Enter' && !event.nativeEvent.isComposing) {
              event.preventDefault();
              if (hasMenu && activeOptionIndex >= 0) {
                selectMenuItem(activeOptionIndex);
                return;
              }

              commitTypedOption();
            }
          }}
          role="combobox"
          required={required}
          type="text"
          value={query}
        />
        <button
          aria-label={translateLiteral(`Show ${ariaLabel} options`)}
          className="searchable-option-toggle"
          disabled={disabled}
          onMouseDown={(event) => {
            event.preventDefault();
          }}
          onClick={() => {
            setQuery(formattedValue);
            setHasUserQuery(false);
            setActiveOptionIndex(-1);
            setIsOpen((current) => (current && !hasUserQuery ? false : true));
          }}
          tabIndex={-1}
          type="button"
        >
          <ChevronDown aria-hidden="true" size={16} />
        </button>
        {portalHost && menu ? createPortal(menu, portalHost) : menu}
      </div>
    </HoverTooltip>
  );
}

function isEnabledMenuItem(item: MenuItem | undefined) {
  return item !== undefined && (
    item.kind === 'empty' ? !item.disabled : !item.option.disabled
  );
}

function nextEnabledMenuItemIndex(
  items: readonly MenuItem[],
  currentIndex: number,
  direction: 1 | -1
) {
  if (items.length === 0) {
    return -1;
  }

  for (let offset = 1; offset <= items.length; offset += 1) {
    const baseIndex = currentIndex < 0
      ? direction === 1 ? -1 : 0
      : currentIndex;
    const candidateIndex = (baseIndex + direction * offset + items.length) % items.length;
    if (isEnabledMenuItem(items[candidateIndex])) {
      return candidateIndex;
    }
  }

  return -1;
}
