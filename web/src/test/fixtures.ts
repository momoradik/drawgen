/**
 * Golden backend fixtures — canned core result packages with fully known values.
 *
 * Each fixture has:
 * - A known transform
 * - Known per-point deviations (including a planted defect, no-data, out-of-range)
 * - Known verdict
 * - Known geometry description
 *
 * Used by the mock core and by all frontend tests.
 */
import type { InspectResponse } from '../api/alignmesh-types.generated'

/** Base fixture factory with all required fields. */
function base(overrides: Partial<InspectResponse>): InspectResponse {
  return {
    valid: true,
    core_version: 'alignmesh 0.1.0',
    timestamp: '2026-06-05T12:00:00Z',
    reference_hash: 'aabbccdd00112233445566778899aabb00112233445566778899aabbccddeeff',
    measured_hash: 'ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100',
    verdict: 'PASS',
    verdict_label: 'PASS',
    tolerance_mm: 0.1,
    alignment_mode: 'best-fit',
    alignment_rms: 0.005,
    precision_tier: 'COARSE (>=100um)',
    heatmap_label: 'CORROBORATING — not authoritative',
    stats: { n_points: 10, mean: 0.01, rms: 0.012, max: 0.03, std_dev: 0.005, percent_within_tolerance: 100 },
    fingerprint: { compiler: 'MSVC 19.41', cpu: 'Test CPU' },
    n_display_points: 10,
    heatmap_min: -0.1,
    heatmap_max: 0.1,
    fully_constrained: true,
    num_under_constrained: 0,
    expanded_uncertainty: 0.01,
    coverage_factor: 2,
    acceptance_lower: -0.04,
    acceptance_upper: 0.04,
    transform_matrix: [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1],
    point_deviations: [0.01, -0.005, 0.02, -0.01, 0.015, -0.008, 0.03, -0.02, 0.005, -0.003],
    deviation_checksum: 0.024,
    max_deviation_index: 6,
    max_deviation_value: 0.03,
    warnings: [],
    errors: [],
    ...overrides,
  }
}

// ── PASS fixture: all within tolerance ────────────────────────────────
export const FIXTURE_PASS = base({
  verdict: 'PASS',
  verdict_label: 'PASS',
})

// ── WARNING fixture: inside tolerance but within U of limit ──────────
export const FIXTURE_WARNING = base({
  verdict: 'WARNING',
  verdict_label: 'WARNING',
  stats: { n_points: 10, mean: 0.04, rms: 0.045, max: 0.085, std_dev: 0.01, percent_within_tolerance: 90 },
  warnings: ['Inside tolerance but within U of a limit'],
})

// ── FAIL fixture: outside acceptance zone ────────────────────────────
export const FIXTURE_FAIL = base({
  verdict: 'FAIL',
  verdict_label: 'FAIL',
  valid: true,
  stats: { n_points: 10, mean: 0.08, rms: 0.09, max: 0.15, std_dev: 0.02, percent_within_tolerance: 40 },
})

// ── INVALID fixture: gate failure ────────────────────────────────────
export const FIXTURE_INVALID = base({
  verdict: 'INVALID',
  verdict_label: 'INVALID',
  valid: false,
  fully_constrained: false,
  num_under_constrained: 3,
  errors: ['OBSERVABILITY: not all 6 DOFs constrained'],
})

// ── Fixture with planted local defect ────────────────────────────────
// Points 7-8 have a 0.5mm defect (out of tolerance on a 0.1mm part).
export const FIXTURE_LOCAL_DEFECT = base({
  verdict: 'FAIL',
  verdict_label: 'FAIL',
  stats: { n_points: 10, mean: 0.1, rms: 0.18, max: 0.5, std_dev: 0.15, percent_within_tolerance: 80 },
  point_deviations: [0.01, -0.005, 0.02, -0.01, 0.015, -0.008, 0.03, 0.5, 0.45, -0.003],
  deviation_checksum: 0.989,
  max_deviation_index: 7,
  max_deviation_value: 0.5,
})

// ── Fixture with known non-identity transform ────────────────────────
// 90° rotation around Z + translation (10, 20, 30).
export const FIXTURE_TRANSFORMED = base({
  transform_matrix: [0,-1,0,10, 1,0,0,20, 0,0,1,30, 0,0,0,1],
})

// ── Fixture: format-gate INVALID (STL at tight tolerance) ────────────
// Distinct from FIXTURE_INVALID (observability). This one fails the
// precision-tier gate — the error says "STL format not allowed".
export const FIXTURE_FORMAT_GATE_INVALID = base({
  verdict: 'INVALID',
  verdict_label: 'INVALID',
  valid: false,
  precision_tier: 'PRECISE (1-10um)',
  warnings: ['Precision gate: INVALID CLAIM: input data does not meet requirements for 0.005 mm tolerance (PRECISE (1-10um)). 1 violation(s).', 'STL format not allowed at PRECISE (1-10um) tier — use high-density mesh or CAD reference'],
  errors: [],
})

// ── Fixture with known vertex positions (for byte-fidelity check) ────
// 4 vertices of a unit square — the UI must display these EXACTLY
// as received, with no swap, center, or scale.
export const FIXTURE_KNOWN_VERTICES = base({
  n_display_points: 4,
  point_deviations: [0.01, -0.02, 0.03, -0.04],
  deviation_checksum: -0.02,
  max_deviation_index: 2,
  max_deviation_value: 0.03,
})

// ── All fixtures for iteration ───────────────────────────────────────
export const ALL_FIXTURES = {
  PASS: FIXTURE_PASS,
  WARNING: FIXTURE_WARNING,
  FAIL: FIXTURE_FAIL,
  INVALID: FIXTURE_INVALID,
  FORMAT_GATE_INVALID: FIXTURE_FORMAT_GATE_INVALID,
  LOCAL_DEFECT: FIXTURE_LOCAL_DEFECT,
  TRANSFORMED: FIXTURE_TRANSFORMED,
  KNOWN_VERTICES: FIXTURE_KNOWN_VERTICES,
}
