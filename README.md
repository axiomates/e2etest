# E2ETest

E2ETest 是一个面向 Windows 桌面软件的端到端测试工具。它通过全局键鼠录制、时间轴回放和指定时点截图，建立可重复执行的测试样例。

当前已实现：

- 全局鼠标、键盘、滚轮录制
- 键盘扫描码、扩展键、组合键和鼠标拖拽记录
- 自定义单键截图/停止控制键
- 截图事件时间轴与停止时自动结尾截图
- 主显示器截图，非全屏模式排除任务栏
- `SendInput` 时间轴回放
- 样例级重置命令、超时和批量错误隔离
- 不可变样例版本、原子版本切换和跨进程读写锁
- Console + 按天/大小轮转的文件日志
- Windows x64 自包含单文件发布

尚未实现：

- 本地像素与 openai-chat 多模态图片对比
- 可选的图形化测试样例管理与可视化报告（是否实现待定）
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

CLI 默认使用当前工作目录作为数据根目录，也可以通过 `--root <目录>` 指定。

```text
<root>/
  config.json
  logs/
  samples/
    <sampleId>/
      current.json
      versions/
        <versionId>/
          manifest.json
          baseline/
  replays/
    <roundId>/
      result.json
      samples/
        <sampleId>/
          result.json
          shot-0001.png
```

每次重录会创建不可变版本，全部截图和 manifest 校验成功后才原子切换 `current.json`。失败录制不会覆盖当前有效版本。

旧 schema 1 样例存在 DPI 坐标歧义，不支持直接回放，需要重新录制。当前 manifest schema 为 2。

## CLI 总览

```powershell
.\e2etest.exe help
```

当前命令：

```text
e2etest record
e2etest replay
e2etest config init
e2etest config show
```

### 通用参数

| 参数 | 说明 |
| --- | --- |
| `--root <目录>` | 指定配置、样例、回放和日志的数据根目录；默认当前工作目录 |

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
    "samples": "./samples",
    "replays": "./replays",
    "reports": "./reports"
  },
  "logging": {
    "directory": "./logs",
    "minimumLevel": "Information",
    "retainedFileCount": 14
  }
}
```

控制键只支持单键，例如 `F11`、`F12`、`Pause`、字母或数字；不支持 `Ctrl+F11` 形式的组合控制键。配置的截图键和停止键不会作为普通键盘事件写入时间轴。

如果代码默认值已更新，但目录中存在旧 `config.json`，以现有配置文件为准。

日志同时写入 stderr 和日志文件：

```text
logs\e2etest-YYYYMMDD.log
```

日志按天和 50 MB 文件大小轮转，默认保留 14 个文件。

## 录制命令

```powershell
.\e2etest.exe record `
  --sample <sampleId> `
  [--name <显示名称>] `
  [--fullscreen | --no-fullscreen] `
  [--json] `
  [--root <目录>]
```

参数：

| 参数 | 必需 | 说明 |
| --- | --- | --- |
| `--sample <sampleId>` | 是 | 稳定样例 ID；允许字母、数字、点、下划线和连字符，长度 1～80 |
| `--name <显示名称>` | 否 | 测试人员看到的名称；默认使用 sampleId |
| `--fullscreen` | 否 | 强制整张主屏截图，包含任务栏 |
| `--no-fullscreen` | 否 | 强制使用主屏工作区截图，排除任务栏 |
| `--json` | 否 | stdout 输出 NDJSON 状态事件，供未来 GUI 管理器调用 |
| `--root <目录>` | 否 | 指定数据根目录 |

截图模式优先级：命令行参数高于 `config.json` 中的 `record.fullscreen`。

示例：

```powershell
.\e2etest.exe record --sample login-flow --name "登录流程"
```

录制开始后控制台会隐藏，只保留托盘图标：

- 默认 F11：截图
- 默认 F12：停止录制
- 托盘右键也可以截图或停止

停止后会：

1. 固化结尾截图；
2. 等待 PNG 后台编码完成；
3. 校验时间轴、截图映射、尺寸和路径；
4. 提交不可变版本；
5. 弹出完成或失败通知。

`--json` 模式会在 stdout 输出一行一个 JSON：

```json
{"type":"recording_started","sampleId":"login-flow"}
{"type":"recording_completed","sampleId":"login-flow","eventCount":100,"screenshotCount":3,"durationMs":12000}
```

失败时输出 `recording_failed`，并返回非零退出码。

## 回放命令

```powershell
.\e2etest.exe replay `
  [--sample <sampleId>] `
  [--round <roundId>] `
  [--root <目录>]
```

参数：

| 参数 | 必需 | 说明 |
| --- | --- | --- |
| `--sample <sampleId>` | 否 | 只回放指定样例；省略时回放全部有效样例 |
| `--round <roundId>` | 否 | 指定本轮 ID；省略时自动生成唯一 ID |
| `--root <目录>` | 否 | 指定数据根目录 |

示例：

```powershell
# 回放单条样例
.\e2etest.exe replay --sample login-flow

# 回放全部样例
.\e2etest.exe replay

# 指定轮次 ID
.\e2etest.exe replay --round nightly-20260722
```

回放行为：

- 隐藏控制台，避免进入截图；
- 校验主屏分辨率、DPI、截图尺寸和 manifest；
- 执行样例的 `resetCommand`；
- 根据扫描码和时间轴注入键鼠事件；
- 在截图实际抓取时点生成 replay 截图；
- 在 `durationMs` 时点生成结尾图；
- 单个样例失败不会影响其他样例；
- 异常或取消时尽力释放所有按下的键和鼠标按钮；
- Ctrl+C 取消后返回退出码 130，并为未执行样例写入 cancelled 结果。

样例级回放配置保存在样例 metadata 中：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `resetCommand` | `null` | 回放前执行的 Windows shell 命令 |
| `resetWaitMs` | `3000` | reset 成功后等待时间 |
| `resetTimeoutMs` | `30000` | reset 超时；超时后终止进程树 |
| `speedFactor` | `1.0` | 回放速度；1.0 为原始时序 |
| `maxIdleGapMs` | `0` | 0 表示严格时间轴；大于 0 时压缩超长空闲间隔 |

## 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 命令成功 |
| `1` | 运行失败或至少一个样例失败 |
| `2` | 参数错误、缺少样例或无可运行样例 |
| `130` | 用户取消回放 |

## 推荐的录制/回放验收

在主屏执行：

```powershell
.\e2etest.exe record --sample core-acceptance
```

录制内容建议包含：

1. 鼠标移动、点击和拖拽；
2. 普通文字输入；
3. `Ctrl+A`、Shift 或 Alt 组合键；
4. 多次按截图键；
5. 按停止键并等待托盘提示完成。

恢复被测软件初始状态后执行：

```powershell
.\e2etest.exe replay --sample core-acceptance
```

检查 `replays/<roundId>/result.json`、样例 `result.json` 和 replay 截图数量是否与 baseline 一致。
