import { ref, onMounted, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { DataChangedPayload, ConnectionStatePayload } from '../types/plc'

export function useSignalR() {
  const connection = ref<signalR.HubConnection | null>(null)
  const isConnected = ref(false)

  const dataChangeHandlers = new Set<(payload: DataChangedPayload) => void>()
  const connectionStateHandlers = new Set<(payload: ConnectionStatePayload) => void>()

  function onDataChanged(handler: (payload: DataChangedPayload) => void) {
    dataChangeHandlers.add(handler)
    return () => dataChangeHandlers.delete(handler)
  }

  function onConnectionStateChanged(handler: (payload: ConnectionStatePayload) => void) {
    connectionStateHandlers.add(handler)
    return () => connectionStateHandlers.delete(handler)
  }

  async function joinPlcGroup(readerName: string) {
    if (connection.value?.state === signalR.HubConnectionState.Connected) {
      await connection.value.invoke('JoinPlcGroup', readerName)
    }
  }

  async function start() {
    const hub = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/plc')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    hub.on('DataChanged', (payload: DataChangedPayload) => {
      dataChangeHandlers.forEach(fn => fn(payload))
    })

    hub.on('ConnectionStateChanged', (payload: ConnectionStatePayload) => {
      connectionStateHandlers.forEach(fn => fn(payload))
    })

    hub.onreconnecting(() => {
      isConnected.value = false
    })

    hub.onreconnected(() => {
      isConnected.value = true
    })

    hub.onclose(() => {
      isConnected.value = false
    })

    connection.value = hub
    await hub.start()
    isConnected.value = true
  }

  async function stop() {
    if (connection.value) {
      await connection.value.stop()
      connection.value = null
      isConnected.value = false
    }
  }

  return {
    isConnected,
    start,
    stop,
    joinPlcGroup,
    onDataChanged,
    onConnectionStateChanged,
  }
}
