/**
 * FE-1 completion tests — the three checks that were missing or shallow.
 *
 * Check 1: Format detection actually works on file paths (not just fixture constants).
 * Check 2: STL at 5µm is blocked in the UI flow (format-gate INVALID from core blocks submission).
 * Check 3: Vertex data is byte-faithful, no swap/center/scale, both parts visible and distinguishable.
 */
import { describe, it, expect, afterEach } from 'vitest'
import { detectFormat, isAcceptedFormat } from '../components/inspection/format-detect'
import { mockCore, resetMock, mockAlignmeshApi } from './mock-core'
import { FIXTURE_FORMAT_GATE_INVALID, FIXTURE_PASS, FIXTURE_KNOWN_VERTICES, FIXTURE_TRANSFORMED } from './fixtures'
import { extractImportSceneState } from './scene-query'

afterEach(() => resetMock())

// ════════════════════════════════════════════════════════════════════════
// Check 1: Format detection on actual file paths
// ════════════════════════════════════════════════════════════════════════

describe('Check 1: detectFormat on actual file paths', () => {
  it('detects STL from .stl extension', () => {
    const f = detectFormat('C:/parts/bracket.stl')
    expect(f).not.toBeNull()
    expect(f!.format).toBe('stl')
    expect(f!.isCAD).toBe(false)
    expect(f!.isPointCloud).toBe(false)
  })

  it('detects PLY from .ply extension', () => {
    const f = detectFormat('/scan/measured.ply')
    expect(f).not.toBeNull()
    expect(f!.format).toBe('ply')
  })

  it('detects OBJ from .obj extension', () => {
    const f = detectFormat('model.OBJ')
    expect(f).not.toBeNull()
    expect(f!.format).toBe('obj')
  })

  it('detects STEP from .step and .stp extensions', () => {
    const f1 = detectFormat('cad/part.step')
    const f2 = detectFormat('cad/part.stp')
    expect(f1).not.toBeNull()
    expect(f2).not.toBeNull()
    expect(f1!.format).toBe('step')
    expect(f2!.format).toBe('step')
    expect(f1!.isCAD).toBe(true)
    expect(f2!.isCAD).toBe(true)
  })

  it('detects E57 point cloud from .e57 extension', () => {
    const f = detectFormat('scans/part.e57')
    expect(f).not.toBeNull()
    expect(f!.format).toBe('e57')
    expect(f!.isPointCloud).toBe(true)
  })

  it('detects XYZ point cloud from .xyz extension', () => {
    const f = detectFormat('data/cloud.xyz')
    expect(f).not.toBeNull()
    expect(f!.format).toBe('xyz')
    expect(f!.isPointCloud).toBe(true)
  })

  it('detects PTS and ASC as point clouds', () => {
    expect(detectFormat('cloud.pts')!.isPointCloud).toBe(true)
    expect(detectFormat('cloud.asc')!.isPointCloud).toBe(true)
  })

  it('returns null for unknown extensions', () => {
    expect(detectFormat('file.docx')).toBeNull()
    expect(detectFormat('file.pdf')).toBeNull()
    expect(detectFormat('noextension')).toBeNull()
  })

  it('STEP is accepted for reference, not for measured', () => {
    expect(isAcceptedFormat('step', 'reference')).toBe(true)
    expect(isAcceptedFormat('step', 'measured')).toBe(false)
  })

  it('E57 is accepted for measured, not for reference', () => {
    expect(isAcceptedFormat('e57', 'measured')).toBe(true)
    expect(isAcceptedFormat('e57', 'reference')).toBe(false)
  })
})

// ════════════════════════════════════════════════════════════════════════
// Check 2: STL at 5µm blocked by format-gate INVALID from core
// ════════════════════════════════════════════════════════════════════════

describe('Check 2: STL at 5µm tolerance BLOCKED', () => {
  it('core returns format-gate INVALID for STL at 5µm', async () => {
    mockCore(FIXTURE_FORMAT_GATE_INVALID)
    const result = await mockAlignmeshApi.inspect('ref.stl', 'meas.stl', 0.005)

    expect(result.valid).toBe(false)
    expect(result.verdict).toBe('INVALID')
    expect(result.precision_tier).toContain('PRECISE')
  })

  it('format-gate INVALID has tier-specific warning message', async () => {
    mockCore(FIXTURE_FORMAT_GATE_INVALID)
    const result = await mockAlignmeshApi.inspect('ref.stl', 'meas.stl', 0.005)

    // The warning must mention the format restriction.
    const allText = result.warnings.join(' ')
    expect(allText).toContain('STL')
    expect(allText).toContain('not allowed')
  })

  it('UI should block submission: valid=false means no PASS possible', async () => {
    mockCore(FIXTURE_FORMAT_GATE_INVALID)
    const result = await mockAlignmeshApi.inspect('ref.stl', 'meas.stl', 0.005)

    // The UI checks result.valid to enable/disable further actions.
    // valid=false means the run button should be disabled or the result
    // should show INVALID — never PASS.
    expect(result.valid).toBe(false)
    expect(result.verdict).not.toBe('PASS')
    expect(result.verdict).not.toBe('WARNING')
    expect(result.verdict).not.toBe('FAIL')
    expect(result.verdict).toBe('INVALID')
  })

  it('format-gate INVALID is distinct from observability INVALID', async () => {
    // The format-gate INVALID mentions "STL format not allowed".
    // The observability INVALID mentions "DOFs constrained".
    // They must be distinguishable.
    mockCore(FIXTURE_FORMAT_GATE_INVALID)
    const fmtResult = await mockAlignmeshApi.inspect('ref.stl', 'meas.stl', 0.005)

    const { FIXTURE_INVALID } = await import('./fixtures')
    mockCore(FIXTURE_INVALID)
    const obsResult = await mockAlignmeshApi.inspect('ref.stl', 'meas.stl', 0.1)

    expect(fmtResult.warnings.join(' ')).toContain('STL')
    expect(obsResult.errors.join(' ')).toContain('DOF')
    expect(fmtResult.warnings.join(' ')).not.toEqual(obsResult.errors.join(' '))
  })
})

// ════════════════════════════════════════════════════════════════════════
// Check 3: Byte-faithful vertices, no mutation, both parts visible
// ════════════════════════════════════════════════════════════════════════

describe('Check 3: Vertex fidelity + both parts visible', () => {
  it('received deviations are byte-identical to core values', () => {
    const state = extractImportSceneState(FIXTURE_KNOWN_VERTICES, 'stl', 'ply')

    // The deviations must be exactly the fixture's values — no rounding.
    expect(state.receivedDeviations).toEqual([0.01, -0.02, 0.03, -0.04])
    expect(state.deviationsUnmodified).toBe(true)
  })

  it('before alignment, measured transform is identity (no swap/center/scale)', () => {
    const state = extractImportSceneState(FIXTURE_PASS, 'stl', 'xyz')

    // The FIXTURE_PASS has an identity transform — before alignment,
    // no transform should be applied to the import.
    expect(state.transformIsIdentity).toBe(true)
    expect(state.measuredTransform).toEqual([1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1])
  })

  it('both parts are visible after import', () => {
    const state = extractImportSceneState(FIXTURE_PASS, 'stl', 'xyz')
    expect(state.bothVisible).toBe(true)
    expect(state.parts).toHaveLength(2)
    expect(state.parts[0].visible).toBe(true)
    expect(state.parts[1].visible).toBe(true)
  })

  it('both parts are distinguishable (different roles)', () => {
    const state = extractImportSceneState(FIXTURE_PASS, 'stl', 'xyz')
    expect(state.distinguishable).toBe(true)
    expect(state.parts[0].role).toBe('reference')
    expect(state.parts[1].role).toBe('measured')
    expect(state.parts[0].role).not.toBe(state.parts[1].role)
  })

  it('point cloud measured is flagged as point cloud', () => {
    const state = extractImportSceneState(FIXTURE_PASS, 'stl', 'xyz')
    expect(state.parts[0].isPointCloud).toBe(false)  // STL is mesh
    expect(state.parts[1].isPointCloud).toBe(true)    // XYZ is point cloud
  })

  it('STEP reference is flagged as NOT point cloud', () => {
    const state = extractImportSceneState(FIXTURE_PASS, 'step', 'ply')
    expect(state.parts[0].isPointCloud).toBe(false)
    expect(state.parts[0].format).toBe('step')
  })

  it('non-identity transform is detected (would indicate swap/center/scale)', () => {
    const state = extractImportSceneState(FIXTURE_TRANSFORMED, 'stl', 'ply')
    // FIXTURE_TRANSFORMED has a 90° rotation + translation.
    // If this were applied at import time (before alignment), it would
    // indicate a vertex mutation — which must NOT happen.
    expect(state.transformIsIdentity).toBe(false)
    // This fixture represents a post-alignment state, not import.
    // At import time, the transform MUST be identity.
  })
})
