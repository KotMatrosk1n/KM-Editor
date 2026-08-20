/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect } from 'react';
import type { BalanceLabProjectBridgeApi } from '../../bridge/balanceLabProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
import {
  BalanceLabSection,
  BalanceLabStatusPanel
} from './BalanceLabSection';
import type { SemanticQueryStatus } from '../semantic-explore/useSemanticExploreController';
import {
  useBalanceLabController,
  type BalanceLabLayer
} from './useBalanceLabController';

export type BalanceLabRuntimeProps = {
  availableLayers?: readonly BalanceLabLayer[];
  bridge: BalanceLabProjectBridgeApi;
  capabilityStatus: SemanticQueryStatus;
  onEnsureCapabilities: () => Promise<void>;
  onNavigateFinding: (record: SemanticExploreRecordRef) => void;
  onStaleRevision: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export function BalanceLabRuntime({
  availableLayers,
  bridge,
  capabilityStatus,
  onEnsureCapabilities,
  onNavigateFinding,
  onStaleRevision,
  revision,
  scope
}: BalanceLabRuntimeProps) {
  const { t } = useLocalization();
  useEffect(() => {
    if (!revision && capabilityStatus === 'idle') {
      void onEnsureCapabilities();
    }
  }, [capabilityStatus, onEnsureCapabilities, revision]);

  if (!revision) {
    const isError = capabilityStatus === 'error' || capabilityStatus === 'ready';
    return (
      <section aria-labelledby="balance-lab-title" className="km-balance-lab wide-panel">
        <header className="km-balance-heading">
          <div>
            <p>{t('balanceLab.eyebrow')}</p>
            <h2 id="balance-lab-title">{t('balanceLab.title')}</h2>
            <span>{t('balanceLab.description')}</span>
          </div>
        </header>
        <BalanceLabStatusPanel
          kind={isError ? 'error' : 'loading'}
          {...(isError ? { onRetry: () => void onEnsureCapabilities() } : {})}
        />
      </section>
    );
  }

  return (
    <BalanceLabReadyRuntime
      {...(availableLayers ? { availableLayers } : {})}
      bridge={bridge}
      onNavigateFinding={onNavigateFinding}
      onStaleRevision={onStaleRevision}
      revision={revision}
      scope={scope}
    />
  );
}

function BalanceLabReadyRuntime({
  availableLayers,
  bridge,
  onNavigateFinding,
  onStaleRevision,
  revision,
  scope
}: Omit<BalanceLabRuntimeProps, 'capabilityStatus' | 'onEnsureCapabilities'> & {
  revision: SemanticExploreRevision;
}) {
  const controller = useBalanceLabController({
    bridge,
    onStaleRevision,
    revision,
    scope
  });
  return (
    <BalanceLabSection
      {...(availableLayers ? { availableLayers } : {})}
      controller={controller}
      onNavigateFinding={onNavigateFinding}
    />
  );
}
