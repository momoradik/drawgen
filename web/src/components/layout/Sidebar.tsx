import { NavLink } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { brandingApi } from '../../api/client'

const allNavItems = [
  { to: '/dashboard',         label: 'Dashboard',      icon: '⬛', devOnly: false },
  { to: '/import',            label: 'Import STL',     icon: '📁', devOnly: false },
  { to: '/printer-config',    label: 'Printer Config',  icon: '🖨', devOnly: false },
]

const navItems = allNavItems.filter(n => !n.devOnly || import.meta.env.DEV)

export default function Sidebar() {
  const { data: branding } = useQuery({
    queryKey: ['branding'],
    queryFn: brandingApi.get,
    staleTime: Infinity,
  })

  return (
    <nav className="w-56 bg-gray-900 border-r border-gray-800 flex flex-col shrink-0">
      <div className="px-4 py-4 border-b border-gray-800 flex items-center gap-3">
        <img src="/logo.png" alt="" className="w-8 h-8 rounded-md" />
        <div>
          <h1 className="text-lg font-bold text-primary-400 truncate leading-tight">
            {branding?.appTitle ?? 'Fabrium'}
          </h1>
          {branding?.companyName && (
            <p className="text-xs text-gray-500">{branding.companyName}</p>
          )}
        </div>
      </div>

      <ul className="flex-1 py-3 space-y-0.5 overflow-y-auto">
        {navItems.map(({ to, label, icon }) => (  // devOnly already filtered out
          <li key={to}>
            <NavLink
              to={to}
              className={({ isActive }) =>
                `flex items-center gap-3 px-4 py-2.5 text-sm transition-colors rounded-md mx-2 ` +
                (isActive
                  ? 'bg-primary/20 text-primary-300 font-medium'
                  : 'text-gray-400 hover:text-gray-200 hover:bg-gray-800')
              }
            >
              <span>{icon}</span>
              {label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
