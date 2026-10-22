import type { BuildMode, BuildPhase } from '../../shared/types'

type StepKey = 'publish' | 'package' | 'run' | 'done'
type StepState = 'done' | 'active' | 'todo' | 'failed'

interface Step {
  key: StepKey
  label: string
  hint: string
}

const PUBLISH: Step = { key: 'publish', label: '发布', hint: 'dotnet publish -c Release' }
const RUN: Step = { key: 'run', label: '运行', hint: '管理员权限启动' }

function buildSteps(mode: BuildMode, autoRun: boolean): Step[] {
  if (mode === 'zip') {
    return [
      PUBLISH,
      { key: 'package', label: '打包', hint: '压缩为 zip' },
      { key: 'done', label: '完成', hint: '可下载产物' },
    ]
  }
  return [
    PUBLISH,
    ...(autoRun ? [RUN] : []),
    { key: 'done', label: '完成', hint: '已输出到项目文件夹' },
  ]
}

const ACTIVE_KEY: Partial<Record<BuildPhase, StepKey>> = {
  publishing: 'publish',
  packaging: 'package',
  launching: 'run',
}

function stepState(
  index: number,
  steps: Step[],
  phase: BuildPhase,
  exitCode: number | null,
  launchFailed: boolean,
): StepState {
  const key = steps[index].key
  if (phase === 'success') return key === 'run' && launchFailed ? 'failed' : 'done'
  if (phase === 'failed') {
    // publish 退出码非 0（或进程没起来）是发布失败；退出码 0 说明卡在打包
    const failedKey: StepKey = exitCode === 0 && steps.some((s) => s.key === 'package') ? 'package' : 'publish'
    const failedIndex = steps.findIndex((s) => s.key === failedKey)
    if (index < failedIndex) return 'done'
    return index === failedIndex ? 'failed' : 'todo'
  }
  const activeKey = ACTIVE_KEY[phase]
  const activeIndex = activeKey ? steps.findIndex((s) => s.key === activeKey) : -1
  if (activeIndex < 0) return 'todo'
  return index < activeIndex ? 'done' : index === activeIndex ? 'active' : 'todo'
}

interface StepsProps {
  mode: BuildMode
  autoRun: boolean
  phase: BuildPhase
  exitCode: number | null
  /** 构建成功但自动运行失败 */
  launchFailed: boolean
}

export function Steps({ mode, autoRun, phase, exitCode, launchFailed }: StepsProps) {
  const steps = buildSteps(mode, autoRun)
  return (
    <ol className="flex items-start">
      {steps.map((step, i) => {
        const state = stepState(i, steps, phase, exitCode, launchFailed)
        return (
          <li key={step.key} className="flex flex-1 items-start last:flex-none">
            <div className="flex flex-col items-center gap-1.5 text-center">
              <span
                className={
                  state === 'done'
                    ? 'flex h-8 w-8 items-center justify-center rounded-full bg-emerald-500 text-sm font-bold text-white'
                    : state === 'active'
                      ? 'flex h-8 w-8 items-center justify-center rounded-full bg-sky-500 text-sm font-bold text-white animate-pulse'
                      : state === 'failed'
                        ? 'flex h-8 w-8 items-center justify-center rounded-full bg-red-500 text-sm font-bold text-white'
                        : 'flex h-8 w-8 items-center justify-center rounded-full bg-zinc-800 text-sm font-medium text-zinc-500 ring-1 ring-zinc-700'
                }
              >
                {state === 'done' ? '✓' : state === 'failed' ? '✕' : i + 1}
              </span>
              <span
                className={`text-sm font-medium ${state === 'todo' ? 'text-zinc-500' : 'text-zinc-200'}`}
              >
                {step.label}
              </span>
              <span className="hidden text-xs text-zinc-500 sm:block">{step.hint}</span>
            </div>
            {i < steps.length - 1 && (
              <div
                className={`mx-3 mt-4 h-px flex-1 ${state === 'done' ? 'bg-emerald-500' : 'bg-zinc-800'}`}
              />
            )}
          </li>
        )
      })}
    </ol>
  )
}
