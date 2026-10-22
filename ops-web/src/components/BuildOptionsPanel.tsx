import type { BuildMode, BuildOptions } from '../../shared/types'

interface ModeChoice {
  value: BuildMode
  title: string
  desc: string
}

const MODES: ModeChoice[] = [
  { value: 'zip', title: '打包 zip 并下载', desc: '发布后压缩为 zip，可在页面下载' },
  {
    value: 'folder',
    title: '直接构建到项目文件夹',
    desc: '输出到项目 publish\\ 目录，不打包 zip',
  },
]

interface BuildOptionsPanelProps {
  options: BuildOptions
  disabled: boolean
  onChange: (patch: Partial<BuildOptions>) => void
}

export function BuildOptionsPanel({ options, disabled, onChange }: BuildOptionsPanelProps) {
  return (
    <fieldset disabled={disabled} className="min-w-0 disabled:opacity-60">
      <legend className="mb-2 text-xs font-medium tracking-wide text-zinc-400">构建方式</legend>
      <div role="radiogroup" className="grid gap-2 sm:grid-cols-2">
        {MODES.map((mode) => {
          const selected = options.mode === mode.value
          return (
            <label
              key={mode.value}
              className={`flex cursor-pointer items-start gap-3 rounded-xl px-4 py-3 ring-1 transition has-disabled:cursor-not-allowed ${
                selected
                  ? 'bg-sky-950/40 ring-sky-600'
                  : 'bg-zinc-950 ring-zinc-800 hover:ring-zinc-600'
              }`}
            >
              <input
                type="radio"
                name="build-mode"
                value={mode.value}
                checked={selected}
                onChange={() => onChange({ mode: mode.value })}
                className="mt-1 h-4 w-4 accent-sky-500"
              />
              <span className="min-w-0">
                <span className="block text-sm font-medium text-zinc-100">{mode.title}</span>
                <span className="mt-0.5 block text-xs text-zinc-500">{mode.desc}</span>
              </span>
            </label>
          )
        })}
      </div>

      {options.mode === 'folder' && (
        <label className="mt-3 flex cursor-pointer items-center gap-2 text-sm text-zinc-300 select-none has-disabled:cursor-not-allowed">
          <input
            type="checkbox"
            checked={options.autoRun}
            onChange={(e) => onChange({ autoRun: e.target.checked })}
            className="h-4 w-4 accent-sky-500"
          />
          构建完成后直接运行
          <span className="text-xs text-zinc-500">（程序需要管理员权限，会弹出 UAC 确认）</span>
        </label>
      )}
    </fieldset>
  )
}
