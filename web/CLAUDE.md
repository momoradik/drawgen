# CLAUDE.md — Compute firewall rules for the drawgen UI layer

## The rule (inherited by every session)

This frontend is **DISPLAY and ORCHESTRATION only**.

It NEVER:
- Computes a measurement, transform, deviation, or distance
- Computes a pass/fail or conformance decision
- Maps raw deviation values to colors (the core pre-computes heatmap colors)
- Re-rounds, re-formats, or recomputes any numeric value from the core
- Fabricates a result when the core is unavailable

It ALWAYS:
- Renders values **exactly** as returned by the core at `localhost:8000`
- Shows a clear error when the core is unreachable
- Displays the core's version fingerprint with every result
- Labels the heatmap as "CORROBORATING — not authoritative"

## Stack (do not change)

- React 18 + TypeScript + Vite
- Tailwind CSS (dark theme, `--color-primary` / `--color-accent` CSS vars)
- TanStack React Query for server state
- Axios for HTTP
- react-router-dom for routing
- Three.js for 3D (future)

## Core API

The alignmesh core runs at `http://localhost:8000` with these endpoints:
- `GET /health` — `{"status": "ok"}`
- `GET /version` — version + compiler + CPU + FP flags
- `POST /inspect` — full inspection pipeline, returns pre-computed ResultPackage
