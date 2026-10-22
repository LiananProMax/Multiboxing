import { useEffect, useState } from 'react'
import { ArtifactCard } from './components/ArtifactCard'
import { BuildOptionsPanel } from './components/BuildOptionsPanel'
import { LogConsole } from './components/LogConsole'
import { OutputCard } from './components/OutputCard'
import { StatusBadge } from './components/StatusBadge'
import { Steps } from './components/Steps'
import { useBuild } from './hooks/useBuild'
import { useBuildOptions } from './hooks/useBuildOptions'
import { formatDateTime, formatDuration } from './lib/format'

/** 构建进行中时每秒刷新一次耗时显示 */
function useNow(running: boolean): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!running) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [running])
  return now
}

export default function App() {
  const { status, logs, connected, starting, launching, notice, startBuild, runApp, clearNotice } =
    useBuild()
  const [options, updateOptions] = useBuildOptions()
  const now = useNow(status.running)

  const elapsedMs =
    status.startedAt !== null
      ? (status.finishedAt ? Date.parse(status.finishedAt) : now) -
        Date.parse(status.startedAt)
      : null

  const busy = status.running || starting || launching

  // 步骤条跟随最近一次构建的模式；尚未构建过时预览当前选择
  const stepsMode = status.mode ?? options.mode
  const stepsAutoRun = status.mode ? status.autoRun : options.autoRun

  const idleLabel =
    options.mode === 'zip'
      ? '一键构建并打包'
      : options.autoRun
        ? '一键构建并运行'
        : '一键构建到项目文件夹'

  return (
    <div className="mx-auto flex min-h-screen w-full max-w-4xl flex-col px-4 py-8 sm:px-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">运维构建</h1>
          <p className="mt-1 text-sm text-zinc-400">
            KeyMouseSyncReplica · .NET 9 WPF · win-x86 自包含发布
          </p>
        </div>
        <span
          className={`inline-flex items-center gap-1.5 text-xs ${connected ? 'text-emerald-400' : 'text-zinc-500'}`}
          title={connected ? '日志流已连接' : '日志流未连接（将自动重连）'}
        >
          <span
            className={`h-1.5 w-1.5 rounded-full ${connected ? 'bg-emerald-400' : 'bg-zinc-600'}`}
          />
          {connected ? '已连接' : '未连接'}
        </span>
      </header>

      <main className="mt-6 flex flex-col gap-4">
        <section className="rounded-2xl bg-zinc-900 p-5 ring-1 ring-zinc-800">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <StatusBadge phase={status.phase} />
            {elapsedMs !== null && status.phase !== 'idle' && (
              <span className="text-sm text-zinc-400">
                {status.running ? '已用时' : '耗时'} {formatDuration(elapsedMs)}
                {status.startedAt && (
                  <span className="ml-2 hidden text-xs text-zinc-500 sm:inline">
                    开始于 {formatDateTime(status.startedAt)}
                  </span>
                )}
              </span>
            )}
          </div>

          <div className="mt-6 px-1 sm:px-8">
            <Steps
              mode={stepsMode}
              autoRun={stepsAutoRun}
              phase={status.phase}
              exitCode={status.exitCode}
              launchFailed={status.launchError !== null}
            />
          </div>

          <div className="mt-6">
            <BuildOptionsPanel options={options} disabled={busy} onChange={updateOptions} />
          </div>

          <button
            type="button"
            onClick={() => void startBuild(options)}
            disabled={busy}
            className="mt-4 w-full rounded-xl bg-sky-600 py-3.5 text-base font-semibold text-white transition hover:bg-sky-500 focus:outline-none focus-visible:ring-2 focus-visible:ring-sky-400 disabled:cursor-not-allowed disabled:bg-zinc-800 disabled:text-zinc-500"
          >
            {status.phase === 'publishing'
              ? '正在发布…'
              : status.phase === 'packaging'
                ? '正在打包…'
                : status.phase === 'launching'
                  ? '正在启动…（请在 UAC 弹窗中确认）'
                  : starting
                    ? '正在启动构建…'
                    : launching
                      ? '程序启动中…'
                      : idleLabel}
          </button>

          {notice && (
            <div
              className={`mt-4 flex items-start justify-between gap-3 rounded-xl px-4 py-3 text-sm ring-1 ${
                notice.type === 'error'
                  ? 'bg-amber-950/50 text-amber-200 ring-amber-900'
                  : 'bg-emerald-950/50 text-emerald-200 ring-emerald-900'
              }`}
            >
              <span>{notice.text}</span>
              <button
                type="button"
                onClick={clearNotice}
                className={`shrink-0 ${notice.type === 'error' ? 'text-amber-400 hover:text-amber-200' : 'text-emerald-400 hover:text-emerald-200'}`}
                aria-label="关闭提示"
              >
                ✕
              </button>
            </div>
          )}

          {status.phase === 'failed' && status.error && (
            <div className="mt-4 rounded-xl bg-red-950/50 px-4 py-3 text-sm text-red-200 ring-1 ring-red-900">
              构建失败：{status.error}
            </div>
          )}

          {status.phase === 'success' && status.launchError && (
            <div className="mt-4 rounded-xl bg-amber-950/50 px-4 py-3 text-sm text-amber-200 ring-1 ring-amber-900">
              构建成功，但自动运行失败：{status.launchError}。可点击下方「运行」重试。
            </div>
          )}

          {(status.output || status.artifact) && (
            <div className="mt-4 flex flex-col gap-3">
              {status.output && (
                <OutputCard
                  output={status.output}
                  disabled={status.running || starting}
                  launching={launching}
                  onRun={() => void runApp()}
                />
              )}
              {status.artifact && <ArtifactCard artifact={status.artifact} />}
            </div>
          )}
        </section>

        <LogConsole logs={logs} running={status.running} />
      </main>

      <footer className="mt-6 text-center text-xs text-zinc-600">
        仅本机访问（127.0.0.1）· 执行 dotnet publish -c Release ·{' '}
        {stepsMode === 'zip' ? '打包为 zip 供下载' : '直接输出到项目 publish\\ 文件夹'}
      </footer>
    </div>
  )
}
