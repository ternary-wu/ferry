<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useUiStore } from '../stores/ui';
import { useSettingsStore } from '../stores/settings';
import { getIpc } from '../ipc';
import type { PushTarget } from '../ipc/types';

const ui = useUiStore();
const settingsStore = useSettingsStore();
const name = ref('');
const type = ref<'local' | 'git' | 'ssh'>('git');
const remotePath = ref('');
const branch = ref('main');
const selectedGroupIds = ref<string[]>([]);
const sshUser = ref('root');
const remoteDir = ref('');
const keyFile = ref('');
const userName = ref('');
const userEmail = ref('');

const groups = computed(() => {
  const list = settingsStore.settings.hostGroups ?? [];
  if (list.length === 0) {
    return [{ id: 'default', name: '默认分组' }];
  }
  return list;
});

watch(
  () => ui.pushTargetModalOpen,
  (open) => {
    if (!open) {
      return;
    }
    const index = ui.pushTargetEditIndex;
    const targets = settingsStore.settings.pushTargets ?? [];
    const editing = index !== null ? targets[index] : undefined;
    name.value = editing?.name ?? '';
    type.value = editing?.type ?? 'git';
    remotePath.value = editing?.remotePath ?? '';
    branch.value = editing?.branch ?? 'main';
    selectedGroupIds.value = editing?.groupIds ?? [];
    sshUser.value = editing?.sshUser ?? 'root';
    remoteDir.value = editing?.remoteDir ?? '';
    keyFile.value = editing?.keyFile ?? '';
    userName.value = editing?.userName ?? '';
    userEmail.value = editing?.userEmail ?? '';
  }
);

function outsideClose() {
  return settingsStore.settings.closeOutside !== false;
}

function toggleGroup(groupId: string) {
  selectedGroupIds.value = selectedGroupIds.value.includes(groupId)
    ? selectedGroupIds.value.filter((id) => id !== groupId)
    : [...selectedGroupIds.value, groupId];
}

async function pickKeyFile() {
  const res = await getIpc().send('file:openDialog', {
    title: '选择密钥文件',
    filterName: '密钥文件',
    patterns: ['*.*']
  });
  if (res.path) {
    keyFile.value = res.path;
  }
}

async function save() {
  if (!name.value.trim() || !remotePath.value.trim() || !remoteDir.value.trim()) {
    return;
  }
  const targets = [...(settingsStore.settings.pushTargets ?? [])];
  const index = ui.pushTargetEditIndex;
  const item: PushTarget = {
    id:
      index !== null && targets[index]
        ? targets[index].id
        : Date.now() + '' + Math.round(Math.random() * 1e6),
    name: name.value.trim(),
    type: type.value,
    remotePath: type.value === 'ssh' ? '' : remotePath.value.trim(),
    branch: type.value === 'git' ? branch.value.trim() || 'main' : undefined,
    groupIds: type.value === 'ssh' ? selectedGroupIds.value : undefined,
    sshUser: type.value === 'ssh' ? sshUser.value.trim() || 'root' : undefined,
    remoteDir: type.value === 'ssh' ? remoteDir.value.trim() : undefined,
    keyFile: keyFile.value.trim() || undefined,
    userName: type.value === 'git' ? userName.value.trim() || undefined : undefined,
    userEmail: type.value === 'git' ? userEmail.value.trim() || undefined : undefined
  };
  if (index !== null && targets[index]) {
    targets[index] = item;
  } else {
    targets.push(item);
  }
  await settingsStore.save({ pushTargets: targets });
  ui.closePushTargetModal();
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="ui.pushTargetModalOpen"
      class="ferry-overlay"
      @mousedown.self="outsideClose() && ui.closePushTargetModal()"
    >
      <div class="ferry-modal">
        <div class="ferry-modal-title">
          {{ ui.pushTargetEditIndex === null ? '新增推送目标' : '编辑推送目标' }}
        </div>
        <input v-model="name" class="ferry-input" placeholder="目标名称" />
        <select v-model="type" class="ferry-input ferry-move-select">
          <option value="local">本地目录</option>
          <option value="git">Git 仓库</option>
          <option value="ssh">SSH</option>
        </select>

        <input
          v-if="type !== 'ssh'"
          v-model="remotePath"
          class="ferry-input ferry-move-select"
          :placeholder="type === 'git' ? '本地 Git 仓库路径' : '目标目录'"
        />
        <template v-else>
          <input
            v-model="remoteDir"
            class="ferry-input ferry-move-select"
            placeholder="远端目录（如 /etc/nginx）"
          />
          <input
            v-model="sshUser"
            class="ferry-input ferry-move-select"
            placeholder="登录用户（默认 root）"
          />
          <div class="ferry-push-groups">
            <span class="ferry-settings-label">主机分组（多选）</span>
            <label v-for="g in groups" :key="g.id" class="ferry-push-group-item">
              <input
                type="checkbox"
                :checked="selectedGroupIds.includes(g.id)"
                @change="toggleGroup(g.id)"
              />
              {{ g.name }}
            </label>
          </div>
        </template>

        <template v-if="type === 'git'">
          <input
            v-model="branch"
            class="ferry-input ferry-move-select"
            placeholder="分支（默认 main）"
          />
          <input
            v-model="userName"
            class="ferry-input ferry-move-select"
            placeholder="提交用户名（留空=使用 Git 全局配置）"
          />
          <input
            v-model="userEmail"
            class="ferry-input ferry-move-select"
            placeholder="提交邮箱（留空=使用 Git 全局配置）"
          />
        </template>

        <div class="ferry-settings-row">
          <span class="ferry-settings-label">密钥文件</span>
          <button class="ferry-btn small" @click="pickKeyFile">选择文件…</button>
          <span class="ferry-settings-value">{{ keyFile || '未选择' }}</span>
        </div>

        <div class="ferry-modal-actions">
          <button class="ferry-btn" @click="ui.closePushTargetModal()">取消</button>
          <button
            class="ferry-btn primary"
            :disabled="!name.trim() || (type === 'ssh' ? !remoteDir.trim() : !remotePath.trim())"
            @click="save"
          >
            保存
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
