import { useState, useEffect, useCallback } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { resinMaterialsApi } from '../api/client'
import type { ResinMaterial } from '../types'

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

function Txt({ label, value, onChange, placeholder }: {
  label: string; value: string; onChange: (v: string) => void; placeholder?: string
}) {
  return (
    <label className="flex items-center justify-between gap-2 text-[11px]">
      <span className="text-gray-500 flex-shrink-0">{label}</span>
      <input value={value} onChange={e => onChange(e.target.value)} placeholder={placeholder}
        className="w-24 bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[11px] text-gray-200" />
    </label>
  )
}

function SectionHeader({ title, open, onToggle }: { title: string; open: boolean; onToggle: () => void }) {
  return (
    <button onClick={onToggle}
      className="flex items-center justify-between w-full text-[10px] font-semibold text-violet-400 uppercase tracking-wider py-1.5 hover:text-violet-300 transition">
      {title}
      <svg className={`w-3 h-3 transition-transform ${open ? 'rotate-180' : ''}`} fill="currentColor" viewBox="0 0 20 20">
        <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
      </svg>
    </button>
  )
}

const CATEGORIES = ['Standard', 'Tough', 'Flexible', 'Castable', 'Dental', 'Engineering', 'Water-Washable', 'ABS-Like', 'Other']

const DEFAULTS: Partial<ResinMaterial> = {
  category: 'Standard', normalExposureMs: 2500, bottomExposureMs: 30000,
  bottomLayerCount: 5, lightOffDelayMs: 0, liftDistanceMm: 0, liftSpeedMmPerMin: 0,
  retractSpeedMmPerMin: 0, densityGPerCm3: 1.1, viscosityCps: 0, wavelengthNm: 405, shrinkagePct: 0,
}

interface Props { onMaterialChange?: (mat: ResinMaterial | null) => void }

export default function MaterialProfilePanel({ onMaterialChange }: Props) {
  const qc = useQueryClient()
  const { data: materials = [] } = useQuery({ queryKey: ['resin-materials'], queryFn: resinMaterialsApi.getAll })

  const [activeId, setActiveId] = useState<string | null>(null)
  const [draft, setDraft] = useState<Partial<ResinMaterial>>({ ...DEFAULTS })
  const [dirty, setDirty] = useState(false)
  const [renaming, setRenaming] = useState(false)
  const [renameName, setRenameName] = useState('')
  const [sections, setSections] = useState({ exposure: true, motion: false, physical: false })
  const toggleSec = (k: keyof typeof sections) => setSections(p => ({ ...p, [k]: !p[k] }))

  const active = materials.find(m => m.id === activeId) ?? null

  useEffect(() => {
    if (active) { setDraft({ ...active }); setDirty(false); onMaterialChange?.(active) }
    else { setDraft({ ...DEFAULTS }); onMaterialChange?.(null) }
  }, [activeId, active?.id])

  useEffect(() => {
    if (activeId && !materials.find(m => m.id === activeId))
      setActiveId(materials.length > 0 ? materials[0].id : null)
  }, [materials, activeId])

  const patch = useCallback((f: string, v: any) => { setDraft(p => ({ ...p, [f]: v })); setDirty(true) }, [])

  const createMut = useMutation({
    mutationFn: (d: Partial<ResinMaterial>) => resinMaterialsApi.create(d),
    onSuccess: c => { qc.invalidateQueries({ queryKey: ['resin-materials'] }); setActiveId(c.id); setDirty(false) },
  })
  const updateMut = useMutation({
    mutationFn: ({ id, d }: { id: string; d: Partial<ResinMaterial> }) => resinMaterialsApi.update(id, d),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['resin-materials'] }); setDirty(false) },
  })
  const deleteMut = useMutation({
    mutationFn: (id: string) => resinMaterialsApi.delete(id),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['resin-materials'] }); setActiveId(null) },
  })
  const dupMut = useMutation({
    mutationFn: (id: string) => resinMaterialsApi.duplicate(id),
    onSuccess: c => { qc.invalidateQueries({ queryKey: ['resin-materials'] }); setActiveId(c.id) },
  })

  const handleSave = () => {
    if (!draft.name?.trim()) return
    if (activeId && active) updateMut.mutate({ id: activeId, d: draft })
    else createMut.mutate(draft)
  }

  const handleNew = () => { setActiveId(null); setDraft({ ...DEFAULTS, name: 'New Resin' }); setDirty(true) }

  return (
    <div className="flex flex-col h-full">
      {/* Selector */}
      <div className="p-3 border-b border-gray-800">
        <div className="flex items-center gap-2 mb-2">
          <select value={activeId ?? ''} onChange={e => setActiveId(e.target.value || null)}
            className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-2 py-1.5 text-xs text-gray-200 truncate">
            <option value="">-- Select Material --</option>
            {materials.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
          </select>
          <button onClick={handleNew} className="text-xs px-2 py-1.5 rounded-lg bg-violet-600 hover:bg-violet-500 text-white transition">+</button>
        </div>
        {active && (
          <div className="flex items-center gap-1">
            {renaming ? (
              <input autoFocus value={renameName} onChange={e => setRenameName(e.target.value)}
                onBlur={() => { patch('name', renameName); setRenaming(false) }}
                onKeyDown={e => { if (e.key === 'Enter') { patch('name', renameName); setRenaming(false) } if (e.key === 'Escape') setRenaming(false) }}
                className="flex-1 bg-gray-800 border border-violet-500 rounded px-2 py-0.5 text-xs text-gray-200" />
            ) : (
              <span onClick={() => { setRenaming(true); setRenameName(active.name) }}
                className="flex-1 text-xs text-gray-300 cursor-pointer hover:text-violet-400 truncate transition">
                {draft.name || active.name}
              </span>
            )}
            <button onClick={() => dupMut.mutate(active.id)} className="text-[10px] px-1.5 py-0.5 rounded bg-gray-800 hover:bg-gray-700 text-gray-400 transition">Dup</button>
            <button onClick={() => { if (confirm(`Delete "${active.name}"?`)) deleteMut.mutate(active.id) }}
              className="text-[10px] px-1.5 py-0.5 rounded bg-red-900/30 hover:bg-red-900/50 text-red-400 transition">Del</button>
          </div>
        )}
      </div>

      {/* Form */}
      <div className="flex-1 overflow-y-auto p-3 space-y-2">
        {/* Identity */}
        <div className="space-y-1.5 pb-2">
          <Txt label="Name" value={draft.name ?? ''} onChange={v => patch('name', v)} placeholder="Resin name" />
          <label className="flex items-center justify-between gap-2 text-[11px]">
            <span className="text-gray-500">Category</span>
            <select value={draft.category ?? 'Standard'} onChange={e => patch('category', e.target.value)}
              className="bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[11px] text-gray-200">
              {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </label>
          <Txt label="Manufacturer" value={draft.manufacturer ?? ''} onChange={v => patch('manufacturer', v)} />
          <Txt label="Color" value={draft.colorHex ?? ''} onChange={v => patch('colorHex', v)} placeholder="#FF6600" />
        </div>

        {/* Exposure */}
        <SectionHeader title="Exposure Settings" open={sections.exposure} onToggle={() => toggleSec('exposure')} />
        {sections.exposure && (
          <div className="space-y-1.5 pb-2">
            <Num label="Normal Exposure" value={draft.normalExposureMs ?? 2500} onChange={v => patch('normalExposureMs', v)} step={100} min={100} unit="ms" />
            <Num label="Bottom Exposure" value={draft.bottomExposureMs ?? 30000} onChange={v => patch('bottomExposureMs', v)} step={1000} min={1000} unit="ms" />
            <Num label="Bottom Layers" value={draft.bottomLayerCount ?? 5} onChange={v => patch('bottomLayerCount', Math.round(v))} step={1} min={1} />
            <Num label="Light-Off Delay" value={draft.lightOffDelayMs ?? 0} onChange={v => patch('lightOffDelayMs', v)} step={100} min={0} unit="ms" />
          </div>
        )}

        {/* Motion overrides */}
        <SectionHeader title="Motion Overrides" open={sections.motion} onToggle={() => toggleSec('motion')} />
        {sections.motion && (
          <div className="space-y-1.5 pb-2">
            <p className="text-[9px] text-gray-600 mb-1">0 = use printer default</p>
            <Num label="Lift Distance" value={draft.liftDistanceMm ?? 0} onChange={v => patch('liftDistanceMm', v)} step={0.5} min={0} unit="mm" />
            <Num label="Lift Speed" value={draft.liftSpeedMmPerMin ?? 0} onChange={v => patch('liftSpeedMmPerMin', v)} step={10} min={0} unit="mm/m" />
            <Num label="Retract Speed" value={draft.retractSpeedMmPerMin ?? 0} onChange={v => patch('retractSpeedMmPerMin', v)} step={10} min={0} unit="mm/m" />
          </div>
        )}

        {/* Physical properties */}
        <SectionHeader title="Physical Properties" open={sections.physical} onToggle={() => toggleSec('physical')} />
        {sections.physical && (
          <div className="space-y-1.5 pb-2">
            <Num label="Density" value={draft.densityGPerCm3 ?? 1.1} onChange={v => patch('densityGPerCm3', v)} step={0.01} min={0.1} unit="g/cm3" />
            <Num label="Viscosity" value={draft.viscosityCps ?? 0} onChange={v => patch('viscosityCps', v)} step={10} min={0} unit="cps" />
            <Num label="Wavelength" value={draft.wavelengthNm ?? 405} onChange={v => patch('wavelengthNm', Math.round(v))} step={1} min={350} max={500} unit="nm" />
            <Num label="Shrinkage" value={draft.shrinkagePct ?? 0} onChange={v => patch('shrinkagePct', v)} step={0.1} min={0} max={10} unit="%" />
          </div>
        )}

        {/* Notes */}
        <label className="block text-[11px]">
          <span className="text-gray-500">Notes</span>
          <textarea value={draft.notes ?? ''} onChange={e => patch('notes', e.target.value)}
            rows={2} placeholder="Resin notes..."
            className="w-full mt-1 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-[11px] text-gray-200 resize-none" />
        </label>
      </div>

      {/* Save */}
      <div className="p-3 border-t border-gray-800">
        <button onClick={handleSave} disabled={!dirty && !!activeId}
          className={`w-full text-xs py-2 rounded-lg font-medium transition ${
            dirty || !activeId ? 'bg-violet-600 hover:bg-violet-500 text-white' : 'bg-gray-800 text-gray-500 cursor-not-allowed'
          }`}>
          {activeId ? (dirty ? 'Save Changes' : 'Saved') : 'Create Material'}
        </button>
      </div>
    </div>
  )
}
