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
    [string]$ReferencePath,
    # 把清单里的信息写成 AssemblyMetadata，产出的 DLL 可以单文件分发、直接拖进启动器导入
    [switch]$SingleFile,
    # 编译完成后顺手打成 .pclplugin（调用 tools\pack-plugin.ps1）
    [switch]$Pack
)

$ErrorActionPreference = "Stop"

function Find-Vbc {
    $candidates = @(
        $env:PCL_HARNESS_VBC
    )
    foreach ($base in @("C:\Program Files\Microsoft Visual Studio", "C:\Program Files (x86)\Microsoft Visual Studio")) {
        if (Test-Path $base) {
            $candidates += Get-ChildItem $base -Recurse -Filter vbc.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*\Roslyn\*" } |
                Select-Object -ExpandProperty FullName
        }
    }
    # v7.4（安全审计 S1）：%USERPROFILE%\roslyn 可被普通程序写入，先用文件签名确认它确实是微软的 Roslyn，
    # 再放到 Framework 自带编译器之前——因为 v4.0.30319 的 legacy vbc 只支持 VB 10，
    # 连 $"..." 这样的插值字符串都会报 BC30037，普通插件根本编不过。
    $userVbc = Join-Path $env:USERPROFILE "roslyn\tasks\vbc.exe"
    if (Test-Path $userVbc) {
        # v7.5（安全审计 3.8 S1'）：CompanyName 只是文件里的一段字符串，伪造很容易；改用 Authenticode 签名确认
        $sig = $null
        try { $sig = Get-AuthenticodeSignature -FilePath $userVbc -ErrorAction Stop } catch { $sig = $null }
        $trusted = $false
        if ($null -ne $sig -and $sig.Status -eq 'Valid' -and $null -ne $sig.SignerCertificate) {
            $trusted = $sig.SignerCertificate.Subject -like '*Microsoft*'
        }
        if ($trusted) {
            $candidates += $userVbc
        } else {
            Write-Warning "忽略 $userVbc：它没有有效的微软 Authenticode 签名，可能是被替换过的编译器。"
        }
    }
$candidates += @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\vbc.exe"
    )
    $found = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($found -and $found -eq $userVbc) {
        Write-Warning "正在使用用户目录里的编译器（$found）——该目录可被普通程序写入，请确认来源可信；推荐安装 Visual Studio Build Tools。"
    }
    if ($found -and $found -match '\\v4\.0\.30319\\vbc\.exe$') {
        Write-Warning "找不到 Roslyn 编译器，退回 .NET Framework 自带的 legacy vbc：它只支持 VB 10，使用插值字符串、nameof 等新语法会编译失败。"
    }
    return $found
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
$dllName = if ($manifest.dll) { [string]$manifest.dll } else { "Plugin.dll" }
# v7.4（安全审计 S1）：dll 只能是文件名，且输出必须落在插件目录内
if ($dllName -match '[\\/]' -or $dllName.Contains('..') -or ($dllName -notmatch '^[A-Za-z0-9._\-]+\.dll$')) {
    throw "plugin.json 的 dll 字段不合法（只能是文件名，例如 Demo.Plugin.dll）：$dllName"
}
$outDll = Join-Path $projectDir $dllName
$projectFull = [System.IO.Path]::GetFullPath($projectDir).TrimEnd('\') + '\'
if (-not [System.IO.Path]::GetFullPath($outDll).StartsWith($projectFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "输出路径越界：$outDll"
}

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
    # 旧版 vbc（Framework v4.0.30319）会从自己目录自动引用 mscorlib / Microsoft.VisualBasic / System，
    # 显式再 /r: 它们就会 BC2000（v7.4：之前只剔除 mscorlib，仍会撞上 Microsoft.VisualBasic）
    $effectiveRefs = $refs | Where-Object {
        $_ -notin @("$ReferencePath\mscorlib.dll", "$ReferencePath\Microsoft.VisualBasic.dll", "$ReferencePath\System.dll")
    }
    $vbcArgs = @("/noconfig", "/nologo", "/target:library", "/out:$outDll", "/optionstrict-", "/optionexplicit+", "/optioninfer+", "/codepage:65001", "/imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.Linq,System.Text", "/r:$launcherExe")
} else {
    $vbcArgs = @("/noconfig", "/nostdlib", "/nologo", "/target:library", "/out:$outDll", "/optionstrict-", "/optionexplicit+", "/optioninfer+", "/codepage:65001", "/imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.Linq,System.Text", "/r:$launcherExe")
}
foreach ($r in $effectiveRefs) { $vbcArgs += "/r:$r" }

# -SingleFile：把清单写进程序集元数据，让 DLL 自己就能被识别/导入
$metaFile = $null
if ($SingleFile) {
    function Esc([object]$v) {
        # v7.4（安全审计 S1）：只转义引号不够，换行可以注入任意 VB 代码
        $s = [string]$v
        if ($s -match "[`r`n`t]") { throw "清单字段里不能包含换行/制表符：$s" }
        return $s.Replace('"', '""')
    }
    $perm = ""
    if ($manifest.permissions) { $perm = (@($manifest.permissions) -join ",") }
    $meta = @("Imports System.Reflection")
    $meta += '<Assembly: AssemblyMetadata("PclPluginId", "{0}")>' -f (Esc $manifest.id)
    $meta += '<Assembly: AssemblyMetadata("PclPluginName", "{0}")>' -f (Esc $manifest.name)
    $meta += '<Assembly: AssemblyMetadata("PclPluginVersion", "{0}")>' -f (Esc $manifest.version)
    $meta += '<Assembly: AssemblyMetadata("PclPluginAuthor", "{0}")>' -f (Esc $manifest.author)
    $meta += '<Assembly: AssemblyMetadata("PclPluginDescription", "{0}")>' -f (Esc $manifest.description)
    if ($perm) { $meta += '<Assembly: AssemblyMetadata("PclPluginPermissions", "{0}")>' -f (Esc $perm) }
    $metaFile = Join-Path ([System.IO.Path]::GetTempPath()) ("pclmeta-" + [guid]::NewGuid().ToString('N') + ".vb")
    [System.IO.File]::WriteAllLines($metaFile, $meta, (New-Object System.Text.UTF8Encoding($false)))
    $vbcArgs += $metaFile
    Write-Host "单文件模式 : 已生成程序集元数据（permissions=$perm）"
}

$vbcArgs += $sources

$vbcOutput = & $VbcPath @vbcArgs 2>&1
$code = $LASTEXITCODE
if ($code -ne 0) { $vbcOutput | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow } }
if ($metaFile -and (Test-Path $metaFile)) { Remove-Item $metaFile -Force }
if ($code -ne 0) { throw "编译失败（exit $code）" }

$size = (Get-Item $outDll).Length
Write-Host ""
Write-Host "OK: $outDll ($size bytes)" -ForegroundColor Green

if ($Pack) {
    # v7.4（安全审计 S1）：SDK 压缩包里 pack-plugin.ps1 与 build.ps1 同级，仓库里在 tools\ 下
    $packScript = Join-Path $PSScriptRoot "pack-plugin.ps1"
    if (-not (Test-Path $packScript)) { $packScript = Join-Path (Split-Path $PSScriptRoot -Parent) "pack-plugin.ps1" }
    if (-not (Test-Path $packScript)) { throw "找不到 $packScript（-Pack 需要它与 sdk 目录同级）" }
    & $packScript -Project $projectDir
    exit $LASTEXITCODE
}

Write-Host "把 $projectDir 整个文件夹放到 PCLHarness\Plugins\ 下，然后重启启动器或点插件页的「重新扫描」。"
