<script setup lang="ts">
import { ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { getIpc } from '../ipc';
import { useUiStore } from '../stores/ui';
import { useConfigStore } from '../stores/config';
import { useAppStore } from '../stores/app';
import type { VersionDto } from '../ipc/types';

const ui = useUiStore();
const configStore = useConfigStore();
const app = useAppStore();
const router = useRouter();

const versions = ref<VersionDto[]>([]);
const note = ref('');
const loading = ref(false);

watch(
  () => ui.historyOpen,
  (open) => {
    if (open) {
      note.value = '';
      void refresh();
    }
  }
);

async function refresh() {
  const target = ui.historyTarget;
  if (!target) {
    return;
  }
  loading.value = true;
  try {
    const res = await getIpc().send('versions:list', {
      workspaceId: target.workspaceId,
      configId: target.config.id
    });
    versions.value = res.versions ?? [];
  } catch (error) {
    app.setStatus('加载历史失败：' + (error as Error).message, true);
  } finally {
    loading.value = false;
  }
}

async function snapshot() {
  const target = ui.historyTarget;
  if (!target || configStore.current?.id !== target.config.id) {
    return;
  }
  try {
    await getIpc().send('version:snapshot', { note: note.value || undefined });
    note.value = '';
    await refresh();
  } catch (error) {
    app.setStatus('留档失败：' + (error as Error).message, true);
  }
}

async function restore(version: VersionDto) {
  const target = ui.historyTarget;
  if (!target) {
    return;
  }
  const ok = await ui.confirm({
    title: '回滚版本',
    message: `回滚到 ${version.timestamp} 该版本？当前表单将被该版本源码重建。`
  });
  if (!ok) {
    return;
  }
  try {
    await getIpc().send('version:restore', {
      workspaceId: target.workspaceId,
      configId: target.config.id,
      versionId: version.id
    });
    await configStore.open(target.workspaceId, target.config.id);
    await router.push('/editor');
    await refresh();
  } catch (error) {
    app.setStatus('回滚失败：' + (error as Error).message, true);
  }
}

async function remove(version: VersionDto) {
  const target = ui.historyTarget;
  if (!target) {
    return;
  }
  try {
    await getIpc().send('version:delete', {
      workspaceId: target.workspaceId,
      configId: target.config.id,
      versionId: version.id
    });
    await refresh();
  } catch (error) {
    app.setStatus('删除版本失败：' + (error as Error).message, true);
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="ui.historyOpen" class="ferry-overlay" @mousedown.self="ui.closeHistory()">
      <div class="ferry-modal ferry-modal-wide">
        <div class="ferry-modal-title">版本历史 · {{ ui.historyTarget?.config.name }}</div>
        <div class="ferry-history-note">
          <input
            v-model="note"
            class="ferry-input"
            placeholder="留档备注（可选）"
            @keydown.enter="snapshot"
          />
          <button
            class="ferry-btn primary"
            :disabled="configStore.current?.id !== ui.historyTarget?.config.id"
            title="留档保存当前打开的配置源码"
            @click="snapshot"
          >
            留档
          </button>
        </div>
        <div class="ferry-history-list">
          <div v-if="loading" class="ferry-hint">加载中…</div>
          <div v-else-if="versions.length === 0" class="ferry-hint">暂无留档</div>
          <div v-for="v in versions" :key="v.id" class="ferry-history-item">
            <span class="ferry-history-meta">
              {{ v.timestamp }}{{ v.note ? ' · ' + v.note : '' }}（{{ v.length }} 字符）
            </span>
            <span class="ferry-history-actions">
              <button class="ferry-btn small" @click="restore(v)">回滚</button>
              <button class="ferry-btn small danger" @click="remove(v)">删除</button>
            </span>
          </div>
        </div>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closeHistory()">关闭</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
