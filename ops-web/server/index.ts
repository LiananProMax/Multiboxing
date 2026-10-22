import { existsSync } from 'node:fs'
import path from 'node:path'
import express from 'express'
import type { NextFunction, Request, Response } from 'express'
import type { BuildEvent, BuildOptions, BuildStatus, LogLine } from '../shared/types.ts'
import {
  artifactsDir,
  BuildBusyError,
  buildRunner,
  LaunchError,
  NoOutputError,
  opsWebRoot,
} from './buildRunner.ts'

const PORT = Number(process.env.PORT ?? 4170)
const HOST = '127.0.0.1'

const app = express()
app.disable('x-powered-by')
app.use(express.json())

/**
 * 防跨站触发：写操作只接受本站来源。
 * 浏览器跨站 POST 时 Origin 一定与 Host 不一致（或不是本机地址），直接拒绝。
 * 无 Origin 的请求（如 curl）放行，因为本站点仅监听 127.0.0.1。
 */
function requireSameOrigin(req: Request, res: Response, next: NextFunction): void {
  const origin = req.get('origin')
  if (origin) {
    let originHost: string
    try {
      originHost = new URL(origin).host
    } catch {
      res.status(403).json({ error: '非法 Origin' })
      return
    }
    if (originHost !== req.get('host')) {
      res.status(403).json({ error: '跨站请求已被拒绝' })
      return
    }
  }
  next()
}

app.get('/api/build', (_req, res) => {
  res.json(buildRunner.snapshot())
})

/** 请求体缺省时按旧行为处理：打包 zip、不自动运行 */
function parseBuildOptions(body: unknown): BuildOptions | null {
  const raw = (typeof body === 'object' && body !== null ? body : {}) as Record<string, unknown>
  const mode = raw.mode ?? 'zip'
  const autoRun = raw.autoRun ?? false
  if ((mode !== 'zip' && mode !== 'folder') || typeof autoRun !== 'boolean') return null
  return { mode, autoRun }
}

app.post('/api/build', requireSameOrigin, (req, res) => {
  const options = parseBuildOptions(req.body)
  if (!options) {
    res.status(400).json({ error: '构建参数无效：mode 只能是 zip 或 folder，autoRun 必须是布尔值' })
    return
  }
  try {
    const snapshot = buildRunner.startBuild(options)
    res.status(202).json(snapshot)
  } catch (err) {
    if (err instanceof BuildBusyError) {
      res.status(409).json({ error: err.message, snapshot: buildRunner.snapshot() })
      return
    }
    throw err
  }
})

/** 运行项目文件夹里最近一次构建出的程序（仅限固定路径，不接受客户端传入路径） */
app.post('/api/app/run', requireSameOrigin, async (_req, res) => {
  if (buildRunner.running) {
    res.status(409).json({ error: '构建进行中，请等待完成后再运行' })
    return
  }
  try {
    res.json(await buildRunner.launchApp())
  } catch (err) {
    if (err instanceof BuildBusyError) {
      res.status(409).json({ error: err.message })
    } else if (err instanceof NoOutputError) {
      res.status(404).json({ error: err.message })
    } else if (err instanceof LaunchError) {
      res.status(500).json({ error: err.message })
    } else {
      throw err
    }
  }
})

app.get('/api/build/stream', (req, res) => {
  res.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
    'X-Accel-Buffering': 'no',
  })

  const send = (event: BuildEvent) => {
    res.write(`data: ${JSON.stringify(event)}\n\n`)
  }

  send({ type: 'snapshot', snapshot: buildRunner.snapshot() })

  const onLog = (line: LogLine) => send({ type: 'log', line })
  const onStatus = (status: BuildStatus) => send({ type: 'status', status })

  buildRunner.on('log', onLog)
  buildRunner.on('status', onStatus)

  const heartbeat = setInterval(() => res.write(': ping\n\n'), 25_000)

  req.on('close', () => {
    clearInterval(heartbeat)
    buildRunner.off('log', onLog)
    buildRunner.off('status', onStatus)
  })
})

app.get('/api/artifacts/latest/download', (_req, res) => {
  const artifact = buildRunner.status().artifact
  if (!artifact) {
    res.status(404).json({ error: '暂无可下载的产物，请先构建' })
    return
  }
  const filePath = path.join(artifactsDir, artifact.fileName)
  if (!existsSync(filePath)) {
    res.status(404).json({ error: '产物文件不存在，可能已被清理' })
    return
  }
  res.download(filePath, artifact.fileName)
})

// 生产模式：托管前端构建产物
const distDir = path.join(opsWebRoot, 'dist')
if (existsSync(distDir)) {
  app.use(express.static(distDir))
  app.use((req, res, next) => {
    if (req.method === 'GET' && !req.path.startsWith('/api/')) {
      res.sendFile(path.join(distDir, 'index.html'))
      return
    }
    next()
  })
} else {
  app.get('/', (_req, res) => {
    res
      .status(503)
      .send('前端尚未构建。请先在 ops-web 目录执行 npm run build，或使用 npm run dev 开发模式。')
  })
}

await buildRunner.restoreLatest()

app.listen(PORT, HOST, () => {
  console.log(`运维构建网站已启动：http://${HOST}:${PORT}`)
})
