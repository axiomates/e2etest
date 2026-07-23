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
e2etest testcase annotate
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
    "enableThinking": null,
    "maxImageDimension": 1080,
    "maxEvidenceRegions": 10,
    "maxAttempts": 3,
    "retryDelayMs": 1000,
    "timeoutMs": 300000
  },
  "pixel": {
    "colorTolerance": 12,
    "minRegionPixels": 9,
    "failChangedPixelRatio": 0.01,
    "failLargestRegionPixels": 2500,
    "regionPaddingPixels": 32,
    "maxRegions": 20
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

### `config.json` 配置字段说明

下表集中列出示例文件中的全部配置。时间均以毫秒为单位；相对路径均相对于数据根目录（`--root`、当前目录中的 `config.json`，或 EXE 目录中回退找到的 `config.json` 所在目录）。

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `schemaVersion` | `1` | 当前配置文件结构的版本标识 |
| `hotkeys.startStop` | `F12` | 停止录制并保存的全局单键热键 |
| `hotkeys.screenshot` | `F11` | 录制期间手动截图的全局单键热键 |
| `record.fullscreen` | `false` | `false` 截取主屏工作区并排除任务栏；`true` 截取完整主屏；可被录制命令参数覆盖 |
| `replay.betweenTestCasesMs` | `10000` | 同一轮中，上一条 `afterTestCase` 完成到下一条 `beforeTestCase` 开始之间的最短间隔；`0` 表示不等待 |
| `replay.betweenRoundsMs` | `20000` | 上一轮 `afterRound` 完成到下一轮 `beforeRound` 开始之间的最短间隔；跨 CLI 进程生效，`0` 表示不等待 |
| `ai.baseUrl` | 空 | OpenAI Chat Completions 兼容服务的 API 基址，例如 `https://api.siliconflow.cn/v1` |
| `ai.apiKey` | 空 | AI 服务密钥；以明文保存在本机配置中，禁止提交到 Git |
| `ai.model` | 空 | 服务端模型标识，例如 `Qwen/Qwen3.5-122B-A10B` |
| `ai.contextPrompt` | 空 | 被测软件、业务含义、允许变化和失败条件等项目背景，会附加到固定审查提示词中 |
| `ai.enableThinking` | 不发送 | OpenAI 兼容服务的非标准 `enable_thinking` 字段；`true`/`false` 表示显式发送，省略或 `null` 表示不发送 |
| `ai.maxImageDimension` | `1080` | 报告四宫格及发给 AI 的图片最长边；`0` 表示上传全图不缩放，四宫格仍按 1080 生成；不影响本地原始像素比较 |
| `ai.maxEvidenceRegions` | `10` | 每个测试用例最多发送的差异区域四宫格数量，按差异像素数从大到小选择 |
| `ai.maxAttempts` | `3` | 单个测试用例遇到瞬时 AI 请求故障时的最大请求次数 |
| `ai.retryDelayMs` | `1000` | 第一次 AI 重试前的等待时间；后续指数退避，单次最长 10 秒 |
| `ai.timeoutMs` | `300000` | 单个测试用例整个 AI 复核过程的总超时，包含所有重试 |
| `pixel.colorTolerance` | `12` | RGB 任一通道差值不超过该值时视为渲染噪声 |
| `pixel.minRegionPixels` | `9` | 小于该像素数的差异连通区域视为孤立噪声并忽略 |
| `pixel.failChangedPixelRatio` | `0.01` | 有效差异像素比例达到该值时，本地直接判定失败；`0.01` 即 1% |
| `pixel.failLargestRegionPixels` | `2500` | 最大单个差异区域达到该像素数时，本地直接判定失败 |
| `pixel.regionPaddingPixels` | `32` | 导出区域 baseline/replay/diff/overlay 证据图时向四周扩展的上下文像素 |
| `pixel.maxRegions` | `20` | 每张截图最多导出的差异区域数量；实际检测总数仍记录在报告中 |
| `replayHooks.beforeRound` | `null` | 每轮回放开始前运行一次的 Windows shell 命令 |
| `replayHooks.afterRound` | `null` | 每轮结束后运行一次；失败或取消时也会尽力执行 |
| `replayHooks.beforeTestCase` | `null` | 每条测试用例回放前运行，适合重置软件和测试数据 |
| `replayHooks.afterTestCase` | `null` | 每条测试用例结束后运行，适合清理本例资源 |
| `replayHooks.timeoutMs` | `30000` | 每一条 Hook 命令各自的超时时间 |
| `paths.testCases` | `./testcases` | 录制基准与测试用例目录 |
| `paths.replays` | `./replays` | replay 轮次和实际截图目录 |
| `paths.reports` | `./reports` | compare 结构化报告和证据图目录 |
| `logging.directory` | `./logs` | CLI 日志目录 |
| `logging.minimumLevel` | `Information` | 最低日志级别，可用 `Verbose`、`Debug`、`Information`、`Warning`、`Error`、`Fatal`；无效名称回退为 `Information` |
| `logging.retainedFileCount` | `200` | 最多保留的日志文件数，实际值会限制在 1～200 |
| `logging.retainedDays` | `30` | 日志最多保留的天数，最小按 1 天处理 |

只有使用 `compare --ai` 或 `run --ai` 时才会请求 AI。`ai.baseUrl`、`ai.apiKey`、`ai.model` 为空时，可以分别由当前进程的 `E2ETEST_AI_BASE_URL`、`E2ETEST_AI_API_KEY`、`E2ETEST_AI_MODEL` 环境变量补充；配置文件中的非空值优先。

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
.\e2etest.exe testcase annotate --name "登录流程" `
  [--focus "测试重点"] `
  [--criteria "样例判断标准"] `
  [--root <目录>]
```

`list` 每行输出一个测试用例名称。`delete` 的 `--name` 为必需参数；缺少名称或目标不存在时返回退出码 2。

### 直接编辑样例 AI 指引（常见方式）

录制完成后，QA 直接编辑 testcase 的 `manifest.json` 是常见且正式支持的工作流，无需重新录制。文件位置为：

```text
<数据根目录>\<paths.testcases>\<测试用例名称>\manifest.json
```

默认配置下，例如样例名为“创建立柱”，路径是 `testcases\创建立柱\manifest.json`。在 JSON 顶层加入或修改下面两个可选字段；这是字段示意，不要用它替换原 manifest 中的 `schemaVersion`、时间线、截图和回放字段：

```json
{
  "testFocus": "重点检查立柱是否在目标楼层成功创建，以及流程末尾是否出现错误提示。",
  "acceptanceCriteria": "立柱可见、属性与目标一致且没有错误提示时通过；立柱缺失或出现错误提示时失败；仅凭截图无法确认属性时待人工确认。"
}
```

`testFocus` 是希望 AI 优先观察的测试重点，`acceptanceCriteria` 是该样例自己的通过、失败和待确认标准。两项均可单独存在；字段缺失、值为 `null` 或内容只有空白时，对应内容不会进入 AI prompt。每项最多 4000 字符。不需要修改 `schemaVersion`。

手动编辑时应遵守以下约束：

- 确保该样例没有正在执行 record、replay、compare 或 `run`；保存完成后再执行相关命令；
- 保持文件为合法 JSON；字段之间需要逗号，文本中的双引号、反斜杠和换行必须按 JSON 规则转义；
- 不要修改 `name`、`shots`、`events`、`capture` 等录制结构，除非明确了解 manifest 校验规则；
- 修改只影响后续 compare；已经生成的报告和 AI 结论不会自动变化，需要重新执行对应 round 的 compare；
- 可以在 replay 完成后、compare 开始前补充指引，因为指引不改变截图和回放时间线。

如果希望工具负责测试用例锁、原子写入和 manifest 校验，可以使用等价的 `annotate` 命令：

```powershell
.\e2etest.exe testcase annotate --name "创建立柱" `
  --focus "重点检查立柱是否在目标楼层成功创建，以及流程末尾是否出现错误提示。" `
  --criteria "立柱可见、属性与目标一致且没有错误提示时通过；立柱缺失或出现错误提示时失败；仅凭截图无法确认属性时待人工确认。"
```

命令至少需要 `--focus` 或 `--criteria` 中的一项，未提供的另一项保持原值，再次执行可单独修改。manifest 中的 `testFocus` / `acceptanceCriteria` 会由 compare 复制到最终报告。它们只影响当前样例，和 `ai.contextPrompt` 的全局软件背景互补；无论 compare 是否使用 `--ai`，报告结构都会保留这两项。使用 `--ai` 时，prompt 要求模型优先围绕这些指引观察和解释，但不得用文字指引覆盖图像事实或臆测未附图内容。

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
- 存在局部差异时默认显示该区域的四宫格，可切换差异叠加、差异区域、左右全图对比、baseline 和 replay；
- AI 的“看到了什么”和“为什么这样判断”与当前步骤/区域放在一起；
- 单例 `result.json` 会覆盖整轮汇总中的旧副本，因此单独重跑 AI 后查看器能显示最新结论。

查看器定位是“先找风险、再下钻证据”。首版不提供重新执行测试、修改 verdict 或重新请求 AI 的功能。

`compare` 读取已有的 `replays/<roundId>/`，不重新执行输入回放。仍处于 `running` 状态或保留 `.running.lock` 的 round 会被拒绝，避免对尚未完成的回放生成部分报告；同一 round 同时只能运行一个 compare。新生成的 replay 会记录每张 baseline 的 SHA-256，若 baseline 在 replay 后被重新录制或改变，compare 会以 `baseline_changed` 硬失败拒绝混用；旧 round 没有哈希时仍可兼容比较。报告保留 `replayStatus`、`replayError` 和 `replayLifecycleSucceeded`；包括 `afterRound` 在内的回放生命周期失败会使 compare 返回非零，即使所有截图语义一致也不能覆盖。完整 round 的结果写入 `reports/<roundId>/result.json`；使用 `--name` 时只更新对应的 `reports/<roundId>/testcases/<name>/result.json`，不会用单例结果覆盖已有整轮汇总。每张截图还会生成 `diff-shot-xxxx.png`（仅显示差异）和 `overlay-shot-xxxx.png`（在 replay 图上以不同半透明颜色标记差异区域）。像素碎片会按带 padding 的相邻上下文自动合并为一个证据区域；每个主要区域再导出 baseline、replay、diff 和 overlay 四张裁剪图，供人工或后续 AI 审查。重跑某个 case 后，目录中由 compare 生成且已不被新 `result.json` 引用的旧证据图会自动删除，其他文件不受影响。

本地比较始终使用原始 PNG 尺寸，不会缩放；两图尺寸不一致直接失败。baseline、replay 和 diff 继续使用无损 PNG，不能改用有损 JPG：JPG 压缩本身会制造像素差异，破坏“完全一致”和区域检测的含义。它以 `pixel.colorTolerance` 过滤抗锯齿等细小渲染噪声，以 `pixel.minRegionPixels` 忽略极小孤立区域，并记录差异区域、差异像素数及比例。`detectedRegionCount` 记录实际检测到的合并区域总数，即使 `regions` 因 `pixel.maxRegions` 只导出其中一部分也不会丢失总数。明显的大区域或高比例差异判为 `failed`；较小但真实的差异判为 `uncertain`，为后续 AI 复核保留证据。`--name` 指定的用例不在该 round 时记为 `skipped`，不会被视为失败。

报告以测试用例为单位保留完整截图时间线（`first`、`intermediate`、`last` 与 `atMs`），并将相邻截图中位置接近的差异区域聚合为 `incidents`。每个 incident 有本地关注等级 `P1`、`P2` 或 `P3`，用于优先排序；它不替代通过/失败判定。所有 case、截图和区域均固定包含 `ai.status` 字段；未启用 AI 时为 `not_requested`，像素完全一致或硬失败时为 `skipped`。

无论是否添加 `--ai`，工具都会为报告中每个已导出的差异区域生成 baseline/replay/diff/overlay 四宫格，并通过 region 的 `aiEvidencePath` 写入 `result.json`；报告查看器默认显示所选区域的四宫格。区域导出数量仍受 `pixel.maxRegions` 控制。添加 `--ai` 后，工具仅将存在真实差异且不是硬失败的 case 发送给已配置的 OpenAI 兼容多模态接口。一次请求覆盖一个完整 testcase：结构化时间线保留全部步骤；每个存在差异的步骤依次附 baseline 全图、replay 全图，以及从报告四宫格中按 `ai.maxEvidenceRegions` 选择的一个或多个证据。四宫格会按区域宽高比动态调整整体尺寸，最长边不超过 `maxImageDimension`；四个标题各自位于与画面隔离的标题栏中。prompt 会告诉 AI 标题、边框与留白只是排版，不是产品差异。每个发送给 AI 的四宫格均附原始图坐标 `rect` 和包含周边上下文的 `contextRect`，所以 AI 能知道它属于第几步、位于屏幕何处，以及同一步的其他差异区域。

本地像素 diff、差异像素计数、区域识别和坐标始终在原始 PNG 尺寸上完成，绝不因四宫格或 AI 缩放而改变。报告四宫格按 `ai.maxImageDimension` 生成；发送 AI 前，baseline/replay 全图也按该值缩放，默认最长边为 1080，保持比例且不放大。设为 `0` 时上传全图不缩放，四宫格仍以 1080 为生成预算，避免无界拼图。`ai.maxEvidenceRegions` 默认每个 testcase 最多发送 10 张区域四宫格，但不限制报告在本地生成的四宫格数量。工具先为每个有差异的步骤保留最大区域，再以全 testcase 的差异像素数补满剩余名额，避免单个步骤占满全部 AI 证据；若差异步骤本身已超过上限，则优先选择步骤最大区域中差异像素较多者。报告与 prompt 会明确列出 rect 差异总数和未发送区域，AI 不得把未附区域视为不存在。

AI 同时返回 testcase、步骤和区域三级结果：每层先在 `ai.observation` 客观描述看到了什么，再在 `ai.reason` 说明这些观察为何支持 `ai.verdict`，并填入 `finalVerdict`；本地 `status` 和像素证据始终保留。工具会严格验收三级响应：任一步骤或区域失败会使 testcase 失败，任一待确认会使 testcase 至少待确认；缺少已提交步骤/区域、缺少必需的 observation/reason、testcase confidence 不在 0～1，或仍有区域未附图时，不允许从本地失败自动改判为通过。prompt 还会明确禁止描述未附图步骤的具体 UI、臆测测试脚本意图，并在全部图片之后再次列出必须覆盖的步骤和区域，要求综合整个 testcase 输出 JSON。

### AI prompt 拼装流水线

AI 复核以一个完整 testcase 为一次请求。代码不会拼出一段不可区分来源的长字符串，而是按下表生成有序的文本与图片 part，依次写入 OpenAI Chat Completions 的一个 `user` 消息中：

| 顺序 | 内部阶段名 | 来源 | 何时加入 | 发给 AI 的内容 |
| --- | --- | --- | --- | --- |
| 1 | `fixed_rules` | 工具内置 | 始终 | baseline/replay 定义、四宫格布局、观察后判断、`passed/failed/needs_review` 规则和 JSON 输出结构 |
| 2 | `project_context` | `config.json` 的 `ai.contextPrompt` | 内容非空时 | 被测软件背景、业务术语、全项目允许变化和失败条件；用 `<project-context>` 明确隔离 |
| 3 | `testcase_guidance` | testcase `manifest.json` 的 `testFocus`、`acceptanceCriteria` | 至少一项非空时 | 当前样例的测试重点和判断标准；用 `<testcase-guidance>` 隔离，只输出实际存在的项 |
| 4 | `timeline_metadata` | 本地 compare 结果 | 始终 | case 名称、步骤顺序与角色、时间、像素结论、所有已导出 rect、证据选择和省略统计 |
| 5 | `shot_header` + 图片 | baseline、replay、报告证据 | 每个被选中且有差异的步骤 | 步骤说明，随后严格按 baseline 全图、replay 全图、区域 metadata、区域四宫格的顺序重复 |
| 6 | `final_contract` | 工具内置 | 始终 | 再次列出必须返回判断的 shotIndex 和 region ID，要求综合完整 testcase、先 observation 后 reason、只返回 JSON |

下面是等价的逻辑模板，用来展示一次请求的实际拼装形状。所有非动态文字都按当前实现完整列出，没有使用省略号代替 prompt；`{{...}}` 只表示运行时动态值，`{{#if ...}}` / `{{/if}}` 表示可选条件，`{{#each ...}}` 表示按顺序重复；`<image: ...>` 代表一个 OpenAI `image_url` part，而不是发送这段文字：

```text
[fixed_rules / text]
你是 Windows 桌面软件端到端测试审查员。baseline 是录制时的预期截图序列，replay 是本次实际截图序列。
你会按时间顺序收到一个完整 testcase 的结构化时间线。仅对存在像素差异的步骤附图：每个这样的步骤先给 baseline 全图（期望）和 replay 全图（实际），随后给该步骤一个或多个区域四宫格。四宫格的左上、右上、左下、右下依次是 baseline、replay、diff（仅差异像素）和 overlay（实际图上的差异位置）。四宫格会根据区域宽高比和上传图片尺寸上限动态调整整体宽高；每格有独立的文字标题栏，标题栏、边框和留白是证据排版，不属于被测界面，也不代表产品差异。四格中的图片保持宽高比并可能留白；不要根据格内显示尺寸推断原始区域大小，真实位置和大小只看 rect/contextRect。同一步的所有已附区域共同构成该步骤证据；不要只根据其中最大的一块下结论。
metadata 中 rect 与 contextRect 使用原始完整截图像素坐标，左上角为 (0,0)；图像为便于传输可能缩放，但这些坐标不缩放。结合步骤顺序、全图、区域位置和局部证据判断。没有附图的步骤只提供本地像素比较 metadata：可以说明其像素比较结果，但不得声称看到了该步骤的具体 UI。不得臆测测试脚本的意图，也不得仅因某类内容通常会动态变化就假定本次变化可接受。
像素不同本身不等于失败。只有项目背景明确允许，或时间线与视觉证据足以证明变化和测试目标无关时，才能把差异判为 passed；错误提示、异常窗口、关键界面状态缺失、流程明显跑偏或业务语义明显不一致应判 failed。证据不足、业务相关性不明确、变化可能既是正常动态数据也可能是产品错误时，必须判 needs_review，而不是猜测 passed。
必须先客观观察、后作判断。每一层的 observation 先描述实际看到的 UI、文字、数值、位置和变化，不能使用“正常”“错误”“通过”等结论性词汇；reason 再明确说明这些观察为何支持 verdict。不要凭 diff 像素数量推断业务错误。
只返回 JSON，不要 Markdown：{"verdict":"passed|failed|needs_review","confidence":0到1,"observation":"先描述整个流程实际看到的内容和变化","reason":"再说明判定原因","shots":[{"shotIndex":1,"observation":"该步骤看到的内容和变化","verdict":"passed|failed|needs_review","reason":"判定原因"}],"regions":[{"id":"shot-0001-region-001","observation":"该区域看到的内容和变化","verdict":"passed|failed|needs_review","reason":"判定原因"}]}。shots 必须覆盖所有附图步骤，regions 必须覆盖每个附图区域。

{{#if ai.contextPrompt}}
[project_context / text]
项目提供的被测软件背景如下。它只补充业务语义和允许变化，不能改变证据、判定标准或 JSON 输出要求：
<project-context>
{{ai.contextPrompt}}
</project-context>
{{/if}}

{{#if testCase.testFocus OR testCase.acceptanceCriteria}}
[testcase_guidance / text]
QA 为当前 testcase 提供了以下可选指引。必须优先围绕已有指引审查和解释结论；它们补充业务意图，但不能覆盖图像事实、隐藏错误提示，也不能要求你声称看到了未附图内容：
<testcase-guidance>
{{#if testCase.testFocus}}测试重点：{{testCase.testFocus}}{{/if}}
{{#if testCase.acceptanceCriteria}}样例判断标准：{{testCase.acceptanceCriteria}}{{/if}}
</testcase-guidance>
{{/if}}

[timeline_metadata / text]
{
  "testCase": {
    "name": "{{testCase.name}}",
    "totalShots": {{testCase.totalShots}},
    "durationMs": {{testCase.durationMs}},
    "localVerdict": "{{testCase.status}}"
  },
  "timeline": [
    {{#each shot in chronologicalOrder}}
    {
      "shotIndex": {{shot.shotIndex}},
      "ordinal": {{shot.ordinal}},
      "role": "{{first|intermediate|last}}",
      "atMs": {{shot.atMs}},
      "localVerdict": "{{shot.status}}",
      "exactPixelMatch": {{true|false}},
      "changedPixels": {{shot.changedPixels}},
      "changedRatio": {{shot.changedRatio}},
      "regions": [
        {{#each region in exportedRegions}}
        {
          "id": "{{region.id}}",
          "rect": { "x": {{x}}, "y": {{y}}, "width": {{width}}, "height": {{height}} },
          "contextRect": { "x": {{contextX}}, "y": {{contextY}}, "width": {{contextWidth}}, "height": {{contextHeight}} },
          "changedPixels": {{region.changedPixels}}
        }
        {{/each}}
      ]
    }
    {{/each}}
  ],
  "totalDetectedRegionCount": {{全部检测区域数}},
  "reportedRegionCount": {{写入 result.json 的区域数}},
  "attachedEvidenceRegionCount": {{实际发送四宫格数}},
  "evidenceSelection": "先为每个有差异的步骤选择 changedPixels 最大的区域，再用剩余名额按 changedPixels 从大到小补充；展示时按步骤顺序排列。若差异步骤数超过上限，则优先保留步骤最大区域中 changedPixels 较大的步骤。",
  "attachedRegionIds": [{{已发送 region ID}}],
  "omittedRegionIds": [{{未发送 region ID}}],
  "unreportedRegionCount": {{检测到但未导出到 result.json 的区域数}},
  "note": "{{#if 没有省略区域}}所有发现的 rect 差异均附四宫格。{{else}}仅按跨步骤优先策略选择部分 rect 差异附四宫格；omittedRegionIds 和 unreportedRegionCount 表示未附图证据，不要把已附图以外的区域视为不存在，也不要仅据已附区域判 passed。{{/if}}"
}
审查范围：本 testcase 的本地像素比较共发现 {{全部检测区域数}} 组 rect 差异；本次按跨步骤优先策略实际附上 {{实际发送四宫格数}} 组四宫格。{{#if 没有省略区域}}没有省略的 rect 差异。{{else}}另有 {{有 ID 但未选择的区域数}} 组有 ID 的未选差异和 {{检测到但未导出的区域数}} 组未导出差异未附图。{{/if}}

{{#each selectedShot in chronologicalOrder}}
[shot_header / text]
步骤 {{selectedShot.ordinal}}/{{testCase.totalShots}}
（shotIndex={{selectedShot.shotIndex}}，role={{selectedShot.role}}，atMs={{selectedShot.atMs}}）：
以下依次为 baseline 全图、replay 全图，以及 {{selectedShot.selectedRegionCount}} 个区域四宫格。

[baseline_full / image]
<image: {{selectedShot.baselinePath}}，最长边限制 {{ai.maxImageDimension}}>

[replay_full / image]
<image: {{selectedShot.replayPath}}，最长边限制 {{ai.maxImageDimension}}>

{{#each selectedRegion in changedPixelsOrder}}
[region_metadata / text]
区域 {{selectedRegion.id}}：
rect=({{x}},{{y}},{{width}},{{height}})，
contextRect=({{contextX}},{{contextY}},{{contextWidth}},{{contextHeight}})，
changedPixels={{selectedRegion.changedPixels}}。

[region_evidence / image]
<image: {{selectedRegion.aiEvidencePath}} 四宫格>
{{/each}}
{{/each}}

[final_contract / text]
证据发送完毕。请综合整个 testcase，而不是只判断最后一张图片；只描述实际附图中可见的 UI，未附图步骤不得补写具体界面。shots 必须覆盖这些 shotIndex：{{attachedShotIndexes}}；regions 必须覆盖这些 id：{{attachedRegionIds}}。先写客观 observation，再写 reason，最后给出 verdict；证据不足时使用 needs_review。只返回 JSON，不要 Markdown，并严格使用开头给出的 JSON 结构。
```

上述有序 part 最终放进下面的 HTTP 请求体；这里也展开所有固定请求字段。`enable_thinking` 仅在 `ai.enableThinking` 配置为 `true` 或 `false` 时出现，配置为 `null` 或省略时，整个字段不会发送：

```text
{
  "model": "{{ai.model}}",
  "temperature": 0.1,
  "max_tokens": 12000,
  {{#if ai.enableThinking is configured}}
  "enable_thinking": {{ai.enableThinking}},
  {{/if}}
  "messages": [
    {
      "role": "user",
      "content": [
        "{{上面按顺序生成的 text 与 image_url parts；没有任何额外隐藏 prompt}}"
      ]
    }
  ]
}
```

上面的条件块是说明性占位符，不是实际 JSON 语法。实际图片 part 的形状固定为 `{"type":"image_url","image_url":{"url":"data:image/png;base64,{{压缩后的图片字节}}"}}`，文本 part 的形状固定为 `{"type":"text","text":"{{对应阶段的完整文本}}"}`。

例如一个 case 有 5 个时间线步骤，但只有步骤 2 存在两个被选中的差异区域，那么 `timeline_metadata` 仍列出 5 步，图片段只会出现一次步骤 2 的 baseline 全图、一次 replay 全图和两张四宫格；不会为其余 4 个完全一致步骤发送图片。

可选阶段采用“整段省略”，不是传空值：`contextPrompt` 为空时没有 `project_context` part；样例重点和判断标准都为空时没有 `testcase_guidance` part；只填写其中一项时只发送该项，另一项不会以 `null`、空字符串、“未设置”或空标签出现在 prompt 中。

像素完全一致的步骤保留在 `timeline_metadata`，但不发送图片；真正有差异且可供 AI 判断的步骤，每步发送两张限尺寸全图和选中的 N 张四宫格。报告会为所有已导出区域生成四宫格，但 AI 只接收 `ai.maxEvidenceRegions` 策略选中的部分；metadata 会明确告诉模型总区域数、已发送 ID 和未发送 ID。四宫格已经包含 baseline/replay/diff/overlay，因此不会再把四张局部裁剪图分别上传。

API key、base URL、重试次数、超时时间、报告本地路径和未被选择的图片二进制不会写进 prompt。`enable_thinking`、模型名和采样参数属于 HTTP 请求字段，也不属于审查文字。AI 返回后仍要经过本地结构校验与三级结果归并，模型原始结论不能直接覆盖硬失败或缺失证据约束。

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
