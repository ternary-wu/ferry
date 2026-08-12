import { createRouter, createWebHashHistory } from 'vue-router';
import WelcomeView from '../views/WelcomeView.vue';
import EditorView from '../views/EditorView.vue';
import SettingsView from '../views/SettingsView.vue';

export const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', name: 'welcome', component: WelcomeView },
    { path: '/editor', name: 'editor', component: EditorView },
    { path: '/settings', name: 'settings', component: SettingsView }
  ]
});
