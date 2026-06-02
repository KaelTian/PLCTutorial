import type { PlcConnectionInfo } from '../types/plc'

const API_BASE = '/api'

export function usePlcApi() {
  async function getConnections(): Promise<PlcConnectionInfo[]> {
    const res = await fetch(`${API_BASE}/connections`)
    if (!res.ok) throw new Error(`Failed to fetch connections: ${res.statusText}`)
    return res.json()
  }

  async function readPlc(name: string) {
    const res = await fetch(`${API_BASE}/connections/${encodeURIComponent(name)}/read`)
    if (!res.ok) throw new Error(`Failed to read PLC: ${res.statusText}`)
    return res.json()
  }

  return { getConnections, readPlc }
}
