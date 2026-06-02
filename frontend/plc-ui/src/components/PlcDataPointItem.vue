<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  name: string
  tagPath: string
  value: unknown
  quality: string
  timestamp?: string
}>()

const qualityColor = computed(() => {
  switch (props.quality) {
    case 'Good': return 'var(--color-success)'
    case 'Uncertain': return 'var(--color-warning)'
    case 'Bad': return 'var(--color-danger)'
    default: return 'var(--color-muted)'
  }
})

const displayValue = computed(() => {
  if (props.value === null || props.value === undefined) return '—'
  if (typeof props.value === 'number') {
    if (Number.isInteger(props.value)) return props.value.toString()
    return props.value.toFixed(4).replace(/\.?0+$/, '')
  }
  return String(props.value)
})

const timeAgo = computed(() => {
  if (!props.timestamp) return ''
  const diff = Date.now() - new Date(props.timestamp).getTime()
  if (diff < 1000) return '刚刚'
  if (diff < 60_000) return `${Math.floor(diff / 1000)}s`
  if (diff < 3600_000) return `${Math.floor(diff / 60_000)}m`
  return new Date(props.timestamp).toLocaleTimeString()
})
</script>

<template>
  <div class="plc-point">
    <div class="point-header">
      <span class="point-name">{{ name }}</span>
      <span class="point-tag">{{ tagPath }}</span>
    </div>
    <div class="point-value-row">
      <span class="point-value">{{ displayValue }}</span>
      <span class="point-quality" :style="{ color: qualityColor }">
        {{ quality }}
      </span>
    </div>
    <div class="point-footer">
      <span class="point-time">{{ timeAgo }}</span>
    </div>
  </div>
</template>

<style scoped>
.plc-point {
  background: var(--color-surface-2);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  transition: border-color 0.3s, box-shadow 0.3s;
}

.plc-point:hover {
  border-color: var(--color-accent);
  box-shadow: 0 0 0 1px var(--color-accent-dim);
}

.point-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.point-name {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text);
}

.point-tag {
  font-size: 0.7rem;
  color: var(--color-muted);
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', monospace;
  max-width: 50%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.point-value-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}

.point-value {
  font-size: 1.25rem;
  font-weight: 700;
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', monospace;
  color: var(--color-text);
  letter-spacing: -0.02em;
}

.point-quality {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 4px;
  background: var(--color-surface-1);
  border: 1px solid currentColor;
}

.point-footer {
  display: flex;
  justify-content: flex-end;
}

.point-time {
  font-size: 0.65rem;
  color: var(--color-muted);
}
</style>
