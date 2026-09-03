/* SPDX-License-Identifier: GPL-3.0-only */

export type SessionLocalEditorMutationReservation = Readonly<{
  adapterIdentity: string;
  scopeBaseIdentity: string;
}>;

export type SessionLocalEditorMutationBinding<TDraft> = Readonly<{
  cancelDraftSourceMutation: (
    reservation: SessionLocalEditorMutationReservation
  ) => boolean;
  commitDraftSourceMutation: (
    reservation: SessionLocalEditorMutationReservation,
    reduceLatestPayload: (latestPayload: TDraft) => TDraft
  ) => boolean;
  reserveDraftSourceMutation: () =>
    SessionLocalEditorMutationReservation | null;
}>;

export async function runSessionLocalEditorSourceMutation<TDraft, TResult>(options: {
  binding: SessionLocalEditorMutationBinding<TDraft>;
  didMutate: (result: TResult) => boolean;
  mutation: () => Promise<TResult>;
  reduceLatestPayload: (latestPayload: TDraft, result: TResult) => TDraft;
}): Promise<
  | Readonly<{ kind: 'not-mutated'; result: TResult }>
  | Readonly<{ kind: 'reservation-unavailable' }>
  | Readonly<{ kind: 'source-mutated'; result: TResult }>
> {
  const reservation = options.binding.reserveDraftSourceMutation();
  if (!reservation) {
    return { kind: 'reservation-unavailable' };
  }

  try {
    const result = await options.mutation();
    if (!options.didMutate(result)) {
      options.binding.cancelDraftSourceMutation(reservation);
      return { kind: 'not-mutated', result };
    }
    if (
      !options.binding.commitDraftSourceMutation(
        reservation,
        (latestPayload) => options.reduceLatestPayload(latestPayload, result)
      )
    ) {
      options.binding.cancelDraftSourceMutation(reservation);
      return { kind: 'reservation-unavailable' };
    }
    return { kind: 'source-mutated', result };
  } catch (error) {
    options.binding.cancelDraftSourceMutation(reservation);
    throw error;
  }
}
