/**
 * zip：发布到临时目录 → 打包 zip → 页面下载
 * folder：直接发布到项目文件夹（publish\），不打包 zip，可选构建完成后直接运行
 */
export type BuildMode = 'zip' | 'folder'

export type BuildPhase =
  | 'idle'
  | 'publishing'
  | 'packaging'
  | 'launching'
  | 'success'
  | 'failed'

export interface BuildOptions {
  mode: BuildMode
  /** 仅 folder 模式有效：构建成功后自动运行程序 */
  autoRun: boolean
}

export type LogStream = 'stdout' | 'stderr' | 'system'
export type LogLevel = 'info' | 'warning' | 'error'

export interface LogLine {
  seq: number
  time: string
  stream: LogStream
  level: LogLevel
  text: string
}

export interface ArtifactInfo {
  fileName: string
  sizeBytes: number
  createdAt: string
  downloadUrl: string
}

/** folder 模式的产物：项目文件夹里的发布目录 */
export interface OutputInfo {
  /** 发布目录绝对路径 */
  directory: string
  exeName: string
  createdAt: string
}

export interface BuildSnapshot {
  id: string | null
  /** 最近一次构建使用的模式；服务重启后尚未构建过时为 null */
  mode: BuildMode | null
  autoRun: boolean
  phase: BuildPhase
  running: boolean
  startedAt: string | null
  finishedAt: string | null
  durationMs: number | null
  exitCode: number | null
  error: string | null
  /** 构建本身成功，但自动运行失败（如拒绝了 UAC 授权） */
  launchError: string | null
  artifact: ArtifactInfo | null
  output: OutputInfo | null
  logs: LogLine[]
}

export type BuildStatus = Omit<BuildSnapshot, 'logs'>

export type BuildEvent =
  | { type: 'snapshot'; snapshot: BuildSnapshot }
  | { type: 'log'; line: LogLine }
  | { type: 'status'; status: BuildStatus }

export interface LaunchResult {
  pid: number | null
}
