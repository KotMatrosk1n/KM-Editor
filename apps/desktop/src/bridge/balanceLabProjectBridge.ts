/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import {
  balanceLabQueryRequestSchema,
  balanceLabQueryResponseSchema,
  type BalanceLabQueryRequest,
  type BalanceLabQueryResponse
} from './balanceLabContracts';
import {
  sendProjectBridgeRequest,
  type ProjectBridgeTransport
} from './projectBridgeRequest';

export type BalanceLabProjectBridgeApi = {
  queryBalanceLab: (request: BalanceLabQueryRequest) => Promise<BalanceLabQueryResponse>;
};

export function createBalanceLabProjectBridgeApi(
  transport: ProjectBridgeTransport
): BalanceLabProjectBridgeApi {
  return {
    queryBalanceLab: (request) => sendProjectBridgeRequest(
      transport,
      kmCommandNames.queryBalanceLab,
      balanceLabQueryRequestSchema.parse(request),
      balanceLabQueryResponseSchema
    )
  };
}
