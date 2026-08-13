<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import { useNotificationStore } from '../stores/notification';
import { useSettingsStore } from '../stores/settings';

const notifications = useNotificationStore();
const settingsStore = useSettingsStore();
const open = ref(false);

function onDocClick() {
  open.value = false;
}

function formatTime(time: number): string {
  return new Date(time).toLocaleTimeString('zh-CN', { hour12: false });
}

onMounted(() => {
  notifications.load();
  document.addEventListener('click', onDocClick);
});

onUnmounted(() => {
  document.removeEventListener('click', onDocClick);
});
</script>

<template>
  <div v-if="settingsStore.settings.notifyEnabled !== false" class="ferry-notify-wrap" @click.stop>
    <button
      class="win-btn ferry-notify-bell"
      :class="{ active: open }"
      title="通知"
      @click="open = !open"
    >
      <svg viewBox="0 0 14 14" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.2">
        <path d="M7 1.5c-2.2 0-4 1.8-4 4v2.2L2 10h10l-1-1.3V5.5c0-2.2-1.8-4-4-4z" stroke-linejoin="round" />
        <path d="M5.5 11.5a1.6 1.6 0 0 0 3 0" stroke-linecap="round" />
      </svg>
      <span v-if="notifications.items.length" class="ferry-notify-dot"></span>
    </button>
    <div v-if="open" class="ferry-notify-panel">
      <div class="ferry-notify-header">
        <span class="ferry-notify-title">通知</span>
        <button v-if="notifications.items.length" class="ferry-btn small" @click="notifications.clearAll()">
          清空
        </button>
      </div>
      <div v-if="notifications.items.length === 0" class="ferry-hint">暂无通知</div>
      <div v-for="item in notifications.items" :key="item.id" class="ferry-notify-item" :class="item.type">
        <span class="ferry-notify-text">{{ item.text }}</span>
        <span class="ferry-notify-time">{{ formatTime(item.time) }}</span>
        <button class="ferry-notify-close" title="移除" @click="notifications.consume(item.id)">×</button>
      </div>
    </div>
  </div>
</template>
