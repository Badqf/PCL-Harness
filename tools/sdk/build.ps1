<#
.SYNOPSIS
    编译 PCL Harness 插件。

.DESCRIPTION
    用 Roslyn vbc 把插件源码编译成 .NET Framework 4.8 类库，输出到插件目录。
    需要：启动器 exe（作为引用）+ .NET Framework 4.8 参考程序集 + vbc。

.PARAMETER Project
    插件目录（含 plugin.json），或直接给 .vb 文件路径。

.PARAMETER Launcher
    PCL Harness.exe 路径。也可用环境变量 PCL_HARNESS_EXE，或把 exe 放在本脚本同目录。

.PARAMETER VbcPath
    vbc.exe 路径（一般不填，脚本会自动搜索）。

.PARAMETER ReferencePath
    .NET Framework 4.8 参考程序集目录（一般不填，脚本会自动搜索）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build.ps1 -Project samples\HelloPlugin
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$Launcher,
    [string]$VbcPath,
    [string]$ReferencePath
)

$ErrorActionPreference = "Stop"

function Find-Vbc {
    $candidates = @(
        $env:PCL_HARNESS_VBC,
        (Join-Path $env:USERPROFILE "roslyn\tasks\vbc.exe"),
        "C:\Users\$env:USERNAME\roslyn\tasks\vbc.exe",
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\vbc.exe"
    )
    foreach ($base in @("C:\Program Files\Microsoft Visual Studio", "C:\Program Files (x86)\Microsoft Visual Studio")) {
        if (Test-Path $base) {
            $candidates += Get-ChildItem $base -Recurse -Filter vbc.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*\Roslyn\*" } |
                Select-Object -ExpandProperty FullName
        }
    }
    return $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

function Resolve-ReferenceDir {
    param([string]$Path)
    if (-not $Path) { return $null }
    foreach ($cand in @($Path, (Join-Path $Path ".NETFramework\v4.8"), (Join-Path $Path "v4.8"), (Join-Path $Path ".NETFramework\v4.7.2"))) {
        if ($cand -and (Test-Path (Join-Path $cand "mscorlib.dll"))) { return (Resolve-Path $cand).Path }
    }
    return $null
}

function Find-References {
    $candidates = @(
        "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$env:ProgramFiles\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$env:USERPROFILE\dotnet-fwreal"
    )
    $nuget = "$env:USERPROFILE\.nuget\packages\microsoft.netframework.referenceassemblies.net48"
    if (Test-Path $nuget) {
        $candidates += Get-ChildItem $nuget -Recurse -Filter mscorlib.dll -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*v4.8*" } |
            ForEach-Object { $_.DirectoryName }
    }
    foreach ($c in $candidates) {
        $resolved = Resolve-ReferenceDir $c
        if ($resolved) { return $resolved }
    }
    return $null
}

function Find-Launcher {
    if ($Launcher) { return $Launcher }
    if ($env:PCL_HARNESS_EXE) { return $env:PCL_HARNESS_EXE }
    $local = Join-Path $PSScriptRoot "PCL Harness.exe"
    if (Test-Path $local) { return $local }
    return $null
}

if (-not (Test-Path $Project)) { throw "找不到插件目录或文件：$Project" }
$projectIsDir = (Get-Item $Project).PSIsContainer
$projectDir = if ($projectIsDir) { (Get-Item $Project).FullName } else { Split-Path $Project -Parent }
$sources = if ($projectIsDir) {
    Get-ChildItem $projectDir -Filter *.vb | Select-Object -ExpandProperty FullName
} else {
    @((Get-Item $Project).FullName)
}
if (-not $sources) { throw "目录里没有 .vb 源码：$projectDir" }

$jsonPath = Join-Path $projectDir "plugin.json"
if (-not (Test-Path $jsonPath)) { throw "缺少 plugin.json：$jsonPath" }
$manifest = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$dllName = if ($manifest.dll) { $manifest.dll } else { "Plugin.dll" }
$outDll = Join-Path $projectDir $dllName

$launcherExe = Find-Launcher
if (-not $launcherExe -or -not (Test-Path $launcherExe)) {
    throw "找不到 PCL Harness.exe：用 -Launcher 指定，或设环境变量 PCL_HARNESS_EXE，或把 exe 放在本脚本同目录。"
}
if (-not $VbcPath) { $VbcPath = Find-Vbc }
if (-not $VbcPath) { throw "找不到 vbc.exe：装 Visual Studio Build Tools 或 .NET SDK，或用 -VbcPath 指定。" }
if ($ReferencePath) {
    $normalized = Resolve-ReferenceDir $ReferencePath
    if (-not $normalized) { throw "在 $ReferencePath 下找不到 mscorlib.dll（可指向 v4.8 目录，或包含 .NETFramework\v4.8 的目录）。" }
    $ReferencePath = $normalized
}
if (-not $ReferencePath) { $ReferencePath = Find-References }
if (-not $ReferencePath) { throw "找不到 .NET Framework 4.8 参考程序集：装 .NET Framework 4.8 Developer Pack，或用 -ReferencePath 指定。" }

$isLegacyVbc = $VbcPath -match '\\v4\.0\.30319\\vbc\.exe$'

$refs = @(
    "$ReferencePath\mscorlib.dll",
    "$ReferencePath\Microsoft.VisualBasic.dll",
    "$ReferencePath\System.dll",
    "$ReferencePath\System.Core.dll",
    "$ReferencePath\System.Data.dll",
    "$ReferencePath\System.Xml.dll",
    "$ReferencePath\System.Xml.Linq.dll",
    "$ReferencePath\System.Drawing.dll",
    "$ReferencePath\System.Xaml.dll",
    "$ReferencePath\WindowsBase.dll",
    "$ReferencePath\PresentationCore.dll",
    "$ReferencePath\PresentationFramework.dll"
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "缺少参考程序集：$r" } }

Write-Host "vbc        : $VbcPath"
Write-Host "参考程序集 : $ReferencePath"
Write-Host "启动器     : $launcherExe"
Write-Host "源码       : $($sources.Count) 个文件"
Write-Host "输出       : $outDll"

$effectiveRefs = $refs
if ($isLegacyVbc) {
    # 旧版 vbc（Framework v4.0.30319）会自动引用 mscorlib，显式再引用会报 BC2000
    $effectiveRefs = $refs | Where-Object { $_ -ne "$ReferencePath\mscorlib.dll" }
    $vbcArgs = @("/nologo", "/target:library", "/out:$outDll", "/optionstrict-", "/optionexplicit+", "/optioninfer+", "/codepage:65001", "/imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.Linq,System.Text", "/r:$launcherExe")
} else {
    $vbcArgs = @("/noconfig", "/nostdlib", "/nologo", "/target:library", "/out:$outDll", "/optionstrict-", "/optionexplicit+", "/optioninfer+", "/codepage:65001", "/imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.Linq,System.Text", "/r:$launcherExe")
}
foreach ($r in $effectiveRefs) { $vbcArgs += "/r:$r" }
$vbcArgs += $sources

& $VbcPath @vbcArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$size = (Get-Item $outDll).Length
Write-Host ""
Write-Host "OK: $outDll ($size bytes)" -ForegroundColor Green
Write-Host "把 $projectDir 整个文件夹放到 PCLHarness\Plugins\ 下，然后重启启动器或点插件页的「重新扫描」。"
