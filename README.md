# E2ETest

E2ETest 是一个面向 Windows 桌面软件的端到端测试工具。它通过全局键鼠录制、时间轴回放和指定时点截图，建立可重复执行的测试用例。

当前已实现：

- 全局鼠标、键盘、滚轮录制
- 键盘扫描码、扩展键、组合键和鼠标拖拽记录
- 自定义单键截图/停止控制键
- 仅由截图控制键触发的截图事件时间轴
- 主显示器截图，非全屏模式排除任务栏
- `SendInput` 时间轴回放
- 回放生命周期 hook、超时和批量错误隔离
- 本地像素对比、差异区域合并和结构化对比报告
- 可选的 openai-chat 多模态 AI 语义复核
- `run` 一键串联回放、像素对比与可选 AI 复核
- 测试用例创建、列出、删除、播放和跨进程读写锁
- Console + 按天/大小轮转的文件日志
- Windows x64 自包含单文件发布

尚未实现：

- 独立的可视化 HTML 报告

## 运行前提

- Windows x64
- 录制和回放在同一台机器或等价环境运行
- 分辨率、DPI、缩放和主显示器布局固定
- 实际测试操作应位于主显示器；副屏鼠标事件会被过滤
- 数据目录位于本地磁盘

## 支持边界与执行不变量

工具面向同一台 Windows 机器、同一个交互式用户会话、可信的本地数据目录运行；录制和回放使用固定的主显示器分辨率、DPI 与缩放。`testcases`、`replays` 和 `reports` 应位于本地磁盘，且由正常的工具工作流创建和维护。

在这个边界内，compare/run 保证：

- `run` 为 replay 和 compare 使用同一个 roundId；
- replay、hook 或截图失败会保留为失败，像素结果和 AI 不能将其改为通过；
- 像素比较始终在原始 PNG 尺寸进行，尺寸不一致是硬失败；
- AI 超时、掉线、限流或服务端暂时错误只会使 AI 复核失败，最终结论退回本地结果，绝不因 AI 不可用而判通过；
- round 和 testcase 报告分别落盘，已完成的回放截图仍可用于后续 compare 诊断。

不在本工具当前安全模型内：恶意修改 JSON/图片、恶意 junction 或符号链接、网络盘阻塞或语义差异、跨用户会话、动态显示器/DPI 变化，以及断电时保证所有进行中写入均完整。出现这些条件时，结果不保证可用，应重新录制或重新执行。

## 构建

```powershell
dotnet build E2ETest.slnx -c Release
dotnet test E2ETest.slnx -c Release
```

开发构建的可执行文件位于：

```text
src\E2ETest.Cli\bin\Release\net10.0-windows\e2etest.exe
```

## 单文件发布

```powershell
.\publish.ps1
```

默认输出：

```text
publish\win-x64-single\
  e2etest.exe
  e2etest-report-viewer.exe
```

两个 EXE 会发布到同一个目录，可以连同 `config.json` 和数据目录一起复制部署；它们都是 Windows x64 自包含单文件，目标机器无需安装 .NET。也可以手动发布 CLI：

```powershell
dotnet publish src\E2ETest.Cli\E2ETest.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o publish\win-x64-single
```

## 数据根目录

CLI 可以通过 `--root <目录>` 明确指定数据根目录。未指定时，先查找当前工作目录中的 `config.json`；如果不存在，再查找 `e2etest.exe` 所在目录中的 `config.json`。两处都不存在时继续使用当前工作目录，以兼容首次执行 `config init`。发现的 `config.json` 所在目录就是数据根目录。目录结构如下：

```text
<root>/                                      # 数据根目录
  config.json                                # 热键、截图、路径、日志及后续对比配置
  logs/                                      # 按天和文件大小轮转的运行日志
    e2etest-YYYYMMDD.log                     # 当天日志；超出大小限制时自动分卷
  testcases/                                 # 已创建的全部测试用例，可由 paths.testCases 修改
    <name>/                                  # 单个测试用例；目录名就是测试用例名称
      manifest.json                          # 录制环境、回放设置、输入时间轴及截图索引
      baseline/                              # 录制时生成的基准截图
        shot-0001.png                        # 录制时手动触发的基准截图
  replays/                                   # 历次回放结果，可由 paths.replays 修改
    <roundId>/                               # 一次批量或单用例回放轮次
      result.json                            # 轮次状态、汇总计数及各用例结果
      testcases/                             # 本轮各测试用例的独立输出
        <name>/                              # 一个测试用例在本轮的回放结果
          result.json                        # 状态、错误、耗时及截图明细
          shot-0001.png                      # 回放时在对应时间点重新抓取的截图
  .locks/                                    # 测试用例跨进程读写锁目录
    <name>.lock                              # 仅操作期间存在，操作结束后自动删除
  .staging/                                  # 录制提交前的临时目录，成功或失败后清理
  .trash/                                    # 删除用例时的临时中转目录，删除完成后清理
```

每个名称对应一个测试用例。重录同名用例前必须先删除，不会创建版本历史。`.locks` 目录可能保留，但其中的 `<name>.lock` 文件会在录制、读取、回放或删除结束后删除。`.staging` 和 `.trash` 只在操作过程中短暂出现。

当前 manifest schema 为 4；旧版本测试用例不兼容，需要重新录制。

## CLI 总览

```powershell
.\e2etest.exe help
```

当前命令：

```text
e2etest record
e2etest replay
e2etest compare
e2etest run
e2etest testcase list
e2etest testcase delete
e2etest config init
e2etest config show
```

### 通用参数

| 参数 | 说明 |
| --- | --- |
| `--root <目录>` | 指定配置、测试用例、回放和日志的数据根目录；省略时依次检查当前工作目录和 EXE 目录中的 `config.json` |

## 配置命令

### 生成默认配置

```powershell
.\e2etest.exe config init [--root <目录>]
```

如果 `config.json` 已存在，不会覆盖。

### 查看配置

```powershell
.\e2etest.exe config show [--root <目录>]
```

### 默认配置要点

```json
{
  "schemaVersion": 1,
  "hotkeys": {
    "startStop": "F12",
    "screenshot": "F11"
  },
  "record": {
    "fullscreen": false
  },
  "replay": {
    "betweenTestCasesMs": 10000,
    "betweenRoundsMs": 20000
  },
  "ai": {
    "baseUrl": "",
    "apiKey": "",
    "model": "",
    "contextPrompt": "",
    "maxImageDimension": 1080,
    "maxEvidenceRegions": 10,
    "maxAttempts": 3,
    "retryDelayMs": 1000,
    "timeoutMs": 300000
  },
  "replayHooks": {
    "beforeRound": null,
    "afterRound": null,
    "beforeTestCase": null,
    "afterTestCase": null,
    "timeoutMs": 30000
  },
  "paths": {
    "testCases": "./testcases",
    "replays": "./replays",
    "reports": "./reports"
  },
  "logging": {
    "directory": "./logs",
    "minimumLevel": "Information",
    "retainedFileCount": 200,
    "retainedDays": 30
  }
}
```

控制键只支持单键，例如 `F11`、`F12`、`Pause`、字母或数字；不支持 `Ctrl+F11` 形式的组合控制键。配置的截图键和停止键不会作为普通键盘事件写入时间轴。

日志同时写入 stderr 和日志文件：

```text
logs\e2etest-YYYYMMDD.log
```

正常录制约 4～6 行日志，批量回放约 4 行固定日志加每个测试用例 2 行，通常每天远低于 1 MB。日志按天或达到 10 MB 时轮转，保留最近 30 天，并最多保留 200 个文件，因此日志预算上限约为 2 GB；如果 30 天内产生超过 200 个分卷，会优先遵守 2 GB 预算并删除最旧文件。

## 录制命令

```powershell
.\e2etest.exe record `
  [--name <测试用例名称>] `
  [--fullscreen | --no-fullscreen] `
  [--root <目录>]
```

参数：

| 参数 | 必需 | 说明 |
| --- | --- | --- |
| `--name <测试用例名称>` | 否 | 1～80 字符的合法 Windows 文件名，支持中文和内部空格；省略时自动生成可读唯一名称 |
| `--fullscreen` | 否 | 强制整张主屏截图，包含任务栏 |
| `--no-fullscreen` | 否 | 强制使用主屏工作区截图，排除任务栏 |
| `--root <目录>` | 否 | 指定数据根目录 |

截图模式优先级：命令行参数高于 `config.json` 中的 `record.fullscreen`。示例：

```powershell
.\e2etest.exe record --name "登录流程"
.\e2etest.exe record
```

录制开始后控制台会隐藏，只保留托盘图标：

- 默认 F11：截图
- 默认 F12：停止录制
- 托盘右键也可以截图或停止

托盘图标会显示当前状态：橙色空心圆表示尚无有效截图，红色实心圆表示已有截图且正在录制，紫色相机表示正在截图并写入 PNG（请勿操作），蓝色箭头表示停止后正在保存测试用例，绿色对勾表示完成，红色叉号表示失败。截图状态至少保持约 500ms，并在 PNG 写入完成后才恢复为录制状态。

截图必须在录制期间通过截图键或托盘“立即截图”手动触发；停止录制不会自动截图。停止后会等待 PNG 后台编码、校验时间轴和截图，然后直接提交测试用例。未录制任何截图时，该次录制无效，会提示重新录制且不会创建测试用例。同名测试用例已存在时返回退出码 2，需先删除后再创建。

建议每个测试用例不超过 12 张截图：1～8 张最适合常规流程，9～12 张适合较长流程；再长的流程宜按业务阶段拆成多个测试用例。12 张是为了控制后续 AI 复核的图片输入量和结论复杂度，仅作为录制规范，工具不会提醒、限制或截断截图。

录制失败时输出错误并返回非零退出码。

## 测试用例管理命令

```powershell
.\e2etest.exe testcase list [--root <目录>]
.\e2etest.exe testcase delete --name "登录流程" [--root <目录>]
```

`list` 每行输出一个测试用例名称。`delete` 的 `--name` 为必需参数；缺少名称或目标不存在时返回退出码 2。

## 回放命令

```powershell
.\e2etest.exe replay `
  [--name <测试用例名称>] `
  [--round <roundId>] `
  [--root <目录>]
```

参数：

| 参数 | 必需 | 说明 |
| --- | --- | --- |
| `--name <测试用例名称>` | 否 | 只回放指定测试用例；省略时回放全部有效测试用例 |
| `--round <roundId>` | 否 | 指定本轮 ID；省略时自动生成唯一 ID |
| `--root <目录>` | 否 | 指定数据根目录 |

示例：

```powershell
# 回放单条测试用例
.\e2etest.exe replay --name "登录流程"

# 回放全部测试用例
.\e2etest.exe replay

# 指定轮次 ID
.\e2etest.exe replay --round nightly-20260722
```

回放行为：

- 隐藏控制台，避免进入截图；
- 校验主屏分辨率、DPI、截图尺寸和 manifest；
- 按配置执行回放生命周期 hook；
- 根据扫描码和时间轴注入键鼠事件；
- 在截图实际抓取时点生成 replay 截图；
- 单个测试用例失败不会影响其他测试用例；
- 异常或取消时尽力释放所有按下的键和鼠标按钮；
- Ctrl+C 取消后返回退出码 130，并为未执行测试用例写入 cancelled 结果。

测试用例级回放配置保存在 `manifest.json` 中：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `speedFactor` | `1.0` | 回放速度；1.0 为原始时序 |
| `maxIdleGapMs` | `0` | 0 表示严格时间轴；大于 0 时压缩超长空闲间隔 |

全局回放间隔在 `config.json` 的 `replay` 中配置：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `betweenTestCasesMs` | `10000` | 同一轮中，上一条 `afterTestCase` 完成到下一条 `beforeTestCase` 开始之间的最短间隔 |
| `betweenRoundsMs` | `20000` | 上一轮 `afterRound` 完成到下一次 replay 的 `beforeRound` 开始之间的最短间隔；对后续 CLI 进程同样生效 |

两项都可以设为 `0` 以关闭等待，不能为负数。工具会在 `replays/.last-round-finished` 中维护上一轮完成时间；这是内部状态文件，不需要手工编辑。`run` 中 replay 完成后可以立即进入 compare；轮次间隔只限制下一次 replay，不会无意义地阻塞当前轮的 compare。

### 回放生命周期 Hook

Hook 在 `config.json` 的 `replayHooks` 中全局配置，四个命令均可省略（`null` 或空字符串表示不执行）：

| 字段 | 执行时机 |
| --- | --- |
| `beforeRound` | 一轮回放开始前，执行一次 |
| `afterRound` | 一轮回放结束时，执行一次；即使失败或取消也会尽力执行 |
| `beforeTestCase` | 每条测试用例回放前执行 |
| `afterTestCase` | 每条测试用例结束时执行；即使该用例失败或取消也会尽力执行 |
| `timeoutMs` | 每条 hook 命令的超时时间，默认 `30000` 毫秒 |

命令由 Windows shell 执行。常见用法是将“启动/准备共享环境”放入 `beforeRound`，将“恢复每条用例初始状态”放入 `beforeTestCase`，将清理放入相应的 `after*` hook。例如：

```json
{
  "replayHooks": {
    "beforeRound": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Test Scripts\\start-test-env.ps1\"",
    "afterRound": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Test Scripts\\stop-test-env.ps1\"",
    "beforeTestCase": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Test Scripts\\reset-test-data.ps1\"",
    "afterTestCase": null,
    "timeoutMs": 30000
  }
}
```

推荐使用 PowerShell 的 `-File` 直接运行脚本，不要再套一层 `-Command "& '...'"`；后者同时经过 JSON、`cmd.exe` 和 PowerShell 三层引号解析，很容易因路径空格或转义失败。上面的写法支持带空格的脚本路径。

`beforeRound` 失败时整轮不会执行测试用例；`beforeTestCase` 失败时当前用例不会回放。任意 hook 返回非零退出码、启动失败或超时都会记为失败。超时后工具会尝试终止该命令启动的进程树；若进程树无法终止，为避免污染后续用例，会取消本轮剩余回放。

## 本地像素对比

```powershell
.\e2etest.exe compare --round <roundId> [--name <测试用例名称>] [--ai] [--root <目录>]
```

### 一键执行测试

```powershell
.\e2etest.exe run [--name <测试用例名称>] [--round <roundId>] [--ai] [--root <目录>]
```

`run` 先执行 `replay`，再对同一个 round 执行 `compare`；省略 `--round` 时自动生成 roundId，并将该 ID 传给两个阶段。`--name` 只运行一个测试用例，`--ai` 仅在第二阶段启用 AI 复核。回放阶段返回 `2`（无可回放用例）或 `130`（取消）时不会继续对比；其他回放失败仍会继续比较已经产生的截图，以保留诊断报告。最终退出码同时反映回放和对比：任一阶段失败均返回非零。

`run` 和 `compare` 会严格校验参数：未知参数、重复参数、缺少值、给 `--ai` 额外传值或意外位置参数都会直接报错，不会因 `--name` 拼错或缺值而退化成运行全部测试用例。

## 交互式报告查看器

与 `e2etest.exe` 位于同一发布目录的 `e2etest-report-viewer.exe` 是独立、只读的 Windows 报告分析软件，不执行 replay/compare，不修改报告，也不读取 `config.json`。直接启动时会从当前目录或程序上级目录寻找 `reports`；也可以把 reports 根目录、单个 round 目录或某个 `result.json` 作为第一个参数：

```powershell
.\e2etest-report-viewer.exe C:\public\e2etest\reports
```

界面按测试人员的阅读顺序组织：

- 顶部先汇总“需要查看、明确失败、已通过、全部用例”；
- 有风险时默认只列需要关注的 testcase，通过项可切换查看；没有风险时自动显示全部；
- testcase 按最终结果和 `attentionScore` 排序，默认定位最值得关注的步骤；
- 步骤按录制时间排列，区域按差异像素数排列；
- 证据默认显示 AI 实际收到的四宫格，可切换 overlay、baseline、replay 和 diff；
- AI 的“看到了什么”和“为什么这样判断”与当前步骤/区域放在一起；
- 单例 `result.json` 会覆盖整轮汇总中的旧副本，因此单独重跑 AI 后查看器能显示最新结论。

查看器定位是“先找风险、再下钻证据”。首版不提供重新执行测试、修改 verdict 或重新请求 AI 的功能。

`compare` 读取已有的 `replays/<roundId>/`，不重新执行输入回放。仍处于 `running` 状态或保留 `.running.lock` 的 round 会被拒绝，避免对尚未完成的回放生成部分报告；同一 round 同时只能运行一个 compare。新生成的 replay 会记录每张 baseline 的 SHA-256，若 baseline 在 replay 后被重新录制或改变，compare 会以 `baseline_changed` 硬失败拒绝混用；旧 round 没有哈希时仍可兼容比较。报告保留 `replayStatus`、`replayError` 和 `replayLifecycleSucceeded`；包括 `afterRound` 在内的回放生命周期失败会使 compare 返回非零，即使所有截图语义一致也不能覆盖。完整 round 的结果写入 `reports/<roundId>/result.json`；使用 `--name` 时只更新对应的 `reports/<roundId>/testcases/<name>/result.json`，不会用单例结果覆盖已有整轮汇总。每张截图还会生成 `diff-shot-xxxx.png`（仅显示差异）和 `overlay-shot-xxxx.png`（在 replay 图上以不同半透明颜色标记差异区域）。像素碎片会按带 padding 的相邻上下文自动合并为一个证据区域；每个主要区域再导出 baseline、replay、diff 和 overlay 四张裁剪图，供人工或后续 AI 审查。重跑某个 case 后，目录中由 compare 生成且已不被新 `result.json` 引用的旧证据图会自动删除，其他文件不受影响。

本地比较始终使用原始 PNG 尺寸，不会缩放；两图尺寸不一致直接失败。baseline、replay 和 diff 继续使用无损 PNG，不能改用有损 JPG：JPG 压缩本身会制造像素差异，破坏“完全一致”和区域检测的含义。它以 `pixel.colorTolerance` 过滤抗锯齿等细小渲染噪声，以 `pixel.minRegionPixels` 忽略极小孤立区域，并记录差异区域、差异像素数及比例。`detectedRegionCount` 记录实际检测到的合并区域总数，即使 `regions` 因 `pixel.maxRegions` 只导出其中一部分也不会丢失总数。明显的大区域或高比例差异判为 `failed`；较小但真实的差异判为 `uncertain`，为后续 AI 复核保留证据。`--name` 指定的用例不在该 round 时记为 `skipped`，不会被视为失败。

报告以测试用例为单位保留完整截图时间线（`first`、`intermediate`、`last` 与 `atMs`），并将相邻截图中位置接近的差异区域聚合为 `incidents`。每个 incident 有本地关注等级 `P1`、`P2` 或 `P3`，用于优先排序；它不替代通过/失败判定。所有 case、截图和区域均固定包含 `ai.status` 字段；未启用 AI 时为 `not_requested`，像素完全一致或硬失败时为 `skipped`。

添加 `--ai` 后，工具仅将存在真实差异且不是硬失败的 case 发送给已配置的 OpenAI 兼容多模态接口。一次请求覆盖一个完整 testcase：结构化时间线保留全部步骤；每个存在差异的步骤依次附 baseline 全图、replay 全图，以及该步骤的一个或多个 baseline/replay/diff/overlay 四宫格。四宫格会按区域宽高比动态调整整体尺寸，最长边不超过 `maxImageDimension`；四个标题各自位于与画面隔离的标题栏中。prompt 会告诉 AI 标题、边框与留白只是排版，不是产品差异。每个四宫格均附原始图坐标 `rect` 和包含周边上下文的 `contextRect`，所以 AI 能知道它属于第几步、位于屏幕何处，以及同一步的其他差异区域。实际发送的四宫格会保存在 case 报告目录，并通过 region 的 `aiEvidencePath` 写入 `result.json`，方便调试者核对 AI 当时看到的证据；未使用 `--ai` 时不生成这些额外拼图。

本地像素 diff、差异像素计数、区域识别和坐标始终在原始 PNG 尺寸上完成，绝不因 AI 缩放而改变。仅在发送 AI 前，baseline/replay 全图和四宫格才按 `ai.maxImageDimension` 缩放，默认最长边为 1080，保持比例且不放大；设为 `0` 可禁用缩放。`ai.maxEvidenceRegions` 默认每个 testcase 最多附 10 张区域四宫格。工具先为每个有差异的步骤保留最大区域，再以全 testcase 的差异像素数补满剩余名额，避免单个步骤占满全部证据；若差异步骤本身已超过上限，则优先选择步骤最大区域中差异像素较多者。报告与 prompt 会明确列出 rect 差异总数和未附图区域，AI 不得把未附区域视为不存在。

AI 同时返回 testcase、步骤和区域三级结果：每层先在 `ai.observation` 客观描述看到了什么，再在 `ai.reason` 说明这些观察为何支持 `ai.verdict`，并填入 `finalVerdict`；本地 `status` 和像素证据始终保留。工具会严格验收三级响应：任一步骤或区域失败会使 testcase 失败，任一待确认会使 testcase 至少待确认；缺少已提交步骤/区域、缺少必需的 observation/reason、testcase confidence 不在 0～1，或仍有区域未附图时，不允许从本地失败自动改判为通过。prompt 还会明确禁止描述未附图步骤的具体 UI、臆测测试脚本意图，并在全部图片之后再次列出必须覆盖的步骤和区域，要求综合整个 testcase 输出 JSON。

命令逐 case 输出“计算像素差异”和“AI 语义复核”进度，同时输出本地汇总和最终汇总。AI 请求期间可按 Ctrl+C 取消；工具会停止重试，保存已完成/取消结果，整轮报告标记 `comparisonCancelled`，并返回退出码 130。未启用 AI 时 `finalVerdict` 等于本地 `status`；启用 AI 后退出码以最终汇总为准，`failed` 或 `needs_review` 返回非零。AI 连接失败会按配置重试；最终仍失败时会在 testcase、已提交步骤及区域的 `ai.status` 中记录失败，而不是留下 `not_requested`。

### 配置 AI 复核

在数据根目录的 `config.json` 中填写 `ai`；之后在 compare 时添加 `--ai`：

```json
{
  "ai": {
    "baseUrl": "https://api.siliconflow.cn/v1",
    "apiKey": "<你的 SiliconFlow API Key>",
    "model": "Qwen/Qwen3.5-122B-A10B",
    "contextPrompt": "被测软件是 BIM 桌面软件。3D 动态测距允许因鼠标落点产生小幅变化。诊断步骤显示 e2etest 数据目录；回放创建的 replays、testcases、logs 目录及其数量、排序、修改时间变化属于预期。错误弹窗、关键模型或工具状态缺失、流程跳转错误应判为失败。",
    "enableThinking": false,
    "maxImageDimension": 1080,
    "maxEvidenceRegions": 10,
    "maxAttempts": 3,
    "retryDelayMs": 1000,
    "timeoutMs": 300000
  }
}
```

```powershell
.\e2etest.exe compare --round <roundId> --ai
```

`baseUrl` 是 OpenAI 兼容接口的基址（不要加 `/chat/completions`）；`model` 使用服务商给出的模型 ID。`contextPrompt` 是可选的项目背景，可描述被测软件、业务术语、允许的动态变化和必须关注的错误；通用 prompt 不再默认放行文件数量、时间或 3D 数值变化，项目确实允许时应在这里明确说明。不要在其中放 API key 等秘密。`enableThinking` 可设为 `true` 或 `false`，用于支持该字段的模型；完全省略时请求中也不会发送非标准的 `enable_thinking` 字段。`maxImageDimension` 是上传图片和动态四宫格的最长边，`0` 表示全图不缩放；四宫格在 `0` 时仍以 1080 为生成预算，避免无界拼图。`maxEvidenceRegions` 默认是 `10`，证据先覆盖不同步骤，再用差异像素较大的区域补满。prompt 会同时告知 AI 本 testcase 发现的全部 rect 差异数量、已附区域以及未附图区域的 ID。`maxAttempts` 默认 `3`，只对网络连接失败、408、429 和 5xx 重试；`retryDelayMs` 默认 `1000`，按指数退避且单次最多等待 10 秒；`timeoutMs` 是包含重试在内的总超时。基准图、实际图的本地像素比较始终使用原始尺寸，不受这些 AI 传输参数影响。

API key 会写入 `config.json`，因此不要提交该文件。若希望密钥完全不落盘，可以将 `baseUrl`、`model` 和 `apiKey` 保持为空，并在运行命令的同一 PowerShell 会话设置环境变量；环境变量只在对应配置项为空时生效：

```powershell
$env:E2ETEST_AI_BASE_URL = "https://api.siliconflow.cn/v1"
$env:E2ETEST_AI_MODEL = "Qwen/Qwen3.5-122B-A10B"
$env:E2ETEST_AI_API_KEY = "<你的 SiliconFlow API Key>"
.\e2etest.exe compare --round <roundId> --ai
```

默认阈值可在 `config.json` 的 `pixel` 中调整：

```json
{
  "pixel": {
    "colorTolerance": 12,
    "minRegionPixels": 9,
    "failChangedPixelRatio": 0.01,
    "failLargestRegionPixels": 2500,
    "regionPaddingPixels": 32,
    "maxRegions": 20
  }
}
```

## 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 命令成功 |
| `1` | 运行失败或至少一个测试用例失败 |
| `2` | 参数错误、目标不存在或没有可回放测试用例 |
| `130` | 用户取消回放 |

## 推荐的录制/回放验收

在主屏执行：

```powershell
.\e2etest.exe record --name core-acceptance
```

录制内容建议包含：

1. 鼠标移动、点击和拖拽；
2. 普通文字输入；
3. `Ctrl+A`、Shift 或 Alt 组合键；
4. 多次按截图键；
5. 按停止键并等待托盘提示完成。

恢复被测软件初始状态后执行：

```powershell
.\e2etest.exe replay --name core-acceptance
```

检查 `replays/<roundId>/result.json`、测试用例 `result.json` 和 replay 截图数量是否与 baseline 一致。
