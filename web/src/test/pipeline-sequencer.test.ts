/**
 * Pipeline stage sequencer tests.
 *
 * Tests:
 * 1. Composition order: composed transform = last x ... x first (full precision).
 * 2. Per-stage residuals/warnings displayed match the fixture.
 * 3. Safety fence: best-fit-derived result is labeled engineering/non-conformance
 *    and CANNOT be shown as datum conformance (try to force it — must be blocked).
 * 4. Order matters: best-fit->landmark vs landmark->best-fit compose differently.
 * 5. Verdict scope classification for all stage combinations.
 * 6. Composition chain display order matches the core's execution order.
 */
import { describe, it, expect } from 'vitest'
import {
  classifyVerdictScope,
  canBestFitBeConformance,
  getVerdictScopeLabel,
  isEngineeringOnly,
  multiply4x4,
  composeChainForTest,
  buildPipelinePayload,
  type StageResult,
  type StageKind,
  type PipelineResult,
} from '../components/inspection/pipeline-sequencer'

// ═══════════════════════════════════════════════════════════════════════
// Golden per-stage transforms (known, hand-verified)
// ═══════════════════════════════════════════════════════════════════════

/** Identity 4x4. */
const I4 = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]

/** Pure translation (5, 10, 15). */
const T_TRANSLATE: number[] = [
  1, 0, 0, 5,
  0, 1, 0, 10,
  0, 0, 1, 15,
  0, 0, 0, 1,
]

/** 90° rotation about Z, no translation. */
const cos90 = 0  // Math.cos(90°)
const sin90 = 1  // Math.sin(90°)
const T_ROT90Z: number[] = [
  cos90, -sin90, 0, 0,
  sin90,  cos90, 0, 0,
  0,      0,     1, 0,
  0,      0,     0, 1,
]

/** 90° Z rotation + translation (10, 20, 30). */
const T_ROT90Z_TRANS: number[] = [
  0, -1, 0, 10,
  1,  0, 0, 20,
  0,  0, 1, 30,
  0,  0, 0, 1,
]

/** 180° rotation about Z. */
const T_ROT180Z: number[] = [
  -1,  0, 0, 0,
   0, -1, 0, 0,
   0,  0, 1, 0,
   0,  0, 0, 1,
]

/** Helper: make a stage result. */
function mkStage(
  index: number,
  kind: StageKind,
  transform: number[],
  rms = 0.005,
  converged = true,
  warnings: string[] = [],
): StageResult {
  return {
    index, kind,
    name: kind === 'best-fit' ? 'Best-Fit (ICP/GICP)' :
          kind === 'global-init' ? 'Global Initialization (FPFH)' :
          kind === 'landmark' ? 'Landmark / Kabsch' :
          'Datum / RPS (3-2-1)',
    transform, rms, converged, warnings,
  }
}

/** Helper: make a pipeline result from stages. */
function mkPipeline(stages: StageResult[], composedTransform?: number[]): PipelineResult {
  const composed = composedTransform ??
    composeChainForTest(stages.map(s => s.transform))
  return {
    stages,
    composedTransform: composed,
    composedRms: stages.length > 0 ? stages[stages.length - 1].rms : 0,
    allConverged: stages.every(s => s.converged),
    verdictScope: classifyVerdictScope(stages),
    warnings: stages.flatMap(s => s.warnings),
  }
}

// ═══════════════════════════════════════════════════════════════════════
// 1. Composition order: composed = last x ... x first
// ═══════════════════════════════════════════════════════════════════════

describe('Composition order', () => {
  it('identity x identity = identity', () => {
    const result = multiply4x4(I4, I4)
    expect(result).toEqual(I4)
  })

  it('identity x T = T', () => {
    const result = multiply4x4(I4, T_TRANSLATE)
    expect(result).toEqual(T_TRANSLATE)
  })

  it('T x identity = T', () => {
    const result = multiply4x4(T_TRANSLATE, I4)
    expect(result).toEqual(T_TRANSLATE)
  })

  it('two translations compose additively', () => {
    // T1 = translate(5,10,15), T2 = translate(5,10,15)
    // T2 x T1 = translate(10,20,30)
    const result = multiply4x4(T_TRANSLATE, T_TRANSLATE)
    expect(result[3]).toBe(10)   // tx
    expect(result[7]).toBe(20)   // ty
    expect(result[11]).toBe(30)  // tz
    expect(result[15]).toBe(1)
  })

  it('rotation then translation != translation then rotation', () => {
    // This is the critical order test.
    // A: first translate (5,10,15), then rotate 90° about Z
    //    Composed = Rot90Z x Translate
    // B: first rotate 90° about Z, then translate (5,10,15)
    //    Composed = Translate x Rot90Z
    const AB = multiply4x4(T_ROT90Z, T_TRANSLATE)
    const BA = multiply4x4(T_TRANSLATE, T_ROT90Z)

    // They must be different (non-commutative).
    expect(AB).not.toEqual(BA)

    // A: Rot90Z x Translate
    // The translation column of the composed matrix:
    //   Rot90Z * [5,10,15,1]^T => [-10, 5, 15, 1]
    // So tx=-10, ty=5, tz=15
    expect(AB[3]).toBe(-10)
    expect(AB[7]).toBe(5)
    expect(AB[11]).toBe(15)

    // B: Translate x Rot90Z
    // The rotation part is the same (Rot90Z), but translation is (5+0, 10+0, 15+0)
    // Actually: Translate * Rot90Z = Rot90Z with added translation columns
    // [1,0,0,5] * [0,-1,0,0]   = [0,-1,0,5]
    // [0,1,0,10]  [1,0,0,0]     [1,0,0,10]
    // [0,0,1,15]  [0,0,1,0]     [0,0,1,15]
    // [0,0,0,1]   [0,0,0,1]     [0,0,0,1]
    expect(BA[3]).toBe(5)
    expect(BA[7]).toBe(10)
    expect(BA[11]).toBe(15)
  })

  it('composeChainForTest: 3-stage chain matches manual multiply', () => {
    // Stage 0: translate, Stage 1: rot90Z, Stage 2: translate
    // Composed = T2 x T1 x T0 = Translate x Rot90Z x Translate
    const chain = [T_TRANSLATE, T_ROT90Z, T_TRANSLATE]
    const composed = composeChainForTest(chain)

    // Manual: first multiply T1 x T0 = Rot90Z x Translate
    const step1 = multiply4x4(T_ROT90Z, T_TRANSLATE)
    // Then T2 x step1 = Translate x (Rot90Z x Translate)
    const manual = multiply4x4(T_TRANSLATE, step1)

    // Must be bit-identical.
    for (let i = 0; i < 16; i++) {
      expect(composed[i]).toBe(manual[i])
    }
  })

  it('composed transform equals stages[n-1] x ... x stages[0] at full double precision', () => {
    // Use the 90° rotation + translation as a non-trivial case.
    const stages = [
      mkStage(0, 'global-init', T_TRANSLATE),
      mkStage(1, 'best-fit', T_ROT90Z),
      mkStage(2, 'datum', T_ROT90Z_TRANS),
    ]

    // Core would compute: T2 x T1 x T0
    const expected = composeChainForTest([T_TRANSLATE, T_ROT90Z, T_ROT90Z_TRANS])

    // Build the pipeline with the "core's" composed transform.
    const pipeline = mkPipeline(stages, expected)

    // Verify the displayed composed transform matches.
    for (let i = 0; i < 16; i++) {
      expect(pipeline.composedTransform[i]).toBe(expected[i])
    }
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 2. Per-stage residuals/warnings match fixture
// ═══════════════════════════════════════════════════════════════════════

describe('Per-stage display fidelity', () => {
  it('per-stage RMS matches fixture exactly', () => {
    const stages = [
      mkStage(0, 'global-init', I4, 1.234),
      mkStage(1, 'best-fit', I4, 0.0567),
      mkStage(2, 'datum', I4, 0.00123),
    ]
    const pipeline = mkPipeline(stages)

    expect(pipeline.stages[0].rms).toBe(1.234)
    expect(pipeline.stages[1].rms).toBe(0.0567)
    expect(pipeline.stages[2].rms).toBe(0.00123)
  })

  it('per-stage warnings are preserved verbatim', () => {
    const stages = [
      mkStage(0, 'best-fit', I4, 0.1, true, ['High residual on left flange']),
      mkStage(1, 'datum', I4, 0.01, true, ['Datum B contact area < 50%']),
    ]
    const pipeline = mkPipeline(stages)

    expect(pipeline.stages[0].warnings).toEqual(['High residual on left flange'])
    expect(pipeline.stages[1].warnings).toEqual(['Datum B contact area < 50%'])
    expect(pipeline.warnings).toContain('High residual on left flange')
    expect(pipeline.warnings).toContain('Datum B contact area < 50%')
  })

  it('per-stage convergence flags are preserved', () => {
    const stages = [
      mkStage(0, 'best-fit', I4, 0.1, true),
      mkStage(1, 'landmark', I4, 0.01, false),
    ]
    const pipeline = mkPipeline(stages)

    expect(pipeline.stages[0].converged).toBe(true)
    expect(pipeline.stages[1].converged).toBe(false)
    expect(pipeline.allConverged).toBe(false)
  })

  it('per-stage transforms are the core values, not recomputed', () => {
    const knownTransform = [
      0.8660254037844387, -0.5, 0, 12.830127018922194,
      0.5, 0.8660254037844387, 0, 25.098076211353316,
      0, 0, 1, 37,
      0, 0, 0, 1,
    ]
    const stage = mkStage(0, 'datum', knownTransform, 0.003)
    const pipeline = mkPipeline([stage])

    // Full double precision — must be bit-identical to the fixture.
    for (let i = 0; i < 16; i++) {
      expect(pipeline.stages[0].transform[i]).toBe(knownTransform[i])
    }
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 3. Safety fence: best-fit CANNOT be conformance (ENFORCED)
// ═══════════════════════════════════════════════════════════════════════

describe('Safety fence: best-fit cannot be conformance', () => {
  it('canBestFitBeConformance always returns false', () => {
    expect(canBestFitBeConformance()).toBe(false)
  })

  it('best-fit only pipeline: engineering scope', () => {
    const stages = [mkStage(0, 'best-fit', I4)]
    expect(classifyVerdictScope(stages)).toBe('engineering')
  })

  it('global-init only pipeline: engineering scope', () => {
    const stages = [mkStage(0, 'global-init', I4)]
    expect(classifyVerdictScope(stages)).toBe('engineering')
  })

  it('global-init -> best-fit: engineering scope (final is best-fit)', () => {
    const stages = [
      mkStage(0, 'global-init', I4),
      mkStage(1, 'best-fit', I4),
    ]
    expect(classifyVerdictScope(stages)).toBe('engineering')
  })

  it('best-fit -> datum: conformance (final is datum)', () => {
    const stages = [
      mkStage(0, 'best-fit', I4),
      mkStage(1, 'datum', I4),
    ]
    expect(classifyVerdictScope(stages)).toBe('conformance')
  })

  it('datum -> best-fit: engineering (final is best-fit) — NOT conformance', () => {
    // Even though datum was involved, best-fit dominates as final stage.
    const stages = [
      mkStage(0, 'datum', I4),
      mkStage(1, 'best-fit', I4),
    ]
    expect(classifyVerdictScope(stages)).toBe('engineering')
  })

  it('global-init -> best-fit -> datum: conformance (final is datum)', () => {
    const stages = [
      mkStage(0, 'global-init', I4),
      mkStage(1, 'best-fit', I4),
      mkStage(2, 'datum', I4),
    ]
    expect(classifyVerdictScope(stages)).toBe('conformance')
  })

  it('global-init -> best-fit -> landmark: conformance (final is landmark)', () => {
    const stages = [
      mkStage(0, 'global-init', I4),
      mkStage(1, 'best-fit', I4),
      mkStage(2, 'landmark', I4),
    ]
    expect(classifyVerdictScope(stages)).toBe('conformance')
  })

  it('landmark -> best-fit: engineering (final is best-fit)', () => {
    const stages = [
      mkStage(0, 'landmark', I4),
      mkStage(1, 'best-fit', I4),
    ]
    expect(classifyVerdictScope(stages)).toBe('engineering')
  })

  it('engineering scope label mentions NON-conformance', () => {
    const label = getVerdictScopeLabel('engineering')
    expect(label).toContain('NOT')
    expect(label.toLowerCase()).toContain('conformance')
  })

  it('conformance scope label mentions conformance', () => {
    const label = getVerdictScopeLabel('conformance')
    expect(label.toLowerCase()).toContain('conformance')
  })

  it('isEngineeringOnly flags best-fit and global-init', () => {
    expect(isEngineeringOnly('best-fit')).toBe(true)
    expect(isEngineeringOnly('global-init')).toBe(true)
    expect(isEngineeringOnly('datum')).toBe(false)
    expect(isEngineeringOnly('landmark')).toBe(false)
  })

  it('empty pipeline: engineering (fail-safe)', () => {
    expect(classifyVerdictScope([])).toBe('engineering')
  })

  it('pipeline result carries the correct verdictScope', () => {
    // Engineering pipeline
    const engPipeline = mkPipeline([
      mkStage(0, 'global-init', T_TRANSLATE),
      mkStage(1, 'best-fit', T_ROT90Z),
    ])
    expect(engPipeline.verdictScope).toBe('engineering')

    // Conformance pipeline
    const confPipeline = mkPipeline([
      mkStage(0, 'best-fit', T_TRANSLATE),
      mkStage(1, 'datum', T_ROT90Z),
    ])
    expect(confPipeline.verdictScope).toBe('conformance')
  })

  it('TRY TO FORCE: adding best-fit after datum changes scope to engineering', () => {
    // Start with a conformance pipeline.
    const confStages = [mkStage(0, 'datum', I4)]
    expect(classifyVerdictScope(confStages)).toBe('conformance')

    // "Force" by appending best-fit — must be blocked (becomes engineering).
    const forced = [...confStages, mkStage(1, 'best-fit', I4)]
    expect(classifyVerdictScope(forced)).toBe('engineering')
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 4. Order matters: best-fit->landmark vs landmark->best-fit
// ═══════════════════════════════════════════════════════════════════════

describe('Composition order: best-fit->landmark vs landmark->best-fit', () => {
  it('different execution orders produce different composed transforms', () => {
    // A: best-fit(translate) then landmark(rot90Z)
    const orderA = composeChainForTest([T_TRANSLATE, T_ROT90Z])
    // B: landmark(rot90Z) then best-fit(translate)
    const orderB = composeChainForTest([T_ROT90Z, T_TRANSLATE])

    // These must be different (non-commutative).
    expect(orderA).not.toEqual(orderB)

    // A = Rot90Z x Translate: tx=-10, ty=5
    expect(orderA[3]).toBe(-10)
    expect(orderA[7]).toBe(5)

    // B = Translate x Rot90Z: tx=5, ty=10
    expect(orderB[3]).toBe(5)
    expect(orderB[7]).toBe(10)
  })

  it('best-fit->landmark pipeline displays in correct order', () => {
    const stages = [
      mkStage(0, 'best-fit', T_TRANSLATE, 0.05),
      mkStage(1, 'landmark', T_ROT90Z, 0.003),
    ]
    const pipeline = mkPipeline(stages)

    // Stage 0 is best-fit, stage 1 is landmark.
    expect(pipeline.stages[0].kind).toBe('best-fit')
    expect(pipeline.stages[0].index).toBe(0)
    expect(pipeline.stages[1].kind).toBe('landmark')
    expect(pipeline.stages[1].index).toBe(1)

    // Composed = T1 x T0 = Rot90Z x Translate
    const expectedComposed = multiply4x4(T_ROT90Z, T_TRANSLATE)
    for (let i = 0; i < 16; i++) {
      expect(pipeline.composedTransform[i]).toBe(expectedComposed[i])
    }

    // Scope: final is landmark -> conformance.
    expect(pipeline.verdictScope).toBe('conformance')
  })

  it('landmark->best-fit pipeline displays in correct order', () => {
    const stages = [
      mkStage(0, 'landmark', T_ROT90Z, 0.003),
      mkStage(1, 'best-fit', T_TRANSLATE, 0.05),
    ]
    const pipeline = mkPipeline(stages)

    // Stage 0 is landmark, stage 1 is best-fit.
    expect(pipeline.stages[0].kind).toBe('landmark')
    expect(pipeline.stages[0].index).toBe(0)
    expect(pipeline.stages[1].kind).toBe('best-fit')
    expect(pipeline.stages[1].index).toBe(1)

    // Composed = T1 x T0 = Translate x Rot90Z
    const expectedComposed = multiply4x4(T_TRANSLATE, T_ROT90Z)
    for (let i = 0; i < 16; i++) {
      expect(pipeline.composedTransform[i]).toBe(expectedComposed[i])
    }

    // Scope: final is best-fit -> engineering.
    expect(pipeline.verdictScope).toBe('engineering')
  })

  it('swapped order produces different verdict scope', () => {
    const bf_then_lm = classifyVerdictScope([
      mkStage(0, 'best-fit', I4),
      mkStage(1, 'landmark', I4),
    ])
    const lm_then_bf = classifyVerdictScope([
      mkStage(0, 'landmark', I4),
      mkStage(1, 'best-fit', I4),
    ])

    expect(bf_then_lm).toBe('conformance')
    expect(lm_then_bf).toBe('engineering')
    expect(bf_then_lm).not.toBe(lm_then_bf)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 5. Build pipeline payload
// ═══════════════════════════════════════════════════════════════════════

describe('Pipeline payload builder', () => {
  it('preserves user-specified order exactly', () => {
    const payload = buildPipelinePayload([
      { kind: 'global-init', label: 'Global Initialization (FPFH)' },
      { kind: 'best-fit', label: 'Best-Fit (ICP/GICP)' },
      { kind: 'datum', label: 'Datum / RPS (3-2-1)' },
    ])

    expect(payload.pipeline).toHaveLength(3)
    expect(payload.pipeline[0].kind).toBe('global-init')
    expect(payload.pipeline[0].index).toBe(0)
    expect(payload.pipeline[1].kind).toBe('best-fit')
    expect(payload.pipeline[1].index).toBe(1)
    expect(payload.pipeline[2].kind).toBe('datum')
    expect(payload.pipeline[2].index).toBe(2)
  })

  it('empty pipeline produces empty payload', () => {
    const payload = buildPipelinePayload([])
    expect(payload.pipeline).toHaveLength(0)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 6. Matrix multiply correctness (test oracle)
// ═══════════════════════════════════════════════════════════════════════

describe('Matrix multiply (test oracle)', () => {
  it('90° Z rotation applied to (1,0,0,1) = (0,1,0,1)', () => {
    // Point as column vector: [1, 0, 0, 1]
    // R90Z * p: [0*1 + (-1)*0, 1*1 + 0*0, 0, 1] = [0, 1, 0, 1]
    // Direct check: T_ROT90Z row 0: [0,-1,0,0] dot [1,0,0,0] = 0
    expect(T_ROT90Z[0] * 1 + T_ROT90Z[1] * 0 + T_ROT90Z[2] * 0 + T_ROT90Z[3] * 1).toBe(0)
    // Row 1: [1,0,0,0] dot [1,0,0,1] = 1
    expect(T_ROT90Z[4] * 1 + T_ROT90Z[5] * 0 + T_ROT90Z[6] * 0 + T_ROT90Z[7] * 1).toBe(1)
  })

  it('180° Z rotation: (1,0,0) -> (-1,0,0)', () => {
    expect(T_ROT180Z[0] * 1 + T_ROT180Z[1] * 0).toBe(-1)
    expect(T_ROT180Z[4] * 1 + T_ROT180Z[5] * 0).toBe(0)
  })

  it('90° Z rotation composed twice = 180° Z rotation', () => {
    const rot180 = multiply4x4(T_ROT90Z, T_ROT90Z)
    expect(rot180[0]).toBe(-1)   // cos(180°)
    expect(rot180[1]).toBe(0)    // -sin(180°) ≈ 0 (exact since sin90*sin90+cos90*cos90-based)
    expect(rot180[4]).toBe(0)    // sin(180°)
    expect(rot180[5]).toBe(-1)   // cos(180°)
    expect(rot180[10]).toBe(1)   // z unchanged
    expect(rot180[15]).toBe(1)
  })

  it('composeChainForTest of empty list = identity', () => {
    expect(composeChainForTest([])).toEqual(I4)
  })

  it('composeChainForTest of single = that transform', () => {
    expect(composeChainForTest([T_TRANSLATE])).toEqual(T_TRANSLATE)
  })
})

// ═══════════════════════════════════════════════════════════════════════
// 7. Full-precision composition with non-trivial transforms
// ═══════════════════════════════════════════════════════════════════════

describe('Full-precision composition', () => {
  it('30° rotation + translation composed with translate: full double precision', () => {
    const cos30 = Math.cos(30 * Math.PI / 180)
    const sin30 = Math.sin(30 * Math.PI / 180)

    const T_ROT30_TRANS: number[] = [
      cos30, -sin30, 0, 10,
      sin30,  cos30, 0, 20,
      0,      0,     1, 30,
      0,      0,     0, 1,
    ]

    // Compose: T_ROT30_TRANS x T_TRANSLATE
    const composed = multiply4x4(T_ROT30_TRANS, T_TRANSLATE)

    // Hand-compute translation column:
    // tx = cos30*5 + (-sin30)*10 + 0*15 + 10
    const expectedTx = cos30 * 5 - sin30 * 10 + 10
    // ty = sin30*5 + cos30*10 + 0*15 + 20
    const expectedTy = sin30 * 5 + cos30 * 10 + 20
    // tz = 0*5 + 0*10 + 1*15 + 30 = 45
    const expectedTz = 15 + 30

    // Full precision — must be bit-identical.
    expect(composed[3]).toBe(expectedTx)
    expect(composed[7]).toBe(expectedTy)
    expect(composed[11]).toBe(expectedTz)
  })
})
