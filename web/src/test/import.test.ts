/**
 * Import tests — format acceptance, tier gating, part visibility.
 *
 * Tests against golden fixtures. COMPUTE FIREWALL holds:
 * the UI detects format from extension and sends to core;
 * the core validates and returns the tier result.
 */
import { describe, it, expect, afterEach } from 'vitest'
import {
  IMPORT_REF_STL, IMPORT_REF_PLY, IMPORT_REF_STEP,
  IMPORT_MEAS_XYZ, IMPORT_STL_AT_5UM,
  ACCEPTED_FORMATS, FORMAT_LABELS,
} from './import-fixtures'
import { FIXTURE_PASS, FIXTURE_INVALID } from './fixtures'
import { mockCore, resetMock, mockAlignmeshApi } from './mock-core'

afterEach(() => resetMock())

// ════════════════════════════════════════════════════════════════════════
// Format acceptance
// ════════════════════════════════════════════════════════════════════════

describe('Format acceptance', () => {
  it('reference accepts STL, PLY, OBJ, STEP', () => {
    expect(ACCEPTED_FORMATS.reference).toContain('stl')
    expect(ACCEPTED_FORMATS.reference).toContain('ply')
    expect(ACCEPTED_FORMATS.reference).toContain('obj')
    expect(ACCEPTED_FORMATS.reference).toContain('step')
  })

  it('measured accepts STL, PLY, OBJ, E57, XYZ', () => {
    expect(ACCEPTED_FORMATS.measured).toContain('stl')
    expect(ACCEPTED_FORMATS.measured).toContain('ply')
    expect(ACCEPTED_FORMATS.measured).toContain('obj')
    expect(ACCEPTED_FORMATS.measured).toContain('e57')
    expect(ACCEPTED_FORMATS.measured).toContain('xyz')
  })

  it('each format has a display label', () => {
    for (const fmt of [...ACCEPTED_FORMATS.reference, ...ACCEPTED_FORMATS.measured]) {
      expect(FORMAT_LABELS[fmt]).toBeDefined()
      expect(FORMAT_LABELS[fmt].length).toBeGreaterThan(0)
    }
  })

  it('STL is labeled as float32 precision-limited', () => {
    expect(FORMAT_LABELS.stl).toContain('float32')
  })

  it('STEP is labeled as analytic CAD', () => {
    expect(FORMAT_LABELS.step).toContain('CAD')
  })

  it('E57 is labeled as point cloud', () => {
    expect(FORMAT_LABELS.e57).toContain('point cloud')
  })
})

// ════════════════════════════════════════════════════════════════════════
// Import status
// ════════════════════════════════════════════════════════════════════════

describe('Import status', () => {
  it('STL reference has correct metadata', () => {
    expect(IMPORT_REF_STL.format).toBe('stl')
    expect(IMPORT_REF_STL.precision).toBe('float32')
    expect(IMPORT_REF_STL.triangleCount).toBeGreaterThan(0)
    expect(IMPORT_REF_STL.valid).toBe(true)
  })

  it('PLY double reference has float64 precision', () => {
    expect(IMPORT_REF_PLY.precision).toBe('float64')
  })

  it('STEP reference has float64 and zero triangles (analytic)', () => {
    expect(IMPORT_REF_STEP.precision).toBe('float64')
    expect(IMPORT_REF_STEP.triangleCount).toBe(0)
  })

  it('XYZ point cloud has zero triangles', () => {
    expect(IMPORT_MEAS_XYZ.triangleCount).toBe(0)
    expect(IMPORT_MEAS_XYZ.pointCount).toBeGreaterThan(0)
  })
})

// ════════════════════════════════════════════════════════════════════════
// Tier gate: STL at tight tolerance → INVALID
// ════════════════════════════════════════════════════════════════════════

describe('Tier gate', () => {
  it('STL at 5µm tolerance is INVALID', () => {
    expect(IMPORT_STL_AT_5UM.valid).toBe(false)
    expect(IMPORT_STL_AT_5UM.invalidReason).toContain('not allowed')
    expect(IMPORT_STL_AT_5UM.invalidReason).toContain('PRECISE')
  })

  it('STL at coarse tolerance is valid', () => {
    expect(IMPORT_REF_STL.valid).toBe(true)
    expect(IMPORT_REF_STL.tierResult).toContain('COARSE')
  })

  it('STEP at precise tolerance is valid', () => {
    expect(IMPORT_REF_STEP.valid).toBe(true)
    expect(IMPORT_REF_STEP.tierResult).toContain('PRECISE')
  })
})

// ════════════════════════════════════════════════════════════════════════
// Mock core returns correct response for imported parts
// ════════════════════════════════════════════════════════════════════════

describe('Import → core flow', () => {
  it('mock core returns fixture verbatim for inspect call', async () => {
    mockCore(FIXTURE_PASS)
    const result = await mockAlignmeshApi.inspect(
      IMPORT_REF_STL.path, IMPORT_MEAS_XYZ.path, 0.1
    )
    expect(result.valid).toBe(true)
    expect(result.verdict).toBe('PASS')
    expect(result.stats.n_points).toBeGreaterThan(0)
  })

  it('INVALID fixture blocks submission visually', async () => {
    mockCore(FIXTURE_INVALID)
    const result = await mockAlignmeshApi.inspect(
      IMPORT_STL_AT_5UM.path, IMPORT_MEAS_XYZ.path, 0.005
    )
    expect(result.valid).toBe(false)
    expect(result.verdict).toBe('INVALID')
    expect(result.errors.length).toBeGreaterThan(0)
  })
})

// ════════════════════════════════════════════════════════════════════════
// Both parts distinguishable
// ════════════════════════════════════════════════════════════════════════

describe('Both parts visible', () => {
  it('reference and measured are distinct objects', () => {
    // After import, the UI shows two labeled objects.
    // Assert they have different roles and paths.
    const ref = IMPORT_REF_STL
    const meas = IMPORT_MEAS_XYZ
    expect(ref.path).not.toBe(meas.path)
    expect(ref.format).not.toBe(meas.format)
  })

  it('point cloud renders differently from mesh', () => {
    // XYZ has no triangles (renders as points).
    // STL has triangles (renders as surface).
    expect(IMPORT_MEAS_XYZ.triangleCount).toBe(0)
    expect(IMPORT_REF_STL.triangleCount).toBeGreaterThan(0)
  })
})
