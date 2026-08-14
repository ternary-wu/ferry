<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { getIpc } from '../ipc';
import { useUiStore } from '../stores/ui';
import { useConfigStore } from '../stores/config';
import { useAppStore } from '../stores/app';
import { useSettingsStore } from '../stores/settings';
import type { GitCommitDto, VersionDto } from '../ipc/types';

const ui = useUiStore();
const configStore = useConfigStore();
const app = useAppStore();
const settingsStore = useSettingsStore();
const router = useRouter();

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

const versions = ref<VersionDto[]>([]);
const note = ref('');
const loading = ref(false);
const tab = ref<'versions' | 'git'>('versions');
const gitTargetId = ref('');
const gitCommits = ref<GitCommitDto[]>([]);
const gitLoading = ref(false);
const gitTargets = computed(() =>
  (settingsStore.settings.pushTargets ?? []).filter((t) => t.type === 'git')
);

watch(
  () => ui.historyOpen,
  (open) => {
    if (open) {
      note.value = '';
      tab.value = 'versions';
      gitTargetId.value = gitTargets.value[0]?.id ?? '';
      void refresh();
      void loadGitCommits();
    }
  }
);

watch(gitTargetId, () => {
  void loadGitCommits();
});

async function loadGitCommits() {
  const target = ui.historyTarget;
  if (!target || !gitTargetId.value) {
    gitCommits.value = [];
    return;
  }
  gitLoading.value = true;
  try {
    const res = await getIpc().send('push:gitLog', {
      targetId: gitTargetId.value,
      workspaceId: target.workspaceId,
      configId: target.config.id
    });
    gitCommits.value = res.commits ?? [];
  } catch (error) {
    app.setStatus('加载 Git 历史失败：' + (error as Error).message, true);
  } finally {
    gitLoading.value = false;
  }
}

async function gitRestore(commit: GitCommitDto) {
  const target = ui.historyTarget;
  if (!target || !gitTargetId.value) {
    return;
  }
  const ok = await ui.confirm({
    title: '回滚 Git 版本',
    message: `回滚到 ${commit.timestamp}（${commit.message}）？当前表单将被该提交源码重建，并自动本地留档。`
  });
  if (!ok) {
    return;
  }
  try {
    const res = await getIpc().send('push:gitRestore', {
      targetId: gitTargetId.value,
      workspaceId: target.workspaceId,
      configId: target.config.id,
      commitId: commit.id
    });
    app.setStatus(res.message);
    await configStore.open(target.workspaceId, target.config.id);
    await router.push('/editor');
    await refresh();
    await loadGitCommits();
  } catch (error) {
    app.setStatus('Git 回滚失败：' + (error as Error).message, true);
  }
}

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
    <div v-if="ui.historyOpen" class="ferry-overlay" @mousedown.self="outsideClose() && ui.closeHistory()">
      <div class="ferry-modal ferry-modal-wide">
        <div class="ferry-modal-title">版本历史 · {{ ui.historyTarget?.config.name }}</div>
        <div class="ferry-history-tabs">
          <button
            class="ferry-btn small"
            :class="{ active: tab === 'versions' }"
            @click="tab = 'versions'"
          >
            本地版本
          </button>
          <button
            v-if="gitTargets.length"
            class="ferry-btn small"
            :class="{ active: tab === 'git' }"
            @click="tab = 'git'"
          >
            Git 历史
          </button>
        </div>
        <template v-if="tab === 'versions'">
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
        </template>
        <div v-else class="ferry-history-git">
          <select v-model="gitTargetId" class="ferry-input ferry-move-select">
            <option value="" disabled>选择 Git 目标</option>
            <option v-for="t in gitTargets" :key="t.id" :value="t.id">
              {{ t.name }}（{{ t.remotePath }}）
            </option>
          </select>
          <div class="ferry-history-list ferry-history-git-list">
            <div v-if="gitLoading" class="ferry-hint">加载中…</div>
            <div v-else-if="gitCommits.length === 0" class="ferry-hint">
              暂无 Git 提交（推送后生成）
            </div>
            <div v-for="c in gitCommits" :key="c.id" class="ferry-history-item">
              <span class="ferry-history-meta">
                {{ c.timestamp }} · {{ c.message }}（{{ c.id.slice(0, 8) }}）
              </span>
              <span class="ferry-history-actions">
                <button class="ferry-btn small" @click="gitRestore(c)">回滚</button>
              </span>
            </div>
          </div>
        </div>
        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closeHistory()">关闭</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
