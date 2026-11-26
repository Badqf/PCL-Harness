<#
.SYNOPSIS
    Pre-flight checks for the PCL Harness repo. Run before commit, before a
    release, and after any edit to an encoding-sensitive file.

.DESCRIPTION
    Every check here exists because that exact mistake already cost us a rework
    cycle. The script is deliberately boring: it only reads, and it never
    touches the working tree or the network.

    Checks:
      1  git worktree clean (a release must not ship uncommitted bytes)
      2  line endings and BOM for the files whose encoding is load-bearing
      3  PowerShell parses every tools script (catches a half-written file)
      4  the version in ModBase.vb matches the SDK zip and the changelog
      5  the built exe and the deployed exe are the same bytes
      6  .gitattributes does not treat a PNG as text
      7  no GitHub token or other secret in tracked text files
      8  no local identity, personal network data or absolute local path

.PARAMETER Build
    Also run build.ps1 and fail if the log contains ": error".

.PARAMETER AllowDirty
    Skip the clean-worktree check (use while iterating; never for a release).

.PARAMETER SkipArtifacts
    Skip the built-vs-deployed exe comparison (release.ps1 rebuilds and redeploys later).

.PARAMETER ExpectVersion
    The version that is about to be released. Fails when ModBase.vb does not carry it yet,
    so a forgotten bump is caught in the first second instead of after the network probe.

.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File tools\preflight.ps1
    # 没有 pwsh 的机器把 pwsh 换成 powershell.exe；两种解释器都会解析一遍全部脚本
#>
[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$AllowDirty,
    [switch]$SkipArtifacts,
    [string]$ExpectVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($env:PCL_RELEASE_DIR) { $releaseDir = $env:PCL_RELEASE_DIR } else { $releaseDir = Join-Path (Split-Path $root -Parent) 'PCL2-Release' }

# 执行器双模：有 pwsh 7 就用它跑 build.ps1 和交叉语法检查，没有则回退 5.1
$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
if ($pwshCommand) { $script:PowerShellExe = $pwshCommand.Source; $pwshVersion = $pwshCommand.Version
    if ($pwshVersion -and $pwshVersion.Major -ge 7) { $script:PowerShellLabel = 'pwsh ' + $pwshVersion } else { $script:PowerShellLabel = 'pwsh 7' } }
else { $script:PowerShellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'; $script:PowerShellLabel = 'Windows PowerShell 5.1' }

$fail = 0
$warn = 0

function Pass([string]$name, [string]$detail) { Write-Host ("  OK   {0,-34} {1}" -f $name, $detail) }
function Fail([string]$name, [string]$detail) { Write-Host ("  FAIL {0,-34} {1}" -f $name, $detail) -ForegroundColor Red; $script:fail++ }
function Warn([string]$name, [string]$detail) { Write-Host ("  WARN {0,-34} {1}" -f $name, $detail) -ForegroundColor Yellow; $script:warn++ }
function Section([string]$title) { Write-Host ''; Write-Host "== $title" -ForegroundColor Cyan }

function Read-Bytes([string]$path) { [IO.File]::ReadAllBytes($path) }
function Has-Bom([byte[]]$bytes) { return ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) }
function Count-Seq([byte[]]$bytes, [byte[]]$needle) {
    $n = 0
    for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
        $hit = $true
        for ($j = 0; $j -lt $needle.Length; $j++) { if ($bytes[$i + $j] -ne $needle[$j]) { $hit = $false; break } }
        if ($hit) { $n++ }
    }
    return $n
}

Set-Location $root

# ---------------------------------------------------------------- 1 worktree
Section 'worktree'
$status = & git status --porcelain
if (-not $status) {
    Pass 'git worktree' 'clean'
}
elseif ($AllowDirty) {
    Warn 'git worktree' ("{0} path(s) modified, ignored by -AllowDirty" -f @($status).Count)
}
else {
    $names = (@($status) | ForEach-Object { $_.Substring(3) } | Select-Object -First 5) -join ', '
    Fail 'git worktree' ("dirty: {0}{1}" -f $names, $(if (@($status).Count -gt 5) { ' …' } else { '' }))
}

# ---------------------------------------------------------------- 2 encodings
Section 'encoding'
$crlf = [byte[]](0x0D, 0x0A)
$lf = [byte[]](0x0A)
$rules = @(
    @{ Path = 'Plain Craft Launcher 2/Modules/ModPlugin.vb'; Bom = $false; Eol = 'LF' },
    @{ Path = 'PLUGIN-DEV.md'; Bom = $false; Eol = 'LF' },
    @{ Path = 'docs/更新记录.md'; Bom = $false; Eol = 'LF' }
)
foreach ($ps in (Get-ChildItem (Join-Path $root 'tools') -Filter *.ps1)) {
    $rules += @{ Path = ('tools/' + $ps.Name); Bom = $true; Eol = 'CRLF' }
}
if (Test-Path (Join-Path $root 'build.ps1')) { $rules += @{ Path = 'build.ps1'; Bom = $true; Eol = 'CRLF' } }

foreach ($rule in $rules) {
    $full = Join-Path $root $rule.Path
    if (-not (Test-Path $full)) { Warn $rule.Path 'missing'; continue }
    $b = Read-Bytes $full
    $problems = @()
    $bom = Has-Bom $b
    if ($rule.Bom -and -not $bom) { $problems += 'no BOM' }
    if (-not $rule.Bom -and $bom) { $problems += 'unexpected BOM' }
    $nCrlf = Count-Seq $b $crlf
    $nLf = Count-Seq $b $lf
    if ($rule.Eol -eq 'CRLF' -and ($nLf -ne $nCrlf)) { $problems += ("{0} lone LF" -f ($nLf - $nCrlf)) }
    if ($rule.Eol -eq 'LF' -and $nCrlf -gt 0) { $problems += ("{0} CRLF" -f $nCrlf) }
    if ($bom -and $b.Length -ge 6 -and $b[3] -eq 0xEF -and $b[4] -eq 0xBB -and $b[5] -eq 0xBF) { $problems += 'double BOM' }
    if ($problems.Count -eq 0) { Pass $rule.Path ($rule.Eol + $(if ($rule.Bom) { ' + BOM' } else { ' + no BOM' })) }
    else { Fail $rule.Path ($problems -join ', ') }
}

# the site is authored LF; a CRLF here means an editor rewrote it
$siteText = Get-ChildItem (Join-Path $root 'site') -Recurse -File -Include *.html, *.css, *.js, *.svg -ErrorAction SilentlyContinue
$bad = @()
foreach ($f in $siteText) {
    $b = Read-Bytes $f.FullName
    if ((Count-Seq $b $crlf) -gt 0 -or (Has-Bom $b)) { $bad += $f.Name }
}
if ($bad.Count -eq 0) { Pass 'site text files' ('{0} file(s) LF, no BOM' -f @($siteText).Count) }
else { Fail 'site text files' ('CRLF or BOM in: ' + ($bad -join ', ')) }

# ---------------------------------------------------------------- 3 parse
Section 'powershell parse'
$parseBad = 0
$parseFiles = @(Get-ChildItem (Join-Path $root 'tools') -Filter *.ps1) + @(Get-Item (Join-Path $root 'build.ps1') -ErrorAction SilentlyContinue)
foreach ($ps in $parseFiles) {
    if (-not $ps -or -not (Test-Path $ps.FullName)) { continue }
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($ps.FullName, [ref]$null, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) {
        Fail $ps.Name ("{0} parse error(s): {1}" -f $errors.Count, $errors[0].Message)
        $parseBad++
    }
}
if ($parseBad -eq 0) { Pass 'all scripts' ("parse clean (host: PowerShell {0})" -f $PSVersionTable.PSVersion) }

# 双模交叉检查：5.1 与 7 的语法集不同（??、三元、-Parallel），只在一个版本上干净不算干净。
$otherExe = $null; $otherLabel = $null
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $legacy = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (Test-Path $legacy) { $otherExe = $legacy; $otherLabel = 'Windows PowerShell 5.1' }
} elseif ($pwshCommand) {
    $otherExe = $pwshCommand.Source; $otherLabel = $script:PowerShellLabel
}
if ($otherExe) {
    $probe = Join-Path $env:TEMP 'pcl-parse-probe.ps1'
    $probeLines = @(
        '$bad = 0'
        'foreach ($p in $args) {'
        '    $errors = $null'
        '    [void][System.Management.Automation.Language.Parser]::ParseFile($p, [ref]$null, [ref]$errors)'
        '    if ($errors -and $errors.Count -gt 0) { Write-Output ($p + '': '' + $errors[0].Message); $bad++ }'
        '}'
        'if ($bad -eq 0) { Write-Output ''CLEAN'' }'
    )
    [IO.File]::WriteAllText($probe, ($probeLines -join "`n"), (New-Object Text.UTF8Encoding($false)))
    $paths = @($parseFiles | Where-Object { $_ -and (Test-Path $_.FullName) } | ForEach-Object { $_.FullName })
    $lines = @(& $otherExe -NoProfile -ExecutionPolicy Bypass -File $probe @paths 2>&1 | ForEach-Object { "$_" })
    Remove-Item $probe -ErrorAction SilentlyContinue
    if ($lines.Count -eq 1 -and $lines[0] -eq 'CLEAN') { Pass 'all scripts' ("parse clean ({0})" -f $otherLabel) }
    else { Fail 'all scripts' (("{0}: " -f $otherLabel) + (($lines | Where-Object { $_ -ne 'CLEAN' }) -join '; ')) }
}

# ---------------------------------------------------------------- 4 versions
Section 'version consistency'
$modBase = Join-Path $root 'Plain Craft Launcher 2/Modules/Base/ModBase.vb'
if (Test-Path $modBase) {
    $text = [IO.File]::ReadAllText($modBase)
    $ver = ([regex]::Match($text, 'VersionBaseName As String = "([^"]+)"')).Groups[1].Value
    $code = ([regex]::Match($text, 'VersionCode As Integer = (\d+)')).Groups[1].Value
    if ($ExpectVersion -and $ver -ne $ExpectVersion) {
        Fail 'ModBase.vb' ("carries {0}, expected {1} - bump it first" -f $ver, $ExpectVersion)
    }
    if ($ver -and $code) { Pass 'ModBase.vb' ("{0} / {1}" -f $ver, $code) }
    else { Fail 'ModBase.vb' 'VersionBaseName or VersionCode not found' }

    if ($ver) {
        $zip = Join-Path $releaseDir ("PCL-Harness-Plugin-SDK-{0}.zip" -f $ver)
        if (Test-Path $zip) { Pass 'SDK zip' ((Split-Path $zip -Leaf) + ' present') }
        else { Warn 'SDK zip' ("not packed yet: PCL-Harness-Plugin-SDK-{0}.zip" -f $ver) }

        $log = Join-Path $root 'docs/更新记录.md'
        if (Test-Path $log) {
            $last = (& Select-String -Path $log -Pattern '^## §' | Select-Object -Last 1).Line
            if ($last -and $last -match [regex]::Escape($ver)) { Pass 'changelog' $last.Trim() }
            elseif ($last -and $last -match '(未发布|待发版|unreleased)') { Pass 'changelog' ("pending entry: {0}" -f $last.Trim()) }
            else { Fail 'changelog' ("latest heading does not mention {0}: {1}" -f $ver, "$last".Trim()) }
        }
    }
}
else { Fail 'ModBase.vb' 'file not found' }

# ---------------------------------------------------------------- 5 artifacts
if ($SkipArtifacts) { Section 'artifacts'; Warn 'deployed exe' 'skipped (-SkipArtifacts)' }
else {
Section 'artifacts'
$bin = Join-Path $root 'Plain Craft Launcher 2/bin/PCL Harness.exe'
$dep = Join-Path $releaseDir 'PCL Harness.exe'
if ((Test-Path $bin) -and (Test-Path $dep)) {
    $h1 = (Get-FileHash $bin -Algorithm SHA256).Hash
    $h2 = (Get-FileHash $dep -Algorithm SHA256).Hash
    $s1 = (Get-Item $bin).Length
    if ($h1 -eq $h2) { Pass 'deployed exe' ("matches build, {0:N0} B" -f $s1) }
    else { Fail 'deployed exe' 'release copy differs from bin (deploy it again)' }
}
else { Warn 'deployed exe' 'build output or release copy missing' }
}

# ---------------------------------------------------------------- 6 attributes
Section 'git attributes'
$png = Join-Path $root 'site/img/shot-home.png'
if (Test-Path $png) {
    $attr = (& git check-attr text -- 'site/img/shot-home.png') -join ' '
    if ($attr -match 'text:\s*(auto|unset)') { Pass 'png is binary' $attr }
    else { Fail 'png is binary' ($attr + ' <-- git would rewrite its bytes') }
}
else { Warn 'png is binary' 'site/img/shot-home.png missing' }

# ---------------------------------------------------------------- 8 privacy
Section 'privacy'
# 仓库是公开的：禁止把本机身份、个人网络信息与绝对路径写进 tracked 文件。
# 模式用字符串拼接构造，避免守卫代码自身命中。
$privacyPatterns = @(
    ('XYM' + '_3'),
    ('LAPTOP' + '-9P9RI9RB'),
    ('X' + 'iang'),
    ('222' + '\.69\.'),
    ('F:\\' + 'PCL2')
)
$privacyHits = New-Object System.Collections.Generic.List[string]
foreach ($pat in $privacyPatterns) {
    $out = & git grep -n -I -E $pat -- . 2>$null
    if ($out) { foreach ($line in @($out)) { $privacyHits.Add([string]$line) } }
}
if ($privacyHits.Count -eq 0) { Pass 'identity' 'no local identity or personal network data' }
else {
    $files = @($privacyHits | ForEach-Object { (($_ -split ':')[0]) } | Select-Object -Unique)
    Fail 'identity' ('local identity / personal network data in: ' + ($files -join ', '))
}
# ---------------------------------------------------------------- 7 secrets
Section 'secrets'
$pattern = 'ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}'
$hits = @()
foreach ($f in (& git ls-files | Where-Object { $_ -match '\.(vb|ps1|md|json|yml|yaml|js|html|css|txt|csproj|vbproj)$' })) {
    $full = Join-Path $root $f
    if (-not (Test-Path $full)) { continue }
    if ([IO.File]::ReadAllText($full) -match $pattern) { $hits += $f }
}
if ($hits.Count -eq 0) { Pass 'tracked files' 'no token pattern' }
else { Fail 'tracked files' ('token-like string in: ' + ($hits -join ', ')) }

# ---------------------------------------------------------------- 8 build
if ($Build) {
    Section 'build'
    $log = Join-Path $env:TEMP 'preflight-build.log'
    & $script:PowerShellExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') *> $log
    $code = $LASTEXITCODE
    $errs = @(Select-String -Path $log -Pattern ': error ')
    if ($code -eq 0 -and $errs.Count -eq 0) { Pass 'build.ps1' ("exit 0, log " + $log) }
    else { Fail 'build.ps1' ("exit {0}, {1} error line(s), see {2}" -f $code, $errs.Count, $log) }
}

# ---------------------------------------------------------------- summary
Write-Host ''
if ($fail -eq 0) {
    Write-Host ("PREFLIGHT PASS  ({0} warning(s))" -f $warn) -ForegroundColor Green
    exit 0
}
Write-Host ("PREFLIGHT FAIL  ({0} failure(s), {1} warning(s))" -f $fail, $warn) -ForegroundColor Red
exit 1
