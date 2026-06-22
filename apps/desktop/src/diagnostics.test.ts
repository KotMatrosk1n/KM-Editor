/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { ApiDiagnostic } from './bridge/contracts';
import { formatDiagnosticMessage } from './diagnostics';
import { translateLiteralForLanguage } from './localization';

describe('diagnostics', () => {
  it('formats available diagnostic metadata as plain text', () => {
    const diagnostic: ApiDiagnostic = {
      domain: 'workflow.modMerger',
      expected: 'Select matching RomFS files on both sides.',
      field: 'selectedFiles',
      file: 'romfs/bin/appli/shop/bin/shop_data.bin',
      message: 'Selected files must match on both sides',
      severity: 'error'
    };

    expect(formatDiagnosticMessage(diagnostic)).toBe(
      'Selected files must match on both sides. Area: Mod Merger. File: romfs/bin/appli/shop/bin/shop_data.bin. Field: Selected Files. Expected: Select matching RomFS files on both sides.'
    );
  });

  it('leaves plain messages alone when no metadata is available', () => {
    expect(
      formatDiagnosticMessage({
        message: 'No diagnostics.',
        severity: 'info'
      })
    ).toBe('No diagnostics.');
  });

  it('keeps successful informational diagnostics concise', () => {
    expect(
      formatDiagnosticMessage({
        domain: 'workflow.moves',
        expected: 'A pending move change that can be staged.',
        field: 'moveId',
        message: 'Pending move change is valid',
        severity: 'info'
      })
    ).toBe('Pending move change is valid.');
  });

  it('localizes diagnostic chrome while preserving raw technical values', () => {
    const diagnostic: ApiDiagnostic = {
      domain: 'workflow.modMerger',
      expected: 'Select matching RomFS files on both sides.',
      field: 'selectedFiles',
      file: 'romfs/bin/appli/shop/bin/shop_data.bin',
      message: 'Selected files must match on both sides',
      severity: 'error'
    };

    expect(formatDiagnosticMessage(diagnostic, (literal) => translateLiteralForLanguage('es', literal))).toBe(
      'Selected files must match on both sides. Área: Fusionador de mods. Archivo: romfs/bin/appli/shop/bin/shop_data.bin. Campo: Archivos seleccionados. Esperado: Select matching RomFS files on both sides.'
    );
  });
});
