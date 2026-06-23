/**
 * alignmesh core API client.
 *
 * COMPUTE FIREWALL: this client only FETCHES pre-computed results from the
 * core. It never computes measurements, transforms, deviations, or decisions.
 * On error it returns the error — never fabricates.
 *
 * Types are GENERATED from api/contract.json — do not hand-write interfaces.
 */
import axios from 'axios'
import type {
  HealthResponse,
  VersionResponse,
  InspectResponse,
  Verdict,
  DeviationStats,
  Fingerprint,
} from './alignmesh-types.generated'

// Re-export generated types so consumers import from one place.
export type { Verdict, DeviationStats, Fingerprint, InspectResponse, VersionResponse }

// Alias for backward compat with existing UI components.
export type InspectionResult = InspectResponse
export type CoreVersion = VersionResponse

export interface UploadResponse {
  path: string
  size: number
}

export interface LandmarkPairData {
  ref_x: number; ref_y: number; ref_z: number
  meas_x: number; meas_y: number; meas_z: number
  weight: number
}

export interface RPSLockData {
  axis: string   // 'x' | 'y' | 'z' | 'normal'
  weight: number
}

export interface RPSPointData {
  x: number; y: number; z: number
  locks: RPSLockData[]
}

const core = axios.create({
  baseURL: '',  // same origin — core serves both API and UI
  timeout: 0,  // no timeout — exhaustive rotation search can take a long time
})

export const alignmeshApi = {
  health: () =>
    core.get<HealthResponse>('/health').then(r => r.data),

  version: () =>
    core.get<VersionResponse>('/version').then(r => r.data),

  upload: (file: File) =>
    core.post<UploadResponse>(
      '/upload?filename=' + encodeURIComponent(file.name),
      file,
      { headers: { 'Content-Type': 'application/octet-stream' } },
    ).then(r => r.data),

  /** Fetch a tessellated binary STL for 3D preview (needed for STEP files). */
  previewMesh: (path: string, signal?: AbortSignal) =>
    core.post<ArrayBuffer>('/preview-mesh', { path }, {
      responseType: 'arraybuffer',
      signal,
    }).then(r => r.data),

  inspect: (opts: {
    reference: string; measured: string; tolerance: number
    alignment_mode?: string; landmarks?: LandmarkPairData[]
    rps_points?: RPSPointData[]; angle_step?: number
  }) =>
    core.post<InspectResponse>('/inspect', {
      reference: opts.reference,
      measured: opts.measured,
      tolerance: opts.tolerance,
      alignment_mode: opts.alignment_mode ?? 'coarse-to-fine',
      angle_step: opts.angle_step ?? 30,
      ...(opts.landmarks && opts.landmarks.length > 0 ? { landmarks: opts.landmarks } : {}),
      ...(opts.rps_points && opts.rps_points.length > 0 ? { rps_points: opts.rps_points } : {}),
    }).then(r => r.data),
}
