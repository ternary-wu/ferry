<script setup lang="ts">
import { nextTick, ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useSettingsStore } from '../stores/settings';

const ui = useUiStore();
const settingsStore = useSettingsStore();
const promptInput = ref<HTMLInputElement | null>(null);

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

watch(
  () => ui.promptOpen,
  async (open) => {
    if (!open) {
      return;
    }
    await nextTick();
    promptInput.value?.focus();
  }
);

function submitPrompt() {
  const value = ui.promptValue.trim();
  if (!value) {
    return;
  }
  ui.resolvePrompt(value);
}
</script>

<template>
  <Teleport to="body">
    <div v-if="ui.promptOpen" class="ferry-overlay" @mousedown.self="outsideClose() && ui.resolvePrompt(null)">
      <div class="ferry-modal">
        <div class="ferry-modal-title">{{ ui.promptTitle }}</div>
        <input
          ref="promptInput"
          v-model="ui.promptValue"
          class="ferry-input"
          :placeholder="ui.promptPlaceholder"
          @keydown.enter="submitPrompt"
          @keydown.esc="ui.resolvePrompt(null)"
        />
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.resolvePrompt(null)">取消</button>
          <button class="ferry-btn primary" @click="submitPrompt">确定</button>
        </div>
      </div>
    </div>

    <div v-if="ui.confirmOpen" class="ferry-overlay" @mousedown.self="outsideClose() && ui.resolveConfirm(false)">
      <div class="ferry-modal">
        <div class="ferry-modal-title">{{ ui.confirmTitle }}</div>
        <p class="ferry-modal-message">{{ ui.confirmMessage }}</p>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.resolveConfirm(false)">取消</button>
          <button class="ferry-btn danger" @click="ui.resolveConfirm(true)">确定</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
