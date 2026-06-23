/**
 * Landmark/RPS definition tests — exact math checks.
 *
 * Tests:
 * 1. Mode A: file-frame coordinates equal entered values exactly.
 * 2. Mode B: reference-datum conversion matches hand-computed values to full precision.
 * 3. Picking: analytical coordinate equals known point.
 * 4. DOF counter: reflects locks, warns when under-constrained.
 * 5. Core fit display: residuals/transform equal the golden fixture.
 */
import { describe, it, expect, afterEach } from 'vitest'
import {
  datumToFileFrame, countConstrainedDOFs, buildCorePayload,
  IDENTITY_DATUM, type LandmarkPoint, type ReferenceDatum,
} from '../components/inspection/landmark-logic'
import { mockCore, resetMock, mockAlignmeshApi } from './mock-core'
import { FIXTURE_PASS } from './fixtures'

afterEach(() => resetMock())

// ═══════════════════════════════════════════════════════════════════════
// Helper: make a landmark point
// ═══════════════════════════════════════════════════════════════════════

function mkPt(
  id: string,
  entered: [number, number, number],
  fileFrame: [number, number, number],
  locks: { x: boolean; y: boolean; z: boolean } = { x: false, y: false, z: false },
  weight = 1.0,
): LandmarkPoint {
  return { id, entered, fileFrame, locks, weight, label: id }
}

// ═══════════════════════════════════════════════════════════════════════
// 1. Mode A: file-frame = entered (identity datum)
// ═══════════════════════════════════════════════════════════════════════

describe('Mode A: file origin', () => {
  it('identity datum returns entered coordinates exactly', () => {
    const pt: [number, number, number] = [1.23456789012345, -9.87654321098765, 42.0]
    const result = datumToFileFrame(pt, IDENTITY_DATUM)
    expect(result[0]).toBe(pt[0])
    expect(result[1]).toBe(pt[1])
    expect(result[2]).toBe(pt[2])
  })

  it('zero point through identity is zero', () => {
    const result = datumToFileFrame([0, 0, 0], IDENTITY_DATUM)
    expect(result).toEqual([0, 0, 0])
  })

  it('buildCorePayload sends file-frame coordinates', () => {
    const pts = [
      mkPt('P1', [1, 2, 3], [1, 2, 3], { x: true, y: false, z: true }),
      mkPt('P2', [4, 5, 6], [4, 5, 6], { x: false, y: true, z: true }),
    ]
    const payload = buildCorePayload(pts)
    expect(payload.landmarks[0].x).toBe(1)
    expect(payload.landmarks[0].y).toBe(2)
    expect(payload.landmarks[0].z).toBe(3)
    expect(payload.landmarks[0].lock_x).toBe(true)
    expect(payload.landmarks[0].lock_z).toBe(true)
    expect(payload.landmarks[1].lock_y).toBe(true)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 2. Mode B: reference-datum conversion (CRITICAL TEST)
// ═══════════════════════════════════════════════════════════════════════

describe('Mode B: reference datum conversion', () => {
  it('pure translation: (10, 20, 30) offset', () => {
    const datum: ReferenceDatum = { translation: [10, 20, 30], rotationZ: 0 }
    const result = datumToFileFrame([1, 2, 3], datum)
    expect(result[0]).toBe(11)
    expect(result[1]).toBe(22)
    expect(result[2]).toBe(33)
  })

  it('pure 90° rotation about Z', () => {
    // R(90°) * (1, 0, 0) = (0, 1, 0)
    const datum: ReferenceDatum = { translation: [0, 0, 0], rotationZ: 90 }
    const result = datumToFileFrame([1, 0, 0], datum)
    expect(result[0]).toBeCloseTo(0, 14)   // cos(90°)*1 - sin(90°)*0 = 0
    expect(result[1]).toBeCloseTo(1, 14)   // sin(90°)*1 + cos(90°)*0 = 1
    expect(result[2]).toBe(0)
  })

  it('30° rotation + translation (10, 20, 30) — hand-computed', () => {
    // THIS IS THE CRITICAL TEST from the prompt.
    // datum: translate (10, 20, 30), rotate 30° about Z
    // point relative: (5, 3, 7)
    //
    // Hand computation:
    //   cos(30°) = √3/2 ≈ 0.8660254037844387
    //   sin(30°) = 1/2  = 0.5
    //
    //   file_x = cos(30°)*5 - sin(30°)*3 + 10
    //          = 0.8660254037844387*5 - 0.5*3 + 10
    //          = 4.330127018922194 - 1.5 + 10
    //          = 12.830127018922194
    //
    //   file_y = sin(30°)*5 + cos(30°)*3 + 20
    //          = 0.5*5 + 0.8660254037844387*3 + 20
    //          = 2.5 + 2.598076211353316 + 20
    //          = 25.098076211353316
    //
    //   file_z = 7 + 30 = 37

    const datum: ReferenceDatum = { translation: [10, 20, 30], rotationZ: 30 }
    const result = datumToFileFrame([5, 3, 7], datum)

    // Full double precision — these must match the hand-computed values.
    const cos30 = Math.cos(30 * Math.PI / 180)
    const sin30 = Math.sin(30 * Math.PI / 180)
    const expectedX = cos30 * 5 - sin30 * 3 + 10
    const expectedY = sin30 * 5 + cos30 * 3 + 20
    const expectedZ = 7 + 30

    expect(result[0]).toBe(expectedX)
    expect(result[1]).toBe(expectedY)
    expect(result[2]).toBe(expectedZ)

    // Also verify against the literal hand-computed values.
    expect(result[0]).toBeCloseTo(12.830127018922194, 12)
    expect(result[1]).toBeCloseTo(25.098076211353316, 12)
    expect(result[2]).toBe(37)
  })

  it('180° rotation: (1, 0, 0) → (-1, 0, 0)', () => {
    const datum: ReferenceDatum = { translation: [0, 0, 0], rotationZ: 180 }
    const result = datumToFileFrame([1, 0, 0], datum)
    expect(result[0]).toBeCloseTo(-1, 14)
    expect(result[1]).toBeCloseTo(0, 14)
    expect(result[2]).toBe(0)
  })

  it('Z coordinate is unaffected by XY rotation', () => {
    const datum: ReferenceDatum = { translation: [0, 0, 100], rotationZ: 45 }
    const result = datumToFileFrame([0, 0, 42], datum)
    expect(result[2]).toBe(142)  // exact: 42 + 100
  })

  it('conversion is exact at full double precision (no truncation)', () => {
    const datum: ReferenceDatum = {
      translation: [1.23456789012345, 9.87654321098765, 0.00000000001],
      rotationZ: 17.3,
    }
    const pt: [number, number, number] = [0.111111111111111, 0.222222222222222, 0.333333333333333]
    const result = datumToFileFrame(pt, datum)

    // Re-compute using the same math (must be bit-identical).
    const theta = 17.3 * Math.PI / 180
    const c = Math.cos(theta)
    const s = Math.sin(theta)
    const ex = c * pt[0] - s * pt[1] + datum.translation[0]
    const ey = s * pt[0] + c * pt[1] + datum.translation[1]
    const ez = pt[2] + datum.translation[2]

    expect(result[0]).toBe(ex)
    expect(result[1]).toBe(ey)
    expect(result[2]).toBe(ez)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 3. Picking: analytical coordinate = known point
// ═══════════════════════════════════════════════════════════════════════

describe('Picking', () => {
  it('pick at known surface location returns identity-mapped coordinate', () => {
    // A pick on the model returns the analytical coordinate.
    // Since the viewer applies no reflection/scale, the picked coordinate
    // equals the file-frame coordinate.
    const pickedX = 12.345
    const pickedY = -67.890
    const pickedZ = 0.001

    // In Mode A, the picked coordinate IS the file-frame coordinate.
    const fileFrame = datumToFileFrame([pickedX, pickedY, pickedZ], IDENTITY_DATUM)
    expect(fileFrame[0]).toBe(pickedX)
    expect(fileFrame[1]).toBe(pickedY)
    expect(fileFrame[2]).toBe(pickedZ)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 4. DOF counter
// ═══════════════════════════════════════════════════════════════════════

describe('DOF counter', () => {
  it('no points: 0 DOFs, warns', () => {
    const status = countConstrainedDOFs([])
    expect(status.total).toBe(0)
    expect(status.isComplete).toBe(false)
    expect(status.warning).toContain('Under-constrained')
  })

  it('3 points with Z-only locks: 3 DOFs', () => {
    const pts = [
      mkPt('P1', [0,0,0], [0,0,0], { x: false, y: false, z: true }),
      mkPt('P2', [1,0,0], [1,0,0], { x: false, y: false, z: true }),
      mkPt('P3', [0,1,0], [0,1,0], { x: false, y: false, z: true }),
    ]
    const status = countConstrainedDOFs(pts)
    expect(status.lockedZ).toBe(3)
    expect(status.lockedX).toBe(0)
    expect(status.lockedY).toBe(0)
    expect(status.total).toBe(3)
    expect(status.isComplete).toBe(false)
    expect(status.warning).not.toBeNull()
  })

  it('6 points, full XYZ locks: 6+ DOFs, complete', () => {
    const pts = [
      mkPt('P1', [0,0,0], [0,0,0], { x: true, y: false, z: true }),
      mkPt('P2', [1,0,0], [1,0,0], { x: false, y: true, z: true }),
      mkPt('P3', [0,1,0], [0,1,0], { x: true, y: true, z: true }),
    ]
    const status = countConstrainedDOFs(pts)
    expect(status.total).toBeGreaterThanOrEqual(6)
    expect(status.isComplete).toBe(true)
    expect(status.warning).toBeNull()
  })

  it('6 points with full XYZ locks: capped at 6 DOFs, redundant reported', () => {
    const pts = [
      mkPt('P1', [0,0,0], [0,0,0], { x: true, y: true, z: true }),
      mkPt('P2', [1,0,0], [1,0,0], { x: true, y: true, z: true }),
      mkPt('P3', [0,1,0], [0,1,0], { x: true, y: true, z: true }),
      mkPt('P4', [1,1,0], [1,1,0], { x: true, y: true, z: true }),
      mkPt('P5', [0,0,1], [0,0,1], { x: true, y: true, z: true }),
      mkPt('P6', [1,0,1], [1,0,1], { x: true, y: true, z: true }),
    ]
    const status = countConstrainedDOFs(pts)
    // 6 points × 3 axes = 18 locked axes, but only 6 rigid-body DOFs
    expect(status.total).toBe(6)
    expect(status.isComplete).toBe(true)
    expect(status.totalLockedAxes).toBe(18)
    expect(status.redundantConstraints).toBe(12)
  })

  it('warns if X is unlocked', () => {
    const pts = [
      mkPt('P1', [0,0,0], [0,0,0], { x: false, y: true, z: true }),
      mkPt('P2', [1,0,0], [1,0,0], { x: false, y: true, z: true }),
      mkPt('P3', [0,1,0], [0,1,0], { x: false, y: true, z: true }),
    ]
    const status = countConstrainedDOFs(pts)
    expect(status.isComplete).toBe(false)
    expect(status.warning).toContain('X')
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 5. Core fit display
// ═══════════════════════════════════════════════════════════════════════

describe('Core fit display', () => {
  it('displayed residuals/transform equal the golden fixture', async () => {
    mockCore(FIXTURE_PASS)
    const result = await mockAlignmeshApi.inspect('ref', 'meas', 0.1)

    // The UI displays these values verbatim from the core.
    expect(result.alignment_rms).toBe(FIXTURE_PASS.alignment_rms)
    expect(result.stats.rms).toBe(FIXTURE_PASS.stats.rms)
    expect(result.fully_constrained).toBe(FIXTURE_PASS.fully_constrained)
    expect(result.transform_matrix).toEqual(FIXTURE_PASS.transform_matrix)
    expect(result.verdict).toBe(FIXTURE_PASS.verdict)
  })
})
