import type { BuildPhase } from '../../shared/types'

const PHASE_META: Record<BuildPhase, { label: string; className: string; dot: string }> = {
  idle: {
    label: '未构建',
    className: 'bg-zinc-800 text-zinc-300 ring-zinc-700',
    dot: 'bg-zinc-400',
  },
  publishing: {
    label: '发布中',
    className: 'bg-sky-950 text-sky-300 ring-sky-800',
    dot: 'bg-sky-400 animate-pulse',
  },
  packaging: {
    label: '打包中',
    className: 'bg-violet-950 text-violet-300 ring-violet-800',
    dot: 'bg-violet-400 animate-pulse',
  },
  launching: {
    label: '启动中',
    className: 'bg-amber-950 text-amber-300 ring-amber-800',
    dot: 'bg-amber-400 animate-pulse',
  },
  success: {
    label: '构建成功',
    className: 'bg-emerald-950 text-emerald-300 ring-emerald-800',
    dot: 'bg-emerald-400',
  },
  failed: {
    label: '构建失败',
    className: 'bg-red-950 text-red-300 ring-red-800',
    dot: 'bg-red-400',
  },
}

export function StatusBadge({ phase }: { phase: BuildPhase }) {
  const meta = PHASE_META[phase]
  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-medium ring-1 ring-inset ${meta.className}`}
    >
      <span className={`h-2 w-2 rounded-full ${meta.dot}`} />
      {meta.label}
    </span>
  )
}
