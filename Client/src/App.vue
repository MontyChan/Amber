<script setup lang="ts">
import { onMounted, ref } from 'vue'

type Archive = { id: number; createdAt: string; note?: string; tags?: string; fileCount: number }
type FileRow = { id?: number; name?: string; relativePath: string; size: number; modifiedAt: string; isStored: boolean; isDirectory?: boolean }

const databasePath = ref('')
const archiveDirectory = ref('')
const archives = ref<Archive[]>([])
const selectedArchive = ref<Archive | null>(null)
const files = ref<FileRow[]>([])
const total = ref(0)
const search = ref('')
const prefix = ref('')
const page = ref(1)
const busy = ref(false)
const message = ref('')
const error = ref('')
const outputDirectory = ref('')
const extractTarget = ref<FileRow | null | undefined>(undefined)
const downloadFileName = ref('')
const downloadProgress = ref(0)
const downloading = ref(false)
let messageTimer: number | undefined

async function request(url: string, init?: RequestInit) {
  const response = await fetch(url, init)
  const data = await response.json()
  if (!response.ok) throw new Error(data.message ?? 'Request failed')
  return data
}

async function loadSettings() {
  try {
    const data = await request('/api/settings')
    databasePath.value = data.databasePath
    archiveDirectory.value = data.archiveOutputDirectory
    await loadArchives()
  } catch (e) { error.value = (e as Error).message }
}

async function openStorage() {
  error.value = ''; message.value = ''; busy.value = true
  try {
    await request('/api/settings/open', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ databasePath: databasePath.value, archiveOutputDirectory: archiveDirectory.value }) })
    selectedArchive.value = null; files.value = []; prefix.value = ''; search.value = ''
    await loadArchives(); showMessage('Storage loaded')
  } catch (e) { error.value = (e as Error).message } finally { busy.value = false }
}

async function loadArchives() {
  archives.value = await request('/api/archives')
  if (!selectedArchive.value && archives.value.length) await selectArchive(archives.value[0])
}

async function selectArchive(archive: Archive) {
  selectedArchive.value = archive; prefix.value = ''; search.value = ''; page.value = 1; await loadFiles()
}

async function loadFiles() {
  if (!selectedArchive.value) return
  message.value = ''
  const params = new URLSearchParams({ prefix: prefix.value, query: search.value, page: String(page.value), pageSize: '200' })
  const data = search.value
    ? await request(`/api/archives/${selectedArchive.value.id}/files?${params}`)
    : { files: await request(`/api/archives/${selectedArchive.value.id}/tree?path=${encodeURIComponent(prefix.value)}`), total: 0 }
  files.value = data.files; total.value = data.total
}

function openDirectory(directory: string) { prefix.value = directory; search.value = ''; page.value = 1; loadFiles() }
function clearDirectory() { prefix.value = ''; search.value = ''; page.value = 1; loadFiles() }
function goUp() { prefix.value = prefix.value.split('/').slice(0, -1).join('/'); page.value = 1; loadFiles() }
function showMessage(value: string) { message.value = value; window.clearTimeout(messageTimer); messageTimer = window.setTimeout(() => { message.value = '' }, 4500) }
function dismissMessage() { message.value = ''; window.clearTimeout(messageTimer) }
function formatSize(size: number) { if (size < 1024) return `${size} B`; if (size < 1024 ** 2) return `${(size / 1024).toFixed(1)} KB`; if (size < 1024 ** 3) return `${(size / 1024 ** 2).toFixed(1)} MB`; return `${(size / 1024 ** 3).toFixed(1)} GB` }
function formatDate(value: string) { return new Date(value).toLocaleString() }
function fileName(file: FileRow) { return file.name || file.relativePath.split('/').pop() || file.relativePath }
function downloadUrl(file: FileRow) { return `/api/archives/${selectedArchive.value?.id}/download?path=${encodeURIComponent(file.relativePath)}` }

async function downloadFile(file: FileRow) {
  if (!selectedArchive.value || downloading.value) return
  downloading.value = true; downloadFileName.value = fileName(file); downloadProgress.value = 0; error.value = ''
  const picker = (window as Window & { showSaveFilePicker?: (options?: unknown) => Promise<{ createWritable: () => Promise<{ write: (value: Blob | Uint8Array) => Promise<void>; close: () => Promise<void>; abort?: () => Promise<void> }> }> }).showSaveFilePicker
  let writable: { write: (value: Blob | Uint8Array) => Promise<void>; close: () => Promise<void>; abort?: () => Promise<void> } | undefined
  try {
    if (picker) {
      const handle = await picker({ suggestedName: downloadFileName.value })
      writable = await handle.createWritable()
    }
    const response = await fetch(downloadUrl(file))
    if (!response.ok || !response.body) throw new Error('Download failed')
    const totalBytes = Number(response.headers.get('content-length') ?? 0)
    const reader = response.body.getReader()
    const chunks: Uint8Array[] = []
    let received = 0
    while (true) {
      const result = await reader.read()
      if (result.done) break
      if (result.value) {
        if (writable) await writable.write(result.value)
        else chunks.push(result.value)
        received += result.value.length
        downloadProgress.value = totalBytes > 0 ? Math.min(100, Math.round(received / totalBytes * 100)) : 0
      }
    }
    if (writable) {
      await writable.close()
    } else {
      const blob = new Blob(chunks)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a'); link.href = url; link.download = downloadFileName.value; link.click(); URL.revokeObjectURL(url)
    }
    downloadProgress.value = 100; showMessage(`Downloaded: ${downloadFileName.value}`)
  } catch (e) {
    await writable?.abort?.()
    if ((e as Error).name !== 'AbortError') error.value = (e as Error).message
  } finally { downloading.value = false }
}

function extract(file?: FileRow) {
  if (!selectedArchive.value) return
  extractTarget.value = file ?? null
}

async function confirmExtract() {
  if (!selectedArchive.value || !outputDirectory.value.trim()) return
  busy.value = true; error.value = ''; message.value = ''
  try {
    await request(`/api/archives/${selectedArchive.value.id}/extract`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ relativePath: extractTarget.value?.relativePath ?? '', outputDirectory: outputDirectory.value.trim() }) })
    showMessage(`Extracted: ${extractTarget.value?.relativePath ?? 'archive'}`)
    extractTarget.value = undefined
  } catch (e) { error.value = (e as Error).message } finally { busy.value = false }
}

onMounted(loadSettings)
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <div class="brand"><div><div class="eyebrow">LOCAL ARCHIVE</div><h1>Amber</h1></div></div>
      <div class="topbar-status"><span class="status-dot" :class="{ busy }"></span>{{ busy ? 'Working' : 'Ready' }}</div>
    </header>

    <main class="app-layout">
      <aside class="sidebar">
        <div class="sidebar-label">Storage</div>
        <label>Database<input v-model="databasePath"></label>
        <label>Archive directory<input v-model="archiveDirectory"></label>
        <button class="button primary full" :disabled="busy" @click="openStorage">Load storage</button>
        <div class="sidebar-rule"></div>
        <div class="sidebar-label archive-label">Archives <span>{{ archives.length }}</span></div>
        <div class="archive-list">
          <button v-for="archive in archives" :key="archive.id" class="archive-item" :class="{ active: selectedArchive?.id === archive.id }" @click="selectArchive(archive)">
            <span class="archive-id">#{{ archive.id }}</span><strong>{{ archive.note || 'Untitled archive' }}</strong><small>{{ archive.fileCount.toLocaleString() }} files</small>
          </button>
        </div>
      </aside>

      <section class="content">
        <div v-if="error" class="notice error">{{ error }}</div><div v-if="message" class="notice success"><span>{{ message }}</span><button class="notice-close" aria-label="Dismiss" @click="dismissMessage">×</button></div>
        <template v-if="selectedArchive">
          <div class="content-heading"><div><div class="eyebrow">ARCHIVE #{{ selectedArchive.id }}</div><h2>{{ selectedArchive.note || 'Untitled archive' }}</h2><p>{{ selectedArchive.tags || 'No tags' }} <span class="divider">·</span> {{ selectedArchive.fileCount.toLocaleString() }} files</p></div><button class="button primary" @click="extract()">Extract archive</button></div>
          <div class="toolbar"><button v-if="prefix && !search" class="text-button" @click="goUp">Back</button><button v-if="prefix || search" class="text-button muted" @click="clearDirectory">Clear</button><span class="path">{{ prefix ? `/${prefix}` : '/ root' }}</span><div class="search-box"><span>⌕</span><input v-model="search" placeholder="Search files" @keyup.enter="page = 1; loadFiles()"></div><button class="button" @click="page = 1; loadFiles()">Search</button></div>
          <div class="table-wrap"><table><thead><tr><th>Name</th><th>Location</th><th>Size</th><th>Modified</th><th></th></tr></thead><tbody><tr v-for="file in files" :key="file.relativePath" class="file-row"><td><button v-if="file.isDirectory" class="directory-link" @click="openDirectory(file.relativePath)"><span class="folder-icon">▸</span>{{ fileName(file) }}</button><span v-else class="file-name"><span class="file-icon">·</span>{{ fileName(file) }}</span></td><td class="file-path">{{ file.relativePath }}</td><td>{{ file.isDirectory ? '—' : formatSize(file.size) }}</td><td>{{ file.isDirectory ? '—' : formatDate(file.modifiedAt) }}</td><td class="actions"><button v-if="!file.isDirectory" class="link-button" :disabled="downloading" @click="downloadFile(file)">Download</button><button v-if="!file.isDirectory" class="link-button" :disabled="downloading" @click="extract(file)">Extract</button></td></tr><tr v-if="!files.length"><td colspan="5" class="empty">No files found.</td></tr></tbody></table></div>
          <div v-if="search" class="pager"><span>{{ total.toLocaleString() }} results</span><div><button class="button subtle" :disabled="page <= 1" @click="page--; loadFiles()">Previous</button><span>Page {{ page }}</span><button class="button subtle" :disabled="page * 200 >= total" @click="page++; loadFiles()">Next</button></div></div>
        </template>
        <div v-else class="empty-state"><div class="empty-mark">A</div><h2>Select an archive</h2><p>Choose an archive from the sidebar to browse its indexed files.</p></div>
      </section>
    </main>

    <div v-if="extractTarget !== undefined" class="modal-backdrop" @click.self="extractTarget = undefined"><section class="modal"><div class="modal-heading"><div><div class="eyebrow">RESTORE</div><h2>{{ extractTarget ? 'Extract file' : 'Extract archive' }}</h2><p>{{ extractTarget?.relativePath || selectedArchive?.note || `Archive #${selectedArchive?.id}` }}</p></div><button class="icon-button" aria-label="Close" @click="extractTarget = undefined">×</button></div><label class="output-field">Output directory<input v-model="outputDirectory" placeholder="F:\\Restored" @keyup.enter="confirmExtract"></label><div class="modal-actions"><button class="button" @click="extractTarget = undefined">Cancel</button><button class="button primary" :disabled="busy || !outputDirectory.trim()" @click="confirmExtract">{{ busy ? 'Extracting…' : 'Extract' }}</button></div></section></div>
    <div v-if="downloading" class="download-toast"><div class="download-line"><span>Downloading {{ downloadFileName }}</span><strong>{{ downloadProgress ? `${downloadProgress}%` : 'Preparing…' }}</strong></div><div class="progress-track"><div class="progress-value" :style="{ width: `${downloadProgress}%` }"></div></div></div>
  </div>
</template>
