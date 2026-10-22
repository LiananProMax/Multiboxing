import type { ArtifactInfo } from '../../shared/types'
import { formatBytes, formatDateTime } from '../lib/format'

export function ArtifactCard({ artifact }: { artifact: ArtifactInfo }) {
  return (
    <div className="flex flex-wrap items-center gap-4 rounded-xl bg-emerald-950/40 px-4 py-3 ring-1 ring-emerald-900">
      <div className="min-w-0 flex-1">
        <p className="truncate font-mono text-sm text-emerald-200" title={artifact.fileName}>
          {artifact.fileName}
        </p>
        <p className="mt-0.5 text-xs text-emerald-400/80">
          {formatBytes(artifact.sizeBytes)} · 完成于 {formatDateTime(artifact.createdAt)}
        </p>
      </div>
      <a
        href={artifact.downloadUrl}
        className="shrink-0 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-emerald-500 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400"
      >
        下载 zip
      </a>
    </div>
  )
}
