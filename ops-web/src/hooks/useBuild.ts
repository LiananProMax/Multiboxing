import { useCallback, useEffect, useRef, useState } from 'react'
import type {
  BuildEvent,
  BuildOptions,
  BuildSnapshot,
  BuildStatus,
  LaunchResult,
  LogLine,
} from '../../shared/types'

const EMPTY_STATUS: BuildStatus = {
  id: null,
  mode: null,
  autoRun: false,
  phase: 'idle',
  running: false,
  startedAt: null,
  finishedAt: null,
  durationMs: null,
  exitCode: null,
  error: null,
  launchError: null,
  artifact: null,
  output: null,
}

export interface Notice {
  type: 'info' | 'error'
  text: string
}

export interface UseBuild {
  status: BuildStatus
  logs: LogLine[]
  connected: boolean
  /** 正在向服务端提交「开始构建」请求 */
  starting: boolean
  /** 正在启动项目文件夹里的程序（等待 UAC 确认期间为 true） */
  launching: boolean
  notice: Notice | null
  startBuild: (options: BuildOptions) => Promise<void>
  runApp: () => Promise<void>
  clearNotice: () => void
}

async function readError(res: Response): Promise<string> {
  const body = (await res.json().catch(() => null)) as { error?: string } | null
  return body?.error ?? `请求失败（HTTP ${res.status}）`
}

export function useBuild(): UseBuild {
  const [status, setStatus] = useState<BuildStatus>(EMPTY_STATUS)
  const [logs, setLogs] = useState<LogLine[]>([])
  const [connected, setConnected] = useState(false)
  const [starting, setStarting] = useState(false)
  const [launching, setLaunching] = useState(false)
  const [notice, setNotice] = useState<Notice | null>(null)
  const lastSeqRef = useRef(0)

  const applySnapshot = useCallback((snapshot: BuildSnapshot) => {
    const { logs: snapshotLogs, ...rest } = snapshot
    setStatus(rest)
    setLogs(snapshotLogs)
    lastSeqRef.current = snapshotLogs.at(-1)?.seq ?? 0
  }, [])

  useEffect(() => {
    let cancelled = false

    // 首次快照（SSE 也会发一份，这里保证页面先有内容）
    fetch('/api/build')
      .then((r) => (r.ok ? r.json() : null))
      .then((data: BuildSnapshot | null) => {
        if (!cancelled && data) applySnapshot(data)
      })
      .catch(() => undefined)

    const source = new EventSource('/api/build/stream')
    source.onopen = () => setConnected(true)
    source.onerror = () => setConnected(false)
    source.onmessage = (event: MessageEvent<string>) => {
      let parsed: BuildEvent
      try {
        parsed = JSON.parse(event.data) as BuildEvent
      } catch {
        return
      }
      if (parsed.type === 'snapshot') {
        applySnapshot(parsed.snapshot)
      } else if (parsed.type === 'status') {
        setStatus(parsed.status)
      } else if (parsed.type === 'log') {
        // 重连后 snapshot 已重置日志；用 seq 去重避免重复行
        if (parsed.line.seq <= lastSeqRef.current) return
        lastSeqRef.current = parsed.line.seq
        setLogs((prev) => [...prev.slice(-4999), parsed.line])
      }
    }

    return () => {
      cancelled = true
      source.close()
    }
  }, [applySnapshot])

  const startBuild = useCallback(
    async (options: BuildOptions) => {
      setStarting(true)
      setNotice(null)
      try {
        const res = await fetch('/api/build', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(options),
        })
        if (res.status === 409) {
          const body = (await res.json()) as { error?: string }
          setNotice({ type: 'error', text: body.error ?? '已有构建正在进行中' })
          return
        }
        if (!res.ok) throw new Error(await readError(res))
        const snapshot = (await res.json()) as BuildSnapshot
        applySnapshot(snapshot)
      } catch (err) {
        setNotice({
          type: 'error',
          text: err instanceof Error ? `无法启动构建：${err.message}` : '无法启动构建',
        })
      } finally {
        setStarting(false)
      }
    },
    [applySnapshot],
  )

  const runApp = useCallback(async () => {
    setLaunching(true)
    setNotice(null)
    try {
      const res = await fetch('/api/app/run', { method: 'POST' })
      if (!res.ok) throw new Error(await readError(res))
      const result = (await res.json()) as LaunchResult
      setNotice({
        type: 'info',
        text: result.pid ? `程序已启动（PID ${result.pid}）` : '程序已启动',
      })
    } catch (err) {
      setNotice({
        type: 'error',
        text: err instanceof Error ? err.message : '无法启动程序',
      })
    } finally {
      setLaunching(false)
    }
  }, [])

  const clearNotice = useCallback(() => setNotice(null), [])

  return { status, logs, connected, starting, launching, notice, startBuild, runApp, clearNotice }
}
