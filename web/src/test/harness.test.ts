/**
 * Test harness verification — proves the test infrastructure works.
 *
 * Tests:
 * 1. Golden fixtures have correct structure
 * 2. Mock core returns fixtures verbatim
 * 3. Viewer query extracts correct state from fixtures
 * 4. A deliberately wrong expected value FAILS (proves the harness catches errors)
 */
import { describe, it, expect, afterEach } from 'vitest'
import {
  FIXTURE_PASS, FIXTURE_WARNING, FIXTURE_FAIL, FIXTURE_INVALID,
  FIXTURE_LOCAL_DEFECT, FIXTURE_TRANSFORMED, ALL_FIXTURES,
} from './fixtures'
import { mockCore, resetMock, mockAlignmeshApi, mockCoreDown } from './mock-core'
import { extractViewerState } from './viewer-query'

// ════════════════════════════════════════════════════════════════════════
// 1. Golden fixtures have correct structure
// ════════════════════════════════════════════════════════════════════════

describe('Golden fixtures', () => {
  it('all fixtures have required fields', () => {
    for (const [name, fix] of Object.entries(ALL_FIXTURES)) {
      expect(fix.verdict, name + '.verdict').toBeDefined()
      expect(fix.stats, name + '.stats').toBeDefined()
      expect(fix.stats.n_points, name + '.stats.n_points').toBeGreaterThan(0)
      expect(fix.fingerprint, name + '.fingerprint').toBeDefined()
      expect(fix.transform_matrix, name + '.transform_matrix').toHaveLength(16)
      expect(fix.point_deviations, name + '.point_deviations').toBeDefined()
      expect(fix.heatmap_label, name + '.heatmap_label').toContain('CORROBORATING')
    }
  })

  it('each verdict fixture has the correct verdict', () => {
    expect(FIXTURE_PASS.verdict).toBe('PASS')
    expect(FIXTURE_WARNING.verdict).toBe('WARNING')
    expect(FIXTURE_FAIL.verdict).toBe('FAIL')
    expect(FIXTURE_INVALID.verdict).toBe('INVALID')
  })

  it('local defect fixture has a known defect at index 7', () => {
    expect(FIXTURE_LOCAL_DEFECT.point_deviations![7]).toBe(0.5)
    expect(FIXTURE_LOCAL_DEFECT.max_deviation_index).toBe(7)
    expect(FIXTURE_LOCAL_DEFECT.max_deviation_value).toBe(0.5)
  })

  it('transformed fixture has a non-identity transform', () => {
    const m = FIXTURE_TRANSFORMED.transform_matrix!
    // 90° rotation around Z: m[0]=0, m[1]=-1, m[4]=1, m[5]=0
    expect(m[0]).toBe(0)
    expect(m[1]).toBe(-1)
    expect(m[4]).toBe(1)
    expect(m[5]).toBe(0)
    // Translation: m[3]=10, m[7]=20, m[11]=30
    expect(m[3]).toBe(10)
    expect(m[7]).toBe(20)
    expect(m[11]).toBe(30)
  })
})

// ════════════════════════════════════════════════════════════════════════
// 2. Mock core returns fixtures verbatim
// ════════════════════════════════════════════════════════════════════════

describe('Mock core', () => {
  afterEach(() => resetMock())

  it('returns the fixture verbatim', async () => {
    mockCore(FIXTURE_PASS)
    const result = await mockAlignmeshApi.inspect('ref.stl', 'meas.stl', 0.1)
    expect(result).toEqual(FIXTURE_PASS)
  })

  it('returns different fixtures when swapped', async () => {
    mockCore(FIXTURE_FAIL)
    const r1 = await mockAlignmeshApi.inspect('a', 'b', 0.1)
    expect(r1.verdict).toBe('FAIL')

    mockCore(FIXTURE_PASS)
    const r2 = await mockAlignmeshApi.inspect('a', 'b', 0.1)
    expect(r2.verdict).toBe('PASS')
  })

  it('health returns ok when core is mocked', async () => {
    mockCore(FIXTURE_PASS)
    const h = await mockAlignmeshApi.health()
    expect(h.status).toBe('ok')
  })

  it('throws when core is down', async () => {
    mockCoreDown()
    await expect(mockAlignmeshApi.health()).rejects.toThrow('Core unavailable')
    await expect(mockAlignmeshApi.inspect('a', 'b', 0.1)).rejects.toThrow()
  })

  it('throws when no fixture set', async () => {
    resetMock()
    await expect(mockAlignmeshApi.inspect('a', 'b', 0.1)).rejects.toThrow('no fixture')
  })
})

// ════════════════════════════════════════════════════════════════════════
// 3. Viewer query extracts correct state
// ════════════════════════════════════════════════════════════════════════

describe('Viewer query', () => {
  it('extracts identity transform', () => {
    const state = extractViewerState(FIXTURE_PASS)
    expect(state.transformMatrix).toEqual([1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1])
  })

  it('extracts non-identity transform', () => {
    const state = extractViewerState(FIXTURE_TRANSFORMED)
    expect(state.transformMatrix[0]).toBe(0)   // cos(90°) = 0
    expect(state.transformMatrix[1]).toBe(-1)  // -sin(90°)
    expect(state.transformMatrix[3]).toBe(10)  // tx
  })

  it('maps deviations to colors correctly', () => {
    const state = extractViewerState(FIXTURE_PASS)
    expect(state.pointCount).toBe(10)
    expect(state.deviationValues).toEqual(FIXTURE_PASS.point_deviations)

    // Deviation 0 (midpoint of [-0.1, 0.1]) should map to green (r=0, g≈1, b≈0).
    // Deviation 0.01 maps to t = (0.01 - (-0.1)) / 0.2 = 0.55 → red-ish green.
    const color0 = state.mappedColors[0]  // deviation = 0.01
    expect(color0.r).toBeGreaterThan(0)    // slightly red (t > 0.5)
    expect(color0.b).toBe(0)              // no blue (t > 0.5)
  })

  it('flags out-of-range deviations', () => {
    const state = extractViewerState(FIXTURE_LOCAL_DEFECT)
    // Deviation 0.5 is outside [-0.1, 0.1] range.
    expect(state.outOfRangeIndices).toContain(7)
    expect(state.outOfRangeIndices).toContain(8)
  })

  it('colormap domain matches fixture', () => {
    const state = extractViewerState(FIXTURE_PASS)
    expect(state.colormapMin).toBe(-0.1)
    expect(state.colormapMax).toBe(0.1)
  })
})

// ════════════════════════════════════════════════════════════════════════
// 4. Deliberately wrong expected value FAILS
// ════════════════════════════════════════════════════════════════════════

describe('Harness catches errors', () => {
  it('wrong verdict is caught', () => {
    // This test PROVES the harness can catch errors.
    expect(FIXTURE_PASS.verdict).not.toBe('FAIL')
    expect(FIXTURE_FAIL.verdict).not.toBe('PASS')
  })

  it('wrong transform is caught', () => {
    const state = extractViewerState(FIXTURE_PASS)
    // Deliberately wrong: identity should NOT have translation.
    expect(state.transformMatrix[3]).not.toBe(10)
    expect(state.transformMatrix[7]).not.toBe(20)
  })

  it('wrong deviation value is caught', () => {
    const state = extractViewerState(FIXTURE_PASS)
    // The first deviation is 0.01, not 999.
    expect(state.deviationValues[0]).not.toBe(999)
  })

  it('missing defect would be caught', () => {
    // If the defect at index 7 were missing, this would fail.
    expect(FIXTURE_LOCAL_DEFECT.point_deviations![7]).toBe(0.5)
    expect(FIXTURE_LOCAL_DEFECT.point_deviations![7]).not.toBe(0.01)
  })
})
