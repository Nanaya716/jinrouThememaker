<template>
  <div class="theme-maker">
    <div class="container">
      <h2 class="title">Coffee 主题文件生成器</h2>

      <section class="config-section server-section">
        <h3>在线主题</h3>
        <p class="server-hint">整文件保存，后提交的版本覆盖前一个版本；每次保存都会提交 Git 并 push。</p>
        <div class="server-controls">
          <select v-model="selectedTheme" :disabled="saving || loading" aria-label="选择服务器主题">
            <option value="">选择现有主题</option>
            <option v-for="theme in serverThemes" :key="theme.filename" :value="theme.filename">
              {{ theme.name }} · {{ theme.filename }}{{ theme.unsupported ? '（格式需检查）' : '' }}
            </option>
          </select>
          <button class="btn btn-primary" :disabled="!selectedTheme || saving || loading" @click="loadServerTheme">{{ loading ? '读取中...' : '打开主题' }}</button>
          <button class="btn btn-secondary" :disabled="saving || loading" @click="refreshThemes">刷新列表</button>
        </div>
        <div class="form-grid server-save-fields">
          <div class="form-item">
            <label>保存文件名</label>
            <input v-model="serverFilename" :disabled="saving || loading" placeholder="例如 touhou.coffee" />
          </div>
          <div class="form-item">
            <label>修改说明（选填）</label>
            <input v-model="commitMessage" :disabled="saving || loading" maxlength="200" placeholder="例如 补充头像、修正角色名称" />
          </div>
        </div>
        <div class="server-controls">
          <button class="btn btn-success" :disabled="!canDownload || !serverFilename || saving || loading" @click="saveServerTheme">
            {{ saving ? '提交 / 推送中...' : '保存、提交并 push' }}
          </button>
          <button class="btn btn-secondary" :disabled="saving || loading" @click="retryPush">重试推送</button>
        </div>
        <p v-if="serverStatus.text" class="server-status" :class="{ 'error-msg': serverStatus.error }" role="status">{{ serverStatus.text }}</p>
        <p v-for="(warning, index) in parseWarnings" :key="index" class="server-hint">{{ warning }}</p>
      </section>

      <fieldset :disabled="saving || loading" class="editor-fields">

      <!-- 导入文件区域 -->
      <section class="config-section">
        <h3>导入/导出</h3>
        <div class="form-grid">
          <div class="form-item">
            <label>导入现有 .coffee 文件</label>
            <input type="file" accept=".coffee" @change="handleFileImport" ref="fileInput" />
            <span v-if="importError" class="error-msg">{{ importError }}</span>
          </div>
          <div class="form-item" style="justify-content: flex-end;">
            <button class="btn btn-secondary" @click="resetAll">清空所有</button>
          </div>
        </div>
      </section>

      <details class="config-section">
        <summary>其他主题设置</summary>
        <div class="form-grid server-save-fields">
          <div v-for="field in extraFields" :key="field.key" class="form-item">
            <label>{{ field.label }}</label>
            <input v-model="config[field.key]" type="text" />
          </div>
          <label><input v-model="config.lockable" type="checkbox" /> 允许密码房</label>
          <label><input v-model="availability" type="checkbox" /> 主题可用</label>
        </div>
      </details>

      <!-- 基本配置区域 -->
      <section class="config-section">
        <h3>基本配置</h3>
        <div class="form-grid">
          <div class="form-item">
            <label>主题名称 *</label>
            <input v-model="config.name" type="text" placeholder="例如: 植物大战僵尸" />
            <span v-if="config.name && config.name.trim().length > 0 && config.name.trim().length < 2" class="error-msg">主题名称至少需要2个字符</span>
          </div>
          <div class="form-item">
            <label>开场语（基本无用）</label>
            <input v-model="config.opening" type="text" placeholder="例如: 一大波僵尸正在接近!" />
          </div>
          <div class="form-item">
            <label>作者</label>
            <input v-model="config.author" type="text" placeholder="作者名称" />
          </div>
          <div class="form-item">
            <label>皮肤提示</label>
            <input v-model="config.skin_tip" type="text" placeholder="例如: 你的身份" />
          </div>
        </div>
      </section>

      <!-- 角色管理区域 -->
      <section class="config-section">
        <div class="section-header">
          <h3>角色配置</h3>
          <button class="btn btn-primary" @click="addCharacter">+ 添加角色</button>
        </div>
        <p class="server-hint">头像请填写图片直链；可使用独立图片工具裁剪、上传后粘贴。</p>

        <div v-if="characters.length === 0" class="empty-state">
          <p>暂无角色，请点击上方按钮添加</p>
        </div>

        <div class="characters-list">
          <div v-for="(char, index) in characters" :key="char._id" class="character-card">
            <div class="card-header">
              <span class="char-index">#{{ index + 1 }}</span>
              <button class="btn btn-danger" @click="removeCharacter(index)">删除</button>
            </div>
            <div class="card-body">
              <div class="form-item">
                <label>自动生成 ID</label>
                <span class="generated-id">{{ char.id }}</span>
              </div>
              <div class="form-item">
                <label>显示名称 *</label>
                <input v-model="char.name" type="text" placeholder="例如: 二阶堂希罗"
                  :class="{ 'input-error': char.name && char.name.trim().length === 0 }" />
                <span v-if="char.name !== '' && char.name.trim().length === 0" class="error-msg">显示名称不能为空格</span>
              </div>
              <div class="form-item">
                <label>称号</label>
                <textarea v-model="char.prizeInput" rows="2"
                  placeholder="每行一个称号，支持多个称号（选填）"></textarea>
              </div>
              <div class="form-item">
                <label>头像链接</label>
                <textarea v-model="char.avatarInput" rows="2"
                  placeholder="每行一个链接，支持多个头像（选填）"></textarea>
              </div>

              <!-- 图片预览 -->
              <div v-if="getAvatars(char).length > 0" class="preview-section">
                <label>头像预览:</label>
                <div class="avatar-preview">
                  <div v-for="(avatar, aIndex) in getAvatars(char)" :key="aIndex" class="avatar-item">
                    <img :src="avatar" :alt="`${char.name} avatar ${aIndex + 1}`"
                      @error="handleImageError" />
                    <button class="btn-remove-avatar" @click="removeAvatar(index, aIndex)"
                      v-if="getAvatars(char).length > 1">×</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 验证提示 -->
      <section v-if="validationErrors.length > 0" class="validation-section">
        <div class="validation-header" @click="showValidation = !showValidation">
          <span class="validation-icon">⚠️</span>
          <span class="validation-summary">有 {{ validationErrors.length }} 项需要完善</span>
          <span class="validation-toggle">{{ showValidation ? '▼' : '▶' }}</span>
        </div>
        <ul v-show="showValidation" class="error-list">
          <li v-for="(error, index) in validationErrors" :key="index" class="error-item">{{ error }}</li>
        </ul>
      </section>

      <!-- 预览和下载区域 -->
      <section class="action-section">
        <div class="preview-box">
          <h4>Coffee 文件预览</h4>
          <pre class="code-preview">{{ generatedCoffee }}</pre>
        </div>
        <div class="actions">
          <button class="btn btn-success btn-large" @click="downloadCoffee" :disabled="!canDownload">
            下载 .coffee 文件
          </button>
        </div>
      </section>
      </fieldset>
    </div>

  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue';
import { serializeTheme } from '../lib/theme-format.js';

const STORAGE_KEY = 'coffee-theme-maker-draft';
const createDefaultConfig = () => ({
  name: '',
  opening: '',
  author: '',
  skin_tip: '你的身份',
  color: '#ff00a6',
  background_color: '#000000',
  icon: '',
  vote: '',
  sunrise: '',
  sunset: '',
  lockable: true
});

let nextCharacterNumber = 1;
let nextInternalId = 1;
let skipNextDraftSave = false;
const generateInternalId = () => nextInternalId++;
const generateCharacterId = () => `character_${nextCharacterNumber++}`;

// 导入错误
const importError = ref('');
const fileInput = ref(null);
const serverThemes = ref([]);
const selectedTheme = ref('');
const serverFilename = ref('');
const commitMessage = ref('');
const loading = ref(false);
const saving = ref(false);
const availability = ref(true);
const serverStatus = ref({ text: '', error: false });
const parseWarnings = ref([]);
const originalKeys = ref(null);
const extraFields = [
  { key: 'vote', label: '投票提示' }, { key: 'sunrise', label: '天亮提示' },
  { key: 'sunset', label: '入夜提示' }, { key: 'icon', label: '主题图标' },
  { key: 'color', label: '文字颜色' }, { key: 'background_color', label: '背景颜色' },
];
async function api(url, options = {}) {
  const response = await fetch(`/thememaker-api${url}`, {
    ...options, headers: { 'Content-Type': 'application/json', ...options.headers },
  });
  const result = await response.json();
  if (!response.ok) throw Object.assign(new Error(result.error || '请求失败'), result);
  return result;
}
async function refreshThemes() {
  try { serverThemes.value = await api('/themes'); }
  catch (error) { serverStatus.value = { text: `读取主题列表失败：${error.message}`, error: true }; }
}
function applyTheme(parsed) {
  parseWarnings.value = parsed.warnings || [];
  originalKeys.value = Object.keys(parsed.data);
  const { skins, ...settings } = parsed.data;
  config.value = { ...createDefaultConfig(), ...settings };
  availability.value = parsed.available;
  characters.value = Object.entries(skins || {}).map(([id, skin]) => ({
    _id: generateInternalId(), id, name: skin.name,
    avatarInput: Array.isArray(skin.avatar) ? skin.avatar.join('\n') : skin.avatar || '',
    prizeInput: Array.isArray(skin.prize) ? skin.prize.join('\n') : skin.prize || '',
    _extra: skin,
  }));
  updateNextCharacterNumber();
}
async function loadServerTheme() {
  loading.value = true;
  try {
    const theme = await api(`/themes/${encodeURIComponent(selectedTheme.value)}`);
    applyTheme(theme);
    serverFilename.value = theme.filename;
    commitMessage.value = '';
    serverStatus.value = { text: `已打开 ${theme.filename}，保存时会覆盖该文件。`, error: false };
  } catch (error) { serverStatus.value = { text: error.message, error: true }; }
  finally { loading.value = false; }
}
async function saveServerTheme() {
  saving.value = true;
  const now = new Date().toISOString();
  try {
    const source = serializeTheme({ ...buildThemeData(), lastModified: now }, availability.value);
    const result = await api(`/themes/${encodeURIComponent(serverFilename.value)}`, {
      method: 'PUT', body: JSON.stringify({ source, message: commitMessage.value }),
    });
    config.value.lastModified = now;
    selectedTheme.value = serverFilename.value;
    serverStatus.value = { text: `保存成功，已提交并 push：${result.commit.slice(0, 12)}`, error: false };
    await refreshThemes();
  } catch (error) {
    if (error.saved) config.value.lastModified = now;
    serverStatus.value = { text: `${error.message}${error.commit ? `（提交 ${error.commit.slice(0, 12)}）` : ''}`, error: true };
  } finally { saving.value = false; }
}
async function retryPush() {
  saving.value = true;
  try {
    const result = await api('/push', { method: 'POST', body: '{}' });
    serverStatus.value = { text: `推送成功：${result.commit.slice(0, 12)}`, error: false };
  } catch (error) { serverStatus.value = { text: error.message, error: true }; }
  finally { saving.value = false; }
}

// 验证提示展开状态
const showValidation = ref(false);

// 重置所有数据
const resetAll = () => {
  config.value = createDefaultConfig();
  characters.value = [];
  nextCharacterNumber = 1;
  availability.value = true;
  originalKeys.value = null;
  parseWarnings.value = [];
  serverFilename.value = '';
  selectedTheme.value = '';
  serverStatus.value = { text: '', error: false };
  importError.value = '';
  skipNextDraftSave = true;
  localStorage.removeItem(STORAGE_KEY);
  if (fileInput.value) {
    fileInput.value.value = '';
  }
};

// 主题基本配置
const config = ref(createDefaultConfig());

// 角色列表
const characters = ref([]);

const updateNextCharacterNumber = () => {
  const largestNumber = characters.value.reduce((largest, char) => {
    const match = /^character_(\d+)$/.exec(char.id);
    return match ? Math.max(largest, Number(match[1])) : largest;
  }, 0);
  nextCharacterNumber = largestNumber + 1;
};

const saveDraft = () => {
  if (skipNextDraftSave) {
    skipNextDraftSave = false;
    return;
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify({
    config: config.value,
    characters: characters.value,
    available: availability.value,
    filename: serverFilename.value,
    originalKeys: originalKeys.value,
  }));
};

const restoreDraft = () => {
  const draft = localStorage.getItem(STORAGE_KEY);
  if (!draft) return;

  try {
    const saved = JSON.parse(draft);
    config.value = { ...createDefaultConfig(), ...saved.config };
    availability.value = saved.available ?? true;
    serverFilename.value = saved.filename || '';
    originalKeys.value = saved.originalKeys || null;
    characters.value = Array.isArray(saved.characters)
      ? saved.characters.map(char => ({
        _id: generateInternalId(),
        id: char.id || generateCharacterId(),
        name: char.name || '',
        prizeInput: char.prizeInput || '',
        avatarInput: char.avatarInput || '',
        _extra: char._extra || {},
      }))
      : [];
    updateNextCharacterNumber();
  } catch {
    localStorage.removeItem(STORAGE_KEY);
  }
};

onMounted(() => { restoreDraft(); refreshThemes(); });
watch([config, characters, availability, serverFilename], saveDraft, { deep: true });
// 获取头像数组（从输入文本解析）
const getAvatars = (char) => {
  if (!char.avatarInput) return [];
  return char.avatarInput
    .split('\n')
    .map(url => url.trim())
    .filter(url => url.length > 0);
};

// 获取称号数组（从输入文本解析）
const getPrizes = (char) => {
  if (!char.prizeInput) return [];
  return char.prizeInput
    .split('\n')
    .map(p => p.trim())
    .filter(p => p.length > 0);
};

// 添加新角色
const addCharacter = () => {
  characters.value.push({
    _id: generateInternalId(),
    id: generateCharacterId(),
    name: '',
    prizeInput: '',
    avatarInput: ''
  });
};

// 删除角色
const removeCharacter = (index) => {
  characters.value.splice(index, 1);
};

// 移除单个头像
const removeAvatar = (charIndex, avatarIndex) => {
  const char = characters.value[charIndex];
  const avatars = getAvatars(char);
  avatars.splice(avatarIndex, 1);
  char.avatarInput = avatars.join('\n');
};

// 图片加载错误处理
const handleImageError = (event) => {
  event.target.src = 'data:image/svg+xml,' + encodeURIComponent(`
    <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48">
      <rect fill="#ddd" width="48" height="48"/>
      <text x="24" y="30" text-anchor="middle" fill="#999" font-size="12">加载失败</text>
    </svg>
  `);
};

// 验证错误列表
const validationErrors = computed(() => {
  const errors = [];
  if (!config.value.name || config.value.name.trim().length === 0) {
    errors.push('主题名称不能为空');
  } else if (config.value.name.trim().length < 2) {
    errors.push('主题名称至少需要2个字符');
  }
  if (characters.value.length === 0 && !config.value.openAvatar) {
    errors.push('至少需要添加一个角色');
  }
  characters.value.forEach((char, index) => {
    if (!char.name || char.name.trim().length === 0) {
      errors.push(`角色 #${index + 1} 的显示名称不能为空`);
    }
  });
  return errors;
});

// 验证是否可以下载
const canDownload = computed(() => {
  return validationErrors.value.length === 0;
});

// 导入也走服务端静态解析，和保存使用相同的完整性检查。
const handleFileImport = async (event) => {
  const file = event.target.files[0];
  if (!file) return;
  loading.value = true;
  importError.value = '';
  try {
    const parsed = await api('/parse', { method: 'POST', body: JSON.stringify({ source: await file.text() }) });
    applyTheme(parsed);
    serverFilename.value = file.name;
    selectedTheme.value = '';
    serverStatus.value = { text: '已导入 ' + file.name + '，尚未保存。', error: false };
  } catch (error) {
    importError.value = error.message;
  } finally {
    loading.value = false;
    event.target.value = '';
  }
};

function buildThemeData() {
  const defaults = createDefaultConfig();
  const data = Object.fromEntries(Object.entries(config.value).filter(([key, value]) =>
    !originalKeys.value || originalKeys.value.includes(key) || value !== defaults[key]));
  if (!data.openAvatar || characters.value.length || originalKeys.value?.includes('skins')) {
    data.skins = Object.fromEntries(characters.value.map(char => {
      const original = char._extra || {};
      const avatars = getAvatars(char);
      const prizes = getPrizes(char);
      const value = (items, previous) => Array.isArray(previous) || items.length > 1 ? items : items[0] || '';
      const skin = { ...original, name: char.name };
      const text = previous => Array.isArray(previous) ? previous.join('\n') : previous || '';
      if (avatars.length || original.avatar !== undefined) skin.avatar = char.avatarInput === text(original.avatar)
        ? original.avatar : value(avatars, original.avatar);
      if (prizes.length || original.prize !== undefined) skin.prize = char.prizeInput === text(original.prize)
        ? original.prize : value(prizes, original.prize);
      return [char.id, skin];
    }));
  }
  return data;
}
const generatedCoffee = computed(() => serializeTheme(buildThemeData(), availability.value));
// 下载 Coffee 文件
const downloadCoffee = () => {
  const blob = new Blob([generatedCoffee.value], { type: 'text/plain' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${config.value.name.replace(/[^a-zA-Z0-9\u4e00-\u9fa5]/g, '_')}.coffee`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
};
</script>

<style scoped>
.editor-fields { margin: 0; padding: 0; border: 0; min-width: 0; }
.server-controls { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
.server-controls select { flex: 1; min-width: 180px; padding: 10px; border: 1px solid #ccc; border-radius: 6px; background: white; }
.server-save-fields { margin: 16px 0; }
.server-hint, .server-status { font-size: 14px; line-height: 1.6; overflow-wrap: anywhere; }
.server-hint { color: #666; }
.server-status { margin-bottom: 0; }
button:disabled { opacity: .55; cursor: not-allowed; }
.theme-maker {
  padding: 20px;
  background: #f5f5f5;
  min-height: 100vh;
}

.container {
  max-width: 1200px;
  margin: 0 auto;
}

.title {
  text-align: center;
  color: #333;
  margin-bottom: 30px;
}

.config-section {
  background: white;
  border-radius: 8px;
  padding: 20px;
  margin-bottom: 20px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.config-section h3 {
  margin-top: 0;
  color: #333;
  border-bottom: 2px solid #eee;
  padding-bottom: 10px;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 15px;
}

.section-header h3 {
  margin: 0;
  border: none;
  padding: 0;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 15px;
}

.form-item {
  display: flex;
  flex-direction: column;
}

.form-item label {
  font-weight: 500;
  margin-bottom: 5px;
  color: #555;
  font-size: 14px;
}

.form-item input,
.form-item textarea {
  padding: 8px 12px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 14px;
}

.form-item input[type="file"] {
  padding: 6px;
  background: #f8f9fa;
}

.form-item input[type="color"] {
  height: 40px;
  padding: 2px;
}

.form-item textarea {
  resize: vertical;
  font-family: inherit;
}

.generated-id {
  min-height: 37px;
  box-sizing: border-box;
  padding: 8px 12px;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: #f8f9fa;
  color: #666;
  font-family: monospace;
}

.input-error {
  border-color: #dc3545 !important;
  background-color: #fff8f8;
}

.error-msg {
  font-size: 12px;
  color: #dc3545;
  margin-top: 4px;
}

.empty-state {
  text-align: center;
  padding: 40px;
  color: #999;
}

.validation-section {
  background: #fff3cd;
  border-radius: 8px;
  margin-bottom: 20px;
  border: 1px solid #ffc107;
  overflow: hidden;
}

.validation-header {
  display: flex;
  align-items: center;
  padding: 12px 20px;
  cursor: pointer;
  user-select: none;
  transition: background 0.2s;
  gap: 10px;
}

.validation-header:hover {
  background: #ffe69c;
}

.validation-icon {
  font-size: 18px;
}

.validation-summary {
  flex: 1;
  color: #856404;
  font-weight: 500;
}

.validation-toggle {
  color: #856404;
  font-size: 12px;
  transition: transform 0.2s;
}

.error-list {
  margin: 0;
  padding: 10px 20px 15px 30px;
  border-top: 1px solid #ffc107;
  background: #fff8e1;
}

.error-item {
  color: #856404;
  margin-bottom: 5px;
}

.characters-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 15px;
}

.character-card {
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  overflow: hidden;
}

.character-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 15px;
  background: #f8f9fa;
  border-bottom: 1px solid #e0e0e0;
}

.char-index {
  font-weight: bold;
  color: #666;
}

.card-body {
  padding: 15px;
}

.card-body .form-item {
  margin-bottom: 12px;
}

.card-body .form-item:last-child {
  margin-bottom: 0;
}

.preview-section {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px dashed #e0e0e0;
}

.preview-section label {
  display: block;
  font-size: 12px;
  color: #888;
  margin-bottom: 8px;
}

.avatar-preview {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.avatar-item {
  position: relative;
  width: 48px;
  height: 48px;
}

.avatar-item img {
  width: 48px;
  height: 48px;
  object-fit: cover;
  border-radius: 4px;
  border: 1px solid #ddd;
}

.btn-remove-avatar {
  position: absolute;
  top: -8px;
  right: -8px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: none;
  background: #ff4444;
  color: white;
  cursor: pointer;
  font-size: 14px;
  line-height: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-remove-avatar:hover {
  background: #cc0000;
}

.action-section {
  background: white;
  border-radius: 8px;
  padding: 20px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.preview-box h4 {
  margin-top: 0;
  color: #333;
}

.code-preview {
  background: #2d2d2d;
  color: #f8f8f2;
  padding: 15px;
  border-radius: 4px;
  overflow-x: auto;
  font-size: 12px;
  line-height: 1.5;
  max-height: 300px;
  overflow-y: auto;
}

.actions {
  margin-top: 20px;
  text-align: center;
}

.btn {
  padding: 10px 20px;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  transition: all 0.2s;
}

.btn-primary {
  background: #007bff;
  color: white;
}

.btn-primary:hover {
  background: #0056b3;
}

.btn-secondary {
  background: #6c757d;
  color: white;
}

.btn-secondary:hover {
  background: #545b62;
}

.btn-success {
  background: #28a745;
  color: white;
}

.btn-success:hover:not(:disabled) {
  background: #1e7e34;
}

.btn-success:disabled {
  background: #94d3a2;
  cursor: not-allowed;
}

.btn-danger {
  padding: 6px 12px;
  background: #dc3545;
  color: white;
  font-size: 12px;
}

.btn-danger:hover {
  background: #c82333;
}

.btn-large {
  padding: 15px 40px;
  font-size: 16px;
}
</style>
