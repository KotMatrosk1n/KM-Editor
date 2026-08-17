/* SPDX-License-Identifier: GPL-3.0-only */

import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useMemo,
  useState
} from 'react';
import { useLocalization } from '../localization/LocalizationProvider';

type TooltipIconVisibilityContextValue = {
  setShowIcons: (showIcons: boolean) => void;
  showIcons: boolean;
};

type StoredTooltipIconVisibility = Record<string, boolean>;

const tooltipIconVisibilityStorageKey = 'km-editor.tooltip-icons.v1';
const defaultTooltipIconVisibilityContext: TooltipIconVisibilityContextValue = {
  setShowIcons: () => undefined,
  showIcons: true
};
const TooltipIconVisibilityContext = createContext<TooltipIconVisibilityContextValue>(
  defaultTooltipIconVisibilityContext
);

export function TooltipIconVisibilityProvider({
  children,
  sectionId
}: {
  children: ReactNode;
  sectionId: string;
}) {
  const [visibilityBySection, setVisibilityBySection] = useState<StoredTooltipIconVisibility>(
    readStoredTooltipIconVisibility
  );
  const showIcons = visibilityBySection[sectionId] ?? true;
  const setShowIcons = useCallback(
    (nextShowIcons: boolean) => {
      setVisibilityBySection((currentVisibility) => {
        if ((currentVisibility[sectionId] ?? true) === nextShowIcons) {
          return currentVisibility;
        }

        const nextVisibility = {
          ...currentVisibility,
          [sectionId]: nextShowIcons
        };
        writeStoredTooltipIconVisibility(nextVisibility);
        return nextVisibility;
      });
    },
    [sectionId]
  );
  const value = useMemo(
    () => ({ setShowIcons, showIcons }),
    [setShowIcons, showIcons]
  );

  return (
    <TooltipIconVisibilityContext.Provider value={value}>
      {children}
    </TooltipIconVisibilityContext.Provider>
  );
}

export function TooltipIconVisibilityControl() {
  const { t } = useLocalization();
  const { setShowIcons, showIcons } = useTooltipIconVisibility();
  const label = t('contextHelp.showIcons');

  return (
    <label className="compact-checkbox tooltip-icon-visibility-control">
      <input
        aria-label={label}
        checked={showIcons}
        onChange={(event) => setShowIcons(event.target.checked)}
        type="checkbox"
      />
      <span>{label}</span>
    </label>
  );
}

export function useTooltipIconVisibility() {
  return useContext(TooltipIconVisibilityContext);
}

function readStoredTooltipIconVisibility(): StoredTooltipIconVisibility {
  if (typeof window === 'undefined') {
    return {};
  }

  try {
    const storedValue = window.localStorage.getItem(tooltipIconVisibilityStorageKey);
    if (!storedValue) {
      return {};
    }

    const parsedValue: unknown = JSON.parse(storedValue);
    if (!isRecord(parsedValue)) {
      return {};
    }

    return Object.fromEntries(
      Object.entries(parsedValue).filter(
        (entry): entry is [string, boolean] => typeof entry[1] === 'boolean'
      )
    );
  } catch {
    return {};
  }
}

function writeStoredTooltipIconVisibility(visibility: StoredTooltipIconVisibility) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(tooltipIconVisibilityStorageKey, JSON.stringify(visibility));
  } catch {
    // The setting remains active for this session when persistent storage is unavailable.
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
