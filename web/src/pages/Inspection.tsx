/**
 * Inspection page — dimensional inspection within the drawgen shell.
 *
 * COMPUTE FIREWALL: this page only DISPLAYS values returned by the
 * alignmesh core. It never computes a measurement, deviation, or verdict.
 */
import { useState, useEffect, useRef } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { alignmeshApi, type InspectionResult, type LandmarkPairData, type RPSPointData } from '../api/alignmesh'
import InspectionViewer, { type OverlayMode } from '../components/viewer/InspectionViewer'
import VerdictPanel from '../components/inspection/VerdictPanel'
import StatisticsPanel from '../components/inspection/StatisticsPanel'
import HistogramPanel from '../components/inspection/HistogramPanel'
import ObservabilityPanel from '../components/inspection/ObservabilityPanel'
import OfficialSeparation from '../components/inspection/OfficialSeparation'
import ProvenancePanel from '../components/inspection/ProvenancePanel'
import ReportPanel from '../components/inspection/ReportPanel'
import ToleranceSetup from '../components/inspection/ToleranceSetup'
import AlignmentSetup from '../components/inspection/AlignmentSetup'
import ImportPanel, { type ImportedPart, cancelPreviewMesh } from '../components/inspection/ImportPanel'
import PartPreview from '../components/inspection/PartPreview'
import ImportStatusBar from '../components/inspection/ImportStatusBar'
import AlignmentOverlay from '../components/inspection/AlignmentOverlay'
import DOFPanel from '../components/inspection/DOFPanel'
import LandmarkPairEntry from '../components/inspection/LandmarkPairEntry'
import RPSPointEntry from '../components/inspection/RPSPointEntry'
import RPSResultPanel from '../components/inspection/RPSResultPanel'

type Tab = 'setup' | 'verdict' | 'analysis' | 'report'

export default function Inspection() {
  const [refPart, setRefPart] = useState<ImportedPart | null>(null)
  const [measPart, setMeasPart] = useState<ImportedPart | null>(null)
  const [tolerance, setTolerance] = useState(0.1)
  const [alignMode, setAlignMode] = useState('coarse-to-fine')
  const [result, setResult] = useState<InspectionResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('setup')
  const [overlayMode, setOverlayMode] = useState<OverlayMode>('overlay')
  const [busy, setBusy] = useState(false)
  const [landmarks, setLandmarks] = useState<LandmarkPairData[]>([])
  const [rpsPoints, setRpsPoints] = useState<RPSPointData[]>([])
  const [angleStep, setAngleStep] = useState(30)
  const [processing, setProcessing] = useState(false)
  const [showRpsNominal, setShowRpsNominal] = useState(true)
  const [showRpsProjected, setShowRpsProjected] = useState(true)

  const { data: health, isError: coreDown } = useQuery({
    queryKey: ['alignmesh-health'],
    queryFn: alignmeshApi.health,
    refetchInterval: busy ? false : processing ? 30000 : 10000,
    retry: 1,
  })

  const { data: coreVersion } = useQuery({
    queryKey: ['alignmesh-version'],
    queryFn: alignmeshApi.version,
    staleTime: Infinity,
    enabled: !!health,
  })

  const inspectMutation = useMutation({
    mutationFn: async () => {
      cancelPreviewMesh() // Free the single-threaded server
      setBusy(true)
      setProcessing(true)
      setError(null)
      return alignmeshApi.inspect({
        reference: refPart!.path,
        measured: measPart!.path,
        tolerance,
        alignment_mode: alignMode,
        landmarks: alignMode === 'landmark' ? landmarks : undefined,
        rps_points: alignMode === 'pre-aligned-rps' ? rpsPoints : undefined,
        angle_step: angleStep,
      })
    },
    onSuccess: (data) => {
      setResult(data)
      setError(null)
      setProcessing(false)
      setBusy(false)
      setTab('verdict')
    },
    onError: (err: Error) => {
      setResult(null)
      setError('Core error: ' + (err.message || 'unknown'))
      setProcessing(false)
      setBusy(false)
    },
  })

  const handleCancel = () => {
    // Can't truly abort the HTTP request without AbortController wired
    // into axios, but we can dismiss the processing screen.
    setProcessing(false)
    setBusy(false)
  }

  const bothImported = !!refPart && !!measPart
  const anyImported = !!refPart || !!measPart

  const tabs: { id: Tab; label: string; needsResult: boolean }[] = [
    { id: 'setup', label: 'Setup', needsResult: false },
    { id: 'verdict', label: 'Verdict', needsResult: true },
    { id: 'analysis', label: 'Analysis', needsResult: true },
    { id: 'report', label: 'Report', needsResult: true },
  ]

  // ═══════════════════════════════════════════════════════════════════
  // RENDER — single tree, processing overlay via CSS (no remount!)
  // The PartPreview stays mounted always — toggling processing just
  // makes it fullscreen. This avoids re-parsing STL files which
  // freezes the browser on large meshes.
  // ═══════════════════════════════════════════════════════════════════
  return (
    <>
    {/* Processing overlay — fullscreen, covers everything, no remount */}
    {processing && (
      <div className="fixed inset-0 z-50 bg-[#060a14] flex flex-col">
        <div className="flex-1 relative">
          <PartPreview reference={refPart} measured={measPart} isProcessing={true} />
          <div className="absolute bottom-0 left-0 right-0 h-20 bg-gradient-to-t from-[#060a14] to-transparent pointer-events-none" />
        </div>
        <div className="shrink-0 px-6 pb-6 pt-2 space-y-4">
          <ProcessingBar />
          <button onClick={handleCancel}
            className="w-full px-5 py-2.5 bg-gray-800/80 hover:bg-gray-700 text-gray-400 hover:text-gray-200 text-sm font-medium rounded-lg transition-colors border border-gray-700/50">
            Cancel
          </button>
        </div>
      </div>
    )}

    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-white">Dimensional Inspection</h2>
        {coreDown ? (
          <span className="px-3 py-1 rounded-full text-xs font-medium bg-red-900 text-red-300 border border-red-700">Core offline</span>
        ) : (
          <span className="px-3 py-1 rounded-full text-xs font-medium bg-green-900 text-green-300 border border-green-700">Core online</span>
        )}
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 bg-gray-900 rounded-lg p-1 w-fit">
        {tabs.map(t => (
          <button
            key={t.id}
            onClick={() => !(t.needsResult && !result) && setTab(t.id)}
            disabled={t.needsResult && !result}
            className={'px-4 py-1.5 rounded-md text-sm transition ' +
              (tab === t.id
                ? 'bg-primary/30 text-primary-300 font-medium'
                : 'text-gray-400 hover:text-gray-200 disabled:text-gray-600 disabled:cursor-not-allowed')}
          >
            {t.label}
          </button>
        ))}
      </div>

      {error && (
        <div className="bg-red-950 border border-red-800 rounded-xl p-4 text-red-300 text-sm">{error}</div>
      )}

      {/* ── SETUP ──────────────────────────────────────────────────── */}
      {tab === 'setup' && (
        <div className="space-y-4">
          {/* Import panels */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <ImportPanel role="reference" onImport={setRefPart} imported={refPart} onBusyChange={setBusy} />
            <ImportPanel role="measured" onImport={setMeasPart} imported={measPart} onBusyChange={setBusy} />
          </div>

          {/* 3D preview */}
          {anyImported && (
            <PartPreview reference={refPart} measured={measPart} isProcessing={false} />
          )}

          {/* Import status */}
          {bothImported && (
            <ImportStatusBar
              result={result}
              refPath={refPart.path}
              measPath={measPart.path}
              refFormat={refPart.displayLabel}
              measFormat={measPart.displayLabel}
            />
          )}

          {/* Tolerance + alignment setup */}
          <ToleranceSetup onToleranceConfirmed={setTolerance} />
          <AlignmentSetup currentMode={alignMode} onModeSelected={setAlignMode}
            angleStep={angleStep} onAngleStepChanged={setAngleStep} />

          {/* Landmark pair entry */}
          {alignMode === 'landmark' && bothImported && (
            <LandmarkPairEntry onPairsChanged={setLandmarks} />
          )}

          {/* RPS point entry */}
          {alignMode === 'pre-aligned-rps' && bothImported && (
            <RPSPointEntry onPointsChanged={setRpsPoints} />
          )}

          {/* Run button */}
          <button
            onClick={() => inspectMutation.mutate()}
            disabled={!bothImported || tolerance <= 0 || inspectMutation.isPending || coreDown
              || (alignMode === 'landmark' && landmarks.length < 3)
              || (alignMode === 'pre-aligned-rps' && rpsPoints.length < 1)}
            className="w-full px-5 py-3 bg-primary/80 hover:bg-primary text-white text-sm font-medium rounded-lg transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
          >
            Run Inspection
          </button>
        </div>
      )}

      {/* ── VERDICT ────────────────────────────────────────────────── */}
      {tab === 'verdict' && result && (
        <div className="space-y-4">
          <VerdictPanel result={result} />
          <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
            <InspectionViewer
              result={result}
              reference={refPart}
              measured={measPart}
              overlayMode={overlayMode}
              rpsPoints={alignMode === 'pre-aligned-rps' ? rpsPoints : undefined}
              rpsProjectedPoints={alignMode === 'pre-aligned-rps' ? result.rps_projected_points : undefined}
              showRpsNominal={showRpsNominal}
              showRpsProjected={showRpsProjected}
            />
          </div>
          <div className="flex items-center gap-3">
            <div className="flex-1">
              <AlignmentOverlay result={result} mode={overlayMode} onModeChange={setOverlayMode} />
            </div>
            {alignMode === 'pre-aligned-rps' && rpsPoints.length > 0 && (
              <div className="flex gap-1.5">
                <button
                  onClick={() => setShowRpsNominal(!showRpsNominal)}
                  className={'px-2.5 py-1.5 rounded-lg text-xs transition border ' +
                    (showRpsNominal
                      ? 'bg-red-900/30 border-red-700/50 text-red-400'
                      : 'bg-gray-800 border-gray-700 text-gray-500')}
                >
                  {showRpsNominal ? 'Hide' : 'Show'} Nominal
                </button>
                <button
                  onClick={() => setShowRpsProjected(!showRpsProjected)}
                  className={'px-2.5 py-1.5 rounded-lg text-xs transition border ' +
                    (showRpsProjected
                      ? 'bg-blue-900/30 border-blue-700/50 text-blue-400'
                      : 'bg-gray-800 border-gray-700 text-gray-500')}
                >
                  {showRpsProjected ? 'Hide' : 'Show'} Projected
                </button>
              </div>
            )}
          </div>
          <DOFPanel result={result} />
          {result.alignment_mode === 'pre-aligned-rps' && result.rps_result && (
            <RPSResultPanel result={result} />
          )}
          <OfficialSeparation result={result} />
        </div>
      )}

      {/* ── ANALYSIS ───────────────────────────────────────────────── */}
      {tab === 'analysis' && result && (
        <div className="space-y-4">
          <StatisticsPanel result={result} />
          <HistogramPanel result={result} />
          <ObservabilityPanel result={result} />
          <ProvenancePanel result={result} />
        </div>
      )}

      {/* ── REPORT ─────────────────────────────────────────────────── */}
      {tab === 'report' && result && (
        <ReportPanel result={result} />
      )}

      {coreVersion && (
        <div className="text-xs text-gray-600 text-right">
          {coreVersion.version + ' | ' + coreVersion.fp_flags}
        </div>
      )}
    </div>
    </>
  )
}

// ═══════════════════════════════════════════════════════════════════
// Processing progress bar
// ═══════════════════════════════════════════════════════════════════

const STAGES = [
  { name: 'Importing reference (STEP can take minutes)', duration: 270 },
  { name: 'Importing measured mesh', duration: 10 },
  { name: 'Validating + downsampling', duration: 5 },
  { name: 'Aligning (coarse)', duration: 5 },
  { name: 'Aligning (fine ICP)', duration: 5 },
  { name: 'RPS coupling loop', duration: 5 },
  { name: 'Computing deviations', duration: 25 },
  { name: 'Analyzing results', duration: 5 },
]
const TOTAL_EST = STAGES.reduce((s, st) => s + st.duration, 0)

function ProcessingBar() {
  const [elapsed, setElapsed] = useState(0)
  const startRef = useRef(Date.now())

  useEffect(() => {
    startRef.current = Date.now()
    const id = setInterval(() => setElapsed(Math.floor((Date.now() - startRef.current) / 1000)), 200)
    return () => clearInterval(id)
  }, [])

  const progress = Math.min(0.95, elapsed / TOTAL_EST)
  const pct = Math.round(progress * 100)
  const remaining = Math.max(0, TOTAL_EST - elapsed)

  let cumulative = 0
  let currentStage = STAGES[STAGES.length - 1].name
  for (const st of STAGES) {
    cumulative += st.duration
    if (elapsed < cumulative) { currentStage = st.name; break }
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-5 h-5 border-2 border-blue-500/30 border-t-blue-400 rounded-full animate-spin" />
          <span className="text-gray-200 text-sm font-medium">{currentStage}...</span>
        </div>
        <span className="text-gray-500 text-xs font-mono">{elapsed}s</span>
      </div>
      <div className="w-full bg-gray-800/60 rounded-full h-2.5 overflow-hidden backdrop-blur-sm">
        <div
          className="h-full rounded-full transition-all duration-500 ease-out"
          style={{
            width: pct + '%',
            background: 'linear-gradient(90deg, #3b82f6, #8b5cf6, #06b6d4)',
            boxShadow: '0 0 20px rgba(59,130,246,0.5), 0 0 40px rgba(139,92,246,0.3)',
          }}
        />
      </div>
      <div className="flex items-center justify-between text-xs">
        <span className="text-gray-400">{pct}%</span>
        <span className="text-gray-500">
          {remaining > 0 ? '~' + remaining + 's remaining' : 'Finishing up...'}
        </span>
      </div>
    </div>
  )
}
