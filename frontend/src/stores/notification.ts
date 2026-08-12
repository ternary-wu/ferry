import { ref } from 'vue';
import { defineStore } from 'pinia';

export interface NotificationItem {
  id: string;
  type: 'ok' | 'error';
  text: string;
  time: number;
}

const STORAGE_KEY = 'ferry.notifications';
const MAX_ITEMS = 50;

export const useNotificationStore = defineStore('notification', () => {
  const items = ref<NotificationItem[]>([]);

  function load() {
    try {
      items.value = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]') as NotificationItem[];
    } catch {
      items.value = [];
    }
    return items.value;
  }

  function save() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items.value));
  }

  function add(type: NotificationItem['type'], text: string) {
    items.value.unshift({
      id: Date.now() + '' + Math.round(Math.random() * 1e6),
      type,
      text,
      time: Date.now()
    });
    if (items.value.length > MAX_ITEMS) {
      items.value.length = MAX_ITEMS;
    }
    save();
  }

  function consume(id: string) {
    items.value = items.value.filter((item) => item.id !== id);
    save();
  }

  function clearAll() {
    items.value = [];
    save();
  }

  return { items, load, add, consume, clearAll };
});
