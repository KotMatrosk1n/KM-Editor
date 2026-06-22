/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  LocalizationProvider,
  languageStorageKey,
  translateKeyForLanguage,
  translateLiteralForLanguage,
  useLocalization
} from './LocalizationProvider';

describe('LocalizationProvider', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('keeps English as the default language', () => {
    render(
      <LocalizationProvider>
        <h1>Settings</h1>
      </LocalizationProvider>
    );

    expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument();
  });

  it('localizes existing text, common attributes, and dynamic button labels', () => {
    window.localStorage.setItem(languageStorageKey, 'es');

    render(
      <LocalizationProvider>
        <section>
          <h1>Settings</h1>
          <button title="Cancel" type="button">
            Save
          </button>
          <button type="button">Clear Cache (4 MB)</button>
          <input aria-label="Search text entries" placeholder="Search text" />
          <p data-localization-ignore="true" data-testid="game-text">
            Save
          </p>
        </section>
      </LocalizationProvider>
    );

    expect(screen.getByRole('heading', { name: 'Configuración' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toHaveAttribute('title', 'Cancelar');
    expect(screen.getByRole('button', { name: 'Borrar caché (4 MB)' })).toBeInTheDocument();
    expect(screen.getByLabelText('Buscar entradas de texto')).toHaveAttribute(
      'placeholder',
      'Buscar texto'
    );
    expect(screen.getByTestId('game-text')).toHaveTextContent('Save');
  });

  it('localizes narrow dynamic UI phrases without changing embedded values', () => {
    expect(translateLiteralForLanguage('es', 'Grass / Poison')).toBe('Planta / Veneno');
    expect(translateLiteralForLanguage('es', 'FIR')).toBe('FUE');
    expect(translateLiteralForLanguage('es', 'Super Effective')).toBe('Supereficaz');
    expect(translateLiteralForLanguage('es', 'Browse for Base RomFS')).toBe(
      'Buscar RomFS base'
    );
    expect(translateLiteralForLanguage('es', 'Select Dump Import source file')).toBe(
      'Seleccionar archivo de origen para importar volcado'
    );
    expect(translateLiteralForLanguage('es', 'Slot 3')).toBe('Hueco 3');
    expect(translateLiteralForLanguage('es', 'Nature raises Attack.')).toBe(
      'La naturaleza sube Ataque.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Grass encounters are not available for this location.'
      )
    ).toBe('Los encuentros de Grass no están disponibles para esta ubicación.');
    expect(translateLiteralForLanguage('es', 'Writes 8 PID rolls.')).toBe(
      'Escribe 8 tiradas PID.'
    );
    expect(translateLiteralForLanguage('es', 'Pending changes: 12')).toBe(
      'Cambios pendientes: 12'
    );
    expect(translateLiteralForLanguage('es', 'Current cache size: 92 MB')).toBe(
      'Tamaño actual de caché: 92 MB'
    );
    expect(
      translateLiteralForLanguage('es', 'Downloading update (4 MB of 8 MB).')
    ).toBe('Descargando actualización (4 MB de 8 MB).');
    expect(translateLiteralForLanguage('es', 'Dump Importer')).toBe('Importador de volcados');
    expect(translateLiteralForLanguage('es', 'Editable')).toBe('Modo editable');
    expect(translateLiteralForLanguage('es', 'Made by Matroskin')).toBe('Hecho por Matroskin');
    expect(translateLiteralForLanguage('es', 'Browse for destination folder')).toBe(
      'Examinar carpeta de destino'
    );
    expect(translateLiteralForLanguage('es', 'Browse for dump import source file')).toBe(
      'Examinar archivo de origen para importar volcado'
    );
    expect(translateLiteralForLanguage('es', 'S/V Mod Merger')).toBe(
      'Fusionador de mods S/V'
    );
    expect(translateLiteralForLanguage('es', 'Add Folder')).toBe('Agregar carpeta');
    expect(translateLiteralForLanguage('es', 'Stage Merge')).toBe('Preparar fusión');
    expect(translateLiteralForLanguage('es', 'Selected Item')).toBe('Objeto seleccionado');
    expect(translateLiteralForLanguage('es', 'Field pocket')).toBe('Bolsillo de campo');
    expect(
      translateLiteralForLanguage(
        'es',
        'Base RomFS contains the Trinity archive required for Pokemon Scarlet.'
      )
    ).toBe('RomFS base contiene el archivo Trinity requerido para Pokémon Scarlet.');
    expect(
      translateLiteralForLanguage('es', 'Dump Importer preview accepted 1 row and rejected 2.')
    ).toBe('El Importador de volcados aceptó 1 fila aceptada y rechazó 2 filas rechazadas.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Dump Importer source could not be parsed at line 3, byte 12: A JSON array of row objects is required.'
      )
    ).toBe(
      'No se pudo analizar el origen del Importador de volcados en la línea 3, byte 12: Se necesita un arreglo JSON de objetos de fila.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        "Row 4: Buy price value 'abc' must be between 0 and 999999."
      )
    ).toBe("Fila 4: el valor 'abc' de precio de compra debe estar entre 0 y 999999.");
  });

  it('switches language immediately and persists the choice', async () => {
    const user = userEvent.setup();

    function Fixture() {
      const { language, setLanguage, t } = useLocalization();

      return (
        <div>
          <h1>{t('settings.language.title')}</h1>
          <h2>Settings</h2>
          <p data-localization-ignore="true">{language}</p>
          <button
            data-localization-ignore="true"
            onClick={() => setLanguage('es')}
            type="button"
          >
            Switch to Spanish
          </button>
          <button
            data-localization-ignore="true"
            data-testid="switch-to-english"
            onClick={() => setLanguage('en')}
            type="button"
          >
            Switch to English
          </button>
        </div>
      );
    }

    render(
      <LocalizationProvider>
        <Fixture />
      </LocalizationProvider>
    );

    expect(screen.getByRole('heading', { name: 'Language and Localization' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Switch to Spanish' }));

    expect(screen.getByRole('heading', { name: 'Idioma y localización' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Configuración' })).toBeInTheDocument();
    expect(screen.getByText('es')).toBeInTheDocument();
    expect(window.localStorage.getItem(languageStorageKey)).toBe('es');

    await user.click(screen.getByTestId('switch-to-english'));

    expect(screen.getByRole('heading', { name: 'Language and Localization' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Settings' })).toBeInTheDocument();
    expect(screen.getByText('en')).toBeInTheDocument();
    expect(window.localStorage.getItem(languageStorageKey)).toBe('en');
  });

  it('falls back to English for missing Spanish keys', () => {
    expect(translateKeyForLanguage('es', 'settings.language.groupLabel')).toBe(
      'Idioma de la interfaz'
    );
    expect(translateKeyForLanguage('es', 'missing.key')).toBe('missing.key');
  });
});
