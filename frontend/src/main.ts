import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import { router } from './router';
import { setSpikeRunHandler } from './ipc';
import { runSpikeSelfCheck } from './selfcheck';
import './styles/main.css';

setSpikeRunHandler(() => {
  void runSpikeSelfCheck();
});

const app = createApp(App);
app.use(createPinia());
app.use(router);
app.mount('#app');
