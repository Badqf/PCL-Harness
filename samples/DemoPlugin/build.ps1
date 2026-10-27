# Build the PCL Harness sample plugin (samples/DemoPlugin).
# Usage: powershell -ExecutionPolicy Bypass -File samples\DemoPlugin\build.ps1
# The script walks up to find the repository root (the folder with the .sln).

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
while ($root -and -not (Test-Path (Join-Path $root "Plain Craft Launcher 2.sln"))) {
    $parent = Split-Path $root -Parent
    if ($parent -eq $root) { $root = $null; break }
    $root = $parent
}
if (-not $root) { throw "Repository root not found (no Plain Craft Launcher 2.sln)." }

$vbc = "C:\Users\XYM_3\roslyn\tasks\vbc.exe"
$fw = "C:\Users\XYM_3\dotnet-fwreal\.NETFramework\v4.8"
$exe = Join-Path $root "Plain Craft Launcher 2\bin\PCL Harness.exe"

if (-not (Test-Path $vbc)) { throw "vbc not found: $vbc" }
if (-not (Test-Path $fw)) { throw ".NET Framework 4.8 reference assemblies not found: $fw" }
if (-not (Test-Path $exe)) { throw "Launcher exe not found: $exe (run build.ps1 in the repo root first)." }

$out = Join-Path $PSScriptRoot "Demo.Plugin.dll"
& $vbc /nologo /target:library /out:$out /optionstrict- /optionexplicit+ /optioninfer+ /codepage:65001 `
    /r:"$exe" `
    /r:"$fw\mscorlib.dll" /r:"$fw\System.dll" /r:"$fw\System.Core.dll" /r:"$fw\Microsoft.VisualBasic.dll" `
    /r:"$fw\System.Xaml.dll" /r:"$fw\WindowsBase.dll" `
    /r:"$fw\PresentationCore.dll" /r:"$fw\PresentationFramework.dll" `
    (Join-Path $PSScriptRoot "DemoPlugin.vb")

if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

Write-Host "OK: $out ($((Get-Item $out).Length) bytes)"
Write-Host "Copy Demo.Plugin.dll and plugin.json into PCL\Plugins\demo-harness\."
