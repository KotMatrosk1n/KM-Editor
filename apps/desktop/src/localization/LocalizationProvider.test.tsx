/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import deResource from './resources/de.json';
import enResource from './resources/en.json';
import esResource from './resources/es.json';
import frResource from './resources/fr.json';
import ruResource from './resources/ru.json';
import ukResource from './resources/uk.json';
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
    expect(translateLiteralForLanguage('es', 'Editable')).toBe('Listo para editar');
    expect(translateLiteralForLanguage('es', 'Made by Matroskin')).toBe('Hecho por Matroskin');
    expect(translateLiteralForLanguage('es', 'Output for Trinity Bypass')).toBe(
      'Exportar para Trinity Bypass'
    );
    expect(translateLiteralForLanguage('es', 'Output for Trinity Bypass?')).toBe(
      '¿Exportar para Trinity Bypass?'
    );
    expect(
      translateLiteralForLanguage('es', 'as loose LayeredFS RomFS files for Trinity Bypass.')
    ).toBe('como archivos RomFS sueltos de LayeredFS para Trinity Bypass.');
    expect(translateLiteralForLanguage('es', 'This creates or replaces files under')).toBe(
      'Esto crea o reemplaza archivos bajo'
    );
    expect(translateLiteralForLanguage('es', 'the configured Output Root')).toBe(
      'la raíz de salida configurada'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        '. Use this only when Trinity Bypass is already installed for the selected game.'
      )
    ).toBe('. Usa esto solo cuando Trinity Bypass ya esté instalado para el juego seleccionado.');
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
    expect(translateLiteralForLanguage('es', 'Selected Trade')).toBe('Intercambio seleccionado');
    expect(translateLiteralForLanguage('es', 'Requested form')).toBe('Forma solicitada');
    expect(translateLiteralForLanguage('es', 'Field pocket')).toBe('Bolsillo de campo');
    expect(translateLiteralForLanguage('es', 'Fails with Me First')).toBe(
      'Falla con Yo Primero'
    );
    expect(translateLiteralForLanguage('es', 'Future attack')).toBe('Ataque futuro');
    expect(translateLiteralForLanguage('es', 'Affected by Pressure')).toBe(
      'Afectado por Presión'
    );
    expect(translateLiteralForLanguage('es', 'Boosted by Sheer Force')).toBe(
      'Potenciado por Potencia Bruta'
    );
    expect(translateLiteralForLanguage('es', 'Makes contact. Allowed range: 0-1')).toBe(
      'Hace contacto. Rango permitido: 0-1'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Hyperspace Bypass is not installed. Installing lets non-Hoopa and wrong-form users pass the Hyperspace runtime gate.'
      )
    ).toBe(
      'Omisión de hiperspacio no está instalado. Al instalarlo, usuarios que no sean Hoopa o tengan una forma incorrecta pueden pasar la compuerta de ejecución de hiperspacio.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Base RomFS contains the Trinity archive required for Pokemon Scarlet.'
      )
    ).toBe('RomFS base contiene el archivo Trinity requerido para Pokémon Scarlet.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Base ExeFS matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe(
      'ExeFS base coincide con el ID de título seleccionado de Pokémon Scarlet 0x0100A3D008C5C000.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Output root folder matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe(
      'La carpeta raíz de salida coincide con el ID de título seleccionado de Pokémon Scarlet 0x0100A3D008C5C000.'
    );
    expect(translateLiteralForLanguage('es', '0 Not Shiny')).toBe('0 No shiny');
    expect(translateLiteralForLanguage('es', '1 Random Ability 1 or 2')).toBe(
      '1 Habilidad 1 o 2 aleatoria'
    );
    expect(translateLiteralForLanguage('es', '11 Grass')).toBe('11 Planta');
    expect(translateLiteralForLanguage('es', 'Show Form options')).toBe(
      'Mostrar opciones de Forma'
    );
    expect(translateLiteralForLanguage('es', 'Custom fixed IVs')).toBe(
      'IVs fijos personalizados'
    );
    expect(translateLiteralForLanguage('es', 'Mixed fixed IVs')).toBe('IVs fijos mixtos');
    expect(translateLiteralForLanguage('es', 'Rummaging Points')).toBe('Puntos de rebusca');
    expect(translateLiteralForLanguage('es', 'Point name')).toBe('Nombre del punto');
    expect(translateLiteralForLanguage('es', 'No alternate forms available for this Pokemon.')).toBe(
      'No hay formas alternativas disponibles para este Pokémon.'
    );
    expect(translateLiteralForLanguage('es', '#0 Pikachu - Common')).toBe(
      '#0 Pikachu - Común'
    );
    expect(translateLiteralForLanguage('es', 'Hidden Item Slot 3')).toBe(
      'Hueco de objeto oculto 3'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Fixed IVs: HP 27, Atk 18, Def 25, SpA 16, SpD 31, Spe 13'
      )
    ).toBe('IV fijos: PS 27, Ata 18, Def 25, At. Esp. 16, Def. Esp. 31, Vel 13');
    expect(
      translateLiteralForLanguage(
        'es',
        'Behavior data source could not be read: file was locked'
      )
    ).toBe('Origen de datos de comportamiento no se pudo leer: file was locked');
    expect(
      translateLiteralForLanguage(
        'es',
        'Most vanilla entries use 1.0, matching a model scale multiplier slot. Likely, but read-only until the loader mapping is confirmed.'
      )
    ).toBe(
      'La mayoría de entradas vanilla usa 1.0, coincidiendo con un hueco de multiplicador de escala de modelo. Probable, pero de solo lectura hasta confirmar el mapeo del cargador.'
    );
    expect(
      translateLiteralForLanguage('es', 'Dump Importer preview accepted 1 row and rejected 2.')
    ).toBe('El Importador de volcados aceptó 1 fila aceptada y rechazó 2 filas rechazadas.');
    expect(
      translateLiteralForLanguage('es', 'Change plan preview contains 2 target files.')
    ).toBe('La vista previa del plan de cambios contiene 2 archivos de destino.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Type Chart effectiveness values are staged for change-plan review.'
      )
    ).toBe(
      'Valores de efectividad de tipos están preparados para revisión del plan de cambios.'
    );
    expect(translateLiteralForLanguage('es', '60FPS Patch is installed.')).toBe(
      'Parche 60FPS está instalado.'
    );
    expect(
      translateLiteralForLanguage('es', '60FPS Patch installed 1,107 output file(s).')
    ).toBe('Parche 60FPS instaló 1,107 archivo(s) de salida.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Selected Pokemon Shield, but Base ExeFS contains Pokemon Sword title id 0x0100ABF008968000.'
      )
    ).toBe(
      'Seleccionaste Pokémon Shield, pero ExeFS base contiene el ID de título de Pokémon Sword 0x0100ABF008968000.'
    );
    expect(
      translateLiteralForLanguage('es', 'Patch code cave: expected 0xC bytes, actual text+0x7BC338.')
    ).toBe('Cueva de código de parche: esperado 0xC bytes, actual text+0x7BC338.');
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
    expect(
      translateLiteralForLanguage(
        'es',
        'Save work key 0xDDEEFF00 is derived from WK_SCENE_MAIN.'
      )
    ).toBe('Clave de trabajo de guardado 0xDDEEFF00 se deriva de WK_SCENE_MAIN.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Output root is not configured; write actions are disabled.'
      )
    ).toBe('La raíz de salida no está configurada; las acciones de escritura están desactivadas.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Edit Scarlet/Violet placement tables and inspect scene-only placement fields.'
      )
    ).toBe(
      'Edita tablas de colocación de Scarlet/Violet y revisa campos de colocación solo de escena.'
    );
  });

  it('localizes French static and dynamic UI phrases', () => {
    expect(translateKeyForLanguage('fr', 'settings.language.title')).toBe(
      'Langue et localisation'
    );
    expect(translateLiteralForLanguage('fr', 'Settings')).toBe('Paramètres');
    expect(translateLiteralForLanguage('fr', 'Made by Matroskin')).toBe('Créé par Matroskin');
    expect(translateLiteralForLanguage('fr', 'Editable')).toBe('Prêt pour la modification');
    expect(translateLiteralForLanguage('fr', 'Grass / Poison')).toBe('Plante / Poison');
    expect(translateLiteralForLanguage('fr', 'Browse for Base RomFS')).toBe(
      'Parcourir RomFS de base'
    );
    expect(translateLiteralForLanguage('fr', 'Makes contact. Allowed range: 0-1')).toBe(
      'Prend contact. Plage autorisée: 0-1'
    );
    expect(
      translateLiteralForLanguage(
        'fr',
        'Base ExeFS matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe(
      "ExeFS de base correspond à l'ID de titre sélectionné de Pokémon Écarlate 0x0100A3D008C5C000."
    );
    expect(translateLiteralForLanguage('fr', 'Dump Importer preview accepted 1 row and rejected 2.')).toBe(
      "L'Importateur de dumps a accepté 1 ligne acceptée et rejeté 2 lignes rejetées."
    );
  });

  it('localizes German static and dynamic UI phrases', () => {
    expect(translateKeyForLanguage('de', 'settings.language.title')).toBe(
      'Sprache und Lokalisierung'
    );
    expect(translateLiteralForLanguage('de', 'Settings')).toBe('Einstellungen');
    expect(translateLiteralForLanguage('de', 'Made by Matroskin')).toBe(
      'Erstellt von Matroskin'
    );
    expect(translateLiteralForLanguage('de', 'Grass / Poison')).toBe('Pflanze / Gift');
    expect(translateLiteralForLanguage('de', 'Browse for Base RomFS')).toBe(
      'Basis RomFS durchsuchen'
    );
    expect(translateLiteralForLanguage('de', 'Makes contact. Allowed range: 0-1')).toBe(
      'Stellt Kontakt her. Zulässiger Bereich: 0-1'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Base ExeFS matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe(
      'Basis ExeFS entspricht der ausgewählten Titel ID von Pokémon Karmesin 0x0100A3D008C5C000.'
    );
  });

  it('localizes Russian static and dynamic UI phrases', () => {
    expect(translateKeyForLanguage('ru', 'settings.language.title')).toBe(
      'Язык и локализация'
    );
    expect(translateLiteralForLanguage('ru', 'Settings')).toBe('Настройки');
    expect(translateLiteralForLanguage('ru', 'Made by Matroskin')).toBe('Создано Matroskin');
    expect(translateLiteralForLanguage('ru', 'Grass / Poison')).toBe(
      'Травяной / Ядовитый'
    );
    expect(translateLiteralForLanguage('ru', 'Browse for Base RomFS')).toBe(
      'Выбрать Базовый RomFS'
    );
    expect(translateLiteralForLanguage('ru', 'Makes contact. Allowed range: 0-1')).toBe(
      'Контактирует. Допустимый диапазон: 0-1'
    );
    expect(
      translateLiteralForLanguage(
        'ru',
        'Base ExeFS matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe(
      'Базовый ExeFS соответствует выбранному title ID для Pokémon Scarlet 0x0100A3D008C5C000.'
    );
  });

  it('localizes Ukrainian static and dynamic UI phrases', () => {
    expect(translateKeyForLanguage('uk', 'settings.language.title')).toBe(
      'Мова та локалізація'
    );
    expect(translateLiteralForLanguage('uk', 'Settings')).toBe('Налаштування');
    expect(translateLiteralForLanguage('uk', 'Made by Matroskin')).toBe(
      'Створено Matroskin'
    );
    expect(translateLiteralForLanguage('uk', 'Grass / Poison')).toBe(
      "Трав'яний / Отруйний"
    );
    expect(translateLiteralForLanguage('uk', 'Browse for Base RomFS')).toBe(
      'Вибрати Базовий RomFS'
    );
    expect(translateLiteralForLanguage('uk', 'Makes contact. Allowed range: 0-1')).toBe(
      'Контактує. Допустимий діапазон: 0-1'
    );
    expect(
      translateLiteralForLanguage(
        'uk',
        'Base ExeFS matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe(
      'Базовий ExeFS відповідає вибраному title ID для Pokémon Scarlet 0x0100A3D008C5C000.'
    );
  });

  it('keeps localized resource keys aligned with the English catalogue', () => {
    const localizedResources = {
      de: deResource,
      es: esResource,
      fr: frResource,
      ru: ruResource,
      uk: ukResource
    };

    const missingEntries = Object.entries(localizedResources).flatMap(([language, resource]) => {
      const missingKeys = Object.keys(enResource.keys).map((key) =>
        key in resource.keys ? null : `${language}:key:${key}`
      );
      const missingLiterals = Object.keys(enResource.literals).map((literal) =>
        literal in resource.literals ? null : `${language}:literal:${literal}`
      );

      return [...missingKeys, ...missingLiterals].filter((entry): entry is string => entry !== null);
    });

    expect(missingEntries).toEqual([]);
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
            data-testid="switch-to-french"
            onClick={() => setLanguage('fr')}
            type="button"
          >
            Switch to French
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

    await user.click(screen.getByTestId('switch-to-french'));

    expect(screen.getByRole('heading', { name: 'Langue et localisation' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Paramètres' })).toBeInTheDocument();
    expect(screen.getByText('fr')).toBeInTheDocument();
    expect(window.localStorage.getItem(languageStorageKey)).toBe('fr');

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
