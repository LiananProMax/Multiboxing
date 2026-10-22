import { useCallback, useState } from 'react'
import type { BuildOptions } from '../../shared/types'

const STORAGE_KEY = 'ops-web:build-options'

// 默认保持原有行为：打包 zip；切到文件夹模式时默认构建完成后直接运行
const DEFAULT_OPTIONS: BuildOptions = { mode: 'zip', autoRun: true }

function load(): BuildOptions {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return DEFAULT_OPTIONS
    const parsed = JSON.parse(raw) as Partial<BuildOptions>
    return {
      mode: parsed.mode === 'folder' ? 'folder' : 'zip',
      autoRun: typeof parsed.autoRun === 'boolean' ? parsed.autoRun : DEFAULT_OPTIONS.autoRun,
    }
  } catch {
    return DEFAULT_OPTIONS
  }
}

export function useBuildOptions(): [BuildOptions, (patch: Partial<BuildOptions>) => void] {
  const [options, setOptions] = useState<BuildOptions>(load)

  const update = useCallback((patch: Partial<BuildOptions>) => {
    setOptions((prev) => {
      const next = { ...prev, ...patch }
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
      } catch {
        // 隐私模式等场景下无法持久化，忽略
      }
      return next
    })
  }, [])

  return [options, update]
}
