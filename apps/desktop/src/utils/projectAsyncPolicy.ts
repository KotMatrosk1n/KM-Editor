/* SPDX-License-Identifier: GPL-3.0-only */

export type ProjectQueryTicket<TChannel> = Readonly<{
  channel: TChannel;
  epoch: number;
  generation: number;
  signal: AbortSignal;
}>;

/**
 * Owns the freshness boundary for one project-scoped controller. Superseding a
 * channel aborts cooperative local work and makes every older ticket stale.
 * Bridge calls that cannot consume AbortSignal are still protected because a
 * stale ticket can never publish its response.
 */
export class ProjectQueryEpoch<TChannel> {
  private abortControllers = new Map<TChannel, AbortController>();
  private epoch = 0;
  private generations = new Map<TChannel, number>();

  public capture(channel: TChannel): ProjectQueryTicket<TChannel> {
    let abortController = this.abortControllers.get(channel);
    if (!abortController || abortController.signal.aborted) {
      abortController = new AbortController();
      this.abortControllers.set(channel, abortController);
    }
    return {
      channel,
      epoch: this.epoch,
      generation: this.generations.get(channel) ?? 0,
      signal: abortController.signal
    };
  }

  public supersede(channel: TChannel): ProjectQueryTicket<TChannel> {
    this.abortControllers.get(channel)?.abort();
    this.abortControllers.set(channel, new AbortController());
    this.generations.set(channel, (this.generations.get(channel) ?? 0) + 1);
    return this.capture(channel);
  }

  public invalidateAll() {
    for (const controller of this.abortControllers.values()) controller.abort();
    this.abortControllers.clear();
    this.generations.clear();
    this.epoch += 1;
  }

  public isCurrent(ticket: ProjectQueryTicket<TChannel>) {
    return !ticket.signal.aborted &&
      ticket.epoch === this.epoch &&
      ticket.generation === (this.generations.get(ticket.channel) ?? 0);
  }
}

type BoundedLruCacheOptions<TKey, TValue> = Readonly<{
  maximumEntries: number;
  maximumWeight?: number;
  weight?: (value: TValue, key: TKey) => number;
}>;

type WeightedValue<TValue> = {
  value: TValue;
  weight: number;
};

/** A settled-value cache with exact entry and optional memory-weight bounds. */
export class BoundedLruCache<TKey, TValue> {
  private readonly entries = new Map<TKey, WeightedValue<TValue>>();
  private readonly maximumEntries: number;
  private readonly maximumWeight: number;
  private readonly weight: (value: TValue, key: TKey) => number;
  private totalWeight = 0;

  public constructor(options: BoundedLruCacheOptions<TKey, TValue>) {
    if (!Number.isSafeInteger(options.maximumEntries) || options.maximumEntries < 1) {
      throw new RangeError('Bounded LRU maximumEntries must be a positive safe integer.');
    }
    const maximumWeight = options.maximumWeight ?? Number.MAX_SAFE_INTEGER;
    if (!Number.isSafeInteger(maximumWeight) || maximumWeight < 1) {
      throw new RangeError('Bounded LRU maximumWeight must be a positive safe integer.');
    }
    this.maximumEntries = options.maximumEntries;
    this.maximumWeight = maximumWeight;
    this.weight = options.weight ?? (() => 1);
  }

  public get size() {
    return this.entries.size;
  }

  public clear() {
    this.entries.clear();
    this.totalWeight = 0;
  }

  public delete(key: TKey) {
    const existing = this.entries.get(key);
    if (!existing) return false;
    this.totalWeight -= existing.weight;
    return this.entries.delete(key);
  }

  public get(key: TKey): TValue | undefined {
    const existing = this.entries.get(key);
    if (!existing) return undefined;
    this.entries.delete(key);
    this.entries.set(key, existing);
    return existing.value;
  }

  public set(key: TKey, value: TValue) {
    this.delete(key);
    const measuredWeight = this.weight(value, key);
    if (!Number.isSafeInteger(measuredWeight) || measuredWeight < 0) {
      throw new RangeError('Bounded LRU entry weight must be a nonnegative safe integer.');
    }
    if (measuredWeight > this.maximumWeight) return;
    this.entries.set(key, { value, weight: measuredWeight });
    this.totalWeight += measuredWeight;
    while (
      this.entries.size > this.maximumEntries ||
      this.totalWeight > this.maximumWeight
    ) {
      const oldest = this.entries.keys().next();
      if (oldest.done) break;
      this.delete(oldest.value);
    }
  }
}

class ExactKeySingleFlight {
  private readonly requests = new Map<string, Promise<unknown>>();
  private readonly maximumInFlightKeys: number;

  public constructor(maximumInFlightKeys: number) {
    if (!Number.isSafeInteger(maximumInFlightKeys) || maximumInFlightKeys < 1) {
      throw new RangeError('Single-flight maximumInFlightKeys must be a positive safe integer.');
    }
    this.maximumInFlightKeys = maximumInFlightKeys;
  }

  public run<T>(key: string, read: () => Promise<T>): Promise<T> {
    const existing = this.requests.get(key);
    if (existing) return existing as Promise<T>;
    if (this.requests.size >= this.maximumInFlightKeys) {
      return Promise.reject(new ProjectReadAdmissionError(this.maximumInFlightKeys));
    }

    let pending: Promise<T>;
    try {
      pending = Promise.resolve(read());
    } catch (error) {
      pending = Promise.reject(error);
    }
    this.requests.set(key, pending);
    const removeSettledRequest = () => {
      if (this.requests.get(key) === pending) this.requests.delete(key);
    };
    void pending.then(removeSettledRequest, removeSettledRequest);
    return pending;
  }
}

const projectQueryKeyTextEncoder = new TextEncoder();

/**
 * Coalesces only identical, explicitly classified independent reads. Owners are
 * weakly held, and rejected or fulfilled requests are removed immediately. It
 * intentionally has no API for edits, output writes, or resource lifecycles.
 */
export class ProjectReadSingleFlight {
  private readonly byOwner = new WeakMap<object, ExactKeySingleFlight>();
  private readonly maximumKeyBytes: number;
  private readonly maximumInFlightKeysPerOwner: number;

  public constructor(
    maximumInFlightKeysPerOwner = 64,
    maximumKeyBytes = 256 * 1_024
  ) {
    if (
      !Number.isSafeInteger(maximumInFlightKeysPerOwner) ||
      maximumInFlightKeysPerOwner < 1
    ) {
      throw new RangeError(
        'Project read maximumInFlightKeysPerOwner must be a positive safe integer.'
      );
    }
    if (!Number.isSafeInteger(maximumKeyBytes) || maximumKeyBytes < 1) {
      throw new RangeError('Project read maximumKeyBytes must be a positive safe integer.');
    }
    this.maximumInFlightKeysPerOwner = maximumInFlightKeysPerOwner;
    this.maximumKeyBytes = maximumKeyBytes;
  }

  public run<T>(owner: object, exactKey: string, read: () => Promise<T>): Promise<T> {
    if (projectQueryKeyTextEncoder.encode(exactKey).byteLength > this.maximumKeyBytes) {
      return Promise.reject(new ProjectReadKeyTooLargeError(this.maximumKeyBytes));
    }
    let requests = this.byOwner.get(owner);
    if (!requests) {
      requests = new ExactKeySingleFlight(this.maximumInFlightKeysPerOwner);
      this.byOwner.set(owner, requests);
    }
    return requests.run(exactKey, read);
  }
}

export class ProjectReadAdmissionError extends Error {
  public constructor(maximumInFlightKeys: number) {
    super(
      `The project read queue already contains ${maximumInFlightKeys} distinct in-flight requests.`
    );
    this.name = 'ProjectReadAdmissionError';
  }
}

export class ProjectReadKeyTooLargeError extends Error {
  public constructor(maximumKeyBytes: number) {
    super(
      `The canonical project read identity exceeds ${maximumKeyBytes} UTF-8 bytes.`
    );
    this.name = 'ProjectReadKeyTooLargeError';
  }
}

export const projectReadSingleFlight = new ProjectReadSingleFlight();

/** Serial admission for stateful operations that must never overlap. */
export class ProjectSerialTaskQueue {
  private readonly maximumPendingOperations: number;
  private pendingOperations = 0;
  private tail: Promise<void> = Promise.resolve();

  public constructor(maximumPendingOperations = 64) {
    if (!Number.isSafeInteger(maximumPendingOperations) || maximumPendingOperations < 1) {
      throw new RangeError(
        'Project operation maximumPendingOperations must be a positive safe integer.'
      );
    }
    this.maximumPendingOperations = maximumPendingOperations;
  }

  public run<T>(operation: () => Promise<T>): Promise<T> {
    if (this.pendingOperations >= this.maximumPendingOperations) {
      return Promise.reject(
        new ProjectOperationAdmissionError(this.maximumPendingOperations)
      );
    }
    this.pendingOperations += 1;
    const result = this.tail.then(operation, operation);
    this.tail = result.then(() => undefined, () => undefined);
    const releaseAdmission = () => { this.pendingOperations -= 1; };
    void result.then(releaseAdmission, releaseAdmission);
    return result;
  }
}

export class ProjectOperationAdmissionError extends Error {
  public constructor(maximumPendingOperations: number) {
    super(
      `The ordered project operation queue already contains ${maximumPendingOperations} pending operations.`
    );
    this.name = 'ProjectOperationAdmissionError';
  }
}

/** One-task cleanup deferral used to distinguish StrictMode replay from unmount. */
export class DeferredStrictModeCleanup {
  private timer: number | null = null;

  public cancel() {
    if (this.timer === null) return;
    globalThis.clearTimeout(this.timer);
    this.timer = null;
  }

  public schedule(cleanup: () => void) {
    this.cancel();
    this.timer = globalThis.setTimeout(() => {
      this.timer = null;
      cleanup();
    }, 0);
  }
}

export type ProjectAsyncOperationClass =
  | 'independentRead'
  | 'orderedRead'
  | 'resourceMutation'
  | 'mutation';

/**
 * Exhaustive frontend policy for the project-scoped analysis bridge calls.
 * New controller calls must be classified before the contract check accepts
 * them. Only independentRead entries may enter exact-key single flight.
 */
export const analysisBridgeOperationPolicies = {
  closeResearchSource: 'resourceMutation',
  compare: 'independentRead',
  compareExternal: 'orderedRead',
  compareResearchSources: 'orderedRead',
  exportKmRecipe: 'orderedRead',
  getCapabilities: 'independentRead',
  getEntity: 'independentRead',
  getGameModuleCapabilities: 'independentRead',
  getGuidedDesignCapabilities: 'independentRead',
  getImpact: 'independentRead',
  getOwnership: 'independentRead',
  getReferences: 'independentRead',
  getResearchLabCapabilities: 'independentRead',
  getSemanticChanges: 'independentRead',
  getSemanticMergeCapabilities: 'independentRead',
  mutateResearchAnnotations: 'mutation',
  openResearchSource: 'resourceMutation',
  openSemanticMergeSource: 'resourceMutation',
  previewGuidedDesign: 'independentRead',
  previewKmRecipe: 'orderedRead',
  previewSemanticMerge: 'orderedRead',
  queryBalanceLab: 'independentRead',
  queryGameModule: 'independentRead',
  readProjectSourceRevision: 'independentRead',
  readResearchAnnotations: 'orderedRead',
  readResearchByteWindow: 'orderedRead',
  search: 'independentRead',
  validateKmRecipe: 'orderedRead'
} as const satisfies Record<string, ProjectAsyncOperationClass>;

type AnalysisBridgeOperation = keyof typeof analysisBridgeOperationPolicies;
type AnalysisBridgeOperationOfClass<TClass extends ProjectAsyncOperationClass> = {
  [TOperation in AnalysisBridgeOperation]:
    (typeof analysisBridgeOperationPolicies)[TOperation] extends TClass
      ? TOperation
      : never;
}[AnalysisBridgeOperation];

export function runIndependentProjectRead<
  TOperation extends AnalysisBridgeOperationOfClass<'independentRead'>,
  TResult
>(
  operation: TOperation,
  owner: object,
  exactRequest: unknown,
  read: () => Promise<TResult>
) {
  return projectReadSingleFlight.run(
    owner,
    stableProjectQueryKey(operation, exactRequest),
    read
  );
}

/**
 * exactRequest must be a JSON-compatible DTO. Object properties are sorted at
 * every level so logically identical DTOs share a key regardless of insertion
 * order; non-JSON objects and cycles fail closed.
 */
export function stableProjectQueryKey(operation: string, exactRequest: unknown) {
  return JSON.stringify([operation, canonicalJsonValue(exactRequest, new Set())]);
}

function canonicalJsonValue(value: unknown, ancestors: Set<object>): unknown {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || Object.is(value, -0)) {
      throw new TypeError('Project query identity numbers must be finite JSON numbers other than -0.');
    }
    return value;
  }
  if (typeof value !== 'object') {
    throw new TypeError('Project query identity must contain only JSON-compatible values.');
  }
  if (ancestors.has(value)) {
    throw new TypeError('Project query identity cannot contain a circular value.');
  }
  ancestors.add(value);
  try {
    if (Array.isArray(value)) {
      const ownKeys = Reflect.ownKeys(value);
      if (ownKeys.some((key) => typeof key === 'symbol')) {
        throw new TypeError('Project query identity arrays cannot contain symbol properties.');
      }
      const keys = (ownKeys as string[]).filter((key) => key !== 'length');
      if (
        keys.length !== value.length ||
        keys.some((key, index) => key !== String(index))
      ) {
        throw new TypeError(
          'Project query identity arrays must be dense and cannot contain extra properties.'
        );
      }
      return keys.map((key) => {
        const descriptor = Object.getOwnPropertyDescriptor(value, key);
        if (!descriptor || !('value' in descriptor)) {
          throw new TypeError('Project query identity cannot contain accessor properties.');
        }
        return canonicalJsonValue(descriptor.value, ancestors);
      });
    }
    const prototype = Object.getPrototypeOf(value);
    if (prototype !== Object.prototype && prototype !== null) {
      throw new TypeError('Project query identity must contain only JSON objects and arrays.');
    }
    const canonical = Object.create(null) as Record<string, unknown>;
    const keys = Reflect.ownKeys(value);
    if (keys.some((key) => typeof key === 'symbol')) {
      throw new TypeError('Project query identity objects cannot contain symbol properties.');
    }
    for (const key of (keys as string[]).sort()) {
      const descriptor = Object.getOwnPropertyDescriptor(value, key);
      if (!descriptor?.enumerable || !('value' in descriptor)) {
        throw new TypeError(
          'Project query identity object properties must be enumerable data values.'
        );
      }
      canonical[key] = canonicalJsonValue(descriptor.value, ancestors);
    }
    return canonical;
  } finally {
    ancestors.delete(value);
  }
}

const orderedProjectOperations = new WeakMap<object, ProjectSerialTaskQueue>();

export function runOrderedProjectOperation<
  TOperation extends Exclude<
    AnalysisBridgeOperation,
    AnalysisBridgeOperationOfClass<'independentRead'>
  >,
  TOwner extends object,
  TResult
>(
  _operation: TOperation,
  owner: TOwner,
  operation: (admittedOwner: TOwner) => Promise<TResult>
) {
  let queue = orderedProjectOperations.get(owner);
  if (!queue) {
    queue = new ProjectSerialTaskQueue();
    orderedProjectOperations.set(owner, queue);
  }
  return queue.run(() => operation(owner));
}

export type RealQueryUnitStatus = 'idle' | 'loading' | 'ready' | 'error';
export type RealQueryUnitProgress = Readonly<{
  completedUnitCount: number;
  hasError: boolean;
  isReady: boolean;
  totalUnitCount: number;
}>;

/** Measures determinate progress from completed operations, never elapsed time. */
export function measureRealQueryUnits(
  statuses: readonly RealQueryUnitStatus[]
): RealQueryUnitProgress {
  if (statuses.length === 0) {
    throw new RangeError('Measured query progress requires at least one real operation.');
  }
  const completedUnitCount = statuses.filter((status) => status === 'ready').length;
  return {
    completedUnitCount,
    hasError: statuses.some((status) => status === 'error'),
    isReady: completedUnitCount === statuses.length,
    totalUnitCount: statuses.length
  };
}
