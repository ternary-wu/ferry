<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useConfigStore } from '../../stores/config';
import type { FormFieldSnapshot } from '../../ipc/types';

const props = defineProps<{ node: FormFieldSnapshot }>();
const configStore = useConfigStore();

const localValue = ref(formatValue(props.node.value));
const localError = ref(props.node.validationError ?? '');

function formatValue(value: unknown): string {
  return value === null || value === undefined ? '' : String(value);
}

watch(
  () => props.node.value,
  (value) => {
    localValue.value = formatValue(value);
  }
);

watch(
  () => props.node.validationError,
  (value) => {
    localError.value = value ?? '';
  }
);

function validate(raw: string): string | null {
  const text = raw.trim();
  if (props.node.type === 'Number' || (props.node.type === 'Enum' && props.node.allowCustomValue)) {
    if (text === '') {
      return props.node.required ? '必填字段不能为空' : null;
    }
    if (!/^-?\d+(\.\d+)?$/.test(text) || !Number.isFinite(Number(text))) {
      return '请输入数字';
    }
    const num = Number(text);
    if (props.node.integerOnly && !Number.isInteger(num)) {
      return '仅允许整数';
    }
    if (props.node.min !== null && props.node.min !== undefined && num < props.node.min) {
      return `不能小于 ${props.node.min}`;
    }
    if (props.node.max !== null && props.node.max !== undefined && num > props.node.max) {
      return `不能大于 ${props.node.max}`;
    }
  }
  return null;
}

function onTextInput() {
  if (props.node.type === 'Number' || (props.node.type === 'Enum' && props.node.allowCustomValue)) {
    const message = validate(localValue.value);
    localError.value = message ?? '';
    if (!message) {
      void configStore.setValue(props.node.path, localValue.value.trim());
    }
    return;
  }
  void configStore.setValue(props.node.path, localValue.value);
}

function onSelectChange() {
  void configStore.setValue(props.node.path, localValue.value);
}

function onBooleanToggle() {
  void configStore.setValue(props.node.path, props.node.value !== true);
}

const enumOptions = computed(() => props.node.enumOptions ?? []);
</script>

<template>
  <span class="ferry-field-control">
    <input
      v-if="props.node.type === 'String' || props.node.type === 'Number'"
      v-model="localValue"
      class="ferry-field-input"
      :inputmode="props.node.type === 'Number' ? 'decimal' : undefined"
      @input="onTextInput"
    />
    <span
      v-else-if="props.node.type === 'Boolean'"
      class="ferry-toggle"
      :class="{ active: props.node.value === true }"
      @click="onBooleanToggle"
    ></span>
    <template v-else-if="props.node.type === 'Enum'">
      <select v-model="localValue" class="ferry-field-input" @change="onSelectChange">
        <option v-for="opt in enumOptions" :key="opt.value" :value="opt.value">
          {{ opt.value }}
        </option>
      </select>
      <input
        v-if="props.node.allowCustomValue"
        v-model="localValue"
        class="ferry-field-input"
        placeholder="自定义"
        @input="onTextInput"
      />
    </template>
    <span v-if="localError" class="ferry-field-error">{{ localError }}</span>
  </span>
</template>
