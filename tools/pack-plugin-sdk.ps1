<#
.SYNOPSIS
    打包 PCL Harness 插件开发包（SDK）为 zip，随 Release 一起分发。

.DESCRIPTION
    素材来自 tools\sdk\：
        README.md            上手说明（{{VERSION}} 会被替换成实际版本号）
        build.ps1            通用编译脚本
        samples\HelloPlugin  最小插件
    再从仓库根目录取 PLUGIN-DEV.md、LICENSE、samples\DemoPlugin\DemoPlugin.vb。

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

$name = "PCL-Harness-Plugin-SDK-$Version"
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "$name-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$root = Join-Path $stage $name
New-Item -ItemType Directory -Path $root -Force | Out-Null

# 指南与许可
Copy-Item (Join-Path $repo "PLUGIN-DEV.md") $root
Copy-Item (Join-Path $repo "LICENSE") $root

# SDK 自带素材
Copy-Item (Join-Path $sdkSrc "build.ps1") $root
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

# 打包
$zip = Join-Path $OutputDirectory "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $root -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

Write-Host "OK: $zip ($((Get-Item $zip).Length) bytes)" -ForegroundColor Green
