<script setup lang="ts">
import { computed } from 'vue'
import type { PlcConnectionInfo, PlcDataPoint } from '../types/plc'
import PlcDataPointItem from './PlcDataPointItem.vue'

const props = defineProps<{
  connection: PlcConnectionInfo
  pointData?: Record<string, PlcDataPoint>
  isConnected: boolean
}>()

const isOnline = computed(() => props.isConnected)

const accentColor = computed(() => {
  if (props.connection.name.toLowerCase().includes('siemens'))
    return 'var(--color-accent-siemens)'
  return 'var(--color-accent-omron)'
})

</script>

<template>
  <div class="connection-card" :style="{ '--card-accent': accentColor }">
    <div class="card-header">
      <div class="header-left">
        <div class="status-dot" :class="{ online: isOnline }" />
        <div class="header-info">
          <h3 class="plc-name">{{ connection.name }}</h3>
          <span class="plc-endpoint">{{ connection.endpointUrl }}</span>
        </div>
      </div>
      <div class="header-right">
        <span class="protocol-badge">{{ connection.protocol }}</span>
      </div>
    </div>

    <div class="card-body">
      <div v-if="connection.points.length === 0" class="empty-state">
        未配置监控点位
      </div>
      <div v-else class="points-grid">
        <PlcDataPointItem
          v-for="pt in connection.points"
          :key="pt.id"
          :name="pt.name"
          :tag-path="pt.tagPath"
          :value="pointData?.[pt.tagPath]?.value ?? null"
          :quality="pointData?.[pt.tagPath]?.quality ?? 'Disconnected'"
          :timestamp="pointData?.[pt.tagPath]?.timestamp"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.connection-card {
  background: var(--color-surface-1);
  border: 1px solid var(--color-border);
  border-radius: 12px;
  overflow: hidden;
  transition: border-color 0.3s, box-shadow 0.3s;
}

.connection-card:hover {
  border-color: var(--card-accent);
  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.3);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  background: var(--color-surface-2);
  border-bottom: 1px solid var(--color-border);
}

.header-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.status-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--color-muted);
  transition: background 0.3s, box-shadow 0.3s;
}

.status-dot.online {
  background: var(--color-success);
  box-shadow: 0 0 8px var(--color-success);
}

.header-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.plc-name {
  font-size: 1rem;
  font-weight: 700;
  margin: 0;
  color: var(--color-text);
}

.plc-endpoint {
  font-size: 0.75rem;
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', monospace;
  color: var(--color-muted);
}

.header-right {
  display: flex;
  align-items: center;
}

.protocol-badge {
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--card-accent);
  background: color-mix(in srgb, var(--card-accent) 15%, transparent);
  padding: 4px 10px;
  border-radius: 6px;
  border: 1px solid color-mix(in srgb, var(--card-accent) 30%, transparent);
}

.card-body {
  padding: 16px 20px;
}

.points-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 10px;
}

.empty-state {
  color: var(--color-muted);
  text-align: center;
  padding: 24px;
  font-size: 0.875rem;
}
</style>
