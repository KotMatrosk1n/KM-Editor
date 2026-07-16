/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import deResource from './resources/de.json';
import enResource from './resources/en.json';
import esResource from './resources/es.json';
import frResource from './resources/fr.json';
import ruResource from './resources/ru.json';
import ukResource from './resources/uk.json';
import zhResource from './resources/zh.json';
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
    expect(translateLiteralForLanguage('es', '8 rolls')).toBe('8 tiradas');
    expect(
      translateLiteralForLanguage(
        'es',
        'Closest input is 1/4,096. 8 rolls gives about 1/512 (0.195%).'
      )
    ).toBe(
      'La entrada más cercana es 1/4,096. 8 tiradas dan aproximadamente 1/512 (0.195%).'
    );
    expect(
      translateLiteralForLanguage('es', 'Shiny Rate is fixed at 8 PID rolls.')
    ).toBe('Probabilidad shiny está fijada en 8 tiradas PID.');
    expect(
      translateLiteralForLanguage('es', 'Stage Shiny Rate fixed 8 rolls.')
    ).toBe('Preparar Probabilidad shiny fija de 8 tiradas.');
    expect(
      translateLiteralForLanguage(
        'es',
        "Default restores the game's original runtime-dependent reroll count calculation."
      )
    ).toBe(
      'Predeterminado restaura el cálculo original de rerolls dependiente de la ejecución del juego.'
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
    expect(translateLiteralForLanguage('es', 'KM Editor will write the edited files under')).toBe(
      'KM Editor escribira los archivos editados bajo'
    );
    expect(
      translateLiteralForLanguage('es', 'as loose LayeredFS RomFS files for Trinity Bypass.')
    ).toBe('como archivos RomFS sueltos de LayeredFS para Trinity Bypass.');
    expect(translateLiteralForLanguage('es', 'This creates or replaces files under')).toBe(
      'Esto crea o reemplaza archivos bajo'
    );
    expect(translateLiteralForLanguage('es', 'the Trinity file index')).toBe(
      'el indice de archivos Trinity'
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
    expect(translateLiteralForLanguage('es', 'Item for Bag Hook slot 12')).toBe(
      'Objeto para el hueco 12 de Gancho de bolsa'
    );
    expect(translateLiteralForLanguage('es', 'Quantity for Bag Hook slot 12')).toBe(
      'Cantidad para el hueco 12 de Gancho de bolsa'
    );
    expect(translateLiteralForLanguage('es', 'Show Item for Bag Hook slot 12 options')).toBe(
      'Mostrar opciones de Objeto para el hueco 12 de Gancho de bolsa'
    );
    expect(translateLiteralForLanguage('es', 'slot 12: Master Ball x5')).toBe(
      'hueco 12: Master Ball x5'
    );
    expect(translateLiteralForLanguage('es', 'slot 12: item 9999 x5')).toBe(
      'hueco 12: objeto 9999 x5'
    );
    expect(
      translateLiteralForLanguage('es', 'slot 12: Master Ball x5, slot 13: item 9999 x2')
    ).toBe('hueco 12: Master Ball x5, hueco 13: objeto 9999 x2');
    expect(translateLiteralForLanguage('es', 'Bike (#700) [Key]')).toBe(
      'Bike (#700) [Objeto clave]'
    );
    expect(translateLiteralForLanguage('de', 'Bike (#700) [Key]')).toBe(
      'Bike (#700) [Basis-Item]'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Clear item 1128 from Starting Items slot(s) 2, 7 before installing or refreshing Royal Candy; KM will not delete those grants automatically.'
      )
    ).toBe(
      'Quita el objeto 1128 de los huecos 2, 7 de Objetos iniciales antes de instalar o actualizar Caramelo Royal; KM no eliminará esas entregas automáticamente.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Repair the damaged Bag Hook slots before editing Starting Items.'
      )
    ).toBe('Repara los huecos dañados de Gancho de bolsa antes de editar Objetos iniciales.');
    expect(
      translateLiteralForLanguage(
        'es',
        'Starting Items cannot overwrite damaged Bag Hook slot(s): 3, 7.'
      )
    ).toBe(
      'Objetos iniciales no puede sobrescribir los huecos dañados de Gancho de bolsa: 3, 7.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Bag Hook slot 4 contains an invalid active grant (item missing, quantity 1000).'
      )
    ).toBe(
      'Bag-Hook-Platz 4 enthält eine ungültige aktive Vergabe (Gegenstand fehlend, Menge 1000).'
    );
    expect(
      translateLiteralForLanguage('es', 'Invalid grant (item missing, quantity 0)')
    ).toBe('Entrega no válida (objeto ausente, cantidad 0)');
    expect(
      translateLiteralForLanguage(
        'es',
        'Starting Items cannot stage grants while the Bag Hook slot bank is damaged or incompatible.'
      )
    ).toBe(
      'Objetos iniciales no puede preparar entregas mientras el banco de huecos de Gancho de bolsa esté dañado o sea incompatible.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Starting Items cannot stage grants until item metadata is readable.'
      )
    ).toBe(
      'Startgegenstände können erst bereitgestellt werden, wenn die Gegenstandsmetadaten lesbar sind.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Clear item 1128 from Starting Items slots 2-20 before staging Royal Candy.'
      )
    ).toBe(
      'Entferne Gegenstand 1128 aus den Startgegenstände-Plätzen 2-20, bevor du Royal Candy bereitstellst.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Item options could not be loaded: Starting Items output directory could not be resolved.'
      )
    ).toBe(
      'No se pudieron cargar las opciones de objetos: No se pudo resolver el directorio de salida de Objetos iniciales.'
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

  it('localizes NPC Item Gift dialogs, generated labels, and diagnostics', () => {
    expect(translateLiteralForLanguage('es', 'Stage NPC')).toBe('Preparar NPC');
    expect(translateLiteralForLanguage('es', 'Revive unavailable (#999)')).toBe(
      'Revive no disponible (#999)'
    );
    expect(translateLiteralForLanguage('es', 'Legacy unavailable (#-1)')).toBe(
      'Legacy no disponible (#-1)'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'NPC Item Gift stages one NPC at a time. Review and apply the staged NPC before opening another NPC.'
      )
    ).toBe(
      'Regalo de objeto NPC solo permite preparar un NPC a la vez. Revisa y aplica el NPC preparado antes de abrir otro.'
    );
    expect(translateLiteralForLanguage('es', '2 gift groups staged')).toBe(
      '2 grupos de regalos preparados'
    );
    expect(translateLiteralForLanguage('es', 'Potion unavailable')).toBe(
      'Potion no disponible'
    );
    expect(translateLiteralForLanguage('es', 'Item 9999')).toBe('Objeto 9999');
    expect(translateLiteralForLanguage('es', 'Item -1')).toBe('Objeto -1');
    expect(translateLiteralForLanguage('es', 'Rotom Bike (#41) [Key]')).toBe(
      'Rotom Bike (#41) [Objeto clave]'
    );
    expect(translateLiteralForLanguage('es', 'Key Items')).toBe('Objetos clave');
    expect(translateLiteralForLanguage('es', 'Gym Leaders NPCs')).toBe(
      'NPC de Líderes de gimnasio'
    );
    expect(translateLiteralForLanguage('es', 'Main Game NPCs NPCs')).toBe(
      'NPCs del juego principal'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'event.script is missing. NPC Item Gift can show defaults, but patching needs this AMX file.'
      )
    ).toBe(
      'Falta event.script. Regalo de objeto NPC puede mostrar los valores predeterminados, pero necesita este archivo AMX para aplicar cambios.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Sonia gift quantity could not be inspected: invalid cell. Known vanilla quantity will be shown.'
      )
    ).toBe(
      'No se pudo inspeccionar la cantidad de Sonia gift: invalid cell. Se mostrará la cantidad original conocida.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        "Pending edit domain 'workflow.items' is not supported by NPC Item Gift."
      )
    ).toBe(
      "Regalo de objeto NPC no admite el dominio de edición pendiente 'workflow.items'."
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'NPC Item Gift change plan preview contains 2 target file(s).'
      )
    ).toBe(
      'La vista previa del plan de cambios de Regalo de objeto NPC contiene 2 archivos de destino.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        "Sonia gift item slot 'item-1' is missing."
      )
    ).toBe("Der Gegenstandsplatz 'item-1' von Sonia gift fehlt.");
    expect(translateLiteralForLanguage('es', 'Sonia gift is missing.')).toBe(
      'Estado de Sonia gift: ausente.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Sonia gift cannot be staged while its mapped operands are damaged.'
      )
    ).toBe(
      'Sonia gift no se puede preparar mientras sus operandos asignados estén dañado.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        "NPC Item Gift selection 'sonia-gift' is not recognized for Sword."
      )
    ).toBe(
      "La selección de Regalo de objeto NPC 'sonia-gift' no se reconoce para Espada."
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Sonia gift uses a fixed helper quantity and only its item can be edited.'
      )
    ).toBe(
      'Sonia gift usa una cantidad auxiliar fija y solo se puede editar su objeto.'
    );
    expect(
      translateLiteralForLanguage('es', 'Sonia gift does not have a loaded source record.')
    ).toBe('Sonia gift no tiene un registro de origen cargado.');
    expect(translateLiteralForLanguage('zh', 'Repairable')).toBe('可修复');
    expect(
      translateLiteralForLanguage(
        'zh',
        'Discard the un-staged NPC Item Gift edits and open another NPC?'
      )
    ).toBe('要放弃尚未暂存的NPC道具礼物编辑并打开其他NPC吗？');
  });

  it('includes NPC Item Gift UI and diagnostic templates in every language', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const requiredLiterals = [
      'NPC Item Gift stages one NPC at a time. Review and apply the staged NPC before opening another NPC.',
      'Discard the un-staged NPC Item Gift edits and open another NPC?',
      'Stage NPC',
      '{count} gift group staged',
      '{count} gift groups staged',
      '{item} unavailable',
      'Item {itemId}',
      'Balls',
      'Berries',
      'Treasures',
      'Key Items',
      'Stage NPC Item Gift changes.',
      'Update reviewed NPC item gift operands in the AMX script while preserving unrelated cells.',
      '{gift} has companion operands that disagree with its primary value. Staging this gift will normalize all owned companions.',
      '{gift} contains a missing item selection.',
      'Royal Candy ownership markers could not be inspected. Item 1128 is withheld to avoid an ownership conflict.',
      '{group} NPCs',
      'NPC Item Gift needs its own edit session before staging.',
      'Stage NPC Item Gift changes before validating.',
      'NPC Item Gift expects exactly one staged NPC edit.',
      'Pending NPC Item Gift change is valid for change-plan review.',
      'NPC Item Gift has no changed item gifts to write.',
      'NPC Item Gift source or output target could not be resolved.',
      'Reviewed NPC Item Gift change plan is stale. Review the change plan again before applying.',
      'NPC Item Gift apply requires valid base paths and a valid output root.',
      'NPC Item Gift needs item options before gifts can be staged.',
      'NPC Item Gift can only stage one NPC at a time.',
      'NPC Item Gift needs at least one gift selection.',
      'NPC Item Gift selection is missing a gift id.',
      'NPC Item Gift pending edit has no gift payload.',
      'NPC Item Gift pending edit payload is malformed.',
      'NPC Item Gift pending edit item payload is malformed.',
      'NPC Item Gift apply requires a configured output root.',
      'NPC Item Gift target must stay inside the configured output root.',
      'Item options could not be decoded: {error}',
      'Item options could not be read: {error}',
      'Item options could not be loaded: {error}',
      '{file} is missing. NPC Item Gift can show defaults, but patching needs this AMX file.',
      '{source} could not be read: {error}. Known vanilla values will be shown for that script.',
      '{gift} quantity could not be inspected: {error}. Known vanilla quantity will be shown.',
      '{gift} item could not be inspected: {error}. Known vanilla item will be shown.',
      "Pending edit domain '{domain}' is not supported by NPC Item Gift.",
      "Pending NPC Item Gift edit '{recordId}' is not supported.",
      'NPC Item Gift change plan preview contains {count} target file.',
      'NPC Item Gift change plan preview contains {count} target files.',
      'Applied NPC Item Gift changes to {path}.',
      'NPC Item Gift source file could not be patched: {error}',
      'NPC Item Gift output file could not be written: {error}',
      'NPC Item Gift has conflicting staged values for AMX cell {cell}.',
      '{file} is required before NPC Item Gift can be staged.',
      "NPC Item Gift selection '{giftId}' is duplicated.",
      "NPC Item Gift selection '{giftId}' is not recognized for this game.",
      '{gift} quantity must be between 1 and 999.',
      "{gift} item slot '{slotId}' is not recognized.",
      '{gift} item {itemId} is not selectable for this project.',
      "{gift} item slot '{slotId}' is duplicated.",
      "{gift} item slot '{slotId}' is missing.",
      'The staged NPC Item Gift entry is invalid. Discard it from Pending Changes before editing or reviewing this workflow.',
      'This NPC cannot be edited yet.',
      'Fixed amount',
      'Key item amount',
      'Enter a whole number from 1 to 999.',
      'Restore gift default',
      'No valid item options are loaded.',
      'Resolve the workflow errors before editing this NPC.',
      '{subject} is {status}.',
      '{gift} does not have a loaded source record.',
      'Repairable',
      'Damaged',
      'available',
      'repairable',
      'damaged',
      'Some changed gifts cannot be staged yet.',
      'NPC Item Gift only supports Pokemon Sword and Pokemon Shield projects.',
      'NPC Item Gift requires valid base RomFS and base ExeFS paths before it can load.',
      'NPC Item Gift could not load any selectable item metadata.',
      'NPC Item Gift has no changed or repairable gifts to stage.',
      'NPC Item Gift changes are staged for change-plan review.',
      'Pending NPC Item Gift selections are not in the canonical staged format.',
      'Pending NPC Item Gift selections contain no changed or repairable gifts.',
      'NPC Item Gift sources changed while preparing the verified apply snapshot.',
      'NPC Item Gift did not produce any reviewed operand patches.',
      'NPC Item Gift selection is missing.',
      '{file} is missing. Its mapped NPC gifts are read-only until the base script is restored.',
      '{file} is unreadable or not a supported 64-bit AMX script. Its mapped gifts are blocked.',
      '{file} cannot verify its mapped operands against the base script. Its mapped gifts are blocked.',
      '{gift} has an incompatible mapped operand or an unverified base layout and is blocked from editing.',
      "Pending NPC Item Gift field '{field}' is not supported.",
      'NPC Item Gift verified output could not be written: {error}',
      'NPC Item Gift source file could not be patched safely: {error}',
      'NPC Item Gift source file could not be read: {error}',
      '{gift} cannot be staged while its mapped operands are {status}.',
      "NPC Item Gift selection '{giftId}' is not recognized for {game}.",
      '{gift} uses a fixed helper quantity and only its item can be edited.',
      '{gift} is missing its item selections.'
    ];

    const missingEntries = Object.entries(localizedResources).flatMap(([language, literals]) =>
      requiredLiterals
        .filter((literal) => !literals[literal])
        .map((literal) => `${language}:literal:${literal}`)
    );

    expect(missingEntries).toEqual([]);
  });

  it('includes Catch Cap labels, status messages, and validation templates in every language', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const requiredLiterals = [
      'Catch cap for {label}',
      'No badges',
      'First badge',
      'Second badge',
      'Third badge',
      'Fourth badge',
      'Fifth badge',
      'Sixth badge',
      'Seventh badge',
      'Eighth badge',
      'Display hook',
      'Runtime hook',
      'Enter a whole level.',
      'Use Lv. {minimum}-{maximum}.',
      'Must be Lv. {minimum} or higher.',
      'Lv. {level} (locked: full badges catch any level)',
      'Catch Cap Editor can patch display and runtime capture checks in exefs/main.',
      'Catch Cap Editor cannot load until project paths validate.',
      'Catch Cap Editor cannot inspect the hook because exefs/main is missing.',
      'Catch Cap Editor hook is not installed.',
      'Catch Cap Editor hook is installed for display and runtime capture checks.',
      'Catch Cap Editor has a legacy display-only hook installed; stage and apply to add the runtime capture gate hook.',
      'Catch Cap Editor values are staged for change-plan review.',
      'Catch Cap Editor uninstall is staged for change-plan review.',
      'Catch Cap Editor needs its own edit session before staging.',
      'Catch Cap Editor needs its own edit session before staging uninstall.',
      'Stage Catch Cap Editor values or uninstall before validating.',
      'Pending Catch Cap Editor change is valid for change-plan review.',
      'Catch Cap pending edit has no cap payload.',
      'Reviewed Catch Cap Editor change plan is stale. Review the change plan again before applying.',
      'Catch Cap Editor cannot stage while exefs/main has a foreign or conflicting catch-cap patch.',
      'Catch Cap badge count {badgeCount} is not available.',
      'Catch Cap badge count {badgeCount} was supplied more than once.',
      'Catch Cap badge count {badgeCount} is missing.',
      'Catch Cap badge count {badgeCount} must be between {minimum} and {maximum}.',
      'Catch Cap badge count {badgeCount} is fixed at level {level}; the game treats eight badges as catch any level.',
      "Catch Cap entry '{entry}' is not valid.",
      'Catch Cap Editor change plan preview contains {count} target file(s).',
      'Catch Cap Editor source file could not be patched: {error}',
      'Catch Cap Editor output file could not be written: {error}',
      'Catch Cap Editor uninstall could not restore exefs/main: {error}',
      'Catch Cap Editor uninstall could not update output: {error}',
      'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges remains Lv.100 because the game treats full badges as catch any level. Stage Uninstall removes only Catch Cap bytes and preserves other hook editors.',
      'Catch Cap Editor is independent from Bag Hook, Royal Candy, and Starting Items. It edits only its reserved exefs/main hook bytes for badge levels 0-7 and patches both the trainer-card display path and the runtime capture gate. Eight badges is locked at Lv.100 because the game treats full badge completion as catch any level. Review before apply or uninstall; cleanup preserves Bag Hook, Royal Candy, and Starting Items when present.',
      'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges is locked at Lv.100 because full badges can catch any level.',
      'Caps',
      'Badge caps',
      'Badge level',
      'Catch Cap',
      'Catch Cap Editor values',
      'Catch Cap badge levels',
      'Eight badges is locked at Lv.100 because full badges can catch any level.',
      'Locked: full badges catch any level.',
      'Open Catch Cap',
      'Selected Cap',
      'Selected Catch Cap',
      'Selected cap',
      'Stage Caps',
      'Catch Cap Editor requires valid base RomFS and base ExeFS paths before it can load.',
      'ExeFS main is missing.',
      'Base ExeFS main could not be resolved from the project graph.',
      'Effective ExeFS main could not be resolved from the project graph.',
      'ExeFS main could not be read for Catch Cap verification.',
      'Base exefs/main is not a selected-game vanilla Catch Cap source. {detail}',
      'Catch Cap Editor sources changed while preparing the verified apply snapshot.',
      'Catch Cap Editor source, vanilla base, or output target could not be resolved.',
      'Catch Cap Editor uninstall could not resolve the reviewed generated and vanilla base exefs/main files.',
      'Catch Cap patching rejected {segment} because its required NSO header hash does not match the decompressed segment.',
      'Catch Cap Editor apply requires valid base paths and a valid output root.',
      'Catch Cap Editor uninstall requires valid base paths and a valid output root.',
      'Catch Cap Editor requires Pokemon Sword or Pokemon Shield to be selected before it can load.',
      'Catch Cap Editor cannot load until Pokemon Sword or Pokemon Shield is selected.',
      'Selected Pokemon Sword or Pokemon Shield project',
      'A source changed while verified apply handles were being acquired.',
      'A source file changed after the change plan was reviewed.',
      'Reviewed change plan does not include source-content verification.',
      'Reviewed change plan is stale. {detail}',
      'Change-plan source verification failed: {detail}',
      'Catch cap for {label} is fixed at level {level}; the game treats eight badges as catch any level.',
      'Catch cap for {label} must be between {minimum} and {maximum}.',
      'Catch cap for {label} must be the same as or higher than the previous badge level (level {level}).',
      'Catch Cap badge count {badgeCount} must be level {level} or higher.',
      'Catch Cap Editor uninstall can only remove a generated LayeredFS exefs/main.',
      'Catch Cap Editor verified output could not be prepared: {error}',
      'Catch Cap Editor uninstall could not prepare a verified restoration: {error}',
      'Applied Catch Cap Editor changes to the configured LayeredFS output root.',
      'Uninstalled Catch Cap Editor from the configured LayeredFS output root.',
      'Catch Cap Editor cannot verify the selected-game vanilla base exefs/main.',
      'Catch Cap Editor cannot inspect the effective exefs/main.',
      'Catch Cap Editor cannot inspect the hook because an exefs/main source could not be read.',
      'Catch Cap Editor requires a verified selected-game vanilla base exefs/main before it can edit or restore the effective source.',
      'Catch Cap Editor supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.',
      'Catch Cap Editor marker bytes are present, but the marker version or reserved metadata is damaged.',
      'Catch Cap Editor marker is present, but the display hook branch or cave graph is damaged or redirected.',
      'Catch Cap Editor marker is present, but the runtime catch gate is neither the exact vanilla formula nor the exact KM hook.',
      'Catch-cap formula tail is already branched, but the KM Catch Cap Hook marker is not present.',
      'Catch Cap Editor found non-vanilla bytes in its display formula, runtime formula, protected epilogues, or reserved caves.',
      'Changing values edits badge counts 0-7; eight badges is fixed at Lv.100 by the game.',
      'The installed table has stale Lv.{level} metadata for eight badges; stage and apply to rewrite it to Lv.100.',
      'Selected {selectedGame}, but exefs/main build ID is {detectedGame}. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites.'
    ];

    const missingEntries = Object.entries(localizedResources).flatMap(([language, literals]) =>
      requiredLiterals
        .filter((literal) => !literals[literal])
        .map((literal) => `${language}:literal:${literal}`)
    );

    expect(missingEntries).toEqual([]);
    expect(translateLiteralForLanguage('es', 'Catch cap for First badge')).toBe(
      'Límite de captura para Primera medalla'
    );
    expect(translateLiteralForLanguage('es', 'Use Lv. 25-100.')).toBe('Usa Nv. 25-100.');
    expect(
      translateLiteralForLanguage('de', 'Catch Cap badge count 3 must be between 1 and 100.')
    ).toBe('Das Fanglimit für 3 Abzeichen muss zwischen 1 und 100 liegen.');
    expect(translateLiteralForLanguage('zh', "Catch Cap entry '3=030' is not valid.")).toBe(
      '捕获上限条目“3=030”无效。'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Catch Cap Editor hook is installed for display and runtime capture checks. Changing values edits badge counts 0-7; eight badges is fixed at Lv.100 by the game. The installed table has stale Lv.33 metadata for eight badges; stage and apply to rewrite it to Lv.100.'
      )
    ).toContain('Nv.33');
    expect(
      translateLiteralForLanguage(
        'es',
        'Catch Cap Editor verified output could not be prepared: raw failure 0xABC'
      )
    ).toBe(
      'No se pudo preparar la salida verificada del Editor de límite de captura: raw failure 0xABC'
    );
    expect(
      translateLiteralForLanguage(
        'zh',
        'Uninstalled Catch Cap Editor from the configured LayeredFS output root.'
      )
    ).toBe('已从配置的LayeredFS输出根目录卸载捕获上限编辑器。');
    expect(
      translateLiteralForLanguage(
        'es',
        'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges remains Lv.100 because the game treats full badges as catch any level. Stage Uninstall removes only Catch Cap bytes and preserves other hook editors.'
      )
    ).toContain('conserva los demás editores de hooks');
    expect(
      translateLiteralForLanguage(
        'de',
        'Selected Pokemon Sword, but exefs/main build ID is Pokemon Shield 1.3.2. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites.'
      )
    ).toBe(
      'Ausgewählt ist Pokémon Schwert, aber die Build-ID von exefs/main gehört zu Pokémon Schild 1.3.2. Der Catch Cap-Editor patcht diese Datei nicht, weil Schwert und Schild unterschiedliche Hook-Stellen verwenden.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Base exefs/main is not a selected-game vanilla Catch Cap source. Catch Cap Editor supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.'
      )
    ).toBe(
      'Die Basis-exefs/main ist keine unveränderte Catch Cap-Quelle für das ausgewählte Spiel. Der Catch Cap-Editor unterstützt exefs/main-Dateien der Version 1.3.2 von Schwert und Schild. Diese Build-ID ist unbekannt.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Catch Cap patching rejected main.text because its required NSO header hash does not match the decompressed segment.'
      )
    ).toBe(
      'Der Catch Cap-Patch hat main.text abgelehnt, weil der erforderliche NSO-Header-Hash nicht zum dekomprimierten Segment passt.'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Reviewed change plan is stale. A source changed while verified apply handles were being acquired.'
      )
    ).toBe(
      'El plan de cambios revisado está obsoleto. Una fuente cambió mientras se obtenían los identificadores de aplicación verificada.'
    );
    expect(
      translateLiteralForLanguage(
        'fr',
        'Reviewed change plan is stale. A source file changed after the change plan was reviewed.'
      )
    ).toBe(
      'Le plan de modifications vérifié est obsolète. Un fichier source a changé après la vérification du plan de modifications.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Reviewed change plan is stale. Reviewed change plan does not include source-content verification.'
      )
    ).toBe(
      'Der geprüfte Änderungsplan ist veraltet. Der geprüfte Änderungsplan enthält keine Prüfung des Quellinhalts.'
    );
    expect(
      translateLiteralForLanguage(
        'zh',
        'Change-plan source verification failed: access denied by operating system'
      )
    ).toBe('更改计划源验证失败：access denied by operating system');
    expect(
      translateLiteralForLanguage(
        'es',
        'Catch cap for Third badge must be between 1 and 100.'
      )
    ).toBe('El límite de captura para Tercera medalla debe estar entre 1 y 100.');
    expect(
      translateLiteralForLanguage(
        'uk',
        'Catch cap for Fourth badge must be the same as or higher than the previous badge level (level 25).'
      )
    ).toContain('рівень 25');

    expect(deResource.literals['Stage Caps']).toBe('Fanglimits bereitstellen');
    expect(frResource.literals['Selected Cap']).toBe('Limite de capture sélectionnée');
    expect(ruResource.literals['Catch Cap']).toBe('Лимит поимки');
    expect(ukResource.literals['Open Catch Cap']).toBe('Відкрити ліміт упіймання');
    expect(zhResource.literals['Stage Caps']).toBe('暂存捕获上限');

    const catchCapTerminologyKeys = [
      'Caps',
      'Badge caps',
      'Badge level',
      'Catch Cap',
      'Catch Cap Editor values',
      'Catch Cap badge levels',
      'Eight badges is locked at Lv.100 because full badges can catch any level.',
      'Locked: full badges catch any level.',
      'Lv. {level} (locked: full badges catch any level)',
      'Open Catch Cap',
      'Selected Cap',
      'Selected Catch Cap',
      'Selected cap',
      'Stage Caps',
      'Catch Cap Editor is independent from Bag Hook, Royal Candy, and Starting Items. It edits only its reserved exefs/main hook bytes for badge levels 0-7 and patches both the trainer-card display path and the runtime capture gate. Eight badges is locked at Lv.100 because the game treats full badge completion as catch any level. Review before apply or uninstall; cleanup preserves Bag Hook, Royal Candy, and Starting Items when present.',
      'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges remains Lv.100 because the game treats full badges as catch any level. Stage Uninstall removes only Catch Cap bytes and preserves other hook editors.',
      'Independent ExeFS editor for badge catch caps 0-7. It patches the display and runtime capture checks; eight badges is locked at Lv.100 because full badges can catch any level.'
    ];
    const mistranslatedCatchCapTerms =
      /Kappe|Mütze|Fangkappe|Casquette|capuchon|кепк|шапк|крышк|кришк|ковпак|ковпач|сценичес|сценіч|Bühne|scène|舞台|阶段/i;
    for (const language of ['de', 'fr', 'ru', 'uk', 'zh'] as const) {
      for (const key of catchCapTerminologyKeys) {
        expect(localizedResources[language][key], `${language}: ${key}`).not.toMatch(
          mistranslatedCatchCapTerms
        );
      }
    }

    const exactMismatchMessages = [
      'Selected Pokemon Sword, but exefs/main build ID is Pokemon Shield 1.3.2. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites.',
      'Selected Pokemon Shield, but exefs/main build ID is Pokemon Sword 1.3.2. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites.'
    ];
    for (const exactMismatchMessage of exactMismatchMessages) {
      for (const language of ['en', 'de', 'es', 'fr', 'ru', 'uk', 'zh'] as const) {
        const translatedMismatch = translateLiteralForLanguage(language, exactMismatchMessage);
        expect(translatedMismatch).toContain(
          translateLiteralForLanguage(language, 'Pokemon Sword')
        );
        expect(translatedMismatch).toContain(
          translateLiteralForLanguage(language, 'Pokemon Shield')
        );
        expect(translatedMismatch).toContain('1.3.2');
        if (language !== 'en') {
          expect(translatedMismatch).not.toBe(exactMismatchMessage);
        }
      }
    }
  });

  it('includes IV Screen UI, status, and diagnostic templates in every language', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const requiredLiterals = [
      'IV Screen V1',
      'Primary value source',
      'X-toggle refresh',
      'Applied IV Screen changes to the configured LayeredFS output root.',
      'Base exefs/main is not a selected-game vanilla IV Screen source. {detail}',
      'ExeFS main sources could not be verified for IV Screen: {error}',
      'ExeFS main could not be read for IV Screen verification.',
      'Independent ExeFS editor for raw IV numbers on the Pokemon Summary stats graph. Install and uninstall touch only exact IV Screen-owned bytes.',
      "Install or refresh IV Screen's reviewed Pokemon Summary raw-IV display graph in exefs/main.",
      'IV Screen cannot inspect the effective exefs/main.',
      'IV Screen cannot inspect the hook because the base and effective exefs/main sources could not be verified as compatible.',
      'IV Screen cannot inspect the hook because an exefs/main source could not be read.',
      'IV Screen cannot load until Pokemon Sword or Pokemon Shield is selected.',
      'IV Screen cannot stage while exefs/main has a foreign or conflicting Pokemon Summary graph.',
      'IV Screen cannot verify the selected-game vanilla base exefs/main.',
      'IV Screen is installed with the exact initial Sword hook layout. Reinstall migrates it, while uninstall safely restores only its historical regions.',
      'IV Screen is not installed.',
      'IV Screen marker bytes are present, but the complete marker reservation is damaged or contains unowned data.',
      'IV Screen patching rejected {segment} because its required NSO header hash does not match the decompressed segment.',
      'IV Screen prepared apply received a noncanonical pending edit.',
      'IV Screen prepared apply requires exactly one canonical pending edit.',
      'IV Screen requires exactly one canonical pending edit.',
      'IV Screen requires Pokemon Sword or Pokemon Shield to be selected before it can load.',
      'IV Screen requires valid base RomFS and base ExeFS paths before it can load.',
      'IV Screen requires a verified selected-game vanilla base exefs/main before it can edit or restore the effective source.',
      'IV Screen source, vanilla base, or output target could not be resolved.',
      'IV Screen sources changed while preparing the verified apply snapshot.',
      'IV Screen uninstall could not prepare a verified restoration: {error}',
      'IV Screen uninstall could not resolve the reviewed generated and vanilla base exefs/main files.',
      'IV Screen uninstall requires an exact current or exact supported legacy install.',
      'IV Screen verified output could not be prepared: {error}',
      'Pending edit does not target the canonical IV Screen install or uninstall record.',
      'Pending IV Screen action payload must be exactly true.',
      'Pending IV Screen edit does not have the canonical staged summary.',
      'Pending IV Screen sources do not match the canonical selected-game base, effective main, and action fingerprint.',
      'Pokemon Summary IV Screen hook sites are already modified and do not contain the IV Screen marker.',
      'Stage IV Screen install or refresh.',
      'Stage IV Screen install or uninstall before validating.',
      'Uninstall the exact recognized IV Screen graph from exefs/main while preserving unrelated supported ExeFS edits.',
      'IV Screen apply requires a configured output root.',
      'Base ExeFS main could not be resolved from the project graph.',
      'Effective ExeFS main could not be resolved from the project graph.',
      'Legacy IV Screen uninstall remains available, but migration is unavailable: {error}',
      'Exact legacy IV Screen uninstall remains available, but migration is blocked: {error}',
      'IV Screen is installed with the exact initial Sword hook layout. Uninstall remains available, but migration is unavailable because {error}',
      'IV Screen is not installed, but installation is blocked because {error}',
      'IV Screen legacy migration is unavailable. {detail}',
      'IV Screen cannot stage while exefs/main has a foreign or conflicting Pokemon Summary hook graph.',
      'IV Screen install preflight could not resolve the effective exefs/main.',
      'IV Screen install or legacy migration is unavailable: {error}',
      'IV Screen install preflight could not read exefs/main: {error}',
      'Independent ExeFS editor for raw IV numbers on the Pokemon Summary stats graph. Install and uninstall touch only IV Screen reserved bytes.',
      "Install or refresh IV Screen's independent Pokemon Summary raw-IV hook in exefs/main.",
      'Uninstall IV Screen from exefs/main while preserving Catch Cap and Royal Candy ExeFS bytes when present.',
      'IV Screen can patch exefs/main.',
      'IV Screen cannot inspect the hook because exefs/main cannot be read.',
      'IV Screen cannot inspect the hook because exefs/main could not be read.',
      'IV Screen cannot inspect the hook because exefs/main is missing.',
      'IV Screen cannot load until project paths validate.',
      'IV Screen cannot stage while exefs/main has a foreign or conflicting Pokemon Summary hook.',
      'IV Screen install is staged for change-plan review.',
      'IV Screen install requires valid base paths and a valid output root.',
      'IV Screen is installed. Reinstalling refreshes the existing raw-IV summary hooks and marker instead of adding a second hook.',
      'IV Screen is installed with an older hook layout. Reinstalling migrates it to the IV-owned summary value-source hooks.',
      'IV Screen is not installed in the current project output.',
      'IV Screen is not installed. Installing adds independent Pokemon Summary stats and X-mode raw-IV value hooks.',
      'IV Screen marker is present, but the owned Pokemon Summary hook sites do not match a supported IV Screen layout.',
      'IV Screen needs its own edit session before staging.',
      'IV Screen needs its own edit session before staging uninstall.',
      'IV Screen source or output target could not be resolved.',
      'IV Screen supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.',
      'IV Screen target must stay inside the configured output root.',
      'IV Screen uninstall can only remove a generated LayeredFS exefs/main.',
      'IV Screen uninstall is staged for change-plan review.',
      'IV Screen uninstall requires valid base paths and a valid output root.',
      'IV Screen uninstall target no longer exists. Review the change plan again before applying.',
      'Pending IV Screen change is valid for change-plan review.',
      'Reviewed IV Screen change plan is stale. Review the change plan again before applying.',
      'Stage IV Screen install.',
      'Stage IV Screen uninstall.',
      'Uninstalled IV Screen from the configured LayeredFS output root.',
      'IV Screen change plan preview contains {count} target file(s).',
      "Pending edit domain '{domain}' is not supported by IV Screen.",
      "Pending IV Screen edit '{recordId}' is not supported.",
      'IV Screen reserved slot {offset} is not empty.',
      'IV Screen expected vanilla {site} at {offset}, but found {actual}.',
      'IV Screen expected vanilla or owned {site} at {offset}.',
      'IV Screen expected vanilla or owned {site} at {offset}, but found {actual}.',
      'IV Screen source file could not be patched: {error}',
      'IV Screen output file could not be written: {error}',
      'IV Screen uninstall could not restore exefs/main: {error}',
      'IV Screen uninstall could not update the output file: {error}'
    ];

    const missingEntries = Object.entries(localizedResources).flatMap(([language, literals]) =>
      requiredLiterals
        .filter((literal) => !literals[literal])
        .map((literal) => `${language}:literal:${literal}`)
    );

    expect(missingEntries).toEqual([]);
    expect(
      translateLiteralForLanguage(
        'es',
        "Pending edit domain 'workflow.items' is not supported by IV Screen."
      )
    ).toBe("Pantalla de IV no admite el dominio de edición pendiente 'workflow.items'.");
    expect(
      translateLiteralForLanguage(
        'de',
        'IV Screen change plan preview contains 2 target file(s).'
      )
    ).toContain('2');
    expect(
      translateLiteralForLanguage(
        'zh',
        'IV Screen reserved slot main.text+0x1391704 is not empty.'
      )
    ).toBe('个体值界面保留位置main.text+0x1391704不为空。');
    expect(
      translateLiteralForLanguage(
        'fr',
        'IV Screen output file could not be written: access denied'
      )
    ).toContain('access denied');

    const mismatch =
      'Selected Pokemon Sword, but exefs/main build ID is Pokemon Shield 1.3.2. IV Screen will not patch this file because Sword and Shield use different Pokemon Summary hook sites.';
    for (const language of ['en', 'de', 'es', 'fr', 'ru', 'uk', 'zh'] as const) {
      const translated = translateLiteralForLanguage(language, mismatch);
      expect(translated).toContain('1.3.2');
      expect(translated).toContain(translateLiteralForLanguage(language, 'Pokemon Sword'));
      expect(translated).toContain(translateLiteralForLanguage(language, 'Pokemon Shield'));
      if (language !== 'en') {
        expect(translated).not.toBe(mismatch);
      }
    }
  });

  it('localizes all 140 live IV Screen reserved labels and the Shield suffix', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const templates = [
      'IV Screen legacy normal graph value source {index}',
      'IV Screen legacy normal stats graph refresh hook branch site',
      'IV Screen legacy secondary-stats setup hook branch site',
      'IV Screen legacy summary renderer wrapper call {index}',
      'IV Screen legacy summary renderer wrapper primary call',
      'IV Screen legacy value-pane visibility flag',
      'IV Screen legacy value-pane visibility mask',
      'IV Screen legacy X-mode sparkle value source {index}',
      'IV Screen legacy X-toggle yellow graph bypass branch',
      'IV Screen marker/version fragment {index}',
      'IV Screen multi-chart HP text value source {index}',
      'IV Screen multi-chart stat source {index}',
      'IV Screen multi-chart stat text value source {index}',
      'IV Screen wrapper cave slot {index}',
      'IV Screen X-mode value-pane visibility {index}',
      'IV Screen X-toggle numeric text pane visibility {index}',
      'IV Screen X-toggle stats refresh call',
      'IV Screen yellow graph raw value site {index}',
      '{label} (Shield)'
    ];
    const indexedLabels = (
      prefix: string,
      start: number,
      count: number,
      pad = true
    ) => Array.from({ length: count }, (_, offset) => ({
      index: pad ? String(start + offset).padStart(2, '0') : String(start + offset),
      label: `${prefix} ${pad ? String(start + offset).padStart(2, '0') : start + offset}`
    }));
    const liveLabels: Array<{ index?: string; label: string }> = [
      ...indexedLabels('IV Screen legacy normal graph value source', 1, 8),
      { label: 'IV Screen legacy normal stats graph refresh hook branch site' },
      { label: 'IV Screen legacy secondary-stats setup hook branch site' },
      ...indexedLabels('IV Screen legacy summary renderer wrapper call', 2, 2),
      { label: 'IV Screen legacy summary renderer wrapper primary call' },
      { label: 'IV Screen legacy value-pane visibility flag' },
      { label: 'IV Screen legacy value-pane visibility mask' },
      ...indexedLabels('IV Screen legacy X-mode sparkle value source', 1, 6),
      { label: 'IV Screen legacy X-toggle yellow graph bypass branch' },
      ...indexedLabels('IV Screen marker/version fragment', 1, 2, false),
      ...indexedLabels('IV Screen multi-chart HP text value source', 1, 2),
      ...indexedLabels('IV Screen multi-chart stat source', 1, 12),
      ...indexedLabels('IV Screen multi-chart stat text value source', 1, 5),
      ...indexedLabels('IV Screen wrapper cave slot', 1, 76),
      ...indexedLabels('IV Screen X-mode value-pane visibility', 1, 6),
      ...indexedLabels('IV Screen X-toggle numeric text pane visibility', 1, 8),
      { label: 'IV Screen X-toggle stats refresh call' },
      ...indexedLabels('IV Screen yellow graph raw value site', 1, 6)
    ];

    expect(liveLabels).toHaveLength(140);
    expect(new Set(liveLabels.map(({ label }) => label)).size).toBe(140);
    for (const [language, literals] of Object.entries(localizedResources)) {
      for (const template of templates) {
        const translation = literals[template];
        expect(translation, `${language}:${template}`).toBeTruthy();
        const placeholders = template.match(/\{(?:index|label)\}/g) ?? [];
        const translatedPlaceholders = translation?.match(/\{(?:index|label)\}/g) ?? [];
        expect(translatedPlaceholders, `${language}:${template}:placeholders`).toEqual(placeholders);
      }
      expect(literals['{label} (Shield)']?.startsWith('{label}')).toBe(true);
    }

    for (const language of ['en', 'de', 'es', 'fr', 'ru', 'uk', 'zh'] as const) {
      for (const { index, label } of liveLabels) {
        const translated = translateLiteralForLanguage(language, label);
        const translatedShield = translateLiteralForLanguage(language, `${label} (Shield)`);
        if (index) {
          expect(translated, `${language}:${label}:index`).toContain(index);
        }
        expect(translatedShield, `${language}:${label}:Shield-order`).toMatch(
          new RegExp(`^${translated.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`)
        );
        if (language !== 'en') {
          expect(translated, `${language}:${label}`).not.toBe(label);
          expect(translatedShield, `${language}:${label}:Shield`).not.toBe(`${label} (Shield)`);
        }
      }
    }
  });

  it('recursively localizes IV Screen migration dependency details', () => {
    const diagnostic =
      'IV Screen legacy migration is unavailable. Exact legacy IV Screen uninstall remains available, but migration is blocked: IV Screen expected vanilla raw IV getter at main.text+0x779070, but found 0x00000000.';
    const translated = translateLiteralForLanguage('es', diagnostic);

    expect(translated).toContain('main.text+0x779070');
    expect(translated).toContain('0x00000000');
    expect(translated).toContain(esResource.literals['raw IV getter']);
    expect(translated).not.toContain('legacy migration is unavailable');
    expect(translated).not.toContain('raw IV getter');
    expect(
      translateLiteralForLanguage(
        'fr',
        'IV Screen install preflight could not read exefs/main: access denied'
      )
    ).toContain('access denied');
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

  it('localizes Chinese static and dynamic UI phrases', () => {
    expect(translateKeyForLanguage('zh', 'settings.language.title')).toBe('语言和本地化');
    expect(translateKeyForLanguage('zh', 'settings.language.chinese')).toBe('简体中文');
    expect(translateLiteralForLanguage('zh', 'Settings')).toBe('设置');
    expect(translateLiteralForLanguage('zh', 'Made by Matroskin')).toBe(
      '由 Matroskin 制作'
    );
    expect(translateLiteralForLanguage('zh', 'Grass / Poison')).toBe('草原 / 中毒');
    expect(translateLiteralForLanguage('zh', 'Browse for Base RomFS')).toBe('浏览基础RomFS');
    expect(translateLiteralForLanguage('zh', 'Makes contact. Allowed range: 0-1')).toBe(
      '接触类. 允许范围: 0-1'
    );
    expect(
      translateLiteralForLanguage(
        'zh',
        'Base ExeFS matches selected Pokemon Scarlet title id 0x0100A3D008C5C000.'
      )
    ).toBe('基础ExeFS与所选宝可梦 朱标题ID 0x0100A3D008C5C000匹配。');
  });

  it('keeps localized resource keys aligned with the English catalogue', () => {
    const localizedResources = {
      de: deResource,
      es: esResource,
      fr: frResource,
      ru: ruResource,
      uk: ukResource,
      zh: zhResource
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

  it('ships Dynamax Adventures legacy-remap cleanup guidance in every language', () => {
    const resources = {
      de: deResource,
      en: enResource,
      es: esResource,
      fr: frResource,
      ru: ruResource,
      uk: ukResource,
      zh: zhResource
    };
    const requiredLiterals = [
      'Stage Repair',
      'Stage Table Restore',
      'Stage Repair or a full vanilla table restore must remove the unsupported legacy final-boss target remap before this field can be edited.',
      'Unsupported legacy final-boss target remap detected. Ordinary row editing and default previews are disabled. Stage Repair or a full vanilla table restore removes it.'
    ] as const;

    for (const [language, resource] of Object.entries(resources)) {
      const literals = resource.literals as Record<string, string>;
      for (const literal of requiredLiterals) {
        expect(literals[literal], `${language}:${literal}`).toBeTruthy();
      }
    }
  });

  it('ships and formats Shiny Rate state in every language catalogue', () => {
    const resources = {
      de: deResource,
      en: enResource,
      es: esResource,
      fr: frResource,
      ru: ruResource,
      uk: ukResource,
      zh: zhResource
    };
    const requiredLiterals = [
      'Always Shiny NOPs the loop break branch.',
      'Break offset',
      'Closest input is 1/{denominator}.',
      'Compare offset',
      "Default restores the game's original runtime-dependent reroll count calculation.",
      'Dynamic',
      'Enter odds from 1/2 to 1/4096.',
      'Fixed rolls',
      'Function offset',
      'Gen 3',
      'Masuda + Shiny Charm',
      "Restores the game's runtime-dependent shiny reroll logic.",
      'Shiny Charm',
      'Shiny Rate cannot inspect the reroll loop because an exefs/main source could not be read.',
      'Shiny Rate is fixed at {count} PID rolls.',
      'Shiny Rate is staged for change-plan review.',
      'Staged rate',
      'Variable',
      '{count} rolls',
      '{count} rolls gives about 1/{denominator} ({percent}).'
    ] as const;

    for (const language of Object.keys(resources) as Array<keyof typeof resources>) {
      const literals = resources[language].literals as Record<string, string>;
      for (const literal of requiredLiterals) {
        expect(literals[literal], `${language}:${literal}`).toBeTruthy();
      }

      expect(
        literals['Shiny Rate cannot inspect the reroll loop because exefs/main could not be read.']
      ).toBeUndefined();

      expect(
        translateLiteralForLanguage(language, 'Shiny Rate is fixed at 8 PID rolls.')
      ).toBe(literals['Shiny Rate is fixed at {count} PID rolls.']!.replace('{count}', '8'));
      expect(translateLiteralForLanguage(language, '8 rolls')).toBe(
        literals['{count} rolls']!.replace('{count}', '8')
      );
      expect(
        translateLiteralForLanguage(
          language,
          'Closest input is 1/4,096. 8 rolls gives about 1/512 (0.195%).'
        )
      ).toBe(
        `${literals['Closest input is 1/{denominator}.']!.replace('{denominator}', '4,096')} ${literals['{count} rolls gives about 1/{denominator} ({percent}).']!
          .replace('{count}', '8')
          .replace('{denominator}', '512')
          .replace('{percent}', '0.195%')}`
      );
    }
  });

  it('ships exact Hyper Training validation and optional-source literals in every catalogue', () => {
    const resources = {
      de: deResource,
      en: enResource,
      es: esResource,
      fr: frResource,
      ru: ruResource,
      uk: ukResource,
      zh: zhResource
    };
    const expected = {
      de: ['Gib eine ganze Zahl ein.', 'Optional fehlt'],
      en: ['Enter a whole number.', 'Optional missing'],
      es: ['Introduce un número entero.', 'Opcional ausente'],
      fr: ['Saisissez un nombre entier.', 'Facultatif manquant'],
      ru: ['Введите целое число.', 'Необязательный файл отсутствует'],
      uk: ['Введіть ціле число.', 'Необов’язковий файл відсутній'],
      zh: ['请输入整数。', '可选文件缺失']
    } as const;

    for (const language of Object.keys(resources) as Array<keyof typeof resources>) {
      expect(resources[language].literals['Enter a whole number.']).toBe(
        expected[language][0]
      );
      expect(resources[language].literals['Optional missing']).toBe(expected[language][1]);
    }
  });

  it('ships exact Hyper Training identity and synchronization labels in every catalogue', () => {
    const resources = {
      de: deResource,
      en: enResource,
      es: esResource,
      fr: frResource,
      ru: ruResource,
      uk: ukResource,
      zh: zhResource
    };
    const expected = {
      de: {
        'Cutoff sync': 'Grenzenabgleich',
        'Detected game': 'Erkanntes Spiel',
        'English dialogue cutoff': 'Grenze des englischen Dialogs',
        'NPC script cutoff': 'NPC-Skript-Grenze',
        'Not verified': 'Nicht verifiziert',
        'Out of sync': 'Nicht synchron',
        'Picker runtime cutoff': 'Auswahl-Laufzeitgrenze',
        Synchronized: 'Synchronisiert'
      },
      en: {
        'Cutoff sync': 'Cutoff sync',
        'Detected game': 'Detected game',
        'English dialogue cutoff': 'English dialogue cutoff',
        'NPC script cutoff': 'NPC script cutoff',
        'Not verified': 'Not verified',
        'Out of sync': 'Out of sync',
        'Picker runtime cutoff': 'Picker runtime cutoff',
        Synchronized: 'Synchronized'
      },
      es: {
        'Cutoff sync': 'Sincronización de límites',
        'Detected game': 'Juego detectado',
        'English dialogue cutoff': 'Límite del diálogo en inglés',
        'NPC script cutoff': 'Límite del script del NPC',
        'Not verified': 'No verificado',
        'Out of sync': 'Desincronizado',
        'Picker runtime cutoff': 'Límite del selector',
        Synchronized: 'Sincronizado'
      },
      fr: {
        'Cutoff sync': 'Synchronisation des seuils',
        'Detected game': 'Jeu détecté',
        'English dialogue cutoff': 'Seuil du dialogue anglais',
        'NPC script cutoff': 'Seuil du script du PNJ',
        'Not verified': 'Non vérifié',
        'Out of sync': 'Désynchronisé',
        'Picker runtime cutoff': 'Seuil du sélecteur',
        Synchronized: 'Synchronisé'
      },
      ru: {
        'Cutoff sync': 'Синхронизация порогов',
        'Detected game': 'Обнаруженная игра',
        'English dialogue cutoff': 'Порог английского диалога',
        'NPC script cutoff': 'Порог скрипта NPC',
        'Not verified': 'Не проверено',
        'Out of sync': 'Не синхронизировано',
        'Picker runtime cutoff': 'Порог выбора',
        Synchronized: 'Синхронизировано'
      },
      uk: {
        'Cutoff sync': 'Синхронізація порогів',
        'Detected game': 'Виявлена гра',
        'English dialogue cutoff': 'Поріг англійського діалогу',
        'NPC script cutoff': 'Поріг скрипту NPC',
        'Not verified': 'Не перевірено',
        'Out of sync': 'Не синхронізовано',
        'Picker runtime cutoff': 'Поріг вибору',
        Synchronized: 'Синхронізовано'
      },
      zh: {
        'Cutoff sync': '等级限制同步',
        'Detected game': '检测到的游戏',
        'English dialogue cutoff': '英文对话等级限制',
        'NPC script cutoff': 'NPC脚本等级限制',
        'Not verified': '未验证',
        'Out of sync': '不同步',
        'Picker runtime cutoff': '选择器运行时等级限制',
        Synchronized: '已同步'
      }
    } as const;

    for (const language of Object.keys(resources) as Array<keyof typeof resources>) {
      for (const [literal, translation] of Object.entries(expected[language])) {
        expect((resources[language].literals as Record<string, string>)[literal]).toBe(
          translation
        );
      }
    }
  });

  it('localizes Hyper Training dynamic levels and summaries without changing technical IDs', () => {
    const buildId = 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471';
    const offset = 'main.text+0x00F9A314';
    const expected = {
      de: [
        'Lv. 42',
        'Wähle Lv. 1-100.',
        'Hypertraining akzeptiert derzeit Pokémon ab Lv.42.',
        'Hypertraining ist nicht synchron: NPC-Skript Lv.41, Auswahl Lv.42, englischer Dialog Lv.43. Wende diesen Editor erneut an, um alle verfügbaren Grenzwerte zu synchronisieren.',
        'Die englischen Dialogzeilen 0 und 3 verwenden Lv.43.',
        `Die Auswahlgrenze befindet sich bei ${offset} sowie in den zugehörigen Listen- und Detailprüfungen des Hypertrainings.`
      ],
      en: [
        'Lv. 42',
        'Choose Lv. 1-100.',
        'Hyper Training currently accepts Pokemon at Lv.42 or higher.',
        'Hyper Training is out of sync: NPC script Lv.41, picker Lv.42, English dialogue Lv.43. Apply this editor again to synchronize every available cutoff.',
        'English dialogue lines 0 and 3 use Lv.43.',
        `Picker cutoff lives at ${offset} and related Hyper Training list/detail checks.`
      ],
      es: [
        'Nv. 42',
        'Elige Nv. 1-100.',
        'Entrenamiento extremo acepta actualmente Pokémon de Nv.42 o superior.',
        'Entrenamiento extremo está desincronizado: script del NPC Nv.41, selector Nv.42, diálogo en inglés Nv.43. Vuelve a aplicar este editor para sincronizar todos los límites disponibles.',
        'Las líneas 0 y 3 del diálogo en inglés usan Nv.43.',
        `El límite del selector se encuentra en ${offset} y en las comprobaciones de lista y detalle relacionadas de Entrenamiento extremo.`
      ],
      fr: [
        'Niv. 42',
        'Choisissez Niv. 1-100.',
        'L’Hyper-entraînement accepte actuellement les Pokémon de Niv.42 ou plus.',
        'L’Hyper-entraînement est désynchronisé : script du PNJ Niv.41, sélecteur Niv.42, dialogue anglais Niv.43. Appliquez de nouveau cet éditeur pour synchroniser tous les seuils disponibles.',
        'Les lignes 0 et 3 du dialogue anglais utilisent le Niv.43.',
        `Le seuil du sélecteur se trouve à ${offset} et dans les vérifications de liste et de détail associées à l’Hyper-entraînement.`
      ],
      ru: [
        'Ур. 42',
        'Выберите уровень 1-100.',
        'Гипертренинг сейчас принимает покемонов уровня 42 и выше.',
        'Гипертренинг не синхронизирован: скрипт NPC: уровень 41, выбор: уровень 42, английский диалог: уровень 43. Примените этот редактор ещё раз, чтобы синхронизировать все доступные пороги.',
        'Строки 0 и 3 английского диалога используют уровень 43.',
        `Порог выбора находится по адресу ${offset} и в связанных проверках списка и подробностей гипертренинга.`
      ],
      uk: [
        'Рів. 42',
        'Виберіть рівень 1-100.',
        'Гіпертренінг зараз приймає покемонів рівня 42 і вище.',
        'Гіпертренінг не синхронізовано: скрипт NPC: рівень 41, вибір: рівень 42, англійський діалог: рівень 43. Застосуйте цей редактор ще раз, щоб синхронізувати всі доступні пороги.',
        'Рядки 0 і 3 англійського діалогу використовують рівень 43.',
        `Поріг вибору розташований за адресою ${offset} і в пов’язаних перевірках списку та подробиць гіпертренінгу.`
      ],
      zh: [
        '等级 42',
        '请选择等级 1-100。',
        '极限训练当前接受等级 42 及以上的宝可梦。',
        '极限训练不同步：NPC脚本等级 41，选择器等级 42，英文对话等级 43。 请再次应用此编辑器，以同步所有可用的等级限制。',
        '英文对话第 0 行和第 3 行使用等级 43。',
        `选择器等级限制位于 ${offset} 以及相关的极限训练列表和详情检查中。`
      ]
    } as const;
    const literals = [
      'Lv. 42',
      'Choose Lv. 1-100.',
      'Hyper Training currently accepts Pokemon at Lv.42 or higher.',
      'Hyper Training is out of sync: NPC script Lv.41, picker Lv.42, English dialogue Lv.43. Apply this editor again to synchronize every available cutoff.',
      'English dialogue lines 0 and 3 use Lv.43.',
      `Picker cutoff lives at ${offset} and related Hyper Training list/detail checks.`
    ] as const;

    for (const language of Object.keys(expected) as Array<keyof typeof expected>) {
      expect(literals.map((literal) => translateLiteralForLanguage(language, literal))).toEqual(
        expected[language]
      );
      expect(translateLiteralForLanguage(language, buildId)).toBe(buildId);
      expect(translateLiteralForLanguage(language, literals[5])).toContain(offset);
    }

    expect(
      translateLiteralForLanguage(
        'es',
        'Hyper Training is out of sync: NPC script Lv.41, picker Lv.42, English dialogue unavailable.'
      )
    ).toBe(
      'Entrenamiento extremo está desincronizado: script del NPC Nv.41, selector Nv.42, diálogo en inglés no disponible.'
    );
    expect(
      translateLiteralForLanguage(
        'de',
        'Hyper Training is out of sync: NPC script Lv.41, picker Lv.42, English dialogue unverified.'
      )
    ).toBe(
      'Hypertraining ist nicht synchron: NPC-Skript Lv.41, Auswahl Lv.42, englischer Dialog nicht verifiziert.'
    );
  });

  it('ships Starting Items backend diagnostic templates in every language resource', () => {
    const resources = { deResource, enResource, esResource, frResource, ruResource, ukResource, zhResource };
    const diagnosticLiterals = [
      'Starting Items cannot overwrite damaged Bag Hook slot(s): {slots}.',
      'Bag Hook slot {slot} contains an invalid active grant (item {item}, quantity {quantity}).',
      'Invalid grant (item {item}, quantity {quantity})',
      'Starting Items cannot stage grants while the Bag Hook slot bank is damaged or incompatible.',
      'Starting Items cannot stage grants until item metadata is readable.',
      'Starting Items requires installed Bag Hook V2 before staging grants.',
      'Starting Items could not load any item records from item.dat.',
      'Item options could not be loaded: {error}',
      'Clear item 1128 from Starting Items slots 2-20 before staging Royal Candy.'
    ];

    const missingEntries = Object.entries(resources).flatMap(([language, resource]) =>
      diagnosticLiterals
        .filter((literal) => !(literal in resource.literals))
        .map((literal) => `${language}:${literal}`)
    );

    expect(missingEntries).toEqual([]);
  });

  it('does not ship unresolved localization placeholder tokens', () => {
    const resources = { deResource, enResource, esResource, frResource, ruResource, ukResource, zhResource };
    const unresolvedEntries = Object.entries(resources).flatMap(([language, resource]) =>
      Object.entries(resource.literals)
        .filter(([, value]) => /(?:KM)?PLA(?:CE|C)?HOLDER\d*TOKEN/i.test(value))
        .map(([literal]) => `${language}:${literal}`)
    );

    expect(unresolvedEntries).toEqual([]);
  });

  it('localizes Shops currencies and range validation messages', () => {
    expect(translateLiteralForLanguage('es', 'BP')).toBe('PB');
    expect(translateLiteralForLanguage('es', 'Dynite Ore')).toBe('Maxinium');
    expect(translateLiteralForLanguage('es', 'Multi')).toBe('Múltiple');
    expect(translateLiteralForLanguage('es', 'Inventory 2 of 4')).toBe('Inventario 2 de 4');
    expect(translateLiteralForLanguage('es', 'Poke Mart Inventories [0-8 Badges] [1 Badge]')).toBe(
      'Inventarios de Poke Mart [0-8 insignias] [1 medalla]'
    );
    expect(translateLiteralForLanguage('es', 'Value must be at least 1.')).toBe(
      'El valor debe ser como mínimo 1.'
    );
    expect(translateLiteralForLanguage('es', 'Value must be at most 999999.')).toBe(
      'El valor debe ser como máximo 999999.'
    );
    expect(translateLiteralForLanguage('es', 'Minimum value is 0.')).toBe(
      'El valor debe ser como mínimo 0.'
    );
    expect(translateLiteralForLanguage('es', 'Maximum value is 999.')).toBe(
      'El valor debe ser como máximo 999.'
    );
  });

  it('localizes Raid Rewards fields and dynamic summaries', () => {
    expect(translateLiteralForLanguage('es', '5-star quantity')).toBe(
      '5 estrellas cantidad'
    );
    expect(translateLiteralForLanguage('es', '5-star drop chance')).toBe(
      '5 estrellas probabilidad de recompensa'
    );
    expect(translateLiteralForLanguage('es', 'Quantity 1/2/3/4/5')).toBe(
      'Cantidad 1/2/3/4/5'
    );
    expect(translateLiteralForLanguage('es', 'Drop chance 40/30/20/10/5%')).toBe(
      'Probabilidad de recompensa 40/30/20/10/5%'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        '1-star 1 item / 2-star 2 items / 3-star 3 items / 4-star 4 items / 5-star 5 items'
      )
    ).toBe(
      '1 estrellas: 1 objeto / 2 estrellas: 2 objetos / 3 estrellas: 3 objetos / 4 estrellas: 4 objetos / 5 estrellas: 5 objetos'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        '1-star 40% chance / 2-star 30% chance / 3-star 20% chance / 4-star 10% chance / 5-star 5% chance'
      )
    ).toBe(
      '1 estrellas: 40% de probabilidad / 2 estrellas: 30% de probabilidad / 3 estrellas: 20% de probabilidad / 4 estrellas: 10% de probabilidad / 5 estrellas: 5% de probabilidad'
    );
  });

  it('localizes Sword and Shield move semantics, dynamic values, and compound help', () => {
    expect(translateLiteralForLanguage('es', 'Always hits')).toBe('Siempre acierta');
    expect(translateLiteralForLanguage('es', '2-5 hits')).toBe('2-5 golpes');
    expect(translateLiteralForLanguage('es', '2-5 turns')).toBe('2-5 turnos');
    expect(translateLiteralForLanguage('es', 'Drain 50%')).toBe('Drenaje del 50%');
    expect(translateLiteralForLanguage('es', 'Recoil 25%')).toBe('Retroceso del 25%');
    expect(translateLiteralForLanguage('es', 'Move-defined / scripted effect')).toBe(
      'Efecto definido por el movimiento / mediante script'
    );
    expect(translateLiteralForLanguage('es', 'Restore 50% HP')).toBe(
      'Restaura un 50% de PS'
    );
    expect(
      translateLiteralForLanguage(
        'es',
        'Can use move. Controls whether the move is enabled. Enabling a base-disabled move does not restore missing battle animations, resources, or learnset references; verify its required game assets before using it. Allowed range: 0-1'
      )
    ).toBe(
      'Puede usar movimiento. Controla si el movimiento está habilitado. Habilitar un movimiento deshabilitado en los datos base no restaura animaciones de combate, recursos ni referencias de listas de movimientos que falten; verifica los recursos de juego necesarios antes de usarlo. Rango permitido: 0-1'
    );
  });

  it('includes Scarlet and Violet visible item placement literals in every language', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const requiredLiterals = [
      'Point type',
      'Visible Item',
      'Visible Items - Paldea',
      'Visible Items - The Teal Mask',
      'Visible Items - The Indigo Disk',
      'Scene-only visible item id.',
      'Scene-only visible item property sheet.',
      'Scene-only visible item quantity.'
    ];

    const missingEntries = Object.entries(localizedResources).flatMap(([language, literals]) =>
      requiredLiterals
        .filter((literal) => !literals[literal])
        .map((literal) => `${language}:literal:${literal}`)
    );

    expect(missingEntries).toEqual([]);
  });

  it('includes Fashion Unlock staged-state and Sword/Shield ownership literals in every language', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const requiredLiterals = [
      'Fashion Unlock Shield direct ownership getter',
      'Fashion Unlock Shield mapped ownership getter',
      'Fashion Unlock staging did not match the requested action, game, session, and source state.',
      'Staging install',
      'Staging uninstall',
      'game mismatch',
      'not inspected',
      'return-true ownership stubs',
      'unknown bytes',
      'unreadable',
      'unsupported',
      'vanilla ownership getters'
    ];

    for (const [language, literals] of Object.entries(localizedResources)) {
      for (const literal of requiredLiterals) {
        expect(literals[literal], `${language}: ${literal}`).toBeTruthy();
        if (language !== 'en') {
          expect(literals[literal], `${language}: ${literal}`).not.toBe(literal);
        }
      }
    }
  });

  it('includes Gym Uniform Removal identity, artifact, and staged-state literals in every language', () => {
    const localizedResources: Record<string, Record<string, string>> = {
      en: enResource.literals,
      de: deResource.literals,
      es: esResource.literals,
      fr: frResource.literals,
      ru: ruResource.literals,
      uk: ukResource.literals,
      zh: zhResource.literals
    };
    const requiredLiterals = [
      'Compatible return-true handler',
      'Conflicting handler bytes',
      'Current KM IPS',
      'Delete generated IPS',
      'Deletes only the exact recognized generated build-ID IPS file.',
      'Foreign handler bytes',
      'Foreign IPS',
      'Gym Uniform Removal Shield uniform-change handler',
      'Gym Uniform Removal Sword uniform-change handler',
      'Gym Uniform Removal staging did not match the requested action, game, session, source, and IPS artifact state.',
      'IPS artifact',
      'IPS file',
      'Invalid IPS',
      'KM return-true handler',
      'Main handler',
      'Not inspected',
      'Not present',
      'Owned bytes',
      'Recognized legacy IPS',
      'Uninstall available',
      'Unsupported build',
      'Vanilla handler',
      'Verified sources'
    ];

    for (const [language, literals] of Object.entries(localizedResources)) {
      for (const literal of requiredLiterals) {
        expect(literals[literal], `${language}: ${literal}`).toBeTruthy();
        if (language !== 'en') {
          expect(literals[literal], `${language}: ${literal}`).not.toBe(literal);
        }
      }
    }
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
