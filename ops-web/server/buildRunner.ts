import { execFile, spawn } from 'node:child_process'
import { EventEmitter } from 'node:events'
import { createWriteStream } from 'node:fs'
import { mkdir, open, readdir, rm, stat } from 'node:fs/promises'
import path from 'node:path'
import { StringDecoder } from 'node:string_decoder'
import { fileURLToPath } from 'node:url'
import { ZipArchive } from 'archiver'
import type {
  ArtifactInfo,
  BuildOptions,
  BuildPhase,
  BuildSnapshot,
  BuildStatus,
  LaunchResult,
  LogLevel,
  LogLine,
  LogStream,
  OutputInfo,
} from '../shared/types.ts'

const MAX_LOG_LINES = 5000

const serverDir = path.dirname(fileURLToPath(import.meta.url))
/** 仓库根目录（ops-web/server 的上两级） */
export const repoRoot = path.resolve(serverDir, '..', '..')
export const opsWebRoot = path.resolve(serverDir, '..')
export const artifactsDir = path.join(opsWebRoot, 'artifacts')
const projectFile = path.join(repoRoot, 'KeyMouseSyncReplica.csproj')
/** 「直接构建到项目文件夹」模式的发布目录（已在 .gitignore 中忽略） */
export const projectOutputDir = path.join(repoRoot, 'publish')
const exeName = `${path.basename(projectFile, '.csproj')}.exe`

export class BuildBusyError extends Error {
  readonly code = 'BUILD_BUSY'
  constructor(message = '已有构建正在进行中') {
    super(message)
    this.name = 'BuildBusyError'
  }
}

export class NoOutputError extends Error {
  readonly code = 'NO_OUTPUT'
  constructor() {
    super('项目文件夹里还没有可运行的程序，请先用「直接构建到项目文件夹」构建一次')
    this.name = 'NoOutputError'
  }
}

export class LaunchError extends Error {
  readonly code = 'LAUNCH_FAILED'
  constructor(message: string) {
    super(message)
    this.name = 'LaunchError'
  }
}

function pad(n: number): string {
  return String(n).padStart(2, '0')
}

/** 本地时间戳，如 20261022-215903，用作构建 id 与产物名 */
function timestampId(date = new Date()): string {
  return (
    `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}` +
    `-${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`
  )
}

function detectLevel(text: string, stream: LogStream): LogLevel {
  if (stream === 'system') return 'info'
  if (/\berror\b/i.test(text) || /错误/.test(text)) return 'error'
  if (/\bwarning\b/i.test(text) || /警告/.test(text)) return 'warning'
  return 'info'
}

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

/** 运行 PowerShell 脚本；路径等参数一律走环境变量，避免拼接进脚本造成转义问题 */
function runPowerShell(
  script: string,
  env: Record<string, string>,
  timeoutMs: number,
): Promise<string> {
  const prelude = '$ErrorActionPreference = "Stop"; [Console]::OutputEncoding = [Text.Encoding]::UTF8; '
  return new Promise((resolve, reject) => {
    execFile(
      'powershell.exe',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', prelude + script],
      {
        env: { ...process.env, ...env },
        windowsHide: true,
        timeout: timeoutMs,
        encoding: 'utf8',
      },
      (err, stdout, stderr) => {
        if (err) {
          const detail = stderr.split(/\r?\n/).find((l) => l.trim().length > 0)?.trim()
          const reason = err.killed ? '等待超时' : (detail ?? err.message)
          reject(new Error(reason))
          return
        }
        resolve(stdout)
      },
    )
  })
}

/**
 * 返回发布目录里被占用（无法以写模式打开）的文件。
 * 正在运行的 exe/dll 会被系统锁定，dotnet publish 覆盖它们时会失败。
 * 以管理员身份运行的程序，普通权限下读不到它的路径/PID，所以直接探测文件占用最可靠。
 */
async function findLockedFiles(directory: string): Promise<string[]> {
  const candidates = [exeName, exeName.replace(/\.exe$/i, '.dll')]
  const locked: string[] = []
  for (const name of candidates) {
    try {
      const handle = await open(path.join(directory, name), 'r+')
      await handle.close()
    } catch (err) {
      const code = (err as NodeJS.ErrnoException).code
      if (code === 'EBUSY' || code === 'EPERM' || code === 'EACCES') locked.push(name)
      // ENOENT 等：文件还不存在，说明没有被占用
    }
  }
  return locked
}

type RunnerEvents = {
  log: [line: LogLine]
  status: [status: BuildStatus]
}

class BuildRunner extends EventEmitter<RunnerEvents> {
  private phase: BuildPhase = 'idle'
  private buildId: string | null = null
  private options: BuildOptions | null = null
  private startedAt: string | null = null
  private finishedAt: string | null = null
  private exitCode: number | null = null
  private error: string | null = null
  private launchError: string | null = null
  private artifact: ArtifactInfo | null = null
  private output: OutputInfo | null = null
  private launchInFlight = false
  private logs: LogLine[] = []
  private seq = 0

  get running(): boolean {
    return (
      this.phase === 'publishing' || this.phase === 'packaging' || this.phase === 'launching'
    )
  }

  status(): BuildStatus {
    const started = this.startedAt ? Date.parse(this.startedAt) : null
    const finished = this.finishedAt ? Date.parse(this.finishedAt) : null
    return {
      id: this.buildId,
      mode: this.options?.mode ?? null,
      autoRun: this.options?.autoRun ?? false,
      phase: this.phase,
      running: this.running,
      startedAt: this.startedAt,
      finishedAt: this.finishedAt,
      durationMs:
        started !== null ? (finished ?? Date.now()) - started : null,
      exitCode: this.exitCode,
      error: this.error,
      launchError: this.launchError,
      artifact: this.artifact,
      output: this.output,
    }
  }

  snapshot(): BuildSnapshot {
    return { ...this.status(), logs: [...this.logs] }
  }

  /** 服务重启后找回最近一次的 zip 与项目文件夹产物 */
  async restoreLatest(): Promise<void> {
    if (this.buildId) return
    await this.restoreArtifact()
    await this.restoreOutput()
  }

  private async restoreArtifact(): Promise<void> {
    try {
      const entries = await readdir(artifactsDir, { withFileTypes: true })
      const zips = entries.filter((e) => e.isFile() && e.name.endsWith('.zip'))
      let latest: ArtifactInfo | null = null
      for (const zip of zips) {
        const full = path.join(artifactsDir, zip.name)
        const info = await stat(full)
        if (!latest || info.mtimeMs > Date.parse(latest.createdAt)) {
          latest = {
            fileName: zip.name,
            sizeBytes: info.size,
            createdAt: info.mtime.toISOString(),
            downloadUrl: '/api/artifacts/latest/download',
          }
        }
      }
      if (latest && !this.buildId) this.artifact = latest
    } catch {
      // artifacts 目录还不存在，忽略
    }
  }

  private async restoreOutput(): Promise<void> {
    try {
      const info = await stat(path.join(projectOutputDir, exeName))
      if (info.isFile() && !this.buildId) {
        this.output = {
          directory: projectOutputDir,
          exeName,
          createdAt: info.mtime.toISOString(),
        }
      }
    } catch {
      // 还没有构建到项目文件夹，忽略
    }
  }

  startBuild(options: BuildOptions): BuildSnapshot {
    if (this.running) throw new BuildBusyError()
    if (this.launchInFlight) throw new BuildBusyError('程序正在启动中，请稍后再构建')
    const now = new Date()
    this.buildId = timestampId(now)
    this.options = { mode: options.mode, autoRun: options.mode === 'folder' && options.autoRun }
    this.phase = 'publishing'
    this.startedAt = now.toISOString()
    this.finishedAt = null
    this.exitCode = null
    this.error = null
    this.launchError = null
    this.logs = []
    this.seq = 0
    this.emitStatus()
    void this.run(this.buildId, this.options)
    return this.snapshot()
  }

  /** 以管理员权限运行项目文件夹里的程序（程序清单要求 requireAdministrator，会弹 UAC） */
  async launchApp(): Promise<LaunchResult> {
    if (this.launchInFlight) throw new BuildBusyError('程序正在启动中')
    const output = this.output
    if (!output) throw new NoOutputError()
    const exePath = path.join(output.directory, output.exeName)
    const exists = await stat(exePath).then(
      (s) => s.isFile(),
      () => false,
    )
    if (!exists) {
      this.output = null
      this.emitStatus()
      throw new NoOutputError()
    }

    this.launchInFlight = true
    try {
      this.appendLog('system', `正在启动 ${output.exeName}（程序需要管理员权限，请在 UAC 弹窗中确认）…`)
      const stdout = await runPowerShell(
        '$p = Start-Process -FilePath $env:KMS_EXE -WorkingDirectory $env:KMS_DIR -Verb RunAs -PassThru; $p.Id',
        { KMS_EXE: exePath, KMS_DIR: output.directory },
        150_000,
      )
      const pid = Number.parseInt(stdout.trim(), 10)
      const result: LaunchResult = { pid: Number.isNaN(pid) ? null : pid }
      this.appendLog('system', result.pid ? `程序已启动（PID ${result.pid}）` : '程序已启动')
      if (this.launchError) {
        // 之前自动运行失败，这次手动运行成功了，清掉旧的失败提示
        this.launchError = null
        this.emitStatus()
      }
      return result
    } catch (err) {
      const message = `启动失败：${errorMessage(err)}（如果弹出了 UAC 窗口，请点「是」）`
      this.appendLog('system', message)
      throw new LaunchError(message)
    } finally {
      this.launchInFlight = false
    }
  }

  private emitStatus(): void {
    this.emit('status', this.status())
  }

  private appendLog(stream: LogStream, text: string): void {
    const line: LogLine = {
      seq: ++this.seq,
      time: new Date().toISOString(),
      stream,
      level: detectLevel(text, stream),
      text,
    }
    this.logs.push(line)
    if (this.logs.length > MAX_LOG_LINES) {
      this.logs.splice(0, this.logs.length - MAX_LOG_LINES)
    }
    this.emit('log', line)
  }

  private appendChunk(stream: LogStream, chunk: string, rest: { value: string }): void {
    const data = rest.value + chunk
    const parts = data.split(/\r\n|\r|\n/)
    rest.value = parts.pop() ?? ''
    for (const part of parts) {
      if (part.length > 0) this.appendLog(stream, part)
    }
  }

  private async run(id: string, options: BuildOptions): Promise<void> {
    try {
      if (options.mode === 'zip') {
        await this.buildZip(id)
      } else {
        await this.buildFolder(id, options.autoRun)
      }
      this.phase = 'success'
    } catch (err) {
      this.phase = 'failed'
      this.error = errorMessage(err)
      this.appendLog('system', `构建失败：${this.error}`)
    } finally {
      this.finishedAt = new Date().toISOString()
      this.emitStatus()
    }
  }

  /** zip 模式：发布到临时目录 → 打包 → 删除临时目录，只保留 zip */
  private async buildZip(id: string): Promise<void> {
    const publishDir = path.join(artifactsDir, id, 'publish')
    const zipName = `KeyMouseSyncReplica-${id}-win-x86.zip`
    const zipPath = path.join(artifactsDir, zipName)
    try {
      await mkdir(publishDir, { recursive: true })
      this.appendLog('system', `开始构建 ${id}：dotnet publish -c Release`)
      const code = await this.runPublish(publishDir)
      this.exitCode = code
      if (code !== 0) {
        throw new Error(`dotnet publish 失败，退出码 ${code}（详见日志）`)
      }

      this.phase = 'packaging'
      this.emitStatus()
      this.appendLog('system', '发布成功，正在打包 zip…')
      const size = await this.zipDirectory(publishDir, zipPath)
      this.artifact = {
        fileName: zipName,
        sizeBytes: size,
        createdAt: new Date().toISOString(),
        downloadUrl: '/api/artifacts/latest/download',
      }
      // 只保留 zip，删掉中间 publish 目录（约 148MB）
      await rm(path.join(artifactsDir, id), { recursive: true, force: true })
      this.appendLog('system', `打包完成：${zipName}`)
    } catch (err) {
      // 失败时清掉半成品，保留日志供页面排查
      await rm(path.join(artifactsDir, id), { recursive: true, force: true }).catch(
        () => undefined,
      )
      await rm(zipPath, { force: true }).catch(() => undefined)
      throw err
    }
  }

  /** folder 模式：直接发布到项目文件夹，不打包；可选构建完成后直接运行 */
  private async buildFolder(id: string, autoRun: boolean): Promise<void> {
    this.appendLog(
      'system',
      `开始构建 ${id}：dotnet publish -c Release → ${projectOutputDir}（不打包 zip）`,
    )
    await this.ensureNotRunning()

    await mkdir(projectOutputDir, { recursive: true })
    const code = await this.runPublish(projectOutputDir)
    this.exitCode = code
    if (code !== 0) {
      throw new Error(
        `dotnet publish 失败，退出码 ${code}（详见日志）。若程序仍在运行导致文件被占用，请先关闭它再重试`,
      )
    }

    const info = await stat(path.join(projectOutputDir, exeName)).catch(() => null)
    if (!info?.isFile()) {
      throw new Error(`发布成功但在 ${projectOutputDir} 中没有找到 ${exeName}`)
    }
    this.output = {
      directory: projectOutputDir,
      exeName,
      createdAt: new Date().toISOString(),
    }
    this.appendLog('system', `发布完成：${path.join(projectOutputDir, exeName)}`)

    if (!autoRun) return
    this.phase = 'launching'
    this.emitStatus()
    try {
      await this.launchApp()
    } catch (err) {
      // 构建本身已成功，启动失败只作提示，用户可稍后点「运行」重试
      this.launchError = errorMessage(err)
    }
  }

  /** 程序还在运行时 dotnet publish 无法覆盖 exe/dll，提前检测并给出明确提示 */
  private async ensureNotRunning(): Promise<void> {
    const locked = await findLockedFiles(projectOutputDir)
    if (locked.length > 0) {
      throw new Error(
        `${locked.join('、')} 正在被占用，通常是 ${exeName} 还在运行，无法覆盖。请先手动关闭程序，再重新构建`,
      )
    }
  }

  private runPublish(publishDir: string): Promise<number> {
    return new Promise((resolve, reject) => {
      const child = spawn(
        'dotnet',
        ['publish', projectFile, '-c', 'Release', '-o', publishDir, '--nologo'],
        {
          cwd: repoRoot,
          shell: false,
          windowsHide: true,
          env: {
            ...process.env,
            DOTNET_CLI_UI_LANGUAGE: 'en',
            DOTNET_NOLOGO: '1',
          },
        },
      )

      const stdoutRest = { value: '' }
      const stderrRest = { value: '' }
      const stdoutDecoder = new StringDecoder('utf8')
      const stderrDecoder = new StringDecoder('utf8')

      child.stdout.on('data', (buf: Buffer) => {
        this.appendChunk('stdout', stdoutDecoder.write(buf), stdoutRest)
      })
      child.stderr.on('data', (buf: Buffer) => {
        this.appendChunk('stderr', stderrDecoder.write(buf), stderrRest)
      })

      child.on('error', (err: NodeJS.ErrnoException) => {
        if (err.code === 'ENOENT') {
          reject(new Error('未找到 dotnet 命令，请确认已安装 .NET SDK 并加入 PATH'))
        } else {
          reject(err)
        }
      })
      child.on('close', (code) => {
        const tailOut = stdoutRest.value + stdoutDecoder.end()
        if (tailOut) this.appendLog('stdout', tailOut)
        const tailErr = stderrRest.value + stderrDecoder.end()
        if (tailErr) this.appendLog('stderr', tailErr)
        resolve(code ?? -1)
      })
    })
  }

  private zipDirectory(sourceDir: string, zipPath: string): Promise<number> {
    return new Promise((resolve, reject) => {
      const output = createWriteStream(zipPath)
      const archive = new ZipArchive({ zlib: { level: 6 } })
      output.on('close', () => resolve(archive.pointer()))
      output.on('error', reject)
      archive.on('error', reject)
      archive.on('warning', (err) => this.appendLog('system', `打包警告：${err.message}`))
      archive.pipe(output)
      archive.directory(sourceDir, false)
      void archive.finalize()
    })
  }
}

export const buildRunner = new BuildRunner()
