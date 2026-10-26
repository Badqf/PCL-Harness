# PCL2 2.13.1.1 本机构建脚本（免管理员方案）
# 依赖：
#   - .NET Framework 4.8 运行时（Windows 10/11 自带）
#   - net48 引用程序集：C:\Users\XYM_3\dotnet-fwreal\  （从 Microsoft.NETFramework.ReferenceAssemblies.net48 解出）
#   - Roslyn net472 编译器：C:\Users\XYM_3\roslyn\tasks\vbc.exe （Microsoft.Net.Compilers.Toolset 4.14.0）
# 原理：用 Windows 自带的 .NET Framework 版 MSBuild 4.0 驱动官方 WPF 标记编译器（PresentationBuildTasks 4.0），
#       再用 Roslyn vbc 编译 VB 源码 —— 完全不装 VS / 不需要 UAC。
param(
    [ValidateSet("Release", "Debug", "Snapshot")]
    [string]$Configuration = "Release",
    [string]$Platform = "AnyCPU"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj"
$msbuild = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
$refasmRoot = "C:\Users\XYM_3\dotnet-fwreal\"
$roslyn = "C:\Users\XYM_3\roslyn\tasks"

foreach ($p in @($proj, $msbuild, "$roslyn\vbc.exe", "$refasmRoot.NETFramework\v4.8\mscorlib.dll")) {
    if (-not (Test-Path $p)) { throw "缺少前置条件: $p" }
}

# 依赖 DLL 先用 dotnet SDK 构建（这三个是 SDK 风格 csproj）
if (-not (Test-Path (Join-Path $root "PCLCS\bin\PCLCS.dll"))) {
    Write-Host "构建依赖工程 MeloongCore / MeloongCore.Wpf / PCLCS ..."
    & dotnet build (Join-Path $root "MeloongCore\Shared\MeloongCore.csproj") -c Release | Out-Null
    & dotnet build (Join-Path $root "MeloongCore\Wpf\MeloongCore.Wpf.csproj") -c Release | Out-Null
    & dotnet build (Join-Path $root "PCLCS\PCLCS.csproj") -c Release | Out-Null
}

Write-Host "MSBuild: $msbuild"
& $msbuild $proj /p:Configuration=$Configuration /p:Platform=$Platform `
    /p:TargetFrameworkRootPath="$refasmRoot" `
    /p:VbcToolPath="$roslyn" /p:VbcToolExe=vbc.exe `
    /m /v:m /nologo

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 产出文件名改为 PCL Harness.exe（程序集内部名仍是 Plain Craft Launcher 2，
# 因为源码里大量 pack://...;/Plain Craft Launcher 2;component/... 资源 URI 依赖它，不能改）
$outDir = Join-Path $root "Plain Craft Launcher 2\bin"
$built = Join-Path $outDir "Plain Craft Launcher 2.exe"
$renamed = Join-Path $outDir "PCL Harness.exe"
if (Test-Path $built) {
    Copy-Item $built $renamed -Force
    Write-Host "输出: $renamed"
}
exit 0
