param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$cliProject = Join-Path $root "src\E2ETest.Cli\E2ETest.Cli.csproj"
$launcherProject = Join-Path $root "src\E2ETest.RecordLauncher\E2ETest.RecordLauncher.csproj"
$viewerProject = Join-Path $root "src\E2ETest.ReportViewer\E2ETest.ReportViewer.csproj"
$output = Join-Path $root "publish\$Runtime-single"

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

dotnet publish $cliProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

$exe = Join-Path $output "e2etest.exe"
if (-not (Test-Path $exe)) {
    throw "发布失败：未生成 $exe"
}

$stream = [System.IO.File]::OpenRead($exe)
try {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
        $hash = ([System.BitConverter]::ToString($hashBytes)).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Write-Host "发布完成: $exe"
Write-Host "SHA256: $hash"

dotnet publish $launcherProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

$launcherExe = Join-Path $output "e2etest-record-launcher.exe"
if (-not (Test-Path $launcherExe)) {
    throw "发布失败：未生成 $launcherExe"
}
$launcherHash = (Get-FileHash -LiteralPath $launcherExe -Algorithm SHA256).Hash
Write-Host "录制启动器发布完成: $launcherExe"
Write-Host "录制启动器 SHA256: $launcherHash"

dotnet publish $viewerProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

$viewerExe = Join-Path $output "e2etest-report-viewer.exe"
if (-not (Test-Path $viewerExe)) {
    throw "发布失败：未生成 $viewerExe"
}
$viewerHash = (Get-FileHash -LiteralPath $viewerExe -Algorithm SHA256).Hash
Write-Host "报告查看器发布完成: $viewerExe"
Write-Host "报告查看器 SHA256: $viewerHash"

$localConfig = Join-Path $root "config.json"
if (Test-Path -LiteralPath $localConfig) {
    $publishedConfig = Join-Path $output "config.json"
    Copy-Item -LiteralPath $localConfig -Destination $publishedConfig -Force
    Write-Host "已复制本地配置（不纳入 Git）: $publishedConfig"
}
