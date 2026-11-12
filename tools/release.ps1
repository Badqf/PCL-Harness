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
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 3.9.4 -Notes @("优化: ……","修复: ……")
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 3.9.4 -DryRun     # 只做预检与网络探测
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 3.9.4 -DryRun -ForceResolve   # 强制演练降级通道
        # 本机只有 Windows PowerShell 5.1（没有 pwsh 7），所以统一用 powershell.exe 执行

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
$PinHosts = @('github.com', 'www.github.com', 'api.github.com', 'codeload.github.com', 'uploads.github.com')
# DoH 通常只回一个区域地址；这几个 GitHub 官方网段地址实测在本机也能连，作为探测失败后的兜底
$FallbackIps = @{
    'github.com'     = @('140.82.114.3', '140.82.116.3')
    'api.github.com' = @()
}

# 网络通道状态：hosts = 钉 hosts（默认）；resolve = 不碰 hosts，用 -c http.curloptResolve / curl --resolve
$script:NetChannel = 'hosts'
$script:NetGitConfig = @()
$script:NetIps = @{}
$script:ExitCode = 0    # P0-4：hosts 还原失败等收尾问题置 1，统一在脚本末尾 exit

# P1-2：-NoPush 推不上去时，Release 会指向远端旧提交（或按默认分支补 tag），必须提前拒绝
if ($NoPush -and -not $NoRelease -and -not $DryRun) {
    throw '-NoPush 会让 Release 与上传的二进制不属于同一提交；请去掉 -NoPush，或同时加 -NoRelease 只做本地提交'
}

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
    取一组文件/目录里最新的 LastWriteTime（跳过不存在项；忽略 bin/obj）。
    P0-3：-SkipBuild / -SkipSdk 时用它判断旧产物是否比自己的输入还旧。
#>
function Get-NewestWriteTime {
    param([string[]]$Paths, [string[]]$Extensions = @())
    $newest = [datetime]::MinValue
    foreach ($p in $Paths) {
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $item = Get-Item -LiteralPath $p
        if ($item.PSIsContainer) {
            $files = @(Get-ChildItem -LiteralPath $p -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' })
        } else {
            $files = @($item)
        }
        foreach ($f in $files) {
            if ($Extensions.Count -gt 0 -and $Extensions -notcontains $f.Extension) { continue }
            if ($f.LastWriteTime -gt $newest) { $newest = $f.LastWriteTime }
        }
    }
    return $newest
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
function Invoke-HostsPinnedChannel {
    param([scriptblock]$Body)
    # 返回 $true：hosts 已钉上并且 body 已执行；返回 $false：hosts 写不进去、body 未执行，调用方应降级。
    $backup = Join-Path $env:TEMP ("hosts.release-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $tmpHosts = "${HostsPath}.release-new"
    try {
        # A-8a：字节级读取/备份，避免文本读写改写非 UTF-8 的 hosts；读失败也走降级路径
        $originalBytes = [IO.File]::ReadAllBytes($HostsPath)
        [IO.File]::WriteAllBytes($backup, $originalBytes)
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
        # P0-4：前缀字节 + 原文件字节拼接，写同目录临时文件后 [IO.File]::Replace 原子替换
        $prefixBytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes((($lines -join "`r`n") + "`r`n"))
        $newBytes = [byte[]]($prefixBytes + $originalBytes)
        [IO.File]::WriteAllBytes($tmpHosts, $newBytes)
        try {
            [IO.File]::Replace($tmpHosts, $HostsPath, $null)
        } catch {
            Warn "hosts 原子替换失败（$($_.Exception.Message)），回退为直接写入"
            [IO.File]::WriteAllBytes($HostsPath, $newBytes)
        }
    } catch {
        if (Test-Path -LiteralPath $tmpHosts) { Remove-Item $tmpHosts -Force -ErrorAction SilentlyContinue }
        Remove-Item $backup -Force -ErrorAction SilentlyContinue
        $script:HostsPinFailure = ($_.Exception.Message -split "`r?`n")[0]
        return $false
    }
    if (Test-Path -LiteralPath $tmpHosts) { Remove-Item $tmpHosts -Force -ErrorAction SilentlyContinue }
    try {
        & ipconfig /flushdns | Out-Null
        & $Body
    } finally {
        try {
            [IO.File]::WriteAllBytes($HostsPath, $originalBytes)
            & ipconfig /flushdns | Out-Null
            Ok 'hosts 已还原'
            Remove-Item $backup -Force -ErrorAction SilentlyContinue   # 仅还原成功才删备份
        } catch {
            # P0-4：还原失败保留备份并记退出码；不在这里 throw，避免打断已完成的发布
            $script:ExitCode = 1
            Write-Error "hosts 还原失败（$($_.Exception.Message)）；原文件备份保留在 $backup，请手工恢复" -ErrorAction Continue
        }
    }
    return $true
}

function Invoke-WithGitHubPin([scriptblock]$Body) {
    $null = Get-NetIps

    if (-not $ForceResolve) {
        if (Invoke-HostsPinnedChannel -Body $Body) { return }
        Warn "hosts 不可写（$script:HostsPinFailure）"
    }
    Warn '降级为 resolve 通道（不碰 hosts）'
    Invoke-ResolveChannel -Body $Body
}
<#
    按 HTTP 状态码给出排障提示（P1-6）：401 查 token、403 看限流/方法名、404 查路径、422 查是否已存在。
#>
function Format-ApiHttpHint([string]$Code) {
    switch ([string]$Code) {
        '401' { return '（401：token 无效或过期，检查 %USERPROFILE%\.git-credentials）' }
        '403' { return '（403：常见于限流或方法名大小写错误，检查 X-RateLimit-Remaining / Retry-After）' }
        '404' { return '（404：资源不存在，检查 Owner/Repo/Tag）' }
        '422' { return '（422：请求被拒绝；Release/资产已存在时也会返回 422）' }
        default { return '' }
    }
}

<#
    只探测 TLS/HTTP 通道是否通：拿到任意 HTTP 状态码（curl exit=0）即视为通。
    P1-6：POST 失败后先探测通道，区分「服务端拒绝」与「网络不可达」。
#>
function Test-ApiReachable([string]$HostName, [string]$Ip, [int]$TimeoutSec) {
    $r = Invoke-Native 'curl.exe' @('-sS', '--http1.1', '--resolve', "${HostName}:443:$Ip",
        '-o', 'NUL', '-w', '%{http_code}', '--max-time', "$TimeoutSec", "https://$HostName/")
    return ($r.ExitCode -eq 0)
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
    # P0-1：curl -X 按字面发送方法名："Post" 会被 GitHub 当未知方法直接 403（HTML Forbidden）。
    # 统一在入口归一化，任何调用方漏写大小写都不会再踩坑（Upload-Asset 之前就漏过）。
    $Method = ([string]$Method).ToUpperInvariant()
    $isGet = ($Method -eq 'GET')
    $hostName = ([Uri]$Uri).Host
    # @($null) 的 Count 是 1，必须先滤掉空值，否则未缓存的域名会在走 DoH 兜底之前就拿到空 IP
    $ips = @(@($script:NetIps[$hostName]) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($ips.Count -eq 0) { $ips = @(Resolve-ViaDoH $hostName) }
    if ($ips.Count -eq 0) { throw "DoH 解析不出 $hostName，resolve 通道无法发请求" }

    $token = Get-GitHubToken
    $outFile = Join-Path $env:TEMP ("ghapi-" + [guid]::NewGuid().ToString('N') + '.json')
    $bodyFile = $null
    $errors = New-Object System.Collections.Generic.List[string]
    try {
        if ($null -ne $Body) {
            $bodyFile = Join-Path $env:TEMP ("ghapi-" + [guid]::NewGuid().ToString('N') + '.bin')
            if ($Body -is [byte[]]) { [IO.File]::WriteAllBytes($bodyFile, $Body) }
            else { [IO.File]::WriteAllText($bodyFile, [string]$Body, (New-Object System.Text.UTF8Encoding($false))) }
        }
        # P1-6：GET 幂等，最多两轮（两轮×多 IP）；POST 等非幂等请求只发一次，失败后先 GET 探测通道
        $attempts = if ($isGet) { 2 } else { 1 }
        $lastCode = ''
        for ($attempt = 1; $attempt -le $attempts; $attempt++) {
            foreach ($ip in $ips) {
                $cliArgs = @('-sS', '-L', '--http1.1', '--resolve', "${hostName}:443:$ip", '--max-time', "$TimeoutSec",
                    '-X', $Method, '-o', $outFile, '-w', '%{http_code}',
                    '-H', "Authorization: token $token",
                    '-H', 'User-Agent: pcl-harness-release',
                    '-H', 'Accept: application/vnd.github+json',
                    '-H', 'X-GitHub-Api-Version: 2022-11-28')
                if ($bodyFile) { $cliArgs += @('-H', "Content-Type: $ContentType", '--data-binary', "@$bodyFile") }
                $r = Invoke-Native 'curl.exe' @($cliArgs + @($Uri))
                $code = [string](@($r.Output | Where-Object { $_ -match '^\d{3}$' } | Select-Object -Last 1))
                $lastCode = $code
                if ($r.ExitCode -ne 0) {
                    $errors.Add("curl exit=$($r.ExitCode) @ $ip")
                    Warn "API $hostName @ $ip 失败：curl exit=$($r.ExitCode)"
                    continue
                }
                $text = if (Test-Path $outFile) { [IO.File]::ReadAllText($outFile, [Text.Encoding]::UTF8) } else { '' }
                if ($code -match '^2\d\d$') {
                    if ([string]::IsNullOrWhiteSpace($text)) { return [pscustomobject]@{} }
                    return ($text | ConvertFrom-Json)
                }
                $snippet = if ($text.Length -gt 300) { $text.Substring(0, 300) } else { $text }
                $errors.Add("HTTP $code @ $ip$(Format-ApiHttpHint $code) $snippet")
                Warn "API $hostName @ $ip -> HTTP $code$(Format-ApiHttpHint $code) $snippet"
            }
        }
        # P1-6：汇总所有尝试的错误一并抛出；非 GET 再探测一次通道，区分「被拒」与「不可达」
        $reachable = $false
        if ($isGet) {
            $reachable = $true
        } else {
            $reachable = Test-ApiReachable -HostName $hostName -Ip ([string]$ips[0]) -TimeoutSec ([math]::Min($TimeoutSec, 30))
            if (-not $reachable) { $errors.Add("通道探测失败：GET https://$hostName/ 连不上（@ $($ips[0])）") }
        }
        $detail = (@($errors) | ForEach-Object { "  - $_" }) -join "`r`n"
        if ($isGet) {
            throw "GitHub API 请求失败（resolve 通道，共 $($errors.Count) 次尝试）：`r`n$detail"
        } elseif ($reachable) {
            throw "GitHub API 拒绝 $Method $Uri（通道正常，是服务端拒绝；HTTP $lastCode$(Format-ApiHttpHint $lastCode)）：`r`n$detail"
        } else {
            throw "GitHub API 不可达（resolve 通道，通道探测也失败）：`r`n$detail"
        }
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
    # curl 的 -X 按字面发送方法名："Post" 会被 GitHub 当未知方法直接 403（HTML Forbidden）
    $Method = ([string]$Method).ToUpperInvariant()
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
        $r = Invoke-CurlJson -Method 'POST' -Uri $uri -Body $bytes -ContentType $ContentType -TimeoutSec 900
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

# ---------------------------------------------------------------- 0. Release 幂等预检
# P0-2：先只读查询远端是否已有同 tag 的 Release。已有就立刻失败，避免编译/提交/推送都做完后
# 才在 POST /releases 处吃到 422（already_exists），把仓库留在「代码已动、Release 建不了」的半成品状态。
if (-not $NoRelease) {
    Write-Host "[预检] Release $Tag 是否已存在（只读）" -ForegroundColor Cyan
    $script:existingRelease = $null
    try {
        Invoke-WithGitHubPin {
            $script:existingRelease = Invoke-GitHubApi -Method Get -Path "/repos/$Owner/$Repo/releases/tags/$Tag"
        }
    } catch {
        $probeMsg = [string]$_.Exception.Message
        if ($probeMsg -match 'HTTP 404|\(404\)|Not Found') {
            $script:existingRelease = $null
        } else {
            throw "查询 Release $Tag 失败（未编译、未提交、未推送）：$probeMsg"
        }
    }
    if ($null -ne $script:existingRelease) {
        throw @"
Release $Tag 已存在（id=$($script:existingRelease.id)，tag=$($script:existingRelease.tag_name)）。
GitHub 对已存在的 tag 再次 POST /releases 会返回 422 already_exists，本次发版必然在最后一步失败。
脚本已在预检阶段终止：未编译、未提交、未推送、未创建任何 Release。
处理方式（二选一）：
  1) 先删除旧 Release（tag 保留）：打开 https://github.com/$Owner/$Repo/releases 删除 $Tag 后重跑；
  2) 或改用新的版本号：同步修改 ModBase.vb 的 VersionBaseName 与两处 VersionCode，再重跑。
"@
    }
    Ok "Release $Tag 尚不存在，可以继续"
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
$codeMatches = @($mCode)
if ($codeMatches.Count -ne 2) {
    throw "ModBase.vb 里应有 2 处 VersionCode 声明（正式版与快照版），实际 $($codeMatches.Count) 处；两处都要改"
}
$codes = @($codeMatches | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
if ($codes.Count -gt 1) {
    throw "ModBase.vb 里的 VersionCode 声明值不一致（$($codes -join ' / ')）：正式版与快照版必须同时改成同一个值，否则更新块检查会漏判"
}
$versionCode = $codes[0]
Ok "版本 $Version，VersionCode=$versionCode"

$formMain = Join-Path $RepoRoot 'Plain Craft Launcher 2\FormMain.xaml.vb'
$fmText = [IO.File]::ReadAllText($formMain)
if ($fmText -notmatch "LastVersion < $versionCode") {
    Warn "FormMain.xaml.vb 里没有 If LastVersion < $versionCode 的更新块（更新日志将不显示本版内容）"
}

$changelog = Join-Path $RepoRoot 'docs\更新记录.md'
if ((Test-Path $changelog) -and ([IO.File]::ReadAllText($changelog) -notmatch [regex]::Escape($Version))) {
    Warn "docs\更新记录.md 里没有出现 $Version"
}

Push-Location $RepoRoot
try {
    $gst = Invoke-Native 'git' @('status', '--porcelain')
    if ($gst.ExitCode -ne 0) { throw "git status 失败（exit=$($gst.ExitCode)）：$((@($gst.Output) | Select-Object -First 2) -join ' ')" }
    $dirty = @($gst.Output | Where-Object { $_ -and $_.Trim() })
    Ok "改动文件 $($dirty.Count) 个"
    $gtag = Invoke-Native 'git' @('tag', '-l', $Tag)
    if ($gtag.ExitCode -ne 0) { throw "git tag -l 失败（exit=$($gtag.ExitCode)）" }
    if (@($gtag.Output | Where-Object { $_ -and $_.Trim() }).Count -gt 0) { Warn "本地已存在 tag $Tag（会在 Step 5 校验指向）" }
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
        $st = Invoke-Native 'git' @('status', '--porcelain')
        if ($st.ExitCode -ne 0) { throw "git status 失败（exit=$($st.ExitCode)）" }
        $dirty = @($st.Output | Where-Object { $_ -and $_.Trim() })
        if ($dirty.Count -gt 0) {
            $r = Invoke-Native 'git' @('add', '-A')
            if ($r.ExitCode -ne 0) { throw "git add 失败（exit=$($r.ExitCode)）：$((@($r.Output) | Select-Object -Last 2) -join ' ')" }
            $msgFile = Join-Path $env:TEMP "release-msg-$Version.txt"
            # 消息里可能有引号，必须走文件，否则 git 会把空格后的词当 pathspec
            [IO.File]::WriteAllText($msgFile, "feat: release PCL Harness $Version", (New-Object System.Text.UTF8Encoding($false)))
            $r = Invoke-Native 'git' @('commit', '-F', $msgFile)
            Remove-Item $msgFile -Force -ErrorAction SilentlyContinue
            if ($r.ExitCode -ne 0) { throw "git commit 失败（exit=$($r.ExitCode)）：$((@($r.Output) | Select-Object -Last 3) -join ' | ')" }
            $h = Invoke-Native 'git' @('rev-parse', '--short', 'HEAD')
            Ok "commit $((@($h.Output) | Select-Object -First 1))"
        } else {
            Warn '工作区干净，无新提交'
        }
        $tagList = @(Invoke-Native 'git' @('tag', '-l', $Tag) | ForEach-Object { @($_.Output) } | Where-Object { $_ -and $_.Trim() })
        if ($tagList.Count -eq 0) {
            $r = Invoke-Native 'git' @('tag', '-a', $Tag, '-m', "PCL Harness $Version")
            if ($r.ExitCode -ne 0) { throw "git tag 失败（exit=$($r.ExitCode)）" }
            Ok "tag $Tag"
        } else {
            # A-8b：复用旧 tag 前先确认它指向本次 HEAD，否则要到 Step 8 才报错、push 已完成
            $headR = Invoke-Native 'git' @('rev-parse', 'HEAD')
            if ($headR.ExitCode -ne 0) { throw "git rev-parse HEAD 失败（exit=$($headR.ExitCode)）" }
            $headSha = ([string](@($headR.Output) | Select-Object -First 1)).Trim()
            $tagR = Invoke-Native 'git' @('rev-parse', "$Tag^{commit}")
            if ($tagR.ExitCode -ne 0) { throw "解析本地 tag $Tag 失败（exit=$($tagR.ExitCode)）" }
            $tagSha = ([string](@($tagR.Output) | Select-Object -First 1)).Trim()
            if ($tagSha -ne $headSha) {
                throw "本地已存在 tag $Tag 且指向 $tagSha，但本次 HEAD 是 $headSha；先 git tag -d $Tag 后重跑，或改用新版本号"
            }
            Warn "本地已存在 tag $Tag，已确认指向本次 HEAD（$headSha）"
        }
    } finally { Pop-Location }
}

# ---------------------------------------------------------------- 5.5 资产预检
# P0-3：上传的是 $ReleaseDir 里的两个文件；-SkipBuild / -SkipSdk 时如果它们是上次留下的旧产物，
# 就必须在 push 之前拦下，避免静默发出旧版本，或建完 Release 才发现文件缺失。
if (-not $DryRun -and -not $NoRelease) {
    $exeAsset = Join-Path $ReleaseDir $ExeName
    $sdkAsset = Join-Path $ReleaseDir $SdkName
    if (-not (Test-Path -LiteralPath $exeAsset)) {
        throw "待上传的 exe 不存在：$exeAsset；请去掉 -SkipBuild 或先完整跑一次"
    }
    if (-not (Test-Path -LiteralPath $sdkAsset)) {
        throw "待上传的 SDK 不存在：$sdkAsset；请去掉 -SkipSdk 或先完整跑一次"
    }
    if ($SkipBuild) {
        $exeSrcItem = Get-Item -LiteralPath $exeSrc -ErrorAction SilentlyContinue
        if (-not $exeSrcItem) { throw "-SkipBuild 但编译产物不存在：$exeSrc" }
        $exeInputNewest = Get-NewestWriteTime -Paths @(
            (Join-Path $RepoRoot 'Plain Craft Launcher 2'),
            (Join-Path $RepoRoot 'MeloongCore'),
            (Join-Path $RepoRoot 'PCLCS'),
            (Join-Path $RepoRoot 'build.ps1')) -Extensions @('.vb', '.xaml', '.vbproj', '.resx', '.config', '.cs', '.csproj', '.ps1')
        if ($exeSrcItem.LastWriteTime -lt $exeInputNewest) {
            throw "-SkipBuild 但 exe（$($exeSrcItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))）比源码/构建脚本（$($exeInputNewest.ToString('yyyy-MM-dd HH:mm:ss'))）旧；请去掉 -SkipBuild 走完整编译"
        }
        Ok "exe 新鲜度通过（$($exeSrcItem.LastWriteTime.ToString('HH:mm:ss'))）"
    }
    if ($SkipSdk) {
        $sdkItem = Get-Item -LiteralPath $sdkAsset
        $sdkInputNewest = Get-NewestWriteTime -Paths @(
            (Join-Path $PSScriptRoot 'sdk'),
            (Join-Path $RepoRoot 'samples'),
            (Join-Path $RepoRoot 'PLUGIN-DEV.md'),
            (Join-Path $RepoRoot 'LICENSE'),
            (Join-Path $RepoRoot 'docs\API-REQUESTS-market.md'),
            (Join-Path $PSScriptRoot 'pack-plugin.ps1'),
            (Join-Path $PSScriptRoot 'pack-plugin-sdk.ps1'))
        if ($sdkItem.LastWriteTime -lt $sdkInputNewest) {
            throw "-SkipSdk 但 SDK zip（$($sdkItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))）比打包素材（$($sdkInputNewest.ToString('yyyy-MM-dd HH:mm:ss'))）旧；请去掉 -SkipSdk 走完整打包"
        }
        Ok "SDK 新鲜度通过（$($sdkItem.LastWriteTime.ToString('HH:mm:ss'))）"
    }
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
    # P1-2：显式带上 target_commitish，避免远端没有该 tag 时 GitHub 按默认分支 HEAD 补 tag，
    # 造成 Release 的源码与上传的二进制不是同一份。
    Push-Location $RepoRoot
    try {
        $headRev = Invoke-Native 'git' @('rev-parse', 'HEAD')
        if ($headRev.ExitCode -ne 0) { throw "git rev-parse HEAD 失败（exit=$($headRev.ExitCode)）" }
        $headSha = ([string](@($headRev.Output) | Select-Object -First 1)).Trim()
    } finally { Pop-Location }
    $payload = @{
        tag_name = $Tag; name = "PCL Harness $Version"; body = $body
        draft = $false; prerelease = $false; target_commitish = $headSha
    } | ConvertTo-Json -Compress

    Invoke-WithGitHubPin {
        try {
            $rel = Invoke-GitHubApi -Method Post -Path "/repos/$Owner/$Repo/releases" -Body $payload
        } catch {
            $apiMsg = [string]$_.Exception.Message
            if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $apiMsg = $apiMsg + ' ' + [string]$_.ErrorDetails.Message }
            if ($apiMsg -match '(?i)\b422\b|already_exists') {
                throw "创建 Release 失败：$Tag 的 Release 已存在（HTTP 422 already_exists）。请先删除旧 Release 或改用新版本号"
            }
            throw
        }
        $script:releaseId = $rel.id
        Ok "Release $($rel.id) $($rel.tag_name) target=$headSha"

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
        # P1-3：以下 API 校验失败才算发版失败（硬错误）
        $rel = Invoke-GitHubApi -Method Get -Path "/repos/$Owner/$Repo/releases/tags/$Tag"
        if ($rel.tag_name -ne $Tag) { throw "Release 查询结果为 $($rel.tag_name)，不是 $Tag" }
        $assetNames = @($rel.assets | ForEach-Object { [string]$_.name })
        foreach ($need in @('PCL-Harness.exe', $SdkName)) {
            if ($assetNames -notcontains $need) { throw "Release $Tag 缺少资产 ${need}（现有：$($assetNames -join ', ')）" }
        }
        Ok "Release $Tag assets=$($assetNames -join ', ')"

        # P1-2 硬断言：远端 tag 必须指向本地 HEAD（annotated tag 先比 tag 对象，再比 peeled commit）
        Push-Location $RepoRoot
        try {
            $localHead = Invoke-Native 'git' @('rev-parse', 'HEAD')
            if ($localHead.ExitCode -ne 0) { throw "本地 git rev-parse HEAD 失败（exit=$($localHead.ExitCode)）" }
            $localHeadSha = ([string](@($localHead.Output) | Select-Object -First 1)).Trim()
            $localTag = Invoke-Native 'git' @('rev-parse', $Tag)
            if ($localTag.ExitCode -ne 0) { throw "本地 tag $Tag 不存在（exit=$($localTag.ExitCode)）" }
            $localTagSha = ([string](@($localTag.Output) | Select-Object -First 1)).Trim()
            $net = Get-GitNetArgs
            $remote = Invoke-Native 'git' @($net + @('ls-remote', 'origin', "refs/tags/$Tag"))
            if ($remote.ExitCode -ne 0) { throw "ls-remote refs/tags/$Tag 失败（exit=$($remote.ExitCode)）" }
            $remoteLine = @($remote.Output | Where-Object { $_ -match "\trefs/tags/$([regex]::Escape($Tag))$" } | Select-Object -First 1)
            if ($remoteLine.Count -eq 0) { throw "远端没有 tag $Tag（-NoPush 未推送，或 push 未成功）" }
            $remoteSha = ([string]$remoteLine[0]).Split("`t")[0]
            if ($remoteSha -ne $localTagSha) { throw "远端 tag $Tag = $remoteSha，本地 tag = $localTagSha；远端 tag 与本次发布不一致" }
            $peeled = Invoke-Native 'git' @('rev-parse', "$Tag^{commit}")
            if ($peeled.ExitCode -ne 0) { throw "解析 $Tag^{commit} 失败（exit=$($peeled.ExitCode)）" }
            $peeledSha = ([string](@($peeled.Output) | Select-Object -First 1)).Trim()
            if ($peeledSha -ne $localHeadSha) { throw "tag $Tag 指向 $peeledSha，但本地 HEAD 是 $localHeadSha；tag 与提交不一致" }
            Ok "远端 tag $Tag = $localTagSha（commit $localHeadSha）"
        } finally { Pop-Location }
    }

    # P1-3：ghfast 只代表分发可达性，不属于发版失败；404 也重试（刚发版时 CDN 可能短暂 404），
    # 429/5xx/000/404 重试 3 次后：404 视为资产真的不存在（硬失败），其他只 Warn
    $retryDelays = @(5, 15, 30)
    foreach ($n in @('PCL-Harness.exe', $SdkName)) {
        $u = "https://ghfast.top/https://github.com/$Owner/$Repo/releases/download/$Tag/$n"
        $finalCode = ''
        for ($i = 0; $i -le $retryDelays.Count; $i++) {
            $r = Invoke-Native 'curl.exe' @('-s', '-o', 'NUL', '-w', '%{http_code}', '--max-time', '180', '-L', $u)
            $finalCode = [string](@($r.Output | Where-Object { $_ -match '^\d{3}$' } | Select-Object -Last 1))
            if ($finalCode -eq '200') { break }
            if ($i -lt $retryDelays.Count) {
                Warn "ghfast 第 $($i + 1) 次校验失败（code=$finalCode），$($retryDelays[$i])s 后重试：$u"
                Start-Sleep -Seconds $retryDelays[$i]
            }
        }
        if ($finalCode -eq '200') {
            Ok "ghfast 200 $n"
        } elseif ($finalCode -eq '404') {
            throw "ghfast 404（发布资产真的不存在；已重试 $($retryDelays.Count) 次）：$u"
        } else {
            Warn "ghfast 未通过（code=$finalCode），仅影响镜像分发可达性，不影响 Release 本身：$u"
        }
    }
}

Write-Host ''
$skippedSteps = New-Object System.Collections.Generic.List[string]
if ($DryRun) {
    $skippedSteps.AddRange([string[]]@('编译', 'SDK 打包', '部署', 'commit/tag', 'push', 'Release', '自检'))
} else {
    if ($SkipBuild) { $skippedSteps.Add('编译') }
    if ($SkipSdk) { $skippedSteps.Add('SDK 打包') }
    if ($NoPush) { $skippedSteps.Add('push') }
    if ($NoRelease) { $skippedSteps.Add('Release') }
}
if ($DryRun) {
    Write-Host "PCL Harness $Version 演练通过（DryRun：未编译、未提交、未发布）。" -ForegroundColor Yellow
} elseif ($NoRelease) {
    $pushNote = if ($NoPush) { '未推送' } else { '已推送' }
    Write-Host "PCL Harness $Version 已完成本地提交并$pushNote，按 -NoRelease 未创建 Release。" -ForegroundColor Yellow
} elseif ($NoPush) {
    Write-Host "PCL Harness $Version 已完成本地提交，按 -NoPush 未推送、未创建 Release。" -ForegroundColor Yellow
} else {
    Write-Host "PCL Harness $Version 发版完成。" -ForegroundColor Green
}
if ($skippedSteps.Count -gt 0) { Write-Host ("      跳过：" + ($skippedSteps -join '、')) -ForegroundColor DarkGray }
if ($releaseId) { Write-Host "Release: https://github.com/$Owner/$Repo/releases/tag/$Tag" -ForegroundColor Green }
if ($script:ExitCode -ne 0) {
    Write-Host "      注意：hosts 还原失败（备份已保留），退出码 $script:ExitCode" -ForegroundColor Red
}
exit $script:ExitCode
