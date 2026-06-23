/**
 * Import-specific golden fixtures.
 *
 * Known synthetic files and their expected import results.
 */
/** Supported import formats. */
export type MeshFormat = 'stl' | 'ply' | 'obj' | 'e57' | 'xyz' | 'step'

export interface ImportStatus {
  path: string
  format: MeshFormat
  pointCount: number
  triangleCount: number
  precision: 'float32' | 'float64'
  units: string
  tierResult: string
  valid: boolean
  invalidReason?: string
}

/** Reference part: STL (float32, acceptable for coarse tolerance). */
export const IMPORT_REF_STL: ImportStatus = {
  path: 'C:/test/reference.stl',
  format: 'stl',
  pointCount: 1200,
  triangleCount: 2400,
  precision: 'float32',
  units: 'mm',
  tierResult: 'COARSE (>=100um)',
  valid: true,
}

/** Reference part: PLY double (acceptable for fine tolerance). */
export const IMPORT_REF_PLY: ImportStatus = {
  path: 'C:/test/reference.ply',
  format: 'ply',
  pointCount: 5000,
  triangleCount: 10000,
  precision: 'float64',
  units: 'mm',
  tierResult: 'FINE (10-25um)',
  valid: true,
}

/** Reference part: STEP CAD (required for precise tolerance). */
export const IMPORT_REF_STEP: ImportStatus = {
  path: 'C:/test/reference.step',
  format: 'step',
  pointCount: 0,
  triangleCount: 0,
  precision: 'float64',
  units: 'mm',
  tierResult: 'PRECISE (1-10um)',
  valid: true,
}

/** Measured part: point cloud XYZ. */
export const IMPORT_MEAS_XYZ: ImportStatus = {
  path: 'C:/test/measured.xyz',
  format: 'xyz',
  pointCount: 8000,
  triangleCount: 0,
  precision: 'float64',
  units: 'mm',
  tierResult: 'COARSE (>=100um)',
  valid: true,
}

/** INVALID: STL at 5µm tolerance (requires CAD reference). */
export const IMPORT_STL_AT_5UM: ImportStatus = {
  path: 'C:/test/reference.stl',
  format: 'stl',
  pointCount: 1200,
  triangleCount: 2400,
  precision: 'float32',
  units: 'mm',
  tierResult: 'PRECISE (1-10um)',
  valid: false,
  invalidReason: 'STL format not allowed at PRECISE tier — use CAD reference',
}

/** Accepted formats by role. */
export const ACCEPTED_FORMATS = {
  reference: ['stl', 'ply', 'obj', 'step'] as MeshFormat[],
  measured:  ['stl', 'ply', 'obj', 'e57', 'xyz'] as MeshFormat[],
}

/** Format display labels. */
export const FORMAT_LABELS: Record<MeshFormat, string> = {
  stl:  'STL (tessellated, float32)',
  ply:  'PLY (mesh/cloud, float32 or float64)',
  obj:  'OBJ (mesh, float64)',
  e57:  'E57 (point cloud)',
  xyz:  'XYZ/ASCII (point cloud)',
  step: 'STEP/CAD (analytic NURBS/BREP)',
}
