<#
.SYNOPSIS
    把插件文件夹打包成 PCL Harness 插件包（.pclplugin）。

.DESCRIPTION
    插件包本质就是一个 zip：根目录（或唯一的一层子目录）里放着 plugin.json 和插件 DLL。
    启动器可以直接导入 .pclplugin、含 plugin.json 的 zip，或者单个 DLL（需带 AssemblyMetadata）。

.PARAMETER Project
    插件文件夹，必须包含 plugin.json。

.PARAMETER Output
    输出文件路径，默认为插件文件夹同级目录下的 <id>-<version>.pclplugin。

.EXAMPLE
    powershell -NoProfile -File tools\pack-plugin.ps1 -Project samples\InstanceMenu
#>
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$Output
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "打包失败：$Message" -ForegroundColor Red
    exit 1
}

$root = (Resolve-Path -LiteralPath $Project).Path
$manifestPath = Join-Path $root 'plugin.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { Fail "找不到 $manifestPath" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $manifest.id) { Fail 'plugin.json 缺少 id' }

$id = [string]$manifest.id
$version = if ($manifest.version) { [string]$manifest.version } else { '0.0.0' }

if (-not $Output) {
    $Output = Join-Path (Split-Path -Parent $root) "$id-$version.pclplugin"
}
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }

# 只打插件自己的文件：跳过数据目录、备份、隐藏文件与依赖编译产物
$skipDirs = @('data', '.backup', '.tmp', 'bin', 'obj')
$skipFiles = @('plugin.pdb', '.DS_Store', 'Thumbs.db')

$temp = Join-Path ([IO.Path]::GetTempPath()) ("pclplugin-" + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $temp $id
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    Get-ChildItem -LiteralPath $root -Force | Where-Object {
        ($skipDirs -notcontains $_.Name) -and ($skipFiles -notcontains $_.Name) -and (-not $_.Name.StartsWith('.'))
    } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $Output, [IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}

$size = (Get-Item -LiteralPath $Output).Length
Write-Host "已打包：$Output（$id v$version，$size 字节）" -ForegroundColor Green
