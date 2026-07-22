param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\E2ETest.Cli\E2ETest.Cli.csproj"
$output = Join-Path $root "publish\$Runtime-single"

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

dotnet publish $project `
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
