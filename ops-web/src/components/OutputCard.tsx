import type { OutputInfo } from '../../shared/types'
import { formatDateTime } from '../lib/format'

interface OutputCardProps {
  output: OutputInfo
  /** 构建进行中或正在启动时不允许再次运行 */
  disabled: boolean
  launching: boolean
  onRun: () => void
}

export function OutputCard({ output, disabled, launching, onRun }: OutputCardProps) {
  return (
    <div className="flex flex-wrap items-center gap-4 rounded-xl bg-sky-950/40 px-4 py-3 ring-1 ring-sky-900">
      <div className="min-w-0 flex-1">
        <p className="font-mono text-sm text-sky-200">{output.exeName}</p>
        <p className="mt-0.5 font-mono text-xs break-all text-sky-300/80" title={output.directory}>
          {output.directory}
        </p>
        <p className="mt-0.5 text-xs text-sky-400/80">
          项目文件夹 · 构建于 {formatDateTime(output.createdAt)}
        </p>
      </div>
      <button
        type="button"
        onClick={onRun}
        disabled={disabled || launching}
        className="shrink-0 rounded-lg bg-sky-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-sky-500 focus:outline-none focus-visible:ring-2 focus-visible:ring-sky-400 disabled:cursor-not-allowed disabled:bg-zinc-800 disabled:text-zinc-500"
      >
        {launching ? '等待 UAC 确认…' : '运行'}
      </button>
    </div>
  )
}
