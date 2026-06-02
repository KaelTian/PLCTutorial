export interface PlcPointInfo {
  id: string
  tagPath: string
  name: string
  description: string | null
}

export interface PlcConnectionInfo {
  name: string
  protocol: string
  endpointUrl: string
  isConnected: boolean
  points: PlcPointInfo[]
}

export interface PlcDataPoint {
  tagPath: string
  value: unknown
  quality: string
  timestamp: string
}

export interface DataChangedPayload {
  readerName: string
  protocol: string
  timestamp: string
  points: PlcDataPoint[]
}

export interface ConnectionStatePayload {
  readerName: string
  protocol: string
  isConnected: boolean
  timestamp: string
}
