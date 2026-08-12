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

  async function loadNav() {
    if (!currentProjectId.value) {
      return;
    }
    const res = await getIpc().send('nav:tree', { projectId: currentProjectId.value });
    nav.value = res;
    return res;
  }

  return { projects, currentProjectId, nav, loadProjects, selectProject, loadNav };
});
