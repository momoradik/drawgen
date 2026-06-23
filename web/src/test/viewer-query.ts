/**
 * Viewer test API — queryable state for the 3D viewer.
 *
 * Exposes, for tests only, the internal state of the InspectionViewer:
 * - Applied transform matrix per object
 * - Per-vertex/per-facet color and SOURCE deviation value
 * - Active colormap and domain
 * - Which elements are flagged no-data or out-of-range
 *
 * Tests read this state and assert against expected values.
 * Pixel comparison is NOT the primary method.
 */
import type { InspectResponse } from '../api/alignmesh-types.generated'

export interface ViewerState {
  /** The 4x4 transform matrix applied to the measured mesh (from core). */
  transformMatrix: number[]

  /** Per-point deviation values as received from the core. */
  deviationValues: number[]

  /** Per-point mapped colors [r,g,b] normalized to [0,1]. */
  mappedColors: Array<{ r: number; g: number; b: number }>

  /** The colormap domain [min, max]. */
  colormapMin: number
  colormapMax: number

  /** Indices of points with no data (deviation undefined/NaN). */
  noDataIndices: number[]

  /** Indices of points outside the colormap range. */
  outOfRangeIndices: number[]

  /** Total point count displayed. */
  pointCount: number
}

/**
 * Extract queryable viewer state from a core response.
 *
 * This simulates what the viewer would hold after rendering.
 * FIREWALL: this does NOT compute deviations — it reads the core's values
 * and maps them through the same display logic the viewer uses.
 */
export function extractViewerState(result: InspectResponse): ViewerState {
  const devs = result.point_deviations ?? []
  const min = result.heatmap_min
  const max = result.heatmap_max

  const colors: Array<{ r: number; g: number; b: number }> = []
  const noData: number[] = []
  const outOfRange: number[] = []

  for (let i = 0; i < devs.length; i++) {
    const d = devs[i]

    if (d === undefined || d === null || isNaN(d)) {
      noData.push(i)
      colors.push({ r: 0.5, g: 0.5, b: 0.5 })
      continue
    }

    if (d < min || d > max) {
      outOfRange.push(i)
    }

    // Same color mapping as InspectionViewer.deviationToColor (display only).
    const range = max - min
    if (range <= 0) {
      colors.push({ r: 0.5, g: 0.5, b: 0.5 })
      continue
    }
    const t = Math.max(0, Math.min(1, (d - min) / range))
    if (t < 0.5) {
      const s = t * 2
      colors.push({ r: 0, g: s, b: 1 - s })
    } else {
      const s = (t - 0.5) * 2
      colors.push({ r: s, g: 1 - s, b: 0 })
    }
  }

  return {
    transformMatrix: result.transform_matrix ?? [],
    deviationValues: devs,
    mappedColors: colors,
    colormapMin: min,
    colormapMax: max,
    noDataIndices: noData,
    outOfRangeIndices: outOfRange,
    pointCount: devs.length,
  }
}
