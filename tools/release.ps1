<#
.SYNOPSIS
    PCL Harness 一键发版：版本校验 → 编译 → SDK → 部署 → commit/tag → push → Release → 校验。

.DESCRIPTION
    本机 hosts 里常驻一份第三方屏蔽清单（`#S302`，把 github.com / api.github.com 等指向 127.0.0.1），
    浏览器靠 Secure DNS 绕过，但 git 与 .NET 走系统解析会被挡住，`git push` 直接超时。

    实测还有第二个坑：即使解掉 hosts，github.com 的 AAAA（IPv6）不通会让 git 卡满 300 秒。
    所以固定做法是：用 DoH 查到 IPv4，临时把 A 记录钉进 hosts（只给 A、不给 AAAA），
    推送与 API 全部走 `http.version=HTTP/1.1`，结束后无条件还原 hosts。

    用法：
        pwsh -File tools\release.ps1 -Version 3.9.4 -Notes @("优化: ……","修复: ……")
        pwsh -File tools\release.ps1 -Version 3.9.4 -DryRun     # 只做预检与网络探测

.NOTES
    前置（脚本会自己检查并报错）：
      * Modules\Base\ModBase.vb 里 VersionBaseName 已等于 -Version，VersionCode 已 +1
      * FormMain.xaml.vb 已插入 If LastVersion < <新 VersionCode> 块
      * docs\更新记录.md 已追加 §21.30.xx
    约定（历史教训，别改）：
      * Release 每版必须 POST 新建，禁止 PATCH 复用同一 id
      * 资产名用字面文件名（?name=PCL-Harness.exe），先复制到 %TEMP% 保住空格
      * Release 正文用 UTF-8 字节 + charset=utf-8，否则中文变问号
      * 只把二进制挂在最新一版
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Notes = @(),
    [switch]$SkipBuild,
    [switch]$SkipSdk,
    [switch]$NoPush,
    [switch]$NoRelease,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- 常量
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ReleaseDir = 'F:\PCL2-Release'
$Owner = 'Badqf'
$Repo = 'PCL-Harness'
$Tag = "v$Version"
$ExeName = 'PCL Harness.exe'
$SdkName = "PCL-Harness-Plugin-SDK-$Version.zip"
$HostsPath = 'C:\Windows\System32\drivers\etc\hosts'
$PinHosts = @('github.com', 'www.github.com', 'api.github.com', 'codeload.github.com')

function Step([int]$n, [string]$text) { Write-Host ("[{0}/8] {1}" -f $n, $text) -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "      OK  $text" -ForegroundColor DarkGray }
function Warn([string]$text) { Write-Host "      !!  $text" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 工具函数
function Get-GitHubToken {
    $p = Join-Path $env:USERPROFILE '.git-credentials'
    if (-not (Test-Path $p)) { throw "找不到 $p，无法取得 GitHub token" }
    $m = [regex]::Match((Get-Content $p -Raw), 'https://([^:]+):([^@]+)@github\.com')
    if (-not $m.Success) { throw "$p 里没有 github.com 的凭据" }
    return $m.Groups[2].Value
}

function Resolve-ViaDoH([string]$Name) {
    foreach ($srv in '223.5.5.5', '119.29.29.29', '223.6.6.6') {
        try {
            $r = Invoke-RestMethod -Uri "https://$srv/resolve?name=$Name&type=A" `
                -Headers @{ accept = 'application/dns-json' } -TimeoutSec 10
            $ips = @($r.Answer | Where-Object { $_.type -eq 1 } | ForEach-Object { $_.data })
            if ($ips.Count -gt 0) { return $ips }
        } catch { }
    }
    return @()
}

<#
    把 github 相关域名的 IPv4 临时钉进 hosts，执行 $Body，最后无条件还原。
    这是本机唯一稳定的 git push 通道：绕过 #S302 屏蔽 + 避免 IPv6 回退超时。
#>
function Invoke-WithGitHubPin([scriptblock]$Body) {
    $backup = Join-Path $env:TEMP ("hosts.release-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Copy-Item $HostsPath $backup -Force
    $original = [IO.File]::ReadAllText($HostsPath)
    try {
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add('# PCL Harness release pin (temporary, auto-removed)')
        foreach ($h in $PinHosts) {
            # 注意：函数只返回一个字符串时 PowerShell 会拆成字符数组，必须 @() 包一层
            $ips = @(Resolve-ViaDoH $h)
            if ($ips.Count -eq 0) { Warn "DoH 解析 $h 失败，跳过"; continue }
            $ip = [string]$ips[0]
            if ($ip -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { Warn "DoH 返回的 $h 地址异常（$ip），跳过"; continue }
            $lines.Add("$ip $h")
            Ok "$h -> $ip"
        }
        if ($lines.Count -le 1) { throw 'DoH 一个域名都没解析出来，网络不可用' }
        [IO.File]::WriteAllText($HostsPath, (($lines -join "`r`n") + "`r`n" + $original),
            (New-Object System.Text.UTF8Encoding($false)))
        & ipconfig /flushdns | Out-Null
        & $Body
    } finally {
        Copy-Item $backup $HostsPath -Force
        & ipconfig /flushdns | Out-Null
        Remove-Item $backup -Force -ErrorAction SilentlyContinue
        Ok 'hosts 已还原'
    }
}

function Invoke-GitHubApi {
    param(
        [string]$Method,
        [string]$Path,                   # 形如 /repos/o/r/releases
        $Body,
        [string]$ContentType = 'application/json; charset=utf-8',
        [int]$TimeoutSec = 120
    )
    $headers = @{
        Authorization          = "token $(Get-GitHubToken)"
        'User-Agent'           = 'pcl-harness-release'
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $uri = "https://api.github.com$Path"
    $p = @{ Method = $Method; Uri = $uri; Headers = $headers; TimeoutSec = $TimeoutSec }
    if ($null -ne $Body) {
        $p.Body = if ($Body -is [byte[]]) { $Body } else { [Text.Encoding]::UTF8.GetBytes($Body) }
        $p.ContentType = $ContentType
    }
    return Invoke-RestMethod @p
}

function Upload-Asset {
    param([string]$UploadUrl, [string]$FilePath, [string]$Name, [string]$ContentType)
    $base = $UploadUrl.Split('{')[0]
    # 注意：PowerShell 里 "$base?name=" 会被当成变量名，必须用 ${base}
    $uri = "${base}?name=$Name"
    $bytes = [IO.File]::ReadAllBytes($FilePath)
    $headers = @{
        Authorization = "token $(Get-GitHubToken)"
        'User-Agent'  = 'pcl-harness-release'
        Accept        = 'application/vnd.github+json'
    }
    $r = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $bytes `
        -ContentType $ContentType -TimeoutSec 900
    Ok "上传 $($r.name) $($r.size) B state=$($r.state)"
}

# ---------------------------------------------------------------- 1. 预检
Step 1 "预检版本号与工作区"
$modBase = Join-Path $RepoRoot 'Plain Craft Launcher 2\Modules\Base\ModBase.vb'
$mbText = [IO.File]::ReadAllText($modBase)
$mVer = [regex]::Match($mbText, 'VersionBaseName As String = "([^"]+)"')
$mCode = [regex]::Matches($mbText, 'VersionCode As Integer = (\d+)')
if (-not $mVer.Success) { throw 'ModBase.vb 里找不到 VersionBaseName' }
if ($mVer.Groups[1].Value -ne $Version) {
    throw "ModBase.vb 的 VersionBaseName 是 $($mVer.Groups[1].Value)，要发的版本是 $Version；先改版本号再来"
}
$codes = @($mCode | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
Ok "版本 $Version，VersionCode=$($codes -join '/')"

$formMain = Join-Path $RepoRoot 'Plain Craft Launcher 2\FormMain.xaml.vb'
$fmText = [IO.File]::ReadAllText($formMain)
if ($codes.Count -gt 0 -and $fmText -notmatch "LastVersion < $($codes[0])") {
    Warn "FormMain.xaml.vb 里没有 If LastVersion < $($codes[0]) 的更新块（更新日志将不显示本版内容）"
}

$changelog = Join-Path $RepoRoot 'docs\更新记录.md'
if ((Test-Path $changelog) -and ([IO.File]::ReadAllText($changelog) -notmatch [regex]::Escape($Version))) {
    Warn "docs\更新记录.md 里没有出现 $Version"
}

Push-Location $RepoRoot
try {
    $dirty = @(& git status --porcelain)
    Ok "改动文件 $($dirty.Count) 个"
    if (& git tag -l $Tag) { Warn "本地已存在 tag $Tag（将复用）" }
} finally { Pop-Location }

# ---------------------------------------------------------------- 2. 编译
Step 2 '编译'
$exeSrc = Join-Path $RepoRoot "Plain Craft Launcher 2\bin\$ExeName"
if ($SkipBuild) {
    Warn '跳过编译'
} elseif ($DryRun) {
    Warn 'DryRun：跳过编译'
} else {
    Push-Location $RepoRoot
    try {
        & (Join-Path $RepoRoot 'build.ps1') *> (Join-Path $env:TEMP "release-build-$Version.log")
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 失败（exit=$LASTEXITCODE），日志见 %TEMP%\release-build-$Version.log" }
    } finally { Pop-Location }
    if (-not (Test-Path $exeSrc)) { throw "编译产物不存在：$exeSrc" }
    Ok "exe $([math]::Round((Get-Item $exeSrc).Length/1MB,2)) MB @$((Get-Item $exeSrc).LastWriteTime.ToString('HH:mm:ss'))"
}

# ---------------------------------------------------------------- 3. SDK
Step 3 '打包插件 SDK'
$sdkAtRoot = "F:\PCL-Harness-Plugin-SDK-$Version.zip"
if ($SkipSdk) {
    Warn '跳过 SDK'
} elseif ($DryRun) {
    Warn 'DryRun：跳过 SDK'
} else {
    Push-Location $RepoRoot
    try {
        & (Join-Path $PSScriptRoot 'pack-plugin-sdk.ps1') -Version $Version | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'pack-plugin-sdk.ps1 失败' }
    } finally { Pop-Location }
    if (-not (Test-Path $sdkAtRoot)) { throw "SDK 未生成：$sdkAtRoot" }
    Copy-Item $sdkAtRoot (Join-Path $ReleaseDir $SdkName) -Force
    Ok "SDK $((Get-Item (Join-Path $ReleaseDir $SdkName)).Length) B"
}

# ---------------------------------------------------------------- 4. 部署
Step 4 "部署到 $ReleaseDir"
$exeDst = Join-Path $ReleaseDir $ExeName
if ($DryRun) {
    Warn 'DryRun：跳过部署'
} else {
    Get-Process -Name 'PCL Harness' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Copy-Item $exeSrc $exeDst -Force
    Ok ("exe {0} B sha256={1}" -f (Get-Item $exeDst).Length,
        (Get-FileHash $exeDst -Algorithm SHA256).Hash.Substring(0, 16) + '…')
}

# ---------------------------------------------------------------- 5. commit + tag
Step 5 'commit / tag'
if ($DryRun) {
    Warn 'DryRun：跳过 commit / tag'
} else {
    Push-Location $RepoRoot
    try {
        $dirty = @(& git status --porcelain)
        if ($dirty.Count -gt 0) {
            & git add -A
            $msgFile = Join-Path $env:TEMP "release-msg-$Version.txt"
            # 消息里可能有引号，必须走文件，否则 git 会把空格后的词当 pathspec
            [IO.File]::WriteAllText($msgFile, "feat: release PCL Harness $Version", (New-Object System.Text.UTF8Encoding($false)))
            & git commit -F $msgFile | Out-Null
            Remove-Item $msgFile -Force -ErrorAction SilentlyContinue
            Ok "commit $(& git rev-parse --short HEAD)"
        } else {
            Warn '工作区干净，无新提交'
        }
        if (-not (& git tag -l $Tag)) {
            & git tag -a $Tag -m "PCL Harness $Version"
            Ok "tag $Tag"
        }
    } finally { Pop-Location }
}

# ---------------------------------------------------------------- 6. push
Step 6 'push（临时钉 IPv4 + HTTP/1.1）'
if ($NoPush) {
    Warn '按 -NoPush 跳过'
} elseif ($DryRun) {
    Invoke-WithGitHubPin {
        Push-Location $RepoRoot
        try {
            & git -c http.version=HTTP/1.1 ls-remote origin -h 2>&1 | Select-Object -First 1
            if ($LASTEXITCODE -ne 0) { throw "ls-remote 失败（exit=$LASTEXITCODE）" }
            Ok '连通性正常（DryRun 未推送）'
        } finally { Pop-Location }
    }
} else {
    Invoke-WithGitHubPin {
        Push-Location $RepoRoot
        try {
            & git -c http.version=HTTP/1.1 push origin main 2>&1 | Select-Object -Last 2
            if ($LASTEXITCODE -ne 0) { throw "push main 失败（exit=$LASTEXITCODE）" }
            & git -c http.version=HTTP/1.1 push origin $Tag 2>&1 | Select-Object -Last 2
            if ($LASTEXITCODE -ne 0) { throw "push $Tag 失败（exit=$LASTEXITCODE）" }
            Ok "远端 main = $(& git rev-parse --short origin/main)"
        } finally { Pop-Location }
    }
}

# ---------------------------------------------------------------- 7. Release
Step 7 '新建 Release 并上传资产'
$releaseId = $null
if ($NoRelease) {
    Warn '按 -NoRelease 跳过'
} elseif ($DryRun) {
    Warn 'DryRun：跳过 Release'
} else {
    if (-not $Notes -or $Notes.Count -eq 0) { $Notes = @("PCL Harness $Version") }
    $body = ($Notes -join "`n") + "`n· 详见 docs\更新记录.md 与完整更新日志"
    $payload = @{
        tag_name = $Tag; name = "PCL Harness $Version"; body = $body
        draft = $false; prerelease = $false
    } | ConvertTo-Json -Compress

    Invoke-WithGitHubPin {
        $rel = Invoke-GitHubApi -Method Post -Path "/repos/$Owner/$Repo/releases" -Body $payload
        $script:releaseId = $rel.id
        Ok "Release $($rel.id) $($rel.tag_name)"

        $tmpExe = Join-Path $env:TEMP 'PCL-Harness.exe'   # 保住空格，否则 GitHub 会写成 PCL.Harness.exe
        Copy-Item $exeDst $tmpExe -Force
        Upload-Asset -UploadUrl $rel.upload_url -FilePath $tmpExe -Name 'PCL-Harness.exe' -ContentType 'application/octet-stream'
        Upload-Asset -UploadUrl $rel.upload_url -FilePath (Join-Path $ReleaseDir $SdkName) -Name $SdkName -ContentType 'application/zip'
        Remove-Item $tmpExe -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- 8. 自检
Step 8 '发布后自检'
if ($DryRun -or $NoRelease) {
    Warn '跳过自检'
} else {
    Invoke-WithGitHubPin {
        $latest = Invoke-GitHubApi -Method Get -Path "/repos/$Owner/$Repo/releases/latest"
        if ($latest.tag_name -ne $Tag) { throw "releases/latest 是 $($latest.tag_name)，不是 $Tag" }
        Ok "latest=$($latest.tag_name) assets=$($latest.assets.Count)"
    }
    foreach ($n in 'PCL-Harness.exe', $SdkName) {
        $u = "https://ghfast.top/https://github.com/$Owner/$Repo/releases/download/$Tag/$n"
        $code = & curl.exe -s -o NUL -w '%{http_code}' --max-time 180 -L $u 2>$null
        if ($code -ne '200') { throw "ghfast 校验失败（$code）：$u" }
        Ok "ghfast 200 $n"
    }
}

Write-Host ''
Write-Host "PCL Harness $Version 发版流程结束。" -ForegroundColor Green
if ($releaseId) { Write-Host "Release: https://github.com/$Owner/$Repo/releases/tag/$Tag" -ForegroundColor Green }
