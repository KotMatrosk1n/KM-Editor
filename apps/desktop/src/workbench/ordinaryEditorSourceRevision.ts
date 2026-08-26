/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useState } from 'react';
import { canonicalJsonStringify } from '../authoring/rowClipboardCanonical';
import { workspaceDraftMaximumPayloadBytes } from '../bridge/workspaceDraftContracts';

const fingerprintPattern = /^[a-f0-9]{64}$/u;

export type OrdinaryEditorEntitySourceRevisionState = {
  fingerprint: string | null;
  status: 'idle' | 'loading' | 'ready' | 'error';
};

export async function fingerprintOrdinaryEditorEntitySource(
  projectSourceRevisionFingerprint: string,
  entityPreimage: unknown
) {
  if (!fingerprintPattern.test(projectSourceRevisionFingerprint)) {
    throw new Error('The project source revision is invalid.');
  }
  if (!globalThis.crypto?.subtle) {
    throw new Error('The source revision digest is unavailable.');
  }

  const canonical = canonicalJsonStringify({
    entityPreimage,
    projectSourceRevisionFingerprint,
    schema: 'ordinary-editor-entity-source-v1'
  });
  const bytes = new TextEncoder().encode(canonical);
  if (bytes.byteLength > workspaceDraftMaximumPayloadBytes) {
    throw new Error('The editor source preimage exceeds its bounded limit.');
  }
  const digest = await globalThis.crypto.subtle.digest(
    'SHA-256',
    bytes as unknown as BufferSource
  );
  return [...new Uint8Array(digest)]
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('');
}

export function useOrdinaryEditorEntitySourceRevision(options: {
  entityPreimage: unknown | null;
  projectSourceRevisionFingerprint: string | null;
}) {
  const [state, setState] = useState<OrdinaryEditorEntitySourceRevisionState>({
    fingerprint: null,
    status: 'idle'
  });

  useEffect(() => {
    let current = true;
    if (options.entityPreimage === null || !options.projectSourceRevisionFingerprint) {
      setState({ fingerprint: null, status: 'idle' });
      return;
    }
    setState({ fingerprint: null, status: 'loading' });
    void fingerprintOrdinaryEditorEntitySource(
      options.projectSourceRevisionFingerprint,
      options.entityPreimage
    ).then(
      (fingerprint) => {
        if (current) setState({ fingerprint, status: 'ready' });
      },
      () => {
        if (current) setState({ fingerprint: null, status: 'error' });
      }
    );
    return () => {
      current = false;
    };
  }, [options.entityPreimage, options.projectSourceRevisionFingerprint]);

  return state;
}
