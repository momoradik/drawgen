import { useState, useEffect, useCallback } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { resinPrintProfilesApi } from '../api/client'
import type { ResinPrintProfile } from '../types'

// ── Tiny reusable field components ────────────────────────────────────────────

function Num({ label, value, onChange, step, min, max, unit }: {
  label: string; value: number; onChange: (v: number) => void
  step?: number; min?: number; max?: number; unit?: string
}) {
  return (
    <label className="flex items-center justify-between gap-2 text-[11px]">
      <span className="text-gray-500 flex-shrink-0">{label}</span>
      <div className="flex items-center gap-1">
        <input type="number" value={value} step={step ?? 0.01} min={min} max={max}
          onChange={e => onChange(+e.target.value)}
          className="w-16 bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[11px] text-gray-200 text-right" />
        {unit && <span className="text-gray-600 text-[9px] w-8">{unit}</span>}
      </div>
    </label>
  )
}

function Sel({ label, value, onChange, options }: {
  label: string; value: string; onChange: (v: string) => void; options: { v: string; l: string }[]
}) {
  return (
    <label className="flex items-center justify-between gap-2 text-[11px]">
      <span className="text-gray-500">{label}</span>
      <select value={value} onChange={e => onChange(e.target.value)}
        className="bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[11px] text-gray-200">
        {options.map(o => <option key={o.v} value={o.v}>{o.l}</option>)}
      </select>
    </label>
  )
}

function Chk({ label, checked, onChange }: {
  label: string; checked: boolean; onChange: (v: boolean) => void
}) {
  return (
    <label className="flex items-center gap-2 text-[11px] cursor-pointer">
      <input type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)}
        className="rounded border-gray-600 bg-gray-800 w-3 h-3 text-indigo-500" />
      <span className="text-gray-400">{label}</span>
    </label>
  )
}

function SectionHeader({ title, open, onToggle }: { title: string; open: boolean; onToggle: () => void }) {
  return (
    <button onClick={onToggle}
      className="flex items-center justify-between w-full text-[10px] font-semibold text-indigo-400 uppercase tracking-wider py-1.5 hover:text-indigo-300 transition">
      {title}
      <svg className={`w-3 h-3 transition-transform ${open ? 'rotate-180' : ''}`} fill="currentColor" viewBox="0 0 20 20">
        <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
      </svg>
    </button>
  )
}

// ── Default values ────────────────────────────────────────────────────────────

const DEFAULTS: Partial<ResinPrintProfile> = {
  layerHeightMm: 0.05, antiAliasing: 'None',
  supportEnabled: false, supportType: 'normal', supportPlacement: 'buildplate',
  supportDensity: 0.5, supportPattern: 'default', supportOverhangAngleDeg: 45,
  supportXYDistanceMm: 0.3, supportZDistanceMm: 0.15,
  supportInterfaceEnabled: true, supportInterfaceDensity: 0.8,
  supportRoofEnabled: true, supportFloorEnabled: false,
  hollowingEnabled: false, hollowWallThicknessMm: 1.5,
  drainHoleDiameterMm: 2.5, drainHoleDepthMm: 5.0,
}

// ── Callback prop: when active profile changes, parent gets the data ──────────

interface Props {
  onProfileChange?: (profile: ResinPrintProfile | null) => void
}

// ── Main Component ────────────────────────────────────────────────────────────

export default function PrintProfilePanel({ onProfileChange }: Props) {
  const qc = useQueryClient()
  const { data: profiles = [] } = useQuery({
    queryKey: ['resin-print-profiles'],
    queryFn: resinPrintProfilesApi.getAll,
  })

  const [activeId, setActiveId] = useState<string | null>(null)
  const [draft, setDraft] = useState<Partial<ResinPrintProfile>>({ ...DEFAULTS })
  const [dirty, setDirty] = useState(false)
  const [renaming, setRenaming] = useState(false)
  const [renameName, setRenameName] = useState('')

  // Collapsible sections
  const [openSections, setOpenSections] = useState({ quality: true, support: true, hollow: false, drain: false })
  const toggleSection = (key: keyof typeof openSections) =>
    setOpenSections(prev => ({ ...prev, [key]: !prev[key] }))

  const active = profiles.find(p => p.id === activeId) ?? null

  // Sync draft when active changes
  useEffect(() => {
    if (active) {
      setDraft({ ...active })
      setDirty(false)
      onProfileChange?.(active)
    } else {
      setDraft({ ...DEFAULTS })
      onProfileChange?.(null)
    }
  }, [activeId, active?.id])

  // If active profile is deleted externally, fall back
  useEffect(() => {
    if (activeId && !profiles.find(p => p.id === activeId)) {
      setActiveId(profiles.length > 0 ? profiles[0].id : null)
    }
  }, [profiles, activeId])

  const patch = useCallback((field: string, value: any) => {
    setDraft(prev => ({ ...prev, [field]: value }))
    setDirty(true)
  }, [])

  // ── Mutations ───────────────────────────────────────────────────────────────

  const createMut = useMutation({
    mutationFn: (data: Partial<ResinPrintProfile>) => resinPrintProfilesApi.create(data),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ['resin-print-profiles'] })
      setActiveId(created.id); setDirty(false)
    },
  })

  const updateMut = useMutation({
    mutationFn: ({ id, data }: { id: string; data: Partial<ResinPrintProfile> }) =>
      resinPrintProfilesApi.update(id, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['resin-print-profiles'] })
      setDirty(false)
    },
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => resinPrintProfilesApi.delete(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['resin-print-profiles'] })
      setActiveId(null)
    },
  })

  const dupMut = useMutation({
    mutationFn: (id: string) => resinPrintProfilesApi.duplicate(id),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ['resin-print-profiles'] })
      setActiveId(created.id)
    },
  })

  const handleSave = () => {
    if (!draft.name?.trim()) return
    if (activeId && active) {
      updateMut.mutate({ id: activeId, data: draft })
    } else {
      createMut.mutate(draft)
    }
  }

  const handleNew = () => {
    setActiveId(null)
    setDraft({ ...DEFAULTS, name: 'New Print Profile' })
    setDirty(true)
  }

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col h-full">
      {/* Profile selector */}
      <div className="p-3 border-b border-gray-800">
        <div className="flex items-center gap-2 mb-2">
          <select value={activeId ?? ''} onChange={e => setActiveId(e.target.value || null)}
            className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-2 py-1.5 text-xs text-gray-200 truncate">
            <option value="">-- Select Profile --</option>
            {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          <button onClick={handleNew} title="New profile"
            className="text-xs px-2 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white transition">+</button>
        </div>

        {/* Profile actions */}
        {active && (
          <div className="flex items-center gap-1">
            {renaming ? (
              <input autoFocus value={renameName}
                onChange={e => setRenameName(e.target.value)}
                onBlur={() => { patch('name', renameName); setRenaming(false) }}
                onKeyDown={e => { if (e.key === 'Enter') { patch('name', renameName); setRenaming(false) } if (e.key === 'Escape') setRenaming(false) }}
                className="flex-1 bg-gray-800 border border-indigo-500 rounded px-2 py-0.5 text-xs text-gray-200" />
            ) : (
              <span onClick={() => { setRenaming(true); setRenameName(active.name) }}
                className="flex-1 text-xs text-gray-300 cursor-pointer hover:text-indigo-400 truncate transition" title="Click to rename">
                {draft.name || active.name}
              </span>
            )}
            <button onClick={() => dupMut.mutate(active.id)} title="Duplicate"
              className="text-[10px] px-1.5 py-0.5 rounded bg-gray-800 hover:bg-gray-700 text-gray-400 transition">Dup</button>
            <button onClick={() => { if (confirm(`Delete "${active.name}"?`)) deleteMut.mutate(active.id) }} title="Delete"
              className="text-[10px] px-1.5 py-0.5 rounded bg-red-900/30 hover:bg-red-900/50 text-red-400 transition">Del</button>
          </div>
        )}
      </div>

      {/* Settings form */}
      <div className="flex-1 overflow-y-auto p-3 space-y-2">

        {/* Quality */}
        <SectionHeader title="Quality" open={openSections.quality} onToggle={() => toggleSection('quality')} />
        {openSections.quality && (
          <div className="space-y-1.5 pb-2">
            <Num label="Layer Height" value={draft.layerHeightMm ?? 0.05} onChange={v => patch('layerHeightMm', v)} step={0.01} min={0.01} max={0.3} unit="mm" />
            <Sel label="Anti-Aliasing" value={draft.antiAliasing ?? 'None'} onChange={v => patch('antiAliasing', v)}
              options={[{ v: 'None', l: 'None' }, { v: 'X2', l: '2x' }, { v: 'X4', l: '4x' }, { v: 'X8', l: '8x' }]} />
          </div>
        )}

        {/* Support */}
        <SectionHeader title="Supports" open={openSections.support} onToggle={() => toggleSection('support')} />
        {openSections.support && (
          <div className="space-y-1.5 pb-2">
            <Chk label="Enable Supports" checked={draft.supportEnabled ?? false} onChange={v => patch('supportEnabled', v)} />
            {draft.supportEnabled && (
              <>
                <Sel label="Type" value={draft.supportType ?? 'normal'} onChange={v => patch('supportType', v)}
                  options={[{ v: 'normal', l: 'Normal' }, { v: 'tree', l: 'Tree' }]} />
                <Sel label="Placement" value={draft.supportPlacement ?? 'buildplate'} onChange={v => patch('supportPlacement', v)}
                  options={[{ v: 'buildplate', l: 'Build Plate Only' }, { v: 'everywhere', l: 'Everywhere' }]} />
                <Num label="Density" value={draft.supportDensity ?? 0.5} onChange={v => patch('supportDensity', v)} step={0.05} min={0.05} max={1} />
                <Num label="Overhang Angle" value={draft.supportOverhangAngleDeg ?? 45} onChange={v => patch('supportOverhangAngleDeg', v)} step={1} min={0} max={90} unit="deg" />
                <Num label="XY Distance" value={draft.supportXYDistanceMm ?? 0.3} onChange={v => patch('supportXYDistanceMm', v)} step={0.05} min={0} unit="mm" />
                <Num label="Z Distance" value={draft.supportZDistanceMm ?? 0.15} onChange={v => patch('supportZDistanceMm', v)} step={0.05} min={0} unit="mm" />
                <Chk label="Interface" checked={draft.supportInterfaceEnabled ?? true} onChange={v => patch('supportInterfaceEnabled', v)} />
                {draft.supportInterfaceEnabled && (
                  <Num label="Interface Density" value={draft.supportInterfaceDensity ?? 0.8} onChange={v => patch('supportInterfaceDensity', v)} step={0.05} min={0} max={1} />
                )}
                <Chk label="Roof" checked={draft.supportRoofEnabled ?? true} onChange={v => patch('supportRoofEnabled', v)} />
                <Chk label="Floor" checked={draft.supportFloorEnabled ?? false} onChange={v => patch('supportFloorEnabled', v)} />
              </>
            )}
          </div>
        )}

        {/* Hollowing */}
        <SectionHeader title="Hollowing" open={openSections.hollow} onToggle={() => toggleSection('hollow')} />
        {openSections.hollow && (
          <div className="space-y-1.5 pb-2">
            <Chk label="Enable Hollowing" checked={draft.hollowingEnabled ?? false} onChange={v => patch('hollowingEnabled', v)} />
            {draft.hollowingEnabled && (
              <Num label="Wall Thickness" value={draft.hollowWallThicknessMm ?? 1.5} onChange={v => patch('hollowWallThicknessMm', v)} step={0.1} min={0.3} unit="mm" />
            )}
          </div>
        )}

        {/* Drain Holes */}
        <SectionHeader title="Drain Holes" open={openSections.drain} onToggle={() => toggleSection('drain')} />
        {openSections.drain && (
          <div className="space-y-1.5 pb-2">
            <Num label="Diameter" value={draft.drainHoleDiameterMm ?? 2.5} onChange={v => patch('drainHoleDiameterMm', v)} step={0.1} min={0.5} unit="mm" />
            <Num label="Depth" value={draft.drainHoleDepthMm ?? 5.0} onChange={v => patch('drainHoleDepthMm', v)} step={0.5} min={1} unit="mm" />
          </div>
        )}
      </div>

      {/* Save button */}
      <div className="p-3 border-t border-gray-800">
        <button onClick={handleSave}
          disabled={!dirty && !!activeId}
          className={`w-full text-xs py-2 rounded-lg font-medium transition ${
            dirty || !activeId
              ? 'bg-indigo-600 hover:bg-indigo-500 text-white'
              : 'bg-gray-800 text-gray-500 cursor-not-allowed'
          }`}>
          {activeId ? (dirty ? 'Save Changes' : 'Saved') : 'Create Profile'}
        </button>
      </div>
    </div>
  )
}
