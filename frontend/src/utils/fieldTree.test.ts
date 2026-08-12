import { describe, expect, it } from 'vitest';
import type { FormFieldSnapshot } from '../ipc/types';
import {
  collectCollapsiblePaths,
  nodeMatchesSearch,
  nodePassesFilter,
  subtreeMatchesSearch,
  subtreePassesFilter
} from './fieldTree';

function node(partial: Partial<FormFieldSnapshot> & { id: string }): FormFieldSnapshot {
  return {
    path: partial.id,
    label: partial.label ?? partial.id,
    description: '',
    type: 'String',
    value: null,
    isEnabled: true,
    isVisible: true,
    isModule: false,
    isArrayItem: false,
    isSelectable: true,
    canToggleEnabled: true,
    totalChildModulesCount: 0,
    enabledChildModulesCount: 0,
    enabledChildModulesText: '',
    required: false,
    allowCustomValue: false,
    integerOnly: false,
    enumOptions: [],
    children: [],
    ...partial
  };
}

describe('fieldTree', () => {
  it('matches search by label or id', () => {
    expect(nodeMatchesSearch(node({ id: 'user', label: '用户' }), '用户')).toBe(true);
    expect(nodeMatchesSearch(node({ id: 'user' }), 'user')).toBe(true);
    expect(nodeMatchesSearch(node({ id: 'user' }), 'nginx')).toBe(false);
  });

  it('subtree search finds descendants', () => {
    const root = node({
      id: 'http',
      type: 'Object',
      children: [node({ id: 'upstreams' }), node({ id: 'servers' })]
    });
    expect(subtreeMatchesSearch(root, 'servers')).toBe(true);
    expect(subtreeMatchesSearch(root, 'missing')).toBe(false);
  });

  it('filter checks own state and subtree', () => {
    const enabled = node({ id: 'a', isEnabled: true });
    const disabled = node({ id: 'b', isEnabled: false });
    expect(nodePassesFilter(enabled, 'selected')).toBe(true);
    expect(nodePassesFilter(enabled, 'unselected')).toBe(false);
    expect(nodePassesFilter(disabled, 'unselected')).toBe(true);
    const parent = node({ id: 'p', type: 'Object', isEnabled: false, children: [enabled] });
    expect(subtreePassesFilter(parent, 'selected')).toBe(true);
  });

  it('collects collapsible paths for module/object/array with children', () => {
    const root = node({
      id: 'http',
      type: 'Object',
      isModule: true,
      children: [node({ id: 'servers', type: 'Array', children: [node({ id: 'item' })] })]
    });
    expect(collectCollapsiblePaths([root])).toEqual(['http', 'servers']);
  });
});
