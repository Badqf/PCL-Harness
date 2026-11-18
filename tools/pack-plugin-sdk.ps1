<#
.SYNOPSIS
    打包 PCL Harness 插件开发包（SDK）为 zip，随 Release 一起分发。

.DESCRIPTION
    素材来自 tools\sdk\：
        README.md            上手说明（{{VERSION}} 会被替换成实际版本号）
        build.ps1            通用编译脚本
        samples\HelloPlugin  最小插件
    再从仓库根目录取 PLUGIN-DEV.md、LICENSE，以及 samples\DemoPlugin、samples\V6Demo、samples\InstanceMenu 三个示例。

.PARAMETER Version
    版本号，默认从 ModBase.vb 的 VersionBaseName 读取。

.PARAMETER OutputDirectory
    zip 输出目录，默认仓库根目录的上一级。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\pack-plugin-sdk.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$sdkSrc = Join-Path $PSScriptRoot "sdk"

if (-not $Version) {
    $modBase = Join-Path $repo "Plain Craft Launcher 2\Modules\Base\ModBase.vb"
    $m = Select-String -Path $modBase -Pattern 'VersionBaseName As String = "([^"]+)"'
    if (-not $m) { throw "无法从 ModBase.vb 读取版本号，请用 -Version 指定。" }
    $Version = $m.Matches[0].Groups[1].Value
}
if (-not $OutputDirectory) { $OutputDirectory = Split-Path $repo -Parent }
# v7.4（安全审计 S1）：版本号会拼进输出路径，先校验再使用
$Version = ([string]$Version).Trim()
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._\-]{0,31}$') {
    throw "版本号不合法（只允许字母数字与 . _ -）：$Version"
}

$name = "PCL-Harness-Plugin-SDK-$Version"
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "$name-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$root = Join-Path $stage $name
New-Item -ItemType Directory -Path $root -Force | Out-Null

# 指南与许可
Copy-Item (Join-Path $repo "PLUGIN-DEV.md") $root
Copy-Item (Join-Path $repo "LICENSE") $root
Copy-Item (Join-Path $repo "docs\API-REQUESTS-market.md") $root -ErrorAction SilentlyContinue

# SDK 自带素材
Copy-Item (Join-Path $sdkSrc "build.ps1") $root
Copy-Item (Join-Path $PSScriptRoot "pack-plugin.ps1") $root
Copy-Item (Join-Path $sdkSrc "samples") $root -Recurse

# README：替换版本号占位符
$readme = Get-Content (Join-Path $sdkSrc "README.md") -Raw -Encoding UTF8
$readme = $readme.Replace("{{VERSION}}", $Version)
[System.IO.File]::WriteAllText((Join-Path $root "README.md"), $readme, (New-Object System.Text.UTF8Encoding($false)))

# 演示插件：源码 + 清单
$demoOut = Join-Path $root "samples\DemoPlugin"
New-Item -ItemType Directory -Path $demoOut -Force | Out-Null
Copy-Item (Join-Path $repo "samples\DemoPlugin\DemoPlugin.vb") $demoOut
Copy-Item (Join-Path $repo "samples\DemoPlugin\demo.plugin.json") $demoOut -ErrorAction SilentlyContinue
$demoJson = @"
{
  "id": "demo-harness",
  "name": "示例插件",
  "version": "1.3.0",
  "author": "PCL Harness",
  "description": "演示事件钩子、三种设置项、内嵌卡片与文件/网络 API。",
  "entry": "DemoPlugin",
  "dll": "Demo.Plugin.dll",
  "permissions": [ "files", "net", "args" ]
}
"@
[System.IO.File]::WriteAllText((Join-Path $demoOut "plugin.json"), $demoJson, (New-Object System.Text.UTF8Encoding($false)))
# 仓库里有正式清单时以它为准，避免 SDK 清单与指南漂移
$repoManifest = Join-Path $repo "samples\DemoPlugin\plugin.json"
if (Test-Path $repoManifest) { Copy-Item $repoManifest (Join-Path $demoOut "plugin.json") -Force }

# 仓库里的其它示例（v6 能力演示 / 实例右键菜单）
foreach ($sampleName in @("V6Demo", "InstanceMenu")) {
    $sampleSrc = Join-Path $repo "samples\$sampleName"
    if (Test-Path $sampleSrc) {
        Copy-Item $sampleSrc (Join-Path $root "samples") -Recurse -Force
    }
}

# 打包
$zip = Join-Path $OutputDirectory "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $root -DestinationPath $zip -CompressionLevel Optimal

# 断言（2026-11-17 复盘）：3.9.5 的包里 PLUGIN-DEV.md 还写着 3.9.4，是发完版才发现。
# 这里在打包当场验两件事：指南声明的适用版本不能比本次打的版本还新；包内文件与仓库逐字节一致。
$guide = Join-Path $repo "PLUGIN-DEV.md"
if (-not (Test-Path $guide)) { throw "找不到 $guide" }
$guideText = [IO.File]::ReadAllText($guide)
$mGuide = [regex]::Match($guideText, '适用版本：PCL Harness ([0-9]+(?:\.[0-9]+)*)')
if (-not $mGuide.Success) { Remove-Item $stage -Recurse -Force; throw "PLUGIN-DEV.md 里找不到「适用版本：PCL Harness x.y.z」这一行，打包中止" }
$guideVer = [version]$mGuide.Groups[1].Value
$packVer = [version]$Version
if ($guideVer -gt $packVer) {
    Remove-Item $stage -Recurse -Force
    throw "PLUGIN-DEV.md 声明适用 $guideVer，比要打的 $packVer 还新；先改指南版本再打包"
}
if ($guideVer.Major -ne $packVer.Major -or $guideVer.Minor -ne $packVer.Minor) {
    Write-Host "WARN: 指南适用版本 $guideVer 与本次 $packVer 不在同一小版本线（补丁号不同是允许的）" -ForegroundColor Yellow
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entry = $archive.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq "$name/PLUGIN-DEV.md" }
    if (-not $entry) { throw "zip 里没有 $name/PLUGIN-DEV.md" }
    $reader = New-Object IO.StreamReader($entry.Open(), [Text.Encoding]::UTF8)
    try { $zipGuide = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($zipGuide -ne $guideText) { throw "zip 内的 PLUGIN-DEV.md 与仓库文件不一致（打包过程动了字节？）" }

    $readmeEntry = $archive.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq "$name/README.md" }
    if ($readmeEntry) {
        $reader2 = New-Object IO.StreamReader($readmeEntry.Open(), [Text.Encoding]::UTF8)
        try { $zipReadme = $reader2.ReadToEnd() } finally { $reader2.Dispose() }
        if ($zipReadme -match '\{\{VERSION\}\}') { throw "zip 内 README.md 还留着 {{VERSION}} 占位符" }
    }
    Write-Host "OK: 包内 PLUGIN-DEV.md / README.md 校验通过（指南适用 $guideVer，本次 $packVer）" -ForegroundColor DarkGray
}
finally { $archive.Dispose() }

Remove-Item $stage -Recurse -Force

Write-Host "OK: $zip ($((Get-Item $zip).Length) bytes)" -ForegroundColor Green
