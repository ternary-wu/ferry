import type { FormFieldSnapshot } from '../ipc/types';

export type FieldFilter = 'all' | 'selected' | 'unselected';

export function nodeMatchesSearch(node: FormFieldSnapshot, query: string): boolean {
  return (
    (node.label || '').toLowerCase().includes(query) ||
    node.id.toLowerCase().includes(query)
  );
}

export function subtreeMatchesSearch(node: FormFieldSnapshot, query: string): boolean {
  return node.children.some(
    (child) => nodeMatchesSearch(child, query) || subtreeMatchesSearch(child, query)
  );
}

export function nodePassesFilter(node: FormFieldSnapshot, filter: FieldFilter): boolean {
  if (filter === 'all') {
    return true;
  }
  return filter === 'selected' ? node.isEnabled : !node.isEnabled;
}

export function subtreePassesFilter(node: FormFieldSnapshot, filter: FieldFilter): boolean {
  return node.children.some(
    (child) => nodePassesFilter(child, filter) || subtreePassesFilter(child, filter)
  );
}

export function collectCollapsiblePaths(nodes: FormFieldSnapshot[]): string[] {
  const result: string[] = [];
  const walk = (list: FormFieldSnapshot[]) => {
    for (const node of list) {
      if (
        (node.isModule || node.type === 'Object' || node.type === 'Array') &&
        node.children.length > 0
      ) {
        result.push(node.path);
      }
      walk(node.children);
    }
  };
  walk(nodes);
  return result;
}
