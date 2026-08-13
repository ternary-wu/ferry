export interface NameParts {
  file: string;
  ext: string;
}

/** 按最后一个点拆分配置名：nginx.conf → { file: 'nginx', ext: 'conf' }；无点或点开头视为无扩展名。 */
export function splitName(name: string): NameParts {
  const idx = name.lastIndexOf('.');
  if (idx <= 0) {
    return { file: name, ext: '' };
  }
  return { file: name.slice(0, idx), ext: name.slice(idx + 1) };
}

export function joinName(file: string, ext: string): string {
  return ext ? `${file}.${ext}` : file;
}
