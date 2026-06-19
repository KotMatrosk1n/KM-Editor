/* SPDX-License-Identifier: GPL-3.0-only */

import { type ApiError } from './contracts';

export class ProjectBridgeError extends Error {
  constructor(public readonly apiError: ApiError) {
    super(apiError.message);
    this.name = 'ProjectBridgeError';
  }
}
