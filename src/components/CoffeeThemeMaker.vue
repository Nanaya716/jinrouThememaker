<template>
  <div class="theme-maker">
    <div class="container">
      <h2 class="title">Coffee 主题文件生成器</h2>

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
                <div class="upload-row">
                  <input
                    :ref="element => setAvatarUploadInput(char._id, element)"
                    type="file"
                    accept="image/*"
                    class="avatar-upload-input"
                    @change="event => uploadAvatar(index, event)"
                  />
                  <button class="btn btn-primary btn-upload" type="button" :disabled="uploadingCharacterId === char._id" @click="openAvatarPicker(char._id)">
                    {{ uploadingCharacterId === char._id ? '上传中...' : '上传图片' }}
                  </button>
                  <span v-if="uploadMessage.characterId === char._id" :class="uploadMessage.isError ? 'error-msg' : 'success-msg'">{{ uploadMessage.text }}</span>
                </div>
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
    </div>

    <div v-if="cropDialog.visible" class="crop-modal" @click.self="!cropDialog.processing && cancelCrop()">
      <div class="crop-dialog" role="dialog" aria-modal="true" aria-labelledby="crop-dialog-title">
        <div class="crop-dialog-header">
          <h3 id="crop-dialog-title">裁剪头像</h3>
          <button class="btn-close" type="button" aria-label="关闭" :disabled="cropDialog.processing" @click="cancelCrop">×</button>
        </div>
        <div ref="cropStage" class="crop-stage">
          <img ref="cropImage" :src="cropDialog.sourceUrl" alt="待裁剪图片" draggable="false" @load="initializeCrop" />
          <div v-if="cropDialog.ready" class="crop-selection" :style="cropSelectionStyle" @pointerdown.prevent="!cropDialog.processing && startCropDrag($event)"></div>
        </div>
        <label class="crop-size-control">
          裁剪范围
          <input v-model.number="cropDialog.sizeRatio" type="range" min="0.2" max="1" step="0.01" :disabled="cropDialog.processing" @input="updateCropSize" />
        </label>
        <div class="crop-actions">
          <button class="btn btn-secondary" type="button" :disabled="cropDialog.processing" @click="cancelCrop">取消</button>
          <button class="btn btn-success" type="button" :disabled="!cropDialog.ready || cropDialog.processing" @click="confirmCrop">
            {{ cropDialog.processing ? cropDialog.statusText : '裁剪并上传' }}
          </button>
        </div>
        <div v-if="cropDialog.processing" class="crop-loading"><span class="spinner"></span>{{ cropDialog.statusText }}</div>
      </div>
    </div>

    <details class="image-host-fallback">
      <summary>备用图床设置</summary>
      <label>
        Postimages API Key
        <input v-model="imageHostFallbackApiKey" type="password" autocomplete="off" placeholder="留空时使用内置 Key" />
      </label>
    </details>

    <div v-if="errorDialog.visible" class="error-modal" @click.self="closeErrorDialog">
      <div class="error-dialog" role="alertdialog" aria-modal="true" aria-labelledby="error-dialog-title">
        <h3 id="error-dialog-title">上传失败</h3>
        <p>{{ errorDialog.message }}</p>
        <button class="btn btn-primary" type="button" @click="closeErrorDialog">知道了</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue';

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

const POSTIMAGES_API_KEY = 'b3969a2f93b5206ba2e7e0b26c851742';
const POSTIMAGES_GALLERY = 'default';
const IMAGE_HOST_FALLBACK_API_KEY_STORAGE_KEY = 'coffee-theme-maker-postimages-api-key';

let nextCharacterNumber = 1;
let nextInternalId = 1;
let skipNextDraftSave = false;
const generateInternalId = () => nextInternalId++;
const generateCharacterId = () => `character_${nextCharacterNumber++}`;

// 导入错误
const importError = ref('');
const fileInput = ref(null);

// 验证提示展开状态
const showValidation = ref(false);

// 重置所有数据
const resetAll = () => {
  config.value = createDefaultConfig();
  characters.value = [];
  nextCharacterNumber = 1;
  importError.value = '';
  skipNextDraftSave = true;
  localStorage.removeItem(STORAGE_KEY);
  if (fileInput.value) {
    fileInput.value.value = '';
  }
};

// 主题基本配置
const config = ref(createDefaultConfig());
const imageHostFallbackApiKey = ref(localStorage.getItem(IMAGE_HOST_FALLBACK_API_KEY_STORAGE_KEY) || '');

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
    characters: characters.value
  }));
};

const restoreDraft = () => {
  const draft = localStorage.getItem(STORAGE_KEY);
  if (!draft) return;

  try {
    const saved = JSON.parse(draft);
    config.value = { ...createDefaultConfig(), ...saved.config };
    characters.value = Array.isArray(saved.characters)
      ? saved.characters.map(char => ({
        _id: generateInternalId(),
        id: char.id || generateCharacterId(),
        name: char.name || '',
        prizeInput: char.prizeInput || '',
        avatarInput: char.avatarInput || ''
      }))
      : [];
    updateNextCharacterNumber();
  } catch {
    localStorage.removeItem(STORAGE_KEY);
  }
};

onMounted(restoreDraft);
watch([config, characters], saveDraft, { deep: true });
watch(imageHostFallbackApiKey, value => {
  if (value.trim()) {
    localStorage.setItem(IMAGE_HOST_FALLBACK_API_KEY_STORAGE_KEY, value.trim());
  } else {
    localStorage.removeItem(IMAGE_HOST_FALLBACK_API_KEY_STORAGE_KEY);
  }
});

const avatarUploadInputs = new Map();
const uploadingCharacterId = ref(null);
const uploadMessage = ref({ characterId: null, text: '', isError: false });
const errorDialog = ref({ visible: false, message: '' });
const cropImage = ref(null);
const cropDialog = ref({
  visible: false,
  ready: false,
  sourceUrl: '',
  file: null,
  characterId: null,
  x: 0,
  y: 0,
  size: 0,
  sizeRatio: 1,
  naturalWidth: 0,
  naturalHeight: 0,
  displayScale: 1,
  processing: false,
  statusText: ''
});
let cropDragStart = null;

const setAvatarUploadInput = (characterId, element) => {
  if (element) {
    avatarUploadInputs.set(characterId, element);
  } else {
    avatarUploadInputs.delete(characterId);
  }
};

const openAvatarPicker = (characterId) => {
  avatarUploadInputs.get(characterId)?.click();
};

const readImageAsBase64 = file => new Promise((resolve, reject) => {
  const reader = new FileReader();
  reader.onload = () => resolve(String(reader.result).split(',')[1]);
  reader.onerror = () => reject(new Error('图片读取失败'));
  reader.readAsDataURL(file);
});

const getPostimagesDirectUrl = async (file) => {
  const extension = file.name.includes('.') ? file.name.split('.').pop() : '';
  const name = file.name.replace(/\.[^.]+$/, '');
  const form = new URLSearchParams({
    key: imageHostFallbackApiKey.value.trim() || POSTIMAGES_API_KEY,
    gallery: POSTIMAGES_GALLERY,
    o: '2b819584285c102318568238c7d4a4c7',
    m: '59c2ad4b46b0c1e12d5703302bff0120',
    version: '1.0.1',
    portable: '1',
    name,
    type: extension,
    image: await readImageAsBase64(file)
  });
  const uploadResponse = await fetch('/postimages-api/1/upload', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
    body: form
  });
  const uploadResponseBody = await uploadResponse.text();
  console.log('[Postimages] 上传接口返回：', uploadResponseBody);
  const uploadError = uploadResponseBody.match(/<error>([\s\S]*?)<\/error>/)?.[1].trim();
  if (!uploadResponse.ok || uploadError) {
    throw new Error(uploadError || `上传失败（${uploadResponse.status}）`);
  }
  const page = uploadResponseBody.match(/<page>(https:\/\/postimg\.cc\/\w*)<\/page>/)?.[1];
  if (!page) throw new Error('上传成功但未获取到图片页面地址');

  const pageId = new URL(page).pathname.replace(/^\//, '');
  const pageResponse = await fetch(`/postimages-page/${pageId}`);
  const pageResponseBody = await pageResponse.text();
  console.log('[Postimages] 图片页面返回：', pageResponseBody);
  if (!pageResponse.ok) throw new Error(`获取图片链接失败（${pageResponse.status}）`);
  const directUrl = pageResponseBody.match(/https:\/\/i\.postimg\.cc\/\w{8}\/[^"'\s]+\?dl=1/)?.[0];
  if (!directUrl) throw new Error('未能从图片页面解析直链');
  return directUrl;
};

const closeErrorDialog = () => {
  errorDialog.value.visible = false;
};

const uploadAvatar = async (characterIndex, event) => {
  const file = event.target.files?.[0];
  const char = characters.value[characterIndex];
  event.target.value = '';
  if (!file || !char) return;
  if (!file.type.startsWith('image/')) {
    uploadMessage.value = { characterId: char._id, text: '请选择图片文件', isError: true };
    return;
  }

  cropDialog.value = {
    visible: true,
    ready: false,
    sourceUrl: URL.createObjectURL(file),
    file,
    characterId: char._id,
    x: 0,
    y: 0,
    size: 0,
    sizeRatio: 1,
    naturalWidth: 0,
    naturalHeight: 0,
    displayScale: 1,
    processing: false,
    statusText: ''
  };
};

const initializeCrop = () => {
  const image = cropImage.value;
  if (!image) return;
  const maxSize = Math.min(image.naturalWidth, image.naturalHeight);
  cropDialog.value.naturalWidth = image.naturalWidth;
  cropDialog.value.naturalHeight = image.naturalHeight;
  cropDialog.value.size = maxSize;
  cropDialog.value.x = (image.naturalWidth - maxSize) / 2;
  cropDialog.value.y = (image.naturalHeight - maxSize) / 2;
  cropDialog.value.displayScale = image.clientWidth / image.naturalWidth;
  cropDialog.value.ready = true;
};

const updateCropSize = () => {
  const dialog = cropDialog.value;
  const maxSize = Math.min(dialog.naturalWidth, dialog.naturalHeight);
  const newSize = maxSize * dialog.sizeRatio;
  dialog.x = Math.max(0, Math.min(dialog.x + (dialog.size - newSize) / 2, dialog.naturalWidth - newSize));
  dialog.y = Math.max(0, Math.min(dialog.y + (dialog.size - newSize) / 2, dialog.naturalHeight - newSize));
  dialog.size = newSize;
};

const cropSelectionStyle = computed(() => {
  const dialog = cropDialog.value;
  const scale = dialog.displayScale;
  return {
    left: `${dialog.x * scale}px`,
    top: `${dialog.y * scale}px`,
    width: `${dialog.size * scale}px`,
    height: `${dialog.size * scale}px`
  };
});

const startCropDrag = event => {
  const dialog = cropDialog.value;
  cropDragStart = { clientX: event.clientX, clientY: event.clientY, x: dialog.x, y: dialog.y };
  window.addEventListener('pointermove', moveCropDrag);
  window.addEventListener('pointerup', stopCropDrag, { once: true });
};

const moveCropDrag = event => {
  if (!cropDragStart) return;
  const dialog = cropDialog.value;
  dialog.x = Math.max(0, Math.min(cropDragStart.x + (event.clientX - cropDragStart.clientX) / dialog.displayScale, dialog.naturalWidth - dialog.size));
  dialog.y = Math.max(0, Math.min(cropDragStart.y + (event.clientY - cropDragStart.clientY) / dialog.displayScale, dialog.naturalHeight - dialog.size));
};

const stopCropDrag = () => {
  cropDragStart = null;
  window.removeEventListener('pointermove', moveCropDrag);
};

const cancelCrop = () => {
  stopCropDrag();
  if (cropDialog.value.sourceUrl) URL.revokeObjectURL(cropDialog.value.sourceUrl);
  cropDialog.value.visible = false;
};

const createCroppedImage = () => {
  const dialog = cropDialog.value;
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 512;
  const context = canvas.getContext('2d');
  context.drawImage(cropImage.value, dialog.x, dialog.y, dialog.size, dialog.size, 0, 0, 512, 512);
  return new Promise((resolve, reject) => {
    canvas.toBlob(blob => {
      if (blob) resolve(new File([blob], `${dialog.file.name.replace(/\.[^.]+$/, '')}.png`, { type: 'image/png' }));
      else reject(new Error('裁剪图片失败'));
    }, 'image/png');
  });
};

const confirmCrop = async () => {
  const dialog = cropDialog.value;
  const char = characters.value.find(item => item._id === dialog.characterId);
  if (!char) {
    cancelCrop();
    return;
  }

  uploadingCharacterId.value = char._id;
  dialog.processing = true;
  dialog.statusText = '正在生成 PNG...';
  uploadMessage.value = { characterId: char._id, text: '', isError: false };
  try {
    const croppedFile = await createCroppedImage();
    dialog.statusText = '正在上传图片...';
    const url = await getPostimagesDirectUrl(croppedFile);
    char.avatarInput = [char.avatarInput.trim(), url].filter(Boolean).join('\n');
    uploadMessage.value = { characterId: char._id, text: '上传成功，已添加到头像链接', isError: false };
  } catch (error) {
    const detail = error instanceof TypeError ? '网络请求被浏览器拦截，请检查图床是否允许跨域访问' : error.message;
    uploadMessage.value = { characterId: char._id, text: detail, isError: true };
    errorDialog.value = { visible: true, message: detail };
  } finally {
    uploadingCharacterId.value = null;
    cancelCrop();
  }
};

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
  if (characters.value.length === 0) {
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

// 解析 Coffee 文件
const parseCoffeeFile = (content) => {
  try {
    const result = {
      config: {
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
      },
      characters: []
    };

    // 解析基本配置（支持冒号后有空格的格式）
    const nameMatch = content.match(/name:\s*"([^"]*)"/);
    if (nameMatch) result.config.name = nameMatch[1];

    const openingMatch = content.match(/opening:\s*"([^"]*)"/);
    if (openingMatch) result.config.opening = openingMatch[1];

    const authorMatch = content.match(/author:\s*"([^"]*)"/);
    if (authorMatch) result.config.author = authorMatch[1];

    const skinTipMatch = content.match(/skin_tip:\s*"([^"]*)"/);
    if (skinTipMatch) result.config.skin_tip = skinTipMatch[1];

    const colorMatch = content.match(/color:\s*"([^"]*)"/);
    if (colorMatch) result.config.color = colorMatch[1];

    const bgColorMatch = content.match(/background_color:\s*"([^"]*)"/);
    if (bgColorMatch) result.config.background_color = bgColorMatch[1];

    const lockableMatch = content.match(/lockable:\s*(true|false)/);
    if (lockableMatch) result.config.lockable = lockableMatch[1] === 'true';

    // 解析 skins 部分
    const skinsMatch = content.match(/skins:\s*([\s\S]+)/);
    if (skinsMatch) {
      const skinsContent = skinsMatch[1];
      // 匹配每个 skin 块 - 按照顶格的ID后跟冒号来分割
      const skinBlocks = skinsContent.match(/(?:^|\n)( {8}?)([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*[\s\S]*?(?=\n\s{0,8}[a-zA-Z_][a-zA-Z0-9_]*\s*:|$)/g);
      if (skinBlocks) {
        skinBlocks.forEach(block => {
          const idMatch = block.match(/([a-zA-Z_][a-zA-Z0-9_]*)\s*:/);
          if (!idMatch) return;

          const char = {
            _id: generateInternalId(),
            id: idMatch[1],
            name: '',
            prizeInput: '',
            avatarInput: ''
          };

          // 解析 name（支持空格）
          const nameMatch = block.match(/name:\s*"([^"]*)"/);
          if (nameMatch) char.name = nameMatch[1];

          // 解析 prize (可以是字符串或数组，支持空数组)
          const prizeArrayMatch = block.match(/prize:\s*\[([^\]]*)\]/);
          const prizeStringMatch = block.match(/prize:\s*"([^"]*)"/);
          if (prizeArrayMatch) {
            const prizes = prizeArrayMatch[1].split(',').map(p => p.trim().replace(/"/g, '')).filter(p => p);
            char.prizeInput = prizes.join('\n');
          } else if (prizeStringMatch) {
            char.prizeInput = prizeStringMatch[1];
          }

          // 解析 avatar (可以是字符串或数组，支持空格)
          const avatarArrayMatch = block.match(/avatar:\s*\[([^\]]*)\]/);
          const avatarStringMatch = block.match(/avatar:\s*"([^"]*)"/);
          if (avatarArrayMatch) {
            const avatars = avatarArrayMatch[1].split(',').map(a => a.trim().replace(/"/g, '')).filter(a => a);
            char.avatarInput = avatars.join('\n');
          } else if (avatarStringMatch) {
            char.avatarInput = avatarStringMatch[1];
          }

          result.characters.push(char);
        });
      }
    }

    return result;
  } catch (e) {
    console.error('解析失败:', e);
    return null;
  }
};

// 处理文件导入
const handleFileImport = (event) => {
  const file = event.target.files[0];
  if (!file) return;

  importError.value = '';

  const reader = new FileReader();
  reader.onload = (e) => {
    const content = e.target.result;
    const parsed = parseCoffeeFile(content);

    if (parsed) {
      config.value = parsed.config;
      characters.value = parsed.characters;
      updateNextCharacterNumber();
      importError.value = '';
    } else {
      importError.value = '文件解析失败，请确保是正确的 .coffee 格式';
    }
  };
  reader.onerror = () => {
    importError.value = '文件读取失败';
  };
  reader.readAsText(file);
};

// 生成 CoffeeScript 代码
const generatedCoffee = computed(() => {
  const now = new Date().toISOString();

  let coffee = `module.exports=\n`;
  coffee += `    name:"${config.value.name}"\n`;
  coffee += `    opening:"${config.value.opening}"\n`;
  coffee += `    skin_tip:"${config.value.skin_tip}"\n`;
  coffee += `    author:"${config.value.author}"\n`;
  coffee += `    lastModified:"${now}"\n`;
  coffee += `    vote:"${config.value.vote}"\n`;
  coffee += `    sunrise:"${config.value.sunrise}"\n`;
  coffee += `    sunset:"${config.value.sunset}"\n`;
  coffee += `    icon:"${config.value.icon}"\n`;
  coffee += `    background_color:"${config.value.background_color}"\n`;
  coffee += `    color:"${config.value.color}"\n`;
  coffee += `    lockable:${config.value.lockable}\n`;
  coffee += `    isAvailable:->
        return true
    skins:\n`;

  characters.value.forEach(char => {
    const avatars = getAvatars(char);
    const prizes = getPrizes(char);
    coffee += `        ${char.id}:\n`;

    if (avatars.length === 1) {
      coffee += `            avatar:"${avatars[0]}"\n`;
    } else if (avatars.length > 1) {
      coffee += `            avatar:[${avatars.map(a => `"${a}"`).join(',')}]\n`;
    } else {
      coffee += `            avatar:""\n`;
    }

    coffee += `            name:"${char.name}"\n`;
    if (prizes.length === 1) {
      coffee += `            prize:"${prizes[0]}"\n`;
    } else if (prizes.length > 1) {
      coffee += `            prize:[${prizes.map(p => `"${p}"`).join(',')}]\n`;
    } else {
      coffee += `            prize:""\n`;
    }
  });

  return coffee;
});

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

.success-msg {
  font-size: 12px;
  color: #198754;
  margin-top: 4px;
}

.upload-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 8px;
}

.avatar-upload-input {
  display: none;
}

.btn-upload {
  padding: 7px 12px;
  font-size: 12px;
}

.crop-modal {
  position: fixed;
  z-index: 10;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  background: rgba(0, 0, 0, 0.6);
}

.image-host-fallback {
  position: fixed;
  right: 12px;
  bottom: 10px;
  z-index: 5;
  max-width: calc(100vw - 24px);
  color: #777;
  font-size: 12px;
}

.image-host-fallback summary {
  cursor: pointer;
  user-select: none;
}

.image-host-fallback label {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 250px;
  margin-top: 6px;
  padding: 8px;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: #fff;
}

.image-host-fallback input {
  min-width: 0;
  padding: 6px 8px;
  border: 1px solid #ddd;
  border-radius: 3px;
  font-size: 12px;
}

.error-modal {
  position: fixed;
  z-index: 20;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  background: rgba(0, 0, 0, 0.45);
}

.error-dialog {
  width: min(100%, 420px);
  padding: 20px;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.2);
}

.error-dialog h3 {
  margin: 0;
  color: #b42318;
  font-size: 18px;
}

.error-dialog p {
  margin: 12px 0 18px;
  color: #444;
  line-height: 1.5;
  overflow-wrap: anywhere;
}

.error-dialog .btn {
  display: block;
  margin-left: auto;
}

.crop-dialog {
  width: min(100%, 560px);
  padding: 20px;
  border-radius: 8px;
  background: #fff;
}

.crop-dialog-header,
.crop-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.crop-dialog-header h3 {
  margin: 0;
  color: #333;
}

.btn-close {
  border: none;
  background: none;
  color: #666;
  font-size: 28px;
  line-height: 1;
  cursor: pointer;
}

.crop-stage {
  position: relative;
  display: flex;
  width: fit-content;
  max-width: 100%;
  justify-content: center;
  margin: 16px auto;
  overflow: hidden;
  background: #222;
}

.crop-stage img {
  display: block;
  max-width: 100%;
  max-height: 55vh;
  user-select: none;
}

.crop-selection {
  position: absolute;
  box-sizing: border-box;
  border: 2px solid #fff;
  box-shadow: 0 0 0 9999px rgba(0, 0, 0, 0.5);
  cursor: move;
  touch-action: none;
}

.crop-size-control {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #555;
  font-size: 14px;
}

.crop-size-control input {
  flex: 1;
}

.crop-actions {
  justify-content: flex-end;
  gap: 10px;
  margin-top: 20px;
}

.crop-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-top: 16px;
  padding: 10px;
  color: #555;
  font-size: 14px;
}

.spinner {
  width: 18px;
  height: 18px;
  box-sizing: border-box;
  border: 2px solid #c8d2dc;
  border-top-color: #007bff;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
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
