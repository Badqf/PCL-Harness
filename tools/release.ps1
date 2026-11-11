<#
.SYNOPSIS
    PCL Harness 一键发版：版本校验 → 编译 → SDK → 部署 → commit/tag → push → Release → 校验。

.DESCRIPTION
    本机 hosts 里常驻一份第三方屏蔽清单（`#S302`，把 github.com / api.github.com 等指向 127.0.0.1），
    浏览器靠 Secure DNS 绕过，但 git 与 .NET 走系统解析会被挡住，`git push` 直接超时。

    实测还有第二个坑：即使解掉 hosts，github.com 的 AAAA（IPv6）不通会让 git 卡满 300 秒。
    所以固定做法是：用 DoH 查到 IPv4，临时把 A 记录钉进 hosts（只给 A、不给 AAAA），
    推送与 API 全部走 `http.version=HTTP/1.1`，结束后无条件还原 hosts。

    写 hosts 需要管理员。非管理员会话（或 hosts 被占写不进去）时自动降级为「resolve 通道」：
    完全不碰 hosts，git 用 `-c http.curloptResolve=<host>:443:<ip>` 改连接目标，
    REST 用 `curl.exe --resolve`；Host/SNI 仍是 github.com，证书照常校验。
    注意本机到 github.com 的线路会间歇性 `Connection reset`，所以 resolve 通道会先用
    `git ls-remote` 把候选 IPv4 逐个探测，取第一个真能拉通仓库的再用。

    用法：
        pwsh -File tools\release.ps1 -Version 3.9.4 -Notes @("优化: ……","修复: ……")
        pwsh -File tools\release.ps1 -Version 3.9.4 -DryRun     # 只做预检与网络探测
        pwsh -File tools\release.ps1 -Version 3.9.4 -DryRun -ForceResolve   # 强制演练降级通道

.NOTES
    前置（脚本会自己检查并报错）：
      * Plain Craft Launcher 2\Modules\Base\ModBase.vb 里 VersionBaseName 已等于 -Version，VersionCode 已 +1
      * FormMain.xaml.vb 已插入 If LastVersion < <新 VersionCode> 块
      * docs\更新记录.md 已追加 §21.30.xx
    约定（历史教训，别改）：
      * Release 每版必须 POST 新建，禁止 PATCH 复用同一 id
      * 资产名用字面文件名（?name=PCL-Harness.exe），先复制到 %TEMP% 保住空格
      * Release 正文用 UTF-8 字节 + charset=utf-8，否则中文变问号
      * 只把二进制挂在最新一版
    编码：本文件必须 UTF-8 **带 BOM** + CRLF（本机只有 Windows PowerShell 5.1，
          按 ANSI 读无 BOM 脚本会把中文和引号读坏）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Notes = @(),
    [switch]$SkipBuild,
    [switch]$SkipSdk,
    [switch]$NoPush,
    [switch]$NoRelease,
    [switch]$DryRun,
    [switch]$ForceResolve      # 调试用：跳过写 hosts，强制走 resolve 通道
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
# DoH 通常只回一个区域地址；这几个 GitHub 官方网段地址实测在本机也能连，作为探测失败后的兜底
$FallbackIps = @{
    'github.com'     = @('140.82.114.3', '140.82.116.3')
    'api.github.com' = @()
}

# 网络通道状态：hosts = 钉 hosts（默认）；resolve = 不碰 hosts，用 -c http.curloptResolve / curl --resolve
$script:NetChannel = 'hosts'
$script:NetGitConfig = @()
$script:NetIps = @{}

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

<#
    用 DoH 拿某个域名的全部 IPv4（去重）。
    注意：函数只返回一个值时 PowerShell 会拆成字符数组，调用处必须 @() 包一层。
#>
function Resolve-ViaDoH([string]$Name) {
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($srv in '223.5.5.5', '119.29.29.29', '223.6.6.6') {
        try {
            $r = Invoke-RestMethod -Uri "https://$srv/resolve?name=$Name&type=A" `
                -Headers @{ accept = 'application/dns-json' } -TimeoutSec 10
            foreach ($a in @($r.Answer | Where-Object { $_.type -eq 1 })) {
                $ip = [string]$a.data
                if ($ip -match '^\d{1,3}(\.\d{1,3}){3}$' -and -not $found.Contains($ip)) { $found.Add($ip) }
            }
            if ($found.Count -gt 0) { break }
        } catch { }
    }
    return $found.ToArray()
}

<#
    解析并缓存 PinHosts 的 IPv4（hosts 与 resolve 两条通道都要用）。
#>
function Get-NetIps {
    if ($script:NetIps.Count -gt 0) { return $script:NetIps }
    foreach ($h in $PinHosts) {
        $ips = @(Resolve-ViaDoH $h)
        if ($ips.Count -eq 0) { Warn "DoH 解析 $h 失败，跳过"; continue }
        $script:NetIps[$h] = $ips
        Ok ("{0} -> {1}" -f $h, ($ips -join ' / '))
    }
    if ($script:NetIps.Count -eq 0) { throw 'DoH 一个域名都没解析出来，网络不可用' }
    return $script:NetIps
}

<#
    git 的网络参数：两条通道都要 HTTP/1.1；resolve 通道再追加各域名的 curloptResolve。
#>
function Get-GitNetArgs {
    return @('-c', 'http.version=HTTP/1.1') + $script:NetGitConfig
}

<#
    执行原生命令并吞掉「stderr 变终止异常」的坑。
    PowerShell 5.1 在 $ErrorActionPreference='Stop' 下会把原生命令写 stderr 的每一行
    直接抛成 NativeCommandError，写 2>$null / 2>&1 都拦不住（转换发生在重定向之前），
    而 curl 的超时提示、git 的进度输出恰好都走 stderr。所以这里局部降回 Continue，
    失败与否一律用显式 ExitCode 判断。
    返回 @{ ExitCode = <int>; Output = <string[]> }。
#>
function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $FilePath @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
    }
    return [pscustomobject]@{
        ExitCode = if ($null -eq $code) { 0 } else { $code }
        Output   = @($out | ForEach-Object { "$_" })
    }
}

<#
    探测 github.com 的可达 IPv4。
    不要用 curl 打 https://github.com/ 首页来判断——同一条线路上首页会超时，而 git 的
    info/refs 稳定返回 200；所以直接拿 `git ls-remote` 当探针，和后面 push 走同一条代码路径。
#>
function Select-GitIp([string[]]$Candidates) {
    Push-Location $RepoRoot
    try {
        foreach ($ip in $Candidates) {
            for ($attempt = 1; $attempt -le 2; $attempt++) {
                $r = Invoke-Native 'git' @('-c', 'http.version=HTTP/1.1',
                    '-c', "http.curloptResolve=github.com:443:$ip", 'ls-remote', '--heads', 'origin')
                if ($r.ExitCode -eq 0) { return $ip }
                Warn "探测 github.com @ $ip 第 $attempt 次不通（exit=$($r.ExitCode)）"
            }
        }
    } finally { Pop-Location }
    return $null
}

<#
    探测 api.github.com 的可达 IPv4（未认证的 /rate_limit 返回 200，可用作探针）。
#>
function Select-ApiIp([string[]]$Candidates) {
    foreach ($ip in $Candidates) {
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $r = Invoke-Native 'curl.exe' @('-sS', '--http1.1', '--resolve', "api.github.com:443:$ip",
                '-o', 'NUL', '-w', '%{http_code}', '--max-time', '15', 'https://api.github.com/rate_limit')
            $code = [string](@($r.Output | Where-Object { $_ -match '^\d{3}$' } | Select-Object -Last 1))
            if ($r.ExitCode -eq 0 -and $code -match '^2\d\d$') { return $ip }
            Warn "探测 api.github.com @ $ip 第 $attempt 次不通（exit=$($r.ExitCode) code=$code）"
        }
    }
    return $null
}

<#
    resolve 通道：不写 hosts，改用 -c http.curloptResolve / curl.exe --resolve。
#>
function Invoke-ResolveChannel([scriptblock]$Body) {
    $script:NetChannel = 'resolve'
    $script:NetGitConfig = @()
    $probed = @{}

    $gitCandidates = @((@($script:NetIps['github.com']) + @($FallbackIps['github.com'])) |
        Where-Object { $_ } | Select-Object -Unique)
    $gitIp = Select-GitIp -Candidates @($gitCandidates)
    if (-not $gitIp) {
        $script:NetChannel = 'hosts'
        $script:NetGitConfig = @()
        throw 'resolve 通道连不上 github.com（DoH 地址与备用地址都探测失败）；请用管理员会话走 hosts 通道，或检查网络'
    }
    $probed['github.com'] = $gitIp
    $script:NetGitConfig += @('-c', "http.curloptResolve=github.com:443:$gitIp")
    Ok "resolve 通道 github.com -> $gitIp"

    $apiCandidates = @((@($script:NetIps['api.github.com']) + @($FallbackIps['api.github.com'])) |
        Where-Object { $_ } | Select-Object -Unique)
    $apiIp = $null
    if ($apiCandidates.Count -gt 0) { $apiIp = Select-ApiIp -Candidates @($apiCandidates) }
    if ($apiIp) {
        $probed['api.github.com'] = $apiIp
        $script:NetGitConfig += @('-c', "http.curloptResolve=api.github.com:443:$apiIp")
        Ok "resolve 通道 api.github.com -> $apiIp"
    } else {
        Warn 'resolve 通道没探到可用的 api.github.com，Release 步骤可能失败'
    }

    # 其余域名不在发版关键路径上，用 DoH 首个地址补齐（只为配置完整，失败也不影响 push）
    foreach ($h in @($script:NetIps.Keys)) {
        if ($probed.ContainsKey($h)) { continue }
        $probed[$h] = @($script:NetIps[$h])[0]
        $script:NetGitConfig += @('-c', "http.curloptResolve=${h}:443:$($probed[$h])")
    }
    $script:NetIps = $probed
    try {
        & $Body
    } finally {
        $script:NetChannel = 'hosts'
        $script:NetGitConfig = @()
        Ok 'resolve 通道已退出（未改动 hosts）'
    }
}

<#
    把 github 相关域名的 IPv4 临时钉进 hosts，执行 $Body，最后无条件还原。
    这是本机最稳的 git push 通道：绕过 #S302 屏蔽 + 避免 IPv6 回退超时。
    hosts 不可写（非管理员）时自动降级到 resolve 通道，不再直接抛错。
#>
function Invoke-WithGitHubPin([scriptblock]$Body) {
    $null = Get-NetIps

    $useHosts = -not $ForceResolve
    $backup = Join-Path $env:TEMP ("hosts.release-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    if ($useHosts) {
        try {
            Copy-Item $HostsPath $backup -Force -ErrorAction Stop
        } catch {
            Remove-Item $backup -Force -ErrorAction SilentlyContinue
            $useHosts = $false
            $why = ($_.Exception.Message -split "`r?`n")[0]
            Warn "hosts 不可写（$why）"
        }
    }

    if (-not $useHosts) {
        Warn '降级为 resolve 通道（不碰 hosts）'
        Invoke-ResolveChannel -Body $Body
        return
    }

    $original = [IO.File]::ReadAllText($HostsPath)
    try {
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add('# PCL Harness release pin (temporary, auto-removed)')
        foreach ($h in $PinHosts) {
            $ips = @($script:NetIps[$h])
            if ($ips.Count -eq 0) { Warn "DoH 解析 $h 失败，跳过"; continue }
            $ip = [string]$ips[0]
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

<#
    resolve 通道下的 REST/上传：curl.exe + --resolve，等价于 Invoke-RestMethod 的返回对象。
#>
function Invoke-CurlJson {
    param(
        [string]$Method,
        [string]$Uri,
        $Body,
        [string]$ContentType = 'application/json; charset=utf-8',
        [int]$TimeoutSec = 120
    )
    $hostName = ([Uri]$Uri).Host
    $ips = @($script:NetIps[$hostName])
    if ($ips.Count -eq 0) { $ips = @(Resolve-ViaDoH $hostName) }
    if ($ips.Count -eq 0) { throw "DoH 解析不出 $hostName，resolve 通道无法发请求" }

    $token = Get-GitHubToken
    $outFile = Join-Path $env:TEMP ("ghapi-" + [guid]::NewGuid().ToString('N') + '.json')
    $bodyFile = $null
    try {
        if ($null -ne $Body) {
            $bodyFile = Join-Path $env:TEMP ("ghapi-" + [guid]::NewGuid().ToString('N') + '.bin')
            if ($Body -is [byte[]]) { [IO.File]::WriteAllBytes($bodyFile, $Body) }
            else { [IO.File]::WriteAllText($bodyFile, [string]$Body, (New-Object System.Text.UTF8Encoding($false))) }
        }
        $lastErr = $null
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            foreach ($ip in $ips) {
                $cliArgs = @('-sS', '--http1.1', '--resolve', "${hostName}:443:$ip", '--max-time', "$TimeoutSec",
                    '-X', $Method, '-o', $outFile, '-w', '%{http_code}',
                    '-H', "Authorization: token $token",
                    '-H', 'User-Agent: pcl-harness-release',
                    '-H', 'Accept: application/vnd.github+json',
                    '-H', 'X-GitHub-Api-Version: 2022-11-28')
                if ($bodyFile) { $cliArgs += @('-H', "Content-Type: $ContentType", '--data-binary', "@$bodyFile") }
                $r = Invoke-Native 'curl.exe' @($cliArgs + @($Uri))
                $code = [string](@($r.Output | Where-Object { $_ -match '^\d{3}$' } | Select-Object -Last 1))
                if ($r.ExitCode -ne 0) {
                    $lastErr = "curl exit=$($r.ExitCode)"
                    Warn "API $hostName @ $ip 失败：$lastErr"
                    continue
                }
                $text = if (Test-Path $outFile) { [IO.File]::ReadAllText($outFile, [Text.Encoding]::UTF8) } else { '' }
                if ($code -match '^2\d\d$') {
                    if ([string]::IsNullOrWhiteSpace($text)) { return [pscustomobject]@{} }
                    return ($text | ConvertFrom-Json)
                }
                $lastErr = "HTTP $code"
                $snippet = if ($text.Length -gt 300) { $text.Substring(0, 300) } else { $text }
                Warn "API $hostName @ $ip -> $lastErr $snippet"
            }
        }
        throw "GitHub API 不可达（resolve 通道）：$lastErr"
    } finally {
        Remove-Item $outFile -Force -ErrorAction SilentlyContinue
        if ($bodyFile) { Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue }
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
    $uri = "https://api.github.com$Path"
    if ($script:NetChannel -eq 'resolve') {
        return Invoke-CurlJson -Method $Method -Uri $uri -Body $Body -ContentType $ContentType -TimeoutSec $TimeoutSec
    }
    $headers = @{
        Authorization          = "token $(Get-GitHubToken)"
        'User-Agent'           = 'pcl-harness-release'
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
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
    if ($script:NetChannel -eq 'resolve') {
        $r = Invoke-CurlJson -Method 'Post' -Uri $uri -Body $bytes -ContentType $ContentType -TimeoutSec 900
    } else {
        $headers = @{
            Authorization = "token $(Get-GitHubToken)"
            'User-Agent'  = 'pcl-harness-release'
            Accept        = 'application/vnd.github+json'
        }
        $r = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $bytes `
            -ContentType $ContentType -TimeoutSec 900
    }
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
Step 6 'push（临时钉 IPv4 + HTTP/1.1；hosts 不可写时走 curloptResolve）'
if ($NoPush) {
    Warn '按 -NoPush 跳过'
} elseif ($DryRun) {
    Invoke-WithGitHubPin {
        Push-Location $RepoRoot
        try {
            $net = Get-GitNetArgs
            $r = Invoke-Native 'git' @($net + @('ls-remote', '--heads', 'origin'))
            if ($r.ExitCode -ne 0) {
                throw "ls-remote 失败（exit=$($r.ExitCode)）：$((@($r.Output) | Select-Object -First 2) -join ' ')"
            }
            Ok "连通性正常（DryRun 未推送）：$((@($r.Output) | Select-Object -First 1))"
        } finally { Pop-Location }
    }
} else {
    Invoke-WithGitHubPin {
        Push-Location $RepoRoot
        try {
            $net = Get-GitNetArgs
            $r = Invoke-Native 'git' @($net + @('push', 'origin', 'main'))
            $tail = (@($r.Output) | Select-Object -Last 2) -join ' | '
            if ($r.ExitCode -ne 0) { throw "push main 失败（exit=$($r.ExitCode)）：$tail" }
            Ok "push main：$tail"
            $r = Invoke-Native 'git' @($net + @('push', 'origin', $Tag))
            $tail = (@($r.Output) | Select-Object -Last 2) -join ' | '
            if ($r.ExitCode -ne 0) { throw "push $Tag 失败（exit=$($r.ExitCode)）：$tail" }
            Ok "push $Tag：$tail"
            $rev = Invoke-Native 'git' @('rev-parse', '--short', 'origin/main')
            Ok "远端 main = $(@($rev.Output) | Select-Object -First 1)"
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
        $r = Invoke-Native 'curl.exe' @('-s', '-o', 'NUL', '-w', '%{http_code}', '--max-time', '180', '-L', $u)
        $code = [string](@($r.Output | Where-Object { $_ -match '^\d{3}$' } | Select-Object -Last 1))
        if ($code -ne '200') { throw "ghfast 校验失败（code=$code exit=$($r.ExitCode)）：$u" }
        Ok "ghfast 200 $n"
    }
}

Write-Host ''
Write-Host "PCL Harness $Version 发版流程结束。" -ForegroundColor Green
if ($releaseId) { Write-Host "Release: https://github.com/$Owner/$Repo/releases/tag/$Tag" -ForegroundColor Green }
