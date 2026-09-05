/* SPDX-License-Identifier: GPL-3.0-only */

import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useMemo,
  useState
} from 'react';

export const appearancePreferencesStorageKey = 'km-editor.appearance.v1';
export const appearancePreferencesVersion = 1 as const;
export const visualThemeStorageKey = 'km-editor.visual-theme.v1';

export type AppearanceTheme = 'default' | 'highContrast' | 'colorSafe';
export type VisualTheme = 'classic' | 'renegade' | 'royal' | 'sovereign' | 'arcane' | 'relic';
export type MotionPreference = 'system' | 'reduce';
export type TypeScalePreference = 'default' | 'large' | 'larger';
export type DensityPreference = 'comfortable' | 'compact';

export type AppearancePreferences = {
  density: DensityPreference;
  motion: MotionPreference;
  theme: AppearanceTheme;
  typeScale: TypeScalePreference;
  version: typeof appearancePreferencesVersion;
};

type AppearancePreferencesContextValue = {
  preferences: AppearancePreferences;
  visualTheme: VisualTheme;
  setDensity: (density: DensityPreference) => void;
  setMotion: (motion: MotionPreference) => void;
  setTheme: (theme: AppearanceTheme) => void;
  setTypeScale: (typeScale: TypeScalePreference) => void;
  setVisualTheme: (theme: VisualTheme) => void;
};

export const defaultVisualTheme: VisualTheme = 'classic';

export const defaultAppearancePreferences: AppearancePreferences = {
  density: 'comfortable',
  motion: 'system',
  theme: 'default',
  typeScale: 'default',
  version: appearancePreferencesVersion
};

const defaultContext: AppearancePreferencesContextValue = {
  preferences: defaultAppearancePreferences,
  visualTheme: defaultVisualTheme,
  setDensity: () => undefined,
  setMotion: () => undefined,
  setTheme: () => undefined,
  setTypeScale: () => undefined,
  setVisualTheme: () => undefined
};

const AppearancePreferencesContext =
  createContext<AppearancePreferencesContextValue>(defaultContext);

export function applyStoredAppearancePreferences() {
  const preferences = readAppearancePreferences();
  const visualTheme = readVisualTheme();
  applyAppearancePreferences(preferences);
  applyVisualTheme(visualTheme);
  return preferences;
}

export function AppearancePreferencesProvider({ children }: { children: ReactNode }) {
  const [visualTheme, setVisualThemeState] = useState<VisualTheme>(() => {
    const stored = readVisualTheme();
    applyVisualTheme(stored);
    return stored;
  });
  const [preferences, setPreferences] = useState<AppearancePreferences>(() => {
    const stored = readAppearancePreferences();
    applyAppearancePreferences(stored);
    return stored;
  });

  const updatePreferences = useCallback(
    (update: (current: AppearancePreferences) => AppearancePreferences) => {
      setPreferences((current) => {
        const next = update(current);
        applyAppearancePreferences(next);
        writeAppearancePreferences(next);
        return next;
      });
    },
    []
  );

  const value = useMemo<AppearancePreferencesContextValue>(
    () => ({
      preferences,
      visualTheme,
      setDensity: (density) => {
        if (density === 'comfortable' || density === 'compact') {
          updatePreferences((current) => ({ ...current, density }));
        }
      },
      setMotion: (motion) => {
        if (motion === 'system' || motion === 'reduce') {
          updatePreferences((current) => ({ ...current, motion }));
        }
      },
      setTheme: (theme) => {
        if (theme === 'default' || theme === 'highContrast' || theme === 'colorSafe') {
          updatePreferences((current) => ({ ...current, theme }));
        }
      },
      setTypeScale: (typeScale) => {
        if (typeScale === 'default' || typeScale === 'large' || typeScale === 'larger') {
          updatePreferences((current) => ({ ...current, typeScale }));
        }
      },
      setVisualTheme: (theme) => {
        if (theme === 'classic' || theme === 'renegade' || theme === 'royal' || theme === 'sovereign' || theme === 'arcane' || theme === 'relic') {
          setVisualThemeState(theme);
          applyVisualTheme(theme);
          writeVisualTheme(theme);
        }
      }
    }),
    [preferences, updatePreferences, visualTheme]
  );

  return (
    <AppearancePreferencesContext.Provider value={value}>
      {children}
    </AppearancePreferencesContext.Provider>
  );
}

export function useAppearancePreferences() {
  return useContext(AppearancePreferencesContext);
}

export function applyAppearancePreferences(preferences: AppearancePreferences) {
  if (typeof document === 'undefined') {
    return;
  }
  const root = document.documentElement;
  root.dataset.kmTheme = preferences.theme;
  root.dataset.kmMotion = preferences.motion;
  root.dataset.kmTypeScale = preferences.typeScale;
  root.dataset.kmDensity = preferences.density;
}

export function applyVisualTheme(theme: VisualTheme) {
  if (typeof document === 'undefined') {
    return;
  }
  document.documentElement.dataset.kmVisualTheme = theme;
}

export function readVisualTheme(): VisualTheme {
  if (typeof window === 'undefined') {
    return defaultVisualTheme;
  }
  try {
    const stored = window.localStorage.getItem(visualThemeStorageKey);
    return stored === 'renegade' || stored === 'royal' || stored === 'sovereign' || stored === 'arcane' || stored === 'relic' ? stored : defaultVisualTheme;
  } catch {
    return defaultVisualTheme;
  }
}

function writeVisualTheme(theme: VisualTheme) {
  if (typeof window === 'undefined') {
    return;
  }
  try {
    window.localStorage.setItem(visualThemeStorageKey, theme);
  } catch {
    // The current session still uses the preference when storage is unavailable.
  }
}

function readAppearancePreferences(): AppearancePreferences {
  if (typeof window === 'undefined') {
    return defaultAppearancePreferences;
  }
  try {
    const value: unknown = JSON.parse(
      window.localStorage.getItem(appearancePreferencesStorageKey) ?? 'null'
    );
    return isAppearancePreferences(value) ? value : defaultAppearancePreferences;
  } catch {
    return defaultAppearancePreferences;
  }
}

function writeAppearancePreferences(preferences: AppearancePreferences) {
  if (typeof window === 'undefined') {
    return;
  }
  try {
    window.localStorage.setItem(appearancePreferencesStorageKey, JSON.stringify(preferences));
  } catch {
    // The current session still uses the preference when storage is unavailable.
  }
}

function isAppearancePreferences(value: unknown): value is AppearancePreferences {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }
  const candidate = value as Record<string, unknown>;
  return (
    Object.keys(candidate).length === 5 &&
    candidate.version === appearancePreferencesVersion &&
    (candidate.theme === 'default' ||
      candidate.theme === 'highContrast' ||
      candidate.theme === 'colorSafe') &&
    (candidate.motion === 'system' || candidate.motion === 'reduce') &&
    (candidate.typeScale === 'default' ||
      candidate.typeScale === 'large' ||
      candidate.typeScale === 'larger') &&
    (candidate.density === 'comfortable' || candidate.density === 'compact')
  );
}
