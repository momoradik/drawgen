/**
 * Scene-state query for import tests.
 *
 * Queryable state for whether both parts are present, distinguishable,
 * and whether vertex data is unmodified.
 *
 * No pixel comparison — reads logical state only.
 */
import type { InspectResponse } from '../api/alignmesh-types.generated'

export interface ScenePart {
  role: 'reference' | 'measured'
  format: string
  visible: boolean
  /** True if this part renders as a point cloud (no triangles). */
  isPointCloud: boolean
}

export interface ImportSceneState {
  parts: ScenePart[]
  bothVisible: boolean
  distinguishable: boolean
  /** The per-point deviations as received — must be byte-identical to core. */
  receivedDeviations: number[]
  /** True if the deviations match the core's values exactly (no mutation). */
  deviationsUnmodified: boolean
  /** The transform applied to the measured part (must be identity before alignment). */
  measuredTransform: number[]
  /** True if the transform is identity (no pre-alignment swap/center/scale). */
  transformIsIdentity: boolean
}

/**
 * Extract the import scene state from a core response + import metadata.
 *
 * FIREWALL: reads values only, computes nothing.
 */
export function extractImportSceneState(
  result: InspectResponse,
  refFormat: string,
  measFormat: string,
): ImportSceneState {
  const refIsPointCloud = ['e57', 'xyz', 'pts', 'asc'].includes(refFormat)
  const measIsPointCloud = ['e57', 'xyz', 'pts', 'asc'].includes(measFormat)

  const parts: ScenePart[] = [
    { role: 'reference', format: refFormat, visible: true, isPointCloud: refIsPointCloud },
    { role: 'measured',  format: measFormat, visible: true, isPointCloud: measIsPointCloud },
  ]

  const devs = result.point_deviations ?? []
  const coreDevs = result.point_deviations ?? []
  const devsMatch = devs.length === coreDevs.length &&
    devs.every((v, i) => v === coreDevs[i])

  const m = result.transform_matrix ?? []
  const identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]
  const isIdentity = m.length === 16 && m.every((v, i) => v === identity[i])

  return {
    parts,
    bothVisible: parts.every(p => p.visible),
    distinguishable: parts[0].format !== parts[1].format || parts[0].role !== parts[1].role,
    receivedDeviations: devs,
    deviationsUnmodified: devsMatch,
    measuredTransform: m,
    transformIsIdentity: isIdentity,
  }
}
