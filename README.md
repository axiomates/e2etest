# E2ETest

E2ETest 是一个面向 Windows 桌面软件的端到端测试工具。它通过全局键鼠录制、时间轴回放和指定时点截图，建立可重复执行的测试用例。

当前已实现：

- 全局鼠标、键盘、滚轮录制
- 键盘扫描码、扩展键、组合键和鼠标拖拽记录
- 自定义单键截图/停止控制键
- 截图事件时间轴与停止时自动结尾截图
- 主显示器截图，非全屏模式排除任务栏
- `SendInput` 时间轴回放
- 测试用例级重置命令、超时和批量错误隔离
- 测试用例创建、列出、删除、播放和跨进程读写锁
- Console + 按天/大小轮转的文件日志
- Windows x64 自包含单文件发布

尚未实现：

- 本地像素与 openai-chat 多模态图片对比
- 可视化报告
- `compare` / `run` 命令

## 运行前提

- Windows x64
- 录制和回放在同一台机器或等价环境运行
- 分辨率、DPI、缩放和主显示器布局固定
- 实际测试操作应位于主显示器；副屏鼠标事件会被过滤
- 数据目录位于本地磁盘

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
publish\win-x64-single\e2etest.exe
```

该 EXE 是 Windows x64 自包含单文件，目标机器无需安装 .NET。也可以手动发布：

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

CLI 默认使用当前工作目录作为数据根目录，也可以通过 `--root <目录>` 指定。目录结构如下：

```text
<root>/                                      # 数据根目录
  config.json                                # 热键、截图、路径、日志及后续对比配置
  logs/                                      # 按天和文件大小轮转的运行日志
    e2etest-YYYYMMDD.log                     # 当天日志；超出大小限制时自动分卷
  testcases/                                 # 已创建的全部测试用例，可由 paths.testCases 修改
    <name>/                                  # 单个测试用例；目录名就是测试用例名称
      manifest.json                          # 录制环境、回放设置、输入时间轴及截图索引
      baseline/                              # 录制时生成的基准截图
        shot-0001.png                        # 手动截图或停止时生成的结尾截图
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

当前 manifest schema 为 3；旧版本化 `samples` 数据不兼容，需要重新录制。

## CLI 总览

```powershell
.\e2etest.exe help
```

当前命令：

```text
e2etest record
e2etest replay
e2etest testcase list
e2etest testcase delete
e2etest config init
e2etest config show
```

### 通用参数

| 参数 | 说明 |
| --- | --- |
| `--root <目录>` | 指定配置、测试用例、回放和日志的数据根目录；默认当前工作目录 |

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

停止后会固化结尾截图、等待 PNG 后台编码、校验时间轴和截图，然后直接提交测试用例。同名测试用例已存在时返回退出码 2，需先删除后再创建。

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
- 先执行测试用例的 `resetCommand`，成功并等待后再开始回放；
- 根据扫描码和时间轴注入键鼠事件；
- 在截图实际抓取时点生成 replay 截图；
- 在 `durationMs` 时点生成结尾图；
- 单个测试用例失败不会影响其他测试用例；
- 异常或取消时尽力释放所有按下的键和鼠标按钮；
- Ctrl+C 取消后返回退出码 130，并为未执行测试用例写入 cancelled 结果。

测试用例级回放配置保存在 `manifest.json` 中：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `resetCommand` | `null` | 回放前执行的 Windows shell 命令 |
| `resetWaitMs` | `3000` | reset 成功后等待时间 |
| `resetTimeoutMs` | `30000` | reset 超时；超时后终止进程树 |
| `speedFactor` | `1.0` | 回放速度；1.0 为原始时序 |
| `maxIdleGapMs` | `0` | 0 表示严格时间轴；大于 0 时压缩超长空闲间隔 |

### 回放前重置（`resetCommand`）

`resetCommand` 是**每个测试用例回放开始前**执行的一条 Windows shell 命令，适合将被测软件恢复到录制时的初始状态，例如关闭残留进程、清理测试数据或重新启动应用。它不在 `config.json` 或命令行参数中配置；请在录制完成后编辑对应测试用例的 `testcases/<name>/manifest.json`：

```json
{
  "replay": {
    "resetCommand": "powershell -NoProfile -Command \"& 'C:\\\\scripts\\\\reset-test-app.ps1'\"",
    "resetWaitMs": 3000,
    "resetTimeoutMs": 30000
  }
}
```

单个测试用例的执行顺序如下：

```text
resetCommand → 等待 resetWaitMs → 回放键鼠事件并截图
```

`resetCommand` 返回非零退出码、启动失败或超时时，该测试用例会标记为失败，不会执行其输入回放。超时后工具会尝试终止该命令启动的进程树；如果进程树无法终止，为避免污染后续用例，会取消本轮剩余回放。

目前没有整轮回放开始/结束 hook，也没有单个测试用例结束后的可配置 hook。工具在异常或取消时会自动尽力释放仍按下的键和鼠标按钮，但业务清理应放入重置脚本，或由外部调度器在 `e2etest replay` 前后执行。

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
