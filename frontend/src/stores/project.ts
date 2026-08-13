import { ref } from 'vue';
import { defineStore } from 'pinia';
import { getIpc } from '../ipc';
import type { NavTree, ProjectInfo } from '../ipc/types';

export const useProjectStore = defineStore('project', () => {
  const projects = ref<ProjectInfo[]>([]);
  const currentProjectId = ref('');
  const nav = ref<NavTree>({ workspaces: [], unassigned: [] });

  async function loadProjects(preferredId?: string) {
    const client = getIpc();
    const res = await client.send('projects:list', {});
    projects.value = res.projects ?? [];
    if (projects.value.length === 0) {
      const created = await client.send('project:create', { name: '默认项目' });
      projects.value = [created.project];
      currentProjectId.value = created.project.id;
    } else {
      currentProjectId.value =
        preferredId ?? projects.value[0].id;
      if (!projects.value.some((p) => p.id === currentProjectId.value)) {
        currentProjectId.value = projects.value[0].id;
      }
    }
    return currentProjectId.value;
  }

  function selectProject(projectId: string) {
    currentProjectId.value = projectId;
  }

  async function createProject(name: string) {
    const res = await getIpc().send('project:create', { name });
    projects.value = [...projects.value, res.project];
    currentProjectId.value = res.project.id;
    return res.project;
  }

  async function renameProject(id: string, name: string) {
    const res = await getIpc().send('project:rename', { id, name });
    projects.value = projects.value.map((p) => (p.id === id ? res.project : p));
    return res.project;
  }

  async function deleteProject(id: string) {
    await getIpc().send('project:delete', { id });
    projects.value = projects.value.filter((p) => p.id !== id);
    if (currentProjectId.value === id) {
      currentProjectId.value = projects.value[0]?.id ?? '';
    }
  }

  async function createWorkspace(name: string) {
    const res = await getIpc().send('workspace:create', {
      projectId: currentProjectId.value,
      name
    });
    return res.workspace;
  }

  async function renameWorkspace(id: string, name: string) {
    const res = await getIpc().send('workspace:rename', { id, name });
    return res.workspace;
  }

  async function deleteWorkspace(id: string) {
    await getIpc().send('workspace:delete', { id });
  }

  async function reorderWorkspaces(projectId: string, workspaceIds: string[]) {
    return getIpc().send('workspace:reorder', { projectId, workspaceIds });
  }

  async function moveConfig(configId: string, workspaceId: string) {
    return getIpc().send('config:move', { configId, workspaceId });
  }

  async function duplicateConfig(configId: string, workspaceId: string, name?: string) {
    return getIpc().send('config:duplicate', { workspaceId, configId, name });
  }

  async function renameConfig(configId: string, workspaceId: string, name: string) {
    return getIpc().send('config:rename', { workspaceId, configId, name });
  }

  async function reorderConfigs(workspaceId: string, configIds: string[]) {
    return getIpc().send('config:reorder', { workspaceId, configIds });
  }

  async function loadNav() {
    if (!currentProjectId.value) {
      return;
    }
    const res = await getIpc().send('nav:tree', { projectId: currentProjectId.value });
    nav.value = res;
    return res;
  }

  return {
    projects,
    currentProjectId,
    nav,
    loadProjects,
    selectProject,
    loadNav,
    createProject,
    renameProject,
    deleteProject,
    createWorkspace,
    renameWorkspace,
    deleteWorkspace,
    reorderWorkspaces,
    moveConfig,
    duplicateConfig,
    renameConfig,
    reorderConfigs
  };
});
