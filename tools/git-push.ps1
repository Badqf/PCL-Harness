<#
.SYNOPSIS
    推送分支与 tag：优先走 Steamcommunity302 的本地中继（hosts → 127.0.0.1），
    中继不可用或推送失败时才退回「DoH 现查 IP + curloptResolve」。
.DESCRIPTION
    2026-11-23 实测：plain push 经 S302 中继 1.9 秒完成；强行 curloptResolve 到公网 IP
    会绕过中继、直连被墙地址，得到 21 秒的连接超时。所以顺序必须是 plain 优先。
.EXAMPLE
    .\tools\git-push.ps1 -Tag v3.9.11
.EXAMPLE
    .\tools\git-push.ps1 -Refspec 'main','dev' -Tag 'v3.9.11'
#>
param(
    [string]$Remote = 'origin',
    [string[]]$Refspec = @(),
    [string[]]$Tag = @(),
    [switch]$NoFallback
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if ($Refspec.Count -eq 0) {
        $branch = (& git rev-parse --abbrev-ref HEAD).Trim()
        if (-not $branch -or $branch -eq 'HEAD') { throw '取不到当前分支，请用 -Refspec 指定' }
        $Refspec = @($branch)
    }
    $all = @($Refspec) + @($Tag)
    Write-Host ("推送目标: {0} {1}" -f $Remote, ($all -join ' '))

    function Test-Relay {
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $iar = $client.BeginConnect('127.0.0.1', 443, $null, $null)
            if (-not $iar.AsyncWaitHandle.WaitOne(400)) { return $false }
            $client.EndConnect($iar)
            return $true
        } catch {
            return $false
        } finally {
            $client.Close()
        }
    }

    function Invoke-GitPush([string[]]$GlobalArgs) {
        $a = @() + $GlobalArgs + @('push') + @($Remote) + $all
        # PS 5.1 会把原生命令的 stderr（git 的进度条、"Everything up-to-date"）包成
        # NativeCommandError；在 $ErrorActionPreference='Stop' 下会直接中断脚本，
        # 所以这段临时降到 Continue，成败由 $LASTEXITCODE 判定。
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $out = & git @a 2>&1 | ForEach-Object { $_.ToString() }
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $prevEap
        }
        $out | ForEach-Object { Write-Host $_ }
        return $code
    }

    $relay = Test-Relay
    if ($relay) {
        Write-Host '中继 127.0.0.1:443 可用 → plain push'
    } else {
        Write-Host '中继 127.0.0.1:443 不可用 → 直接进入公网 IP 兜底'
    }

    if ($relay) {
        $code = Invoke-GitPush @()
        if ($code -eq 0) {
            Write-Host 'OK：已通过本地中继推送完成。'
            exit 0
        }
        Write-Warning ("plain push 失败（exit {0}），尝试退回公网 IP。" -f $code)
    }

    if ($NoFallback) { throw 'plain push 未成功，且指定了 -NoFallback' }

    # 兜底：DoH 现查 + 探活 + curloptResolve（只在 127.0.0.1 不通或 plain 失败时使用）
    $ips = @()
    try {
        $doh = Invoke-RestMethod -Uri 'https://223.5.5.5/resolve?name=github.com&type=A' -TimeoutSec 8
        $ips += @($doh.Answer | Where-Object { $_.type -eq 1 } | ForEach-Object { $_.data })
    } catch {
        Write-Warning ("DoH 查询失败: {0}" -f $_.Exception.Message)
    }
    $ips += @('20.205.243.166', '20.27.177.113', '20.247.166.64', '140.82.114.3', '140.82.116.3', '20.6.7.17')
    $ips = @($ips | Where-Object { $_ } | Select-Object -Unique)

    foreach ($ip in $ips) {
        $probe = & curl.exe -s -o NUL --max-time 5 --resolve "github.com:443:$ip" -w '%{http_code}' https://github.com 2>$null
        if ($probe -ne '200') {
            Write-Host ("  跳过 {0}（探活返回 {1}）" -f $ip, $probe)
            continue
        }
        Write-Host ("  探活通过 {0}，推送中…" -f $ip)
        $code = Invoke-GitPush @('-c', 'http.version=HTTP/1.1', '-c', "http.curloptResolve=github.com:443:$ip")
        if ($code -eq 0) {
            Write-Host ("OK：已通过 {0} 推送完成。" -f $ip)
            exit 0
        }
    }
    throw '所有通道都失败：本地中继与公网直连均不可用，稍后重试。'
} finally {
    Pop-Location
}
