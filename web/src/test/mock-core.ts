/**
 * Mock/replayable core mode.
 *
 * Intercepts the alignmesh API client and returns golden fixtures
 * deterministically. Used by tests to run end-to-end without a live core.
 *
 * Usage in tests:
 *   import { mockCore, resetMock } from '../test/mock-core'
 *   beforeEach(() => mockCore(FIXTURE_PASS))
 *   afterEach(() => resetMock())
 */
import type { InspectResponse } from '../api/alignmesh-types.generated'

let activeFixture: InspectResponse | null = null
let healthOverride: { status: string } | 'error' | null = null

/** Set the fixture the mock core will return for /inspect calls. */
export function mockCore(fixture: InspectResponse) {
  activeFixture = fixture
  healthOverride = { status: 'ok' }
}

/** Simulate core being down. */
export function mockCoreDown() {
  activeFixture = null
  healthOverride = 'error'
}

/** Reset the mock. */
export function resetMock() {
  activeFixture = null
  healthOverride = null
}

/** Get the active fixture (for assertions). */
export function getActiveFixture(): InspectResponse | null {
  return activeFixture
}

/** Mock API that mirrors the real alignmeshApi interface. */
export const mockAlignmeshApi = {
  health: async () => {
    if (healthOverride === 'error') throw new Error('Core unavailable')
    return healthOverride ?? { status: 'ok' }
  },

  version: async () => ({
    version: 'alignmesh 0.1.0 (mock)',
    compiler: 'mock',
    cpu: 'mock',
    fp_flags: 'mock',
  }),

  upload: async (_file: File) => ({
    path: '/mock/uploaded/' + (_file?.name ?? 'part.stl'),
    size: 1234,
  }),

  inspect: async (_ref: string, _meas: string, _tol: number, _mode?: string) => {
    if (!activeFixture) throw new Error('Mock core has no fixture set')
    // Return the fixture verbatim — no computation.
    return activeFixture
  },
}
