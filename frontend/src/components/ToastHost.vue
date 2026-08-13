<script setup lang="ts">
import { ref, watch } from 'vue';
import { useNotificationStore } from '../stores/notification';
import { useSettingsStore } from '../stores/settings';

const notifications = useNotificationStore();
const settingsStore = useSettingsStore();
const toasts = ref<{ id: string; type: 'ok' | 'error'; text: string }[]>([]);
let lastId = '';

watch(
  () => notifications.items[0]?.id,
  (id) => {
    if (!id || id === lastId) {
      return;
    }
    lastId = id;
    if (settingsStore.settings.notifyStyle !== 'toast') {
      return;
    }
    const item = notifications.items[0];
    if (!item) {
      return;
    }
    const toast = { id: item.id, type: item.type, text: item.text };
    toasts.value.push(toast);
    setTimeout(() => {
      toasts.value = toasts.value.filter((t) => t.id !== toast.id);
    }, 2600);
  }
);
</script>

<template>
  <Teleport to="body">
    <div class="ferry-toast-host">
      <div v-for="toast in toasts" :key="toast.id" class="ferry-toast" :class="toast.type">
        {{ toast.text }}
      </div>
    </div>
  </Teleport>
</template>
