import { useEffect, useRef, useState } from 'react'
import type { LogLine } from '../../shared/types'

function lineClass(line: LogLine): string {
  if (line.stream === 'system') return 'text-cyan-300'
  if (line.level === 'error') return 'text-red-400'
  if (line.level === 'warning') return 'text-amber-300'
  if (line.stream === 'stderr') return 'text-zinc-300'
  return 'text-zinc-400'
}

function formatTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return d.toLocaleTimeString('zh-CN', { hour12: false })
}

export function LogConsole({ logs, running }: { logs: LogLine[]; running: boolean }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [autoScroll, setAutoScroll] = useState(true)

  useEffect(() => {
    const el = containerRef.current
    if (el && autoScroll) el.scrollTop = el.scrollHeight
  }, [logs, autoScroll])

  const handleScroll = () => {
    const el = containerRef.current
    if (!el) return
    // 距底部小于 40px 视为仍在跟随
    setAutoScroll(el.scrollHeight - el.scrollTop - el.clientHeight < 40)
  }

  return (
    <div className="overflow-hidden rounded-xl bg-zinc-950 ring-1 ring-zinc-800">
      <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-2">
        <span className="text-xs font-medium tracking-wide text-zinc-400">
          构建日志{logs.length > 0 ? `（${logs.length} 行）` : ''}
        </span>
        <label className="flex cursor-pointer items-center gap-1.5 text-xs text-zinc-400 select-none">
          <input
            type="checkbox"
            checked={autoScroll}
            onChange={(e) => setAutoScroll(e.target.checked)}
            className="h-3.5 w-3.5 accent-sky-500"
          />
          自动滚动
        </label>
      </div>
      <div
        ref={containerRef}
        onScroll={handleScroll}
        className="log-scroll h-96 overflow-y-auto px-4 py-3 font-mono text-xs leading-5"
      >
        {logs.length === 0 ? (
          <p className="text-zinc-600">
            {running ? '正在等待输出…' : '暂无日志。点击上方的构建按钮开始。'}
          </p>
        ) : (
          logs.map((line) => (
            <div key={line.seq} className="flex gap-3 whitespace-pre-wrap break-all">
              <span className="shrink-0 text-zinc-600">{formatTime(line.time)}</span>
              <span className={lineClass(line)}>{line.text}</span>
            </div>
          ))
        )}
      </div>
    </div>
  )
}
