const literal = value => JSON.stringify(value, null, 4).replace(/#\{/g, '\\#{');

export function serializeTheme(data, available = true) {
  const lines = ['module.exports ='];
  for (const [key, value] of Object.entries(data)) {
    if (key === 'isAvailable' || value === undefined) continue;
    const name = /^[A-Za-z_][A-Za-z0-9_]*$/.test(key) ? key : literal(key);
    lines.push(`    ${name}: ${literal(value).replace(/\n/g, '\n    ')}`);
  }
  lines.push('    isAvailable: ->', `        return ${available ? 'true' : 'false'}`, '');
  return lines.join('\n');
}
