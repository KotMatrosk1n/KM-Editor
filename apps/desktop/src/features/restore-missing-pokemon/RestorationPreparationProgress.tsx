/* SPDX-License-Identifier: GPL-3.0-only */
import type { RestorationPreparation } from '../../bridge/restorationContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';

const stages = ['sources', 'models', 'gameplay', 'voices', 'validation', 'saving'] as const;
export function RestorationPreparationProgress({ preparation: job, name, onCancel }: {
  preparation: RestorationPreparation; name: string; onCancel: () => void;
}) {
  const { t } = useLocalization();
  const running = job.status === 'preparing';
  const complete = job.status === 'prepared';
  const stage = complete ? stages.length : Math.max(0, stages.indexOf(job.phase as typeof stages[number]));
  const activity = job.activity || job.phase;
  const recentResources = job.recentResources ?? [];
  return <section aria-label={t('restoration.progress.title')} aria-busy={running}>
    <LoadingProgress className="restoration-preparation" label={name} rawLabel
    status={running ? 'running' : complete ? 'complete' : job.status === 'canceled' ? 'canceled' : 'error'}
    statusLabel={t(`restoration.status.${job.status}`)}
    detail={running ? t(`restoration.progress.activity.${activity}`) : undefined}
    elapsedMilliseconds={job.elapsedMilliseconds}
    completed={running ? job.completedUnits : undefined} total={running ? job.totalUnits ?? undefined : undefined}
    steps={stages.map((phase, index) => ({ label: t(`restoration.progress.stage.${phase}`),
      state: index < stage ? 'complete' : index === stage ? 'active' : 'pending' }))}
    onCancel={onCancel} cancelLabel={t('restoration.cancelPreparation')} cancellationRequested={job.cancellationRequested}>
    {running && job.currentResource ? <p className="km-loading-progress-resource" data-localization-ignore="true"><code>{job.currentResource}</code></p> : null}
    {recentResources.length > 0 ? (
      <details className="work-progress-files">
        <summary>{t('restoration.progress.recentResources')}</summary>
        <ul data-localization-ignore="true">
          {recentResources.map(path => <li key={path}><code>{path}</code></li>)}
        </ul>
      </details>
    ) : null}
    {running ? <p>{t(job.cacheMode === 'existing' ? 'restoration.progress.existing' : 'restoration.progress.temporary')}</p> : null}
    {Object.values(job.unavailableSourceMoves).some(ids => ids.length > 0) ? <p>{t('restoration.excludedMoves', {
      count: Object.values(job.unavailableSourceMoves).reduce((count, ids) => count + ids.length, 0) })}</p> : null}
    </LoadingProgress>
  </section>;
}
