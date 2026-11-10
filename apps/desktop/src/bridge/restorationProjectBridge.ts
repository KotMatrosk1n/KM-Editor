/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { kmCommandNames } from './contracts';
import * as contracts from './restorationContracts';
import { sendProjectBridgeRequest, type ProjectBridgeProgressTransport, type ProjectBridgeTransport } from './projectBridgeRequest';

export function createRestorationProjectBridge(transport: ProjectBridgeTransport, progressTransport?: ProjectBridgeProgressTransport) {
  return {
    inspectRestorationDonor: (request: z.infer<typeof contracts.inspectRestorationDonorRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.inspectRestorationDonor, contracts.inspectRestorationDonorRequestSchema.parse(request), contracts.restorationDonorResponseSchema),
    loadRestoration: (request: z.infer<typeof contracts.loadRestorationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.loadRestoration, contracts.loadRestorationRequestSchema.parse(request), contracts.loadRestorationResponseSchema),
    prepareRestoration: (request: z.infer<typeof contracts.prepareRestorationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.prepareRestoration, contracts.prepareRestorationRequestSchema.parse(request), contracts.restorationPreparationResponseSchema),
    getRestorationPreparation: (request: z.infer<typeof contracts.restorationPreparationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.getRestorationPreparation, contracts.restorationPreparationRequestSchema.parse(request), contracts.restorationPreparationResponseSchema),
    cancelRestorationPreparation: (request: z.infer<typeof contracts.restorationPreparationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.cancelRestorationPreparation, contracts.restorationPreparationRequestSchema.parse(request), contracts.restorationPreparationResponseSchema),
    stageRestoration: (request: z.infer<typeof contracts.stageRestorationRequestSchema>,
      onProgress?: (progress: contracts.RestorationStageProgress) => void) => {
      const payload = contracts.stageRestorationRequestSchema.parse(request);
      if (!onProgress || !progressTransport) {
        return sendProjectBridgeRequest(transport, kmCommandNames.stageRestoration, payload, contracts.stageRestorationResponseSchema);
      }
      return sendProjectBridgeRequest(
        requestJson => progressTransport(requestJson, message => {
          const progress = contracts.restorationStageProgressSchema.safeParse(message);
          if (progress.success) onProgress(progress.data);
        }),
        kmCommandNames.stageRestoration, { ...payload, reportProgress: true }, contracts.stageRestorationResponseSchema
      );
    }
  };
}
export type RestorationProjectBridgeApi = ReturnType<typeof createRestorationProjectBridge>;
