<script setup lang="ts">
import { ref, onMounted, onUnmounted, reactive } from 'vue'
import type { PlcConnectionInfo, PlcDataPoint, DataChangedPayload, ConnectionStatePayload } from '../types/plc'
import { usePlcApi } from '../composables/usePlcApi'
import { useSignalR } from '../composables/useSignalR'
import PlcConnectionCard from './PlcConnectionCard.vue'

const connections = ref<PlcConnectionInfo[]>([])
const pointData = reactive<Record<string, Record<string, PlcDataPoint>>>({})
const connectedStates = reactive<Record<string, boolean>>({})
const loading = ref(true)
const error = ref<string | null>(null)

const api = usePlcApi()
const signalR = useSignalR()

onMounted(async () => {
  try {
    connections.value = await api.getConnections()

    // 初始读取所有 PLC 的当前值
    for (const conn of connections.value) {
      try {
        const data = await api.readPlc(conn.name)
        if (!pointData[conn.name]) pointData[conn.name] = {}
        for (const point of data) {
          pointData[conn.name][point.tagPath] = point
        }
      } catch {
        // PLC 可能离线，跳过初始读取
      }
    }

    await signalR.start()

    for (const conn of connections.value) {
      await signalR.joinPlcGroup(conn.name)
    }

    signalR.onDataChanged((payload: DataChangedPayload) => {
      if (!pointData[payload.readerName]) pointData[payload.readerName] = {}
      for (const point of payload.points) {
        pointData[payload.readerName][point.tagPath] = point
      }
    })

    signalR.onConnectionStateChanged((payload: ConnectionStatePayload) => {
      connectedStates[payload.readerName] = payload.isConnected
    })
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
})

onUnmounted(() => {
  signalR.stop()
})
</script>

<template>
  <div class="dashboard">
    <header class="dashboard-header">
      <div class="header-content">
        <div class="brand">
          <span class="brand-icon">⚡</span>
          <h1>PLC 监控面板</h1>
        </div>
        <div class="header-status">
          <span class="signalr-status" :class="{ online: signalR.isConnected.value }">
            <span class="status-indicator" />
            {{ signalR.isConnected.value ? '实时连接' : '连接中…' }}
          </span>
        </div>
      </div>
    </header>

    <main class="dashboard-main">
      <div v-if="loading" class="state-container">
        <div class="loading-spinner" />
        <p>加载 PLC 配置…</p>
      </div>

      <div v-else-if="error" class="state-container error">
        <p>⚠ {{ error }}</p>
      </div>

      <div v-else-if="connections.length === 0" class="state-container">
        <p>未配置 PLC 连接，请检查 appsettings.json</p>
      </div>

      <div v-else class="connections-grid">
        <PlcConnectionCard
          v-for="conn in connections"
          :key="conn.name"
          :connection="conn"
          :point-data="pointData[conn.name]"
          :is-connected="connectedStates[conn.name] ?? conn.isConnected"
        />
      </div>
    </main>
  </div>
</template>

<style scoped>
.dashboard {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.dashboard-header {
  background: var(--color-surface-2);
  border-bottom: 1px solid var(--color-border);
  position: sticky;
  top: 0;
  z-index: 10;
}

.header-content {
  max-width: 1400px;
  margin: 0 auto;
  padding: 16px 24px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
}

.brand-icon {
  font-size: 1.5rem;
}

.brand h1 {
  font-size: 1.25rem;
  font-weight: 800;
  margin: 0;
  background: linear-gradient(135deg, var(--color-accent-siemens), var(--color-accent-omron));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.header-status {
  display: flex;
  align-items: center;
}

.signalr-status {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-muted);
  padding: 6px 12px;
  border-radius: 20px;
  background: var(--color-surface-1);
  border: 1px solid var(--color-border);
}

.signalr-status.online {
  color: var(--color-success);
  border-color: color-mix(in srgb, var(--color-success) 30%, transparent);
}

.status-indicator {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.signalr-status.online .status-indicator {
  animation: pulse 2s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

.dashboard-main {
  flex: 1;
  max-width: 1400px;
  width: 100%;
  margin: 0 auto;
  padding: 24px;
}

.connections-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(480px, 1fr));
  gap: 20px;
}

.state-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 300px;
  gap: 16px;
  color: var(--color-muted);
}

.state-container.error {
  color: var(--color-danger);
}

.loading-spinner {
  width: 32px;
  height: 32px;
  border: 3px solid var(--color-border);
  border-top-color: var(--color-accent);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
