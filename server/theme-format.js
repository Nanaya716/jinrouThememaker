import CoffeeScript from 'coffee-script';
import { parse } from 'acorn';

// Compile and inspect syntax only. Never require/eval an uploaded theme.
export function parseTheme(source) {
  if (typeof source !== 'string' || !source.trim()) throw new Error('主题文件不能为空');
  const program = parse(CoffeeScript.compile(source, { bare: true }), { ecmaVersion: 2020 });
  const statement = program.body[0];
  const assignment = statement?.expression;
  const target = assignment?.left;
  if (program.body.length !== 1 || statement.type !== 'ExpressionStatement' ||
      assignment?.type !== 'AssignmentExpression' || assignment.operator !== '=' ||
      target?.type !== 'MemberExpression' || target.computed ||
      target.object?.name !== 'module' || target.property?.name !== 'exports' ||
      assignment.right?.type !== 'ObjectExpression') {
    throw new Error('只允许一个 module.exports 主题对象，不允许其他执行语句');
  }
  let available;
  const warnings = [];
  function read(node, depth = 0) {
    if (depth > 12) throw new Error('主题对象嵌套过深');
    if (node.type === 'Literal' && !node.regex && !node.bigint &&
        (node.value === null || ['string', 'number', 'boolean'].includes(typeof node.value))) return node.value;
    if (node.type === 'UnaryExpression' && node.operator === '-' && node.argument.type === 'Literal' &&
        typeof node.argument.value === 'number') return -node.argument.value;
    if (node.type === 'ArrayExpression') return node.elements.map(item => {
      if (!item) throw new Error('数组不能包含空项');
      return read(item, depth + 1);
    });
    if (node.type === 'ObjectExpression') {
      const result = Object.create(null);
      for (const prop of node.properties) {
        if (prop.type !== 'Property' || prop.kind !== 'init' || prop.computed || prop.method || prop.shorthand) {
          throw new Error('主题属性必须是普通数据');
        }
        const key = prop.key.type === 'Identifier' ? prop.key.name : prop.key.value;
        if (typeof key !== 'string' || ['__proto__', 'prototype', 'constructor'].includes(key)) {
          throw new Error(`不允许特殊属性：${String(key)}`);
        }
        if (Object.hasOwn(result, key)) warnings.push(`重复属性 ${key}：与游戏一致，仅最后一项生效；表单保存时会合并重复项。`);
        if (depth === 0 && key === 'isAvailable') {
          const fn = prop.value;
          const ret = fn.body?.body?.[0];
          if (fn.type !== 'FunctionExpression' || fn.params.length || fn.async || fn.generator ||
              fn.body.body.length !== 1 || ret?.type !== 'ReturnStatement' ||
              ret.argument?.type !== 'Literal' || typeof ret.argument.value !== 'boolean') {
            throw new Error('isAvailable 只允许返回固定 true 或 false');
          }
          available = ret.argument.value;
        } else result[key] = read(prop.value, depth + 1);
      }
      return result;
    }
    throw new Error('主题只允许字符串、数字、布尔值、数组和对象；不允许代码或字符串插值');
  }
  const data = read(assignment.right);
  if (available === undefined) throw new Error('缺少 isAvailable 方法');
  if (typeof data.name !== 'string' || !data.name.trim()) throw new Error('主题名称不能为空');
  if (typeof data.lockable !== 'boolean') throw new Error('lockable 必须是布尔值');
  if (data.openAvatar !== undefined && typeof data.openAvatar !== 'boolean') throw new Error('openAvatar 必须是布尔值');
  for (const key of ['opening', 'skin_tip', 'author', 'lastModified', 'vote', 'sunrise', 'sunset', 'icon', 'background_color', 'color']) {
    if (data[key] !== undefined && typeof data[key] !== 'string') throw new Error(`${key} 必须是字符串`);
  }
  const isObject = value => value && typeof value === 'object' && !Array.isArray(value);
  if (data.skins !== undefined && !isObject(data.skins)) throw new Error('skins 必须是对象');
  if (!data.openAvatar && !Object.keys(data.skins || {}).length) throw new Error('普通主题至少需要一个角色');
  for (const [id, skin] of Object.entries(data.skins || {})) {
    if (!/^[\p{L}_$][\p{L}\p{N}\p{M}_$]*$/u.test(id)) throw new Error(`角色 ID 不合法：${id}`);
    if (!isObject(skin) || typeof skin.name !== 'string' || !skin.name.trim()) throw new Error(`角色 ${id} 缺少名称`);
    for (const key of ['avatar', 'prize']) {
      const value = skin[key];
      if (value !== undefined && typeof value !== 'string' &&
          !(Array.isArray(value) && value.every(item => typeof item === 'string'))) {
        throw new Error(`角色 ${id} 的 ${key} 必须是字符串或字符串数组`);
      }
    }
  }
  return { data, available, warnings };
}
