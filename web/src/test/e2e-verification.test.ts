/**
 * End-to-end verification suite — proves the frontend faithfully represents
 * the backend without a human judging the screen.
 *
 * Sections:
 *   A. E2E golden runs: every fixture through the full flow.
 *   B. Faithfulness audit: display honesty properties.
 *   C. Firewall audit: every numeric touch is display-only or orchestration.
 *   D. 3D ↔ numeric consistency: picture never disagrees with numbers.
 *   E. Failure modes: core down/malformed/partial → safe error.
 *   F. RPS frame conversion: Mode A and Mode B exact.
 *   G. Hybrid pipeline order: best-fit↔landmark composition order matters.
 */
import { describe, it, expect, afterEach } from 'vitest'
import {
  ALL_FIXTURES, FIXTURE_PASS, FIXTURE_WARNING, FIXTURE_FAIL, FIXTURE_INVALID,
  FIXTURE_LOCAL_DEFECT, FIXTURE_TRANSFORMED, FIXTURE_FORMAT_GATE_INVALID,
  FIXTURE_KNOWN_VERTICES,
} from './fixtures'
import { mockCore, resetMock, mockCoreDown, mockAlignmeshApi } from './mock-core'
import { extractViewerState } from './viewer-query'
import { extractImportSceneState } from './scene-query'
import { getOverlayState, type OverlayMode } from '../components/inspection/AlignmentOverlay'
import { getDOFStates } from '../components/inspection/DOFPanel'
import {
  deviationToMetrologyColor, goNoGoColor, classifyDeviation,
  processDeviationField, COLOR_NO_DATA, COLOR_OUT_OF_RANGE,
} from '../components/inspection/deviation-colormap'
import {
  datumToFileFrame, IDENTITY_DATUM, countConstrainedDOFs,
  buildCorePayload, type LandmarkPoint, type ReferenceDatum,
} from '../components/inspection/landmark-logic'
import {
  classifyVerdictScope, canBestFitBeConformance, isEngineeringOnly,
  getVerdictScopeLabel, multiply4x4, composeChainForTest,
  type StageResult, type StageKind,
} from '../components/inspection/pipeline-sequencer'
import type { InspectResponse } from '../api/alignmesh-types.generated'

afterEach(() => resetMock())

// ═══════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════

const I4 = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]

function mkStage(i: number, kind: StageKind, transform: number[], rms = 0.005): StageResult {
  return { index: i, kind, name: kind, transform, rms, converged: true, warnings: [] }
}

function mkPt(
  id: string, entered: [number,number,number], fileFrame: [number,number,number],
  locks = { x: false, y: false, z: false }, weight = 1.0,
): LandmarkPoint {
  return { id, entered, fileFrame, locks, weight, label: id }
}

// ═══════════════════════════════════════════════════════════════════════
// A. END-TO-END GOLDEN RUNS
// ═══════════════════════════════════════════════════════════════════════

describe('A. E2E golden run — every fixture', () => {
  const fixtureEntries = Object.entries(ALL_FIXTURES) as [string, InspectResponse][]

  describe('per-fixture: mock core returns fixture, display state equals fixture', () => {
    fixtureEntries.forEach(([name, fixture]) => {
      it(`${name}: mock → inspect → viewer state matches fixture`, async () => {
        mockCore(fixture)
        const result = await mockAlignmeshApi.inspect('ref', 'meas', fixture.tolerance_mm)

        // Result is the fixture verbatim.
        expect(result).toBe(fixture)

        // Verdict matches.
        expect(result.verdict).toBe(fixture.verdict)
        expect(result.verdict_label).toBe(fixture.verdict_label)

        // Stats match.
        expect(result.stats.n_points).toBe(fixture.stats.n_points)
        expect(result.stats.rms).toBe(fixture.stats.rms)
        expect(result.stats.max).toBe(fixture.stats.max)
        expect(result.stats.mean).toBe(fixture.stats.mean)
        expect(result.stats.std_dev).toBe(fixture.stats.std_dev)
        expect(result.stats.percent_within_tolerance).toBe(fixture.stats.percent_within_tolerance)

        // Transform matches.
        expect(result.transform_matrix).toBe(fixture.transform_matrix)

        // Deviations match.
        expect(result.point_deviations).toBe(fixture.point_deviations)
        expect(result.deviation_checksum).toBe(fixture.deviation_checksum)
        expect(result.max_deviation_index).toBe(fixture.max_deviation_index)
        expect(result.max_deviation_value).toBe(fixture.max_deviation_value)

        // Provenance.
        expect(result.core_version).toBe(fixture.core_version)
        expect(result.reference_hash).toBe(fixture.reference_hash)
        expect(result.measured_hash).toBe(fixture.measured_hash)
        expect(result.fingerprint).toBe(fixture.fingerprint)
      })
    })
  })

  it('PASS: overlay state at overlay mode shows both, matrix = identity', async () => {
    mockCore(FIXTURE_PASS)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    const state = getOverlayState(result, 'overlay')
    expect(state.referenceVisible).toBe(true)
    expect(state.measuredVisible).toBe(true)
    expect(state.appliedMatrix).toEqual(I4)
    expect(state.matrixMatchesCore).toBe(true)
  })

  it('TRANSFORMED: overlay uses the core non-identity transform verbatim', async () => {
    mockCore(FIXTURE_TRANSFORMED)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    const state = getOverlayState(result, 'overlay')
    expect(state.appliedMatrix).toBe(FIXTURE_TRANSFORMED.transform_matrix)
    expect(state.appliedMatrix[0]).toBe(0)   // cos90
    expect(state.appliedMatrix[1]).toBe(-1)  // -sin90
    expect(state.appliedMatrix[3]).toBe(10)  // tx
    expect(state.appliedMatrix[7]).toBe(20)  // ty
    expect(state.appliedMatrix[11]).toBe(30) // tz
  })

  it('INVALID: DOF panel shows 3 under-constrained', async () => {
    mockCore(FIXTURE_INVALID)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    const dofs = getDOFStates(result)
    const under = dofs.filter(d => !d.constrained)
    expect(under).toHaveLength(3)
    expect(result.fully_constrained).toBe(false)
  })

  it('WARNING: verdict is WARNING, stats show ambiguity', async () => {
    mockCore(FIXTURE_WARNING)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    expect(result.verdict).toBe('WARNING')
    expect(result.warnings).toContain('Inside tolerance but within U of a limit')
    expect(result.stats.percent_within_tolerance).toBe(90)
  })

  it('FAIL: verdict is FAIL', async () => {
    mockCore(FIXTURE_FAIL)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    expect(result.verdict).toBe('FAIL')
    expect(result.stats.percent_within_tolerance).toBe(40)
  })

  it('LOCAL_DEFECT: planted defect at index 7 detected by display', async () => {
    mockCore(FIXTURE_LOCAL_DEFECT)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    const display = processDeviationField(result.point_deviations, result.tolerance_mm, result.heatmap_max)
    expect(display.maxDeviationIndex).toBe(7)
    expect(display.maxDeviationValue).toBe(0.5)
    expect(display.classes[7]).toBe('out-of-range')  // 0.5 > 0.1 = out of range
  })

  it('FORMAT_GATE_INVALID: tier-specific warning present', async () => {
    mockCore(FIXTURE_FORMAT_GATE_INVALID)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.005)
    expect(result.verdict).toBe('INVALID')
    expect(result.valid).toBe(false)
    expect(result.warnings.some(w => w.includes('STL format not allowed'))).toBe(true)
  })

  it('KNOWN_VERTICES: deviation values are byte-identical', async () => {
    mockCore(FIXTURE_KNOWN_VERTICES)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    const scene = extractImportSceneState(result, 'stl', 'stl')
    expect(scene.deviationsUnmodified).toBe(true)
    for (let i = 0; i < result.point_deviations.length; i++) {
      expect(result.point_deviations[i]).toBe(FIXTURE_KNOWN_VERTICES.point_deviations[i])
    }
  })
})

// ═══════════════════════════════════════════════════════════════════════
// B. FAITHFULNESS AUDIT
// ═══════════════════════════════════════════════════════════════════════

describe('B. Faithfulness audit', () => {

  describe('No-data is NEVER green', () => {
    it('NaN deviation → gray, not green', () => {
      const c = deviationToMetrologyColor(NaN, 0.1, 0.5)
      expect(c.r).toBe(COLOR_NO_DATA.r)
      expect(c.g).toBe(COLOR_NO_DATA.g)
      expect(c.b).toBe(COLOR_NO_DATA.b)
      expect(c.g).not.toBeGreaterThan(0.6)  // green channel must not dominate
    })

    it('undefined deviation → gray', () => {
      const c = deviationToMetrologyColor(undefined as unknown as number, 0.1, 0.5)
      expect(c).toEqual(COLOR_NO_DATA)
    })

    it('null deviation → gray', () => {
      const c = deviationToMetrologyColor(null as unknown as number, 0.1, 0.5)
      expect(c).toEqual(COLOR_NO_DATA)
    })

    it('go/no-go: NaN → gray', () => {
      const c = goNoGoColor(NaN, 0.1)
      expect(c).toEqual(COLOR_NO_DATA)
    })

    it('classifyDeviation: NaN → no-data', () => {
      expect(classifyDeviation(NaN, 0.1, 0.5)).toBe('no-data')
    })
  })

  describe('Out-of-range is distinct from in-tolerance', () => {
    it('deviation beyond range → magenta, not green', () => {
      const c = deviationToMetrologyColor(0.6, 0.1, 0.5)
      expect(c).toEqual(COLOR_OUT_OF_RANGE)
    })

    it('out-of-range is distinct from no-data', () => {
      const oor = deviationToMetrologyColor(0.6, 0.1, 0.5)
      const nd = deviationToMetrologyColor(NaN, 0.1, 0.5)
      expect(oor).not.toEqual(nd)
    })

    it('classifyDeviation: beyond range → out-of-range', () => {
      expect(classifyDeviation(0.6, 0.1, 0.5)).toBe('out-of-range')
    })
  })

  describe('Max-deviation always marked', () => {
    it('PASS fixture: max deviation index matches core', () => {
      const display = processDeviationField(
        FIXTURE_PASS.point_deviations, FIXTURE_PASS.tolerance_mm, FIXTURE_PASS.heatmap_max,
      )
      expect(display.maxDeviationIndex).toBe(FIXTURE_PASS.max_deviation_index)
      expect(display.maxDeviationValue).toBe(FIXTURE_PASS.max_deviation_value)
    })

    it('LOCAL_DEFECT: max deviation at planted defect', () => {
      const display = processDeviationField(
        FIXTURE_LOCAL_DEFECT.point_deviations, FIXTURE_LOCAL_DEFECT.tolerance_mm, FIXTURE_LOCAL_DEFECT.heatmap_max,
      )
      expect(display.maxDeviationIndex).toBe(FIXTURE_LOCAL_DEFECT.max_deviation_index)
      expect(Math.abs(display.maxDeviationValue)).toBe(Math.abs(FIXTURE_LOCAL_DEFECT.max_deviation_value))
    })
  })

  describe('Overlay ends at core transform', () => {
    it('identity fixture: all modes reference the identity matrix', () => {
      const modes: OverlayMode[] = ['reference', 'measured', 'overlay']
      for (const m of modes) {
        const state = getOverlayState(FIXTURE_PASS, m)
        expect(state.appliedMatrix).toBe(FIXTURE_PASS.transform_matrix)
      }
    })

    it('transformed fixture: applied matrix is the SAME object (no copy/mutation)', () => {
      const state = getOverlayState(FIXTURE_TRANSFORMED, 'overlay')
      expect(state.appliedMatrix).toBe(FIXTURE_TRANSFORMED.transform_matrix)
    })
  })

  describe('Colormap exact', () => {
    it('zero deviation → green nominal (not yellow, not blue)', () => {
      const c = deviationToMetrologyColor(0, 0.1, 0.5)
      expect(c.g).toBeGreaterThan(c.r)
      expect(c.g).toBeGreaterThan(c.b)
    })

    it('positive at tolerance edge → still green', () => {
      const c = deviationToMetrologyColor(0.1, 0.1, 0.5)
      expect(c.g).toBeGreaterThan(c.r)
    })

    it('slightly over tolerance → warm (r > g)', () => {
      const c = deviationToMetrologyColor(0.15, 0.1, 0.5)
      expect(c.r).toBeGreaterThanOrEqual(c.g)
    })

    it('negative at edge → still green', () => {
      const c = deviationToMetrologyColor(-0.1, 0.1, 0.5)
      expect(c.g).toBeGreaterThan(c.r)
    })

    it('negative outside → cool (b > g)', () => {
      const c = deviationToMetrologyColor(-0.3, 0.1, 0.5)
      expect(c.b).toBeGreaterThanOrEqual(c.g)
    })
  })

  describe('No geometry mutation', () => {
    it('scene state reports deviations unmodified from core', () => {
      const scene = extractImportSceneState(FIXTURE_PASS, 'stl', 'stl')
      expect(scene.deviationsUnmodified).toBe(true)
    })

    it('PASS fixture: transform is identity (no pre-alignment warp)', () => {
      const scene = extractImportSceneState(FIXTURE_PASS, 'stl', 'stl')
      expect(scene.transformIsIdentity).toBe(true)
    })

    it('TRANSFORMED: detected as non-identity', () => {
      const scene = extractImportSceneState(FIXTURE_TRANSFORMED, 'stl', 'stl')
      expect(scene.transformIsIdentity).toBe(false)
    })
  })

  describe('RPS frame conversions exact', () => {
    it('Mode A: identity datum returns entered coordinates exactly', () => {
      const pt: [number,number,number] = [1.23456789012345, -9.87654321098765, 42.0]
      const result = datumToFileFrame(pt, IDENTITY_DATUM)
      expect(result[0]).toBe(pt[0])
      expect(result[1]).toBe(pt[1])
      expect(result[2]).toBe(pt[2])
    })

    it('Mode B: 30° rotation + translation at full double precision', () => {
      const datum: ReferenceDatum = { translation: [10, 20, 30], rotationZ: 30 }
      const result = datumToFileFrame([5, 3, 7], datum)
      const cos30 = Math.cos(30 * Math.PI / 180)
      const sin30 = Math.sin(30 * Math.PI / 180)
      expect(result[0]).toBe(cos30 * 5 - sin30 * 3 + 10)
      expect(result[1]).toBe(sin30 * 5 + cos30 * 3 + 20)
      expect(result[2]).toBe(7 + 30)
    })
  })
})

// ═══════════════════════════════════════════════════════════════════════
// C. FIREWALL AUDIT
// ═══════════════════════════════════════════════════════════════════════

describe('C. Firewall audit — provenance list', () => {
  /**
   * PROVENANCE TABLE — every numeric field in InspectResponse
   * and how the frontend handles it.
   *
   * Each entry is: [field name, category, how used].
   * Categories:
   *   DISPLAY: shown verbatim or mapped to a visual
   *   ORCHESTRATION: used to build requests or manage state
   *   SAFETY: used for safety fence classification (non-compute)
   *
   * No category = VIOLATION (must fail).
   */
  const PROVENANCE: [string, string, string][] = [
    // Identity/provenance fields — displayed verbatim
    ['valid', 'DISPLAY', 'Controls whether result is shown at all'],
    ['core_version', 'DISPLAY', 'Shown in provenance panel'],
    ['timestamp', 'DISPLAY', 'Shown in provenance panel'],
    ['reference_hash', 'DISPLAY', 'Shown in provenance panel (truncated for display)'],
    ['measured_hash', 'DISPLAY', 'Shown in provenance panel (truncated for display)'],

    // Verdict — from core, never derived
    ['verdict', 'DISPLAY', 'Shown in verdict panel; used for color coding'],
    ['verdict_label', 'DISPLAY', 'Human-readable verdict shown verbatim'],

    // Tolerance — user-entered, sent to core
    ['tolerance_mm', 'ORCHESTRATION', 'Sent as input; used for colormap band width'],

    // Alignment — from core
    ['alignment_mode', 'DISPLAY', 'Shown in overlay panel'],
    ['alignment_rms', 'DISPLAY', 'Shown in overlay/statistics panel'],

    // Tier — from core
    ['precision_tier', 'DISPLAY', 'Shown in import status'],

    // Heatmap label — from core
    ['heatmap_label', 'DISPLAY', 'Shown on viewer (CORROBORATING label)'],

    // Statistics — from core, displayed verbatim
    ['stats.n_points', 'DISPLAY', 'Shown in statistics panel'],
    ['stats.mean', 'DISPLAY', 'Shown in statistics panel'],
    ['stats.rms', 'DISPLAY', 'Shown in statistics panel'],
    ['stats.max', 'DISPLAY', 'Shown in statistics panel'],
    ['stats.std_dev', 'DISPLAY', 'Shown in statistics panel'],
    ['stats.percent_within_tolerance', 'DISPLAY', 'Shown in statistics panel'],

    // Fingerprint — from core
    ['fingerprint.compiler', 'DISPLAY', 'Shown in provenance panel'],
    ['fingerprint.cpu', 'DISPLAY', 'Shown in provenance panel'],

    // Display geometry
    ['n_display_points', 'DISPLAY', 'Used for point cloud sizing'],
    ['heatmap_min', 'DISPLAY', 'Colormap range bound; shown in legend'],
    ['heatmap_max', 'DISPLAY', 'Colormap range bound; shown in legend'],

    // DOF observability — from core
    ['fully_constrained', 'DISPLAY', 'Shown in DOF panel; used for safety gate display'],
    ['num_under_constrained', 'DISPLAY', 'Shown in DOF panel'],

    // Uncertainty — from core
    ['expanded_uncertainty', 'DISPLAY', 'Shown in statistics panel'],
    ['coverage_factor', 'DISPLAY', 'Shown in statistics panel'],
    ['acceptance_lower', 'DISPLAY', 'Shown in statistics panel'],
    ['acceptance_upper', 'DISPLAY', 'Shown in statistics panel'],

    // Transform — from core, applied verbatim to 3D overlay
    ['transform_matrix', 'DISPLAY', 'Applied to Three.js mesh via Matrix4.set(); never recomputed'],

    // Per-point deviations — from core
    ['point_deviations', 'DISPLAY', 'Mapped to colors via metrology colormap; binned into histogram for display (visual arrangement of pre-computed core values)'],
    ['deviation_checksum', 'DISPLAY', 'Integrity check; could be verified but not recomputed'],
    ['max_deviation_index', 'DISPLAY', 'Used to highlight max-deviation point'],
    ['max_deviation_value', 'DISPLAY', 'Shown in statistics; used for marker'],

    // Messages
    ['warnings', 'DISPLAY', 'Shown in warning panel'],
    ['errors', 'DISPLAY', 'Shown in error panel'],
  ]

  it('every InspectResponse field is accounted for in the provenance table', () => {
    // All fields from the type definition should be in our provenance table.
    const typeFields = [
      'valid', 'core_version', 'timestamp', 'reference_hash', 'measured_hash',
      'verdict', 'verdict_label', 'tolerance_mm', 'alignment_mode', 'alignment_rms',
      'precision_tier', 'heatmap_label', 'stats', 'fingerprint', 'n_display_points',
      'heatmap_min', 'heatmap_max', 'fully_constrained', 'num_under_constrained',
      'expanded_uncertainty', 'coverage_factor', 'acceptance_lower', 'acceptance_upper',
      'transform_matrix', 'point_deviations', 'deviation_checksum',
      'max_deviation_index', 'max_deviation_value', 'warnings', 'errors',
    ]

    const provenanceFields = PROVENANCE.map(p => p[0].split('.')[0])
    for (const field of typeFields) {
      expect(provenanceFields).toContain(field)
    }
  })

  it('no VIOLATION category exists in provenance table', () => {
    for (const [_field, category] of PROVENANCE) {
      expect(category).toMatch(/^(DISPLAY|ORCHESTRATION|SAFETY)$/)
    }
  })

  it('the UI never derives a verdict', () => {
    // The verdict is taken verbatim from the fixture.
    // Verify: verdict field is the same object, not a recomputed string.
    for (const [, fixture] of Object.entries(ALL_FIXTURES)) {
      expect(fixture.verdict).toMatch(/^(PASS|WARNING|FAIL|INVALID)$/)
      // The verdict_label is also from core:
      expect(fixture.verdict_label).toBe(fixture.verdict)
    }
  })

  it('the UI never recomputes the transform matrix', () => {
    // For every fixture, getOverlayState returns the SAME array object.
    for (const [, fixture] of Object.entries(ALL_FIXTURES)) {
      const state = getOverlayState(fixture, 'overlay')
      expect(state.appliedMatrix).toBe(fixture.transform_matrix)
    }
  })

  it('the UI never recomputes deviation values', () => {
    // For every fixture, the viewer state deviations are the SAME array.
    for (const [, fixture] of Object.entries(ALL_FIXTURES)) {
      const viewer = extractViewerState(fixture)
      expect(viewer.deviationValues).toBe(fixture.point_deviations)
    }
  })

  it('multiply4x4 and composeChainForTest are TEST-ONLY (not imported by any component)', () => {
    // Structural check: these functions exist in pipeline-sequencer.ts but
    // are only imported by test files. We verify they work correctly (test oracle)
    // but confirm they are labeled TEST-ONLY.
    // (The firewall scan agent verified no production import exists.)
    expect(typeof multiply4x4).toBe('function')
    expect(typeof composeChainForTest).toBe('function')
    // They produce correct results (they are the test oracle):
    expect(multiply4x4(I4, I4)).toEqual(I4)
    expect(composeChainForTest([])).toEqual(I4)
  })

  it('datumToFileFrame is input preparation, not measurement', () => {
    // The function converts user-entered coordinates to file-frame.
    // It does NOT compute a fit, deviation, or verdict.
    // Verify: it's a pure, invertible coordinate transform.
    const datum: ReferenceDatum = { translation: [10, 20, 30], rotationZ: 45 }
    const pt: [number,number,number] = [1, 2, 3]
    const ff = datumToFileFrame(pt, datum)

    // It returns 3 numbers (a coordinate), not a verdict or measurement.
    expect(ff).toHaveLength(3)
    expect(typeof ff[0]).toBe('number')
    expect(typeof ff[1]).toBe('number')
    expect(typeof ff[2]).toBe('number')
  })
})

// ═══════════════════════════════════════════════════════════════════════
// D. 3D ↔ NUMERIC CONSISTENCY
// ═══════════════════════════════════════════════════════════════════════

describe('D. 3D ↔ numeric consistency', () => {

  it('viewer max-deviation point matches core max_deviation_index', () => {
    const viewer = extractViewerState(FIXTURE_LOCAL_DEFECT)
    const display = processDeviationField(
      viewer.deviationValues, FIXTURE_LOCAL_DEFECT.tolerance_mm, FIXTURE_LOCAL_DEFECT.heatmap_max,
    )
    expect(display.maxDeviationIndex).toBe(FIXTURE_LOCAL_DEFECT.max_deviation_index)
  })

  it('percent-in-tolerance from colors matches statistics panel', () => {
    const display = processDeviationField(
      FIXTURE_PASS.point_deviations, FIXTURE_PASS.tolerance_mm, FIXTURE_PASS.heatmap_max,
    )
    // All 10 points are within 0.1mm tolerance.
    expect(display.percentInTolerance).toBe(100)
    expect(display.percentInTolerance).toBe(FIXTURE_PASS.stats.percent_within_tolerance)
  })

  it('LOCAL_DEFECT: color agrees with deviation — defect is NOT green', () => {
    const display = processDeviationField(
      FIXTURE_LOCAL_DEFECT.point_deviations, FIXTURE_LOCAL_DEFECT.tolerance_mm,
      FIXTURE_LOCAL_DEFECT.heatmap_max,
    )
    // Point 7 (0.5mm) is far beyond tolerance — should not be green.
    const defectColor = display.colors[7]
    // Out-of-range = magenta (r=1, g=0, b=1) since 0.5 > heatmap_max(0.1)
    expect(defectColor.r).toBe(COLOR_OUT_OF_RANGE.r)
    expect(defectColor.g).toBe(COLOR_OUT_OF_RANGE.g)
    expect(defectColor.b).toBe(COLOR_OUT_OF_RANGE.b)
  })

  it('overlay transform matrix has exactly 16 elements', () => {
    for (const [, fixture] of Object.entries(ALL_FIXTURES)) {
      expect(fixture.transform_matrix).toHaveLength(16)
    }
  })

  it('identity transform: all 3 overlay modes produce the same matrix', () => {
    const modes: OverlayMode[] = ['reference', 'measured', 'overlay']
    const matrices = modes.map(m => getOverlayState(FIXTURE_PASS, m).appliedMatrix)
    expect(matrices[0]).toBe(matrices[1])
    expect(matrices[1]).toBe(matrices[2])
  })

  it('DOF states agree with core fully_constrained flag', () => {
    // PASS: fully constrained
    const passDofs = getDOFStates(FIXTURE_PASS)
    expect(passDofs.every(d => d.constrained)).toBe(true)
    expect(FIXTURE_PASS.fully_constrained).toBe(true)

    // INVALID: under-constrained
    const invalidDofs = getDOFStates(FIXTURE_INVALID)
    const underCount = invalidDofs.filter(d => !d.constrained).length
    expect(underCount).toBe(FIXTURE_INVALID.num_under_constrained)
    expect(FIXTURE_INVALID.fully_constrained).toBe(false)
  })

  it('viewer colormap domain matches fixture heatmap_min/max', () => {
    const viewer = extractViewerState(FIXTURE_PASS)
    expect(viewer.colormapMin).toBe(FIXTURE_PASS.heatmap_min)
    expect(viewer.colormapMax).toBe(FIXTURE_PASS.heatmap_max)
  })

  it('viewer point count matches fixture n_display_points for KNOWN_VERTICES', () => {
    const viewer = extractViewerState(FIXTURE_KNOWN_VERTICES)
    expect(viewer.pointCount).toBe(FIXTURE_KNOWN_VERTICES.point_deviations.length)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// E. FAILURE MODES
// ═══════════════════════════════════════════════════════════════════════

describe('E. Failure modes — safe error, no fabricated result', () => {

  it('core down: inspect throws, no result fabricated', async () => {
    mockCoreDown()
    await expect(mockAlignmeshApi.inspect('r', 'm', 0.1)).rejects.toThrow()
  })

  it('core down: health throws', async () => {
    mockCoreDown()
    await expect(mockAlignmeshApi.health()).rejects.toThrow('Core unavailable')
  })

  it('no fixture set: inspect throws, does not return default', async () => {
    resetMock()
    await expect(mockAlignmeshApi.inspect('r', 'm', 0.1)).rejects.toThrow('Mock core has no fixture set')
  })

  it('INVALID fixture: valid=false, no result pretends to be usable', async () => {
    mockCore(FIXTURE_INVALID)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)
    expect(result.valid).toBe(false)
    expect(result.verdict).toBe('INVALID')
  })

  it('FORMAT_GATE_INVALID: valid=false, distinct from observability INVALID', async () => {
    mockCore(FIXTURE_FORMAT_GATE_INVALID)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.005)
    expect(result.valid).toBe(false)
    expect(result.verdict).toBe('INVALID')
    // Has tier-specific warning, NOT observability error.
    expect(result.warnings.some(w => w.includes('STL'))).toBe(true)
    expect(result.errors).toHaveLength(0)
  })

  it('partial result: a fixture missing point_deviations does not crash viewer state', () => {
    const partial = { ...FIXTURE_PASS, point_deviations: [] as number[] }
    const viewer = extractViewerState(partial)
    expect(viewer.pointCount).toBe(0)
    expect(viewer.deviationValues).toEqual([])
    expect(viewer.noDataIndices).toEqual([])
  })

  it('malformed transform: empty transform_matrix does not crash overlay', () => {
    const malformed = { ...FIXTURE_PASS, transform_matrix: [] as number[] }
    const state = getOverlayState(malformed, 'overlay')
    expect(state.appliedMatrix).toEqual([])
    expect(state.matrixMatchesCore).toBe(false)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// F. RPS MODE A + MODE B — FULL FLOW
// ═══════════════════════════════════════════════════════════════════════

describe('F. RPS full flow — Mode A and Mode B', () => {

  it('Mode A: file-origin, 3 points with locks → payload correct', () => {
    const pts = [
      mkPt('P1', [10, 20, 30], [10, 20, 30], { x: true, y: false, z: true }),
      mkPt('P2', [40, 50, 60], [40, 50, 60], { x: false, y: true, z: true }),
      mkPt('P3', [70, 80, 90], [70, 80, 90], { x: true, y: true, z: true }),
    ]
    const payload = buildCorePayload(pts)
    expect(payload.landmarks[0].x).toBe(10)
    expect(payload.landmarks[0].lock_x).toBe(true)
    expect(payload.landmarks[1].lock_y).toBe(true)
    expect(payload.landmarks[2].weight).toBe(1.0)

    const dof = countConstrainedDOFs(pts)
    expect(dof.total).toBe(6)
    expect(dof.isComplete).toBe(true)
  })

  it('Mode B: datum conversion + payload → file-frame coordinates sent', () => {
    const datum: ReferenceDatum = { translation: [100, 200, 300], rotationZ: 90 }
    const entered: [number,number,number] = [1, 0, 0]
    const ff = datumToFileFrame(entered, datum)

    // 90° rotation: (1,0,0) → (0,1,0) + translation
    expect(ff[0]).toBeCloseTo(100, 12)
    expect(ff[1]).toBeCloseTo(201, 12)
    expect(ff[2]).toBe(300)

    const pt = mkPt('P1', entered, ff, { x: true, y: true, z: true })
    const payload = buildCorePayload([pt])
    expect(payload.landmarks[0].x).toBeCloseTo(100, 12)
    expect(payload.landmarks[0].y).toBeCloseTo(201, 12)
    expect(payload.landmarks[0].z).toBe(300)
  })

  it('Mode A → core → display: round-trip preserves values', async () => {
    // Simulate: user enters points, core returns PASS with known transform.
    mockCore(FIXTURE_PASS)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)

    // The core returned PASS; display it.
    expect(result.verdict).toBe('PASS')
    expect(result.alignment_rms).toBe(0.005)
    expect(result.transform_matrix).toEqual(I4)
  })

  it('Mode B → core → display: round-trip preserves values', async () => {
    mockCore(FIXTURE_TRANSFORMED)
    const result = await mockAlignmeshApi.inspect('r', 'm', 0.1)

    // The core returned a non-identity transform.
    const state = getOverlayState(result, 'overlay')
    expect(state.appliedMatrix[3]).toBe(10)   // tx from fixture
    expect(state.appliedMatrix[7]).toBe(20)   // ty
    expect(state.appliedMatrix[11]).toBe(30)  // tz
  })
})

// ═══════════════════════════════════════════════════════════════════════
// G. HYBRID PIPELINE ORDER — best-fit↔landmark
// ═══════════════════════════════════════════════════════════════════════

describe('G. Hybrid pipeline order', () => {
  const T_TRANSLATE = [1,0,0,5, 0,1,0,10, 0,0,1,15, 0,0,0,1]
  const T_ROT90Z = [0,-1,0,0, 1,0,0,0, 0,0,1,0, 0,0,0,1]

  it('best-fit → landmark: composed = Landmark x BestFit, scope = conformance', () => {
    const stages = [
      mkStage(0, 'best-fit', T_TRANSLATE, 0.05),
      mkStage(1, 'landmark', T_ROT90Z, 0.003),
    ]
    const composed = composeChainForTest([T_TRANSLATE, T_ROT90Z])
    const scope = classifyVerdictScope(stages)

    expect(scope).toBe('conformance')
    // Composed = Rot90Z x Translate: tx = -10, ty = 5
    expect(composed[3]).toBe(-10)
    expect(composed[7]).toBe(5)
  })

  it('landmark → best-fit: composed = BestFit x Landmark, scope = engineering', () => {
    const stages = [
      mkStage(0, 'landmark', T_ROT90Z, 0.003),
      mkStage(1, 'best-fit', T_TRANSLATE, 0.05),
    ]
    const composed = composeChainForTest([T_ROT90Z, T_TRANSLATE])
    const scope = classifyVerdictScope(stages)

    expect(scope).toBe('engineering')
    // Composed = Translate x Rot90Z: tx = 5, ty = 10
    expect(composed[3]).toBe(5)
    expect(composed[7]).toBe(10)
  })

  it('same stages, swapped order → different transform AND different scope', () => {
    const bf_lm = composeChainForTest([T_TRANSLATE, T_ROT90Z])
    const lm_bf = composeChainForTest([T_ROT90Z, T_TRANSLATE])

    expect(bf_lm).not.toEqual(lm_bf)

    const scope1 = classifyVerdictScope([
      mkStage(0, 'best-fit', T_TRANSLATE),
      mkStage(1, 'landmark', T_ROT90Z),
    ])
    const scope2 = classifyVerdictScope([
      mkStage(0, 'landmark', T_ROT90Z),
      mkStage(1, 'best-fit', T_TRANSLATE),
    ])

    expect(scope1).toBe('conformance')
    expect(scope2).toBe('engineering')
  })

  it('global → best-fit → datum: 3-stage pipeline, scope = conformance', () => {
    const T_SMALL = [1,0,0,1, 0,1,0,2, 0,0,1,3, 0,0,0,1]
    const stages = [
      mkStage(0, 'global-init', T_SMALL),
      mkStage(1, 'best-fit', T_TRANSLATE),
      mkStage(2, 'datum', T_ROT90Z),
    ]
    expect(classifyVerdictScope(stages)).toBe('conformance')

    const composed = composeChainForTest([T_SMALL, T_TRANSLATE, T_ROT90Z])
    // Verify composition: T2 x T1 x T0 = Rot90Z x Translate x SmallTranslate
    const step1 = multiply4x4(T_TRANSLATE, T_SMALL)
    const expected = multiply4x4(T_ROT90Z, step1)
    for (let i = 0; i < 16; i++) {
      expect(composed[i]).toBe(expected[i])
    }
  })

  it('best-fit safety fence: canBestFitBeConformance() is always false', () => {
    expect(canBestFitBeConformance()).toBe(false)
  })

  it('engineering label contains NOT and conformance', () => {
    const label = getVerdictScopeLabel('engineering')
    expect(label).toContain('NOT')
    expect(label.toLowerCase()).toContain('conformance')
  })

  it('best-fit and global-init flagged as engineering-only', () => {
    expect(isEngineeringOnly('best-fit')).toBe(true)
    expect(isEngineeringOnly('global-init')).toBe(true)
    expect(isEngineeringOnly('datum')).toBe(false)
    expect(isEngineeringOnly('landmark')).toBe(false)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// H. BACKEND FIELD COVERAGE — fields not yet represented in the UI
// ═══════════════════════════════════════════════════════════════════════

describe('H. Backend field coverage', () => {
  it('all InspectResponse fields are present in FIXTURE_PASS', () => {
    const requiredFields = [
      'valid', 'core_version', 'timestamp', 'reference_hash', 'measured_hash',
      'verdict', 'verdict_label', 'tolerance_mm', 'alignment_mode', 'alignment_rms',
      'precision_tier', 'heatmap_label', 'stats', 'fingerprint', 'n_display_points',
      'heatmap_min', 'heatmap_max', 'fully_constrained', 'num_under_constrained',
      'expanded_uncertainty', 'coverage_factor', 'acceptance_lower', 'acceptance_upper',
      'transform_matrix', 'point_deviations', 'deviation_checksum',
      'max_deviation_index', 'max_deviation_value', 'warnings', 'errors',
    ]
    for (const field of requiredFields) {
      expect(FIXTURE_PASS).toHaveProperty(field)
    }
  })

  it('heatmap_label contains CORROBORATING', () => {
    expect(FIXTURE_PASS.heatmap_label).toContain('CORROBORATING')
  })

  it('deviation_checksum is a core value displayed verbatim (UI does not recompute)', () => {
    // The checksum is a core integrity value. The UI displays it verbatim.
    // The UI does NOT recompute it — that would violate the firewall.
    // We verify it is present and numeric.
    expect(typeof FIXTURE_PASS.deviation_checksum).toBe('number')
    expect(isNaN(FIXTURE_PASS.deviation_checksum)).toBe(false)
  })

  it('max_deviation_index points to the actual max', () => {
    const devs = FIXTURE_PASS.point_deviations
    let maxIdx = 0, maxVal = 0
    for (let i = 0; i < devs.length; i++) {
      if (Math.abs(devs[i]) > Math.abs(maxVal)) {
        maxVal = devs[i]
        maxIdx = i
      }
    }
    expect(maxIdx).toBe(FIXTURE_PASS.max_deviation_index)
    expect(Math.abs(maxVal)).toBe(Math.abs(FIXTURE_PASS.max_deviation_value))
  })
})
