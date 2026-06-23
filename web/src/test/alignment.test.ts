/**
 * Alignment overlay + DOF observability tests.
 *
 * Tests against golden fixtures. COMPUTE FIREWALL:
 * the overlay's applied matrix EQUALS the core's matrix exactly.
 * No alignment is derived in the UI.
 */
import { describe, it, expect } from 'vitest'
import { FIXTURE_PASS, FIXTURE_INVALID, FIXTURE_TRANSFORMED } from './fixtures'
import { getOverlayState, type OverlayMode } from '../components/inspection/AlignmentOverlay'
import { getDOFStates } from '../components/inspection/DOFPanel'

// ════════════════════════════════════════════════════════════════════════
// Overlay transform matches core exactly
// ════════════════════════════════════════════════════════════════════════

describe('Alignment overlay transform', () => {
  it('identity transform: matrix equals fixture exactly', () => {
    const state = getOverlayState(FIXTURE_PASS, 'overlay')
    const expected = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]
    expect(state.appliedMatrix).toEqual(expected)
    // Full precision — no rounding or approximation.
    for (let i = 0; i < 16; i++) {
      expect(state.appliedMatrix[i]).toBe(expected[i])
    }
  })

  it('non-identity transform: 90° Z rotation + translation', () => {
    const state = getOverlayState(FIXTURE_TRANSFORMED, 'overlay')
    const m = state.appliedMatrix
    // Row-major, column-vector: [0,-1,0,10, 1,0,0,20, 0,0,1,30, 0,0,0,1]
    expect(m[0]).toBe(0)    // cos(90°)
    expect(m[1]).toBe(-1)   // -sin(90°)
    expect(m[3]).toBe(10)   // tx
    expect(m[4]).toBe(1)    // sin(90°)
    expect(m[5]).toBe(0)    // cos(90°)
    expect(m[7]).toBe(20)   // ty
    expect(m[10]).toBe(1)   // z unchanged
    expect(m[11]).toBe(30)  // tz
    expect(m[15]).toBe(1)   // homogeneous
  })

  it('applied matrix is the SAME object as the core response (no copy/mutation)', () => {
    const state = getOverlayState(FIXTURE_PASS, 'overlay')
    // The state holds a reference to the fixture's array.
    expect(state.appliedMatrix).toBe(FIXTURE_PASS.transform_matrix)
  })

  it('transform_matrix has exactly 16 elements', () => {
    const state = getOverlayState(FIXTURE_PASS, 'overlay')
    expect(state.appliedMatrix).toHaveLength(16)
  })
})

// ════════════════════════════════════════════════════════════════════════
// Toggle states expose correct objects
// ════════════════════════════════════════════════════════════════════════

describe('Overlay toggle states', () => {
  const modes: OverlayMode[] = ['reference', 'measured', 'overlay']

  it('reference-only: reference visible, measured hidden', () => {
    const state = getOverlayState(FIXTURE_PASS, 'reference')
    expect(state.referenceVisible).toBe(true)
    expect(state.measuredVisible).toBe(false)
    expect(state.mode).toBe('reference')
  })

  it('measured-only: measured visible, reference hidden', () => {
    const state = getOverlayState(FIXTURE_PASS, 'measured')
    expect(state.referenceVisible).toBe(false)
    expect(state.measuredVisible).toBe(true)
    expect(state.mode).toBe('measured')
  })

  it('overlay: both visible', () => {
    const state = getOverlayState(FIXTURE_PASS, 'overlay')
    expect(state.referenceVisible).toBe(true)
    expect(state.measuredVisible).toBe(true)
    expect(state.mode).toBe('overlay')
  })

  it('all three modes are distinct', () => {
    const states = modes.map(m => getOverlayState(FIXTURE_PASS, m))
    const signatures = states.map(s => s.referenceVisible + '|' + s.measuredVisible)
    const unique = new Set(signatures)
    expect(unique.size).toBe(3)
  })
})

// ════════════════════════════════════════════════════════════════════════
// DOF observability flags
// ════════════════════════════════════════════════════════════════════════

describe('DOF observability', () => {
  it('fully constrained: all 6 DOFs are OK', () => {
    const dofs = getDOFStates(FIXTURE_PASS)
    expect(dofs).toHaveLength(6)
    for (const dof of dofs) {
      expect(dof.constrained).toBe(true)
    }
  })

  it('under-constrained: 3 DOFs flagged', () => {
    const dofs = getDOFStates(FIXTURE_INVALID)
    expect(dofs).toHaveLength(6)
    const constrained = dofs.filter(d => d.constrained)
    const unconstrained = dofs.filter(d => !d.constrained)
    expect(constrained.length).toBe(3)
    expect(unconstrained.length).toBe(3)
  })

  it('under-constrained DOFs have label "Rot" or "Trans"', () => {
    const dofs = getDOFStates(FIXTURE_INVALID)
    for (const dof of dofs) {
      expect(dof.label).toMatch(/^(Rot|Trans) [XYZ]$/)
    }
  })

  it('FIXTURE_PASS has fully_constrained=true', () => {
    expect(FIXTURE_PASS.fully_constrained).toBe(true)
    expect(FIXTURE_PASS.num_under_constrained).toBe(0)
  })

  it('FIXTURE_INVALID has fully_constrained=false', () => {
    expect(FIXTURE_INVALID.fully_constrained).toBe(false)
    expect(FIXTURE_INVALID.num_under_constrained).toBe(3)
  })
})
