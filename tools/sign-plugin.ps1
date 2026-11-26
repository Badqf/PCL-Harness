<#
.SYNOPSIS
    为 PCL Harness 插件生成或校验 ECDSA P-256 签名。

.DESCRIPTION
    签名载荷（v7.4 起为四段，逐行用 LF 连接）：
        id
        version
        小写 sha256(DLL)
        小写 sha256(权限指纹)

    权限指纹由宿主 ModPlugin.PermissionFingerprint 生成：
        p=<权限名小写、排序、逗号连接>[;net=*][;hosts=...][;read=...][;write=...]
    hosts / read / write 各自小写并按 Ordinal 排序。签名因此同时绑定清单里的
    权限、网络主机和可读写路径；改其中任何一项都必须重新签名。

    公钥表是插件根目录下的 publishers.json：
        { "发布者名": "Base64(SPKI DER)" }

.PARAMETER Plugin
    插件目录，或直接的 plugin.json 路径。

.PARAMETER KeyFile
    私钥文件路径（默认 %USERPROFILE%\.pcl-harness\publisher-key.json）。
    由 -NewKey 生成，内含 Base64 的 CNG EccPrivateBlob（104 字节），
    不要提交进仓库，也不要发给别人。

.PARAMETER Name
    发布者名；-NewKey 时写入密钥文件，-Register 时作为 publishers.json 的键。

.PARAMETER Publishers
    publishers.json 路径；默认取插件目录的上一级（插件根目录）下的该文件。

.PARAMETER PluginsRoot
    -VerifyAll 时扫描的插件根目录。

.PARAMETER NewKey
    生成新的 P-256 私钥文件并打印公钥（Base64 SPKI）。

.PARAMETER Register
    把当前私钥对应的公钥写入 publishers.json（同名键会被覆盖）。

.PARAMETER Verify
    只校验指定插件的签名状态并打印结果，不改文件。

.PARAMETER VerifyAll
    扫描插件根目录下的所有 plugin.json，逐个打印签名状态；
    发现 Invalid 时以退出码 1 结束，可用于发布前的签名自检。

.EXAMPLE
    tools\sign-plugin.ps1 -NewKey -Name Badqf

.EXAMPLE
    tools\sign-plugin.ps1 -Plugin 'F:\PCL2-Release\PCLHarness\Plugins\download-page' -Register

.EXAMPLE
    tools\sign-plugin.ps1 -PluginsRoot 'F:\PCL2-Release\PCLHarness\Plugins' -VerifyAll
#>
param(
    [string]$Plugin,
    [string]$KeyFile = (Join-Path $env:USERPROFILE '.pcl-harness\publisher-key.json'),
    [string]$Name,
    [string]$Publishers,
    [string]$PluginsRoot,
    [switch]$NewKey,
    [switch]$Register,
    [switch]$Verify,
    [switch]$VerifyAll
)

$ErrorActionPreference = 'Stop'

function Write-Ok([string]$Message) { Write-Host ('[OK] ' + $Message) -ForegroundColor Green }
function Write-Bad([string]$Message) { Write-Host ('[!!] ' + $Message) -ForegroundColor Red }
function Stop-With([string]$Message) { Write-Host ('[XX] ' + $Message) -ForegroundColor Red; exit 1 }

# P-256 SubjectPublicKeyInfo 的固定前缀，后面接 0x04 || X(32) || Y(32)
$Script:SpkiPrefix = [byte[]](0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE,
                              0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D,
                              0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04)

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-TextSha256Hex([string]$Text) {
    return (Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($Text)))
}

function Read-Manifest([string]$ManifestPath) {
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        Stop-With ('找不到清单：' + $ManifestPath)
    }
    $raw = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8)
    try {
        return [pscustomobject]@{ Path = $ManifestPath; Raw = $raw; Json = ($raw | ConvertFrom-Json) }
    } catch {
        Stop-With ('plugin.json 不是合法 JSON：' + $_.Exception.Message)
    }
}

function Resolve-ManifestPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { Stop-With '请用 -Plugin 指定插件目录或 plugin.json。' }
    if ($Path.EndsWith('.json', [System.StringComparison]::OrdinalIgnoreCase)) {
        return (Resolve-Path -LiteralPath $Path).Path
    }
    return (Join-Path (Resolve-Path -LiteralPath $Path).Path 'plugin.json')
}

function Get-StringList($Value) {
    if ($null -eq $Value) { return @() }
    if ($Value -is [string]) { return @($Value) }
    if ($Value -is [System.Array]) {
        $list = @()
        foreach ($Item in $Value) { if ($null -ne $Item) { $list += $Item.ToString() } }
        return $list
    }
    return @()
}

function Sort-OrdinalLower([object[]]$Values) {
    $arr = @($Values | ForEach-Object { $_.ToString().ToLowerInvariant() })
    [System.Array]::Sort($arr, [System.StringComparer]::Ordinal)
    return $arr
}

function Get-PermissionFingerprint($Manifest) {
    $perms = New-Object System.Collections.Generic.List[string]
    $hosts = New-Object System.Collections.Generic.List[string]
    $reads = New-Object System.Collections.Generic.List[string]
    $writes = New-Object System.Collections.Generic.List[string]
    $netAny = $false

    $PermsToken = $Manifest.permissions
    if ($null -ne $PermsToken) {
        if ($PermsToken -is [System.Array]) {
            foreach ($Item in $PermsToken) {
                $Name = $Item.ToString().Trim().ToLowerInvariant()
                if ($Name -ne '') { $perms.Add($Name) }
            }
        } else {
            foreach ($Prop in $PermsToken.PSObject.Properties) {
                $Key = $Prop.Name.Trim().ToLowerInvariant()
                if ($Key -eq '') { continue }
                $perms.Add($Key)
                $Value = $Prop.Value
                if ($Key -eq 'net') {
                    if ($Value -is [bool]) {
                        if ($Value) { $netAny = $true }
                    } else {
                        foreach ($HostName in (Get-StringList $Value)) {
                            if (-not $hosts.Contains($HostName)) { $hosts.Add($HostName) }
                        }
                        if ($Value -is [System.Management.Automation.PSCustomObject]) {
                            foreach ($HostName in (Get-StringList $Value.hosts)) {
                                if (-not $hosts.Contains($HostName)) { $hosts.Add($HostName) }
                            }
                        }
                    }
                } elseif ($Key -eq 'files') {
                    if ($Value -is [System.Management.Automation.PSCustomObject]) {
                        foreach ($Item in (Get-StringList $Value.read)) { $reads.Add($Item) }
                        foreach ($Item in (Get-StringList $Value.write)) { $writes.Add($Item) }
                    } else {
                        foreach ($Item in (Get-StringList $Value)) { $reads.Add($Item); $writes.Add($Item) }
                    }
                }
            }
        }
    }

    $Builder = New-Object System.Text.StringBuilder
    [void]$Builder.Append('p=' + ((Sort-OrdinalLower $perms.ToArray()) -join ','))
    if ($netAny) { [void]$Builder.Append(';net=*') }
    if ($hosts.Count -gt 0) { [void]$Builder.Append(';hosts=' + ((Sort-OrdinalLower $hosts.ToArray()) -join ',')) }
    if ($reads.Count -gt 0) { [void]$Builder.Append(';read=' + ((Sort-OrdinalLower $reads.ToArray()) -join ',')) }
    if ($writes.Count -gt 0) { [void]$Builder.Append(';write=' + ((Sort-OrdinalLower $writes.ToArray()) -join ',')) }
    return $Builder.ToString()
}

function Get-DllSha256($Manifest) {
    $Json = $Manifest.Json
    $Sha = ''
    if ($null -ne $Json.sha256) {
        if ($Json.sha256 -is [string]) { $Sha = $Json.sha256 }
        elseif ($null -ne $Json.sha256.dll) { $Sha = [string]$Json.sha256.dll }
    }
    $Sha = $Sha.Trim()
    if ($Sha -ne '') { return $Sha.ToLowerInvariant() }
    if ([string]::IsNullOrWhiteSpace($Json.dll)) { Stop-With '清单没有 dll 字段，无法计算 DLL 哈希。' }
    $DllPath = Join-Path (Split-Path -Parent $Manifest.Path) $Json.dll
    if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) { Stop-With ('找不到 DLL：' + $DllPath) }
    return (Get-Sha256Hex ([System.IO.File]::ReadAllBytes($DllPath)))
}

function Get-SignPayload($Manifest) {
    $Json = $Manifest.Json
    if ([string]::IsNullOrWhiteSpace($Json.id)) { Stop-With '清单缺少 id。' }
    if ([string]::IsNullOrWhiteSpace($Json.version)) { Stop-With '清单缺少 version。' }
    $DllSha = Get-DllSha256 $Manifest
    $Fingerprint = Get-PermissionFingerprint $Json
    $Lines = @($Json.id, [string]$Json.version, $DllSha, (Get-TextSha256Hex $Fingerprint))
    return [pscustomobject]@{
        Payload = ($Lines -join "`n")
        DllSha = $DllSha
        Fingerprint = $Fingerprint
    }
}

function New-KeyFile([string]$Path, [string]$PublisherName) {
    if ((Test-Path -LiteralPath $Path) -and (-not $NewKey)) {
        Stop-With ('密钥文件已存在：' + $Path + '；确实要覆盖请自行删除。')
    }
    $Directory = Split-Path -Parent $Path
    if ($Directory -and -not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory | Out-Null
    }
    $Ecdsa = New-Object System.Security.Cryptography.ECDsaCng 256
    try {
        $Blob = $Ecdsa.Key.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)
        $Spki = Get-PublicSpki $Ecdsa
    } finally {
        $Ecdsa.Dispose()
    }
    $Record = [ordered]@{
        name = $PublisherName
        algorithm = 'ECDSA-P256-SHA256'
        key = [Convert]::ToBase64String($Blob)
        created = (Get-Date).ToString('s')
    }
    ($Record | ConvertTo-Json) | Set-Content -LiteralPath $Path -Encoding UTF8
    return [pscustomobject]@{ PublicKey = [Convert]::ToBase64String($Spki) }
}

function Open-PrivateKey([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Stop-With ('找不到私钥：' + $Path + '；先用 -NewKey -Name <发布者> 生成。')
    }
    $Record = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($Record.key)) { Stop-With ('私钥文件缺少 key 字段：' + $Path) }
    $Blob = [Convert]::FromBase64String($Record.key)
    $Cng = [System.Security.Cryptography.CngKey]::Import($Blob, [System.Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)
    return [pscustomobject]@{
        Ecdsa = (New-Object System.Security.Cryptography.ECDsaCng $Cng)
        Name = [string]$Record.name
    }
}

function Get-PublicSpki([System.Security.Cryptography.ECDsa]$Ecdsa) {
    $Blob = $Ecdsa.Key.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $Spki = New-Object byte[] ($Script:SpkiPrefix.Length + 64)
    [System.Array]::Copy($Script:SpkiPrefix, 0, $Spki, 0, $Script:SpkiPrefix.Length)
    [System.Array]::Copy($Blob, 8, $Spki, $Script:SpkiPrefix.Length, 64)
    return $Spki
}

function Import-PublicKey([string]$Base64Spki) {
    $Der = [Convert]::FromBase64String($Base64Spki)
    if ($Der.Length -lt 68) { throw '公钥太短，不是 P-256 SPKI。' }
    $Blob = New-Object byte[] 72
    $Blob[0] = 0x45; $Blob[1] = 0x43; $Blob[2] = 0x53; $Blob[3] = 0x31
    $Blob[4] = 32
    [System.Array]::Copy($Der, $Der.Length - 64, $Blob, 8, 64)
    $Cng = [System.Security.Cryptography.CngKey]::Import($Blob, [System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    return (New-Object System.Security.Cryptography.ECDsaCng $Cng)
}

function Get-PublishersPath([string]$ManifestPath, [string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) { return $ExplicitPath }
    $PluginDir = Split-Path -Parent $ManifestPath
    $Root = Split-Path -Parent $PluginDir
    return (Join-Path $Root 'publishers.json')
}

function Read-Publishers([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $Raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    return ($Raw | ConvertFrom-Json)
}

function Save-Publishers([string]$Path, $Object) {
    $Directory = Split-Path -Parent $Path
    if ($Directory -and -not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory | Out-Null
    }
    ($Object | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Register-Publisher([string]$Path, [string]$PublisherName, [string]$Base64Spki) {
    $Object = Read-Publishers $Path
    if ($null -eq $Object) { $Object = New-Object psobject }
    if ($Object.PSObject.Properties[$PublisherName]) {
        $Object.$PublisherName = $Base64Spki
    } else {
        $Object | Add-Member -NotePropertyName $PublisherName -NotePropertyValue $Base64Spki -Force
    }
    Save-Publishers $Path $Object
}

function Set-ManifestSignature([string]$ManifestPath, [string]$Signature) {
    $Text = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8)
    $Eol = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    if ($Text -match '"signature"\s*:') {
        $Text = [regex]::Replace($Text, '"signature"\s*:\s*"[^"]*"', '"signature": "' + $Signature + '"')
    } else {
        $Index = $Text.LastIndexOf('}')
        if ($Index -lt 0) { Stop-With '清单没有结尾的 }，无法写入签名。' }
        $Head = $Text.Substring(0, $Index).TrimEnd()
        $Tail = $Text.Substring($Index)
        $Text = $Head + ',' + $Eol + '    "signature": "' + $Signature + '"' + $Eol + $Tail
    }
    [System.IO.File]::WriteAllText($ManifestPath, $Text, (New-Object System.Text.UTF8Encoding($false)))
    # 写回后立刻解析一遍，避免手写插入破坏 JSON
    [void]([System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
}

function Get-PluginStatus($Manifest, [string]$PublishersPath) {
    $Sig2 = ''
    if ($null -ne $Manifest.Json.signature2 -and $null -ne $Manifest.Json.signature2.sig) {
        $Sig2 = ([string]$Manifest.Json.signature2.sig).Trim()
    }
    $Signature = ''
    if ($null -ne $Manifest.Json.signature) { $Signature = ([string]$Manifest.Json.signature).Trim() }
    if ([string]::IsNullOrWhiteSpace($Sig2) -and [string]::IsNullOrWhiteSpace($Signature)) { return 'Unsigned' }
    $PublishersObject = Read-Publishers $PublishersPath
    if ($null -eq $PublishersObject) { return 'UnknownPublisher' }
    $Entries = @($PublishersObject.PSObject.Properties)
    if ($Entries.Count -eq 0) { return 'UnknownPublisher' }

    $Payload = ''
    $SigText = ''
    if (-not [string]::IsNullOrWhiteSpace($Sig2)) {
        $Listed = @()
        if ($null -ne $Manifest.Json.signature2.files) { $Listed = @($Manifest.Json.signature2.files) }
        if ($Listed.Count -eq 0) { return 'Invalid' }
        $Normalized = @($Listed | ForEach-Object { ([string]$_).Replace('\', '/').ToLowerInvariant().TrimStart('/') })
        $EntryDll = ([string]$Manifest.Json.dll).Replace('\', '/').ToLowerInvariant().TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($EntryDll)) { return 'Invalid' }
        if ($Normalized -notcontains $EntryDll) { return 'Invalid' }
        $Digest = Get-ListedFilesDigest $Manifest $Normalized
        if ($null -eq $Digest) { return 'Invalid' }
        $Fingerprint = Get-PermissionFingerprint $Manifest.Json
        $Payload = ('PCLPLUGIN-SIG-V2' + "`n" + [string]$Manifest.Json.id + "`n" + [string]$Manifest.Json.version + "`n" + $Fingerprint + "`n" + $Digest)
        $SigText = $Sig2
    } else {
        $Payload = (Get-SignPayload $Manifest).Payload
        $SigText = $Signature
    }

    $Hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        $DigestBytes = $Hash.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Payload))
    } finally {
        $Hash.Dispose()
    }
    try {
        $SignatureBytes = [Convert]::FromBase64String($SigText)
    } catch {
        return 'Invalid'
    }
    foreach ($Entry in $Entries) {
        try {
            $Ecdsa = Import-PublicKey ([string]$Entry.Value)
            try {
                if ($Ecdsa.VerifyHash($DigestBytes, $SignatureBytes)) { return 'Signed' }
            } finally {
                $Ecdsa.Dispose()
            }
        } catch {
            # 某个公钥损坏不影响其它公钥
        }
    }
    return 'Invalid'
}
function Get-PluginDllFiles($Manifest) {
    $Root = Split-Path -Parent $Manifest.Path
    $Files = New-Object System.Collections.Generic.List[string]
    foreach ($F in Get-ChildItem -LiteralPath $Root -Recurse -Filter *.dll -File -ErrorAction SilentlyContinue) {
        $Rel = $F.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/').ToLowerInvariant()
        if ($Rel -match '(^|/)(bin|obj|\.backup)/') { continue }
        if ($Rel -match '^data/') { continue }
        $Files.Add($Rel)
    }
    $Files.Sort([System.StringComparer]::Ordinal)
    return ,$Files
}

function Get-SignedFilesInfo($Manifest) {
    $Root = Split-Path -Parent $Manifest.Path
    $Files = Get-PluginDllFiles $Manifest
    if ($Files.Count -eq 0) { Stop-With '插件目录里没有 DLL，无法生成 v2 签名。' }
    $Builder = New-Object System.Text.StringBuilder
    foreach ($Rel in $Files) {
        $Full = Join-Path $Root ($Rel.Replace('/', '\'))
        $Sha = Get-Sha256Hex ([System.IO.File]::ReadAllBytes($Full))
        [void]$Builder.Append($Rel + "`n" + $Sha + "`n")
    }
    $Digest = Get-TextSha256Hex $Builder.ToString()
    return [pscustomobject]@{ Files = $Files; Digest = $Digest }
}

function Get-ListedFilesDigest($Manifest, [string[]]$Listed) {
    $Root = Split-Path -Parent $Manifest.Path
    $Sorted = @($Listed | Sort-Object -CaseSensitive)
    $Builder = New-Object System.Text.StringBuilder
    foreach ($Rel in $Sorted) {
        if ([string]::IsNullOrWhiteSpace($Rel) -or $Rel.Contains('..') -or $Rel.StartsWith('/')) { return $null }
        $Full = Join-Path $Root ($Rel.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $Full -PathType Leaf)) { return $null }
        $Sha = Get-Sha256Hex ([System.IO.File]::ReadAllBytes($Full))
        [void]$Builder.Append($Rel + "`n" + $Sha + "`n")
    }
    return (Get-TextSha256Hex $Builder.ToString())
}

function Get-V2Payload($Manifest, [string]$FilesDigest) {
    $Json = $Manifest.Json
    $Fingerprint = Get-PermissionFingerprint $Json
    return ('PCLPLUGIN-SIG-V2' + "`n" + [string]$Json.id + "`n" + [string]$Json.version + "`n" + $Fingerprint + "`n" + $FilesDigest)
}

function New-V2Signature($Key, $Manifest) {
    $Info = Get-SignedFilesInfo $Manifest
    $Payload = Get-V2Payload $Manifest $Info.Digest
    $Hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        $Digest = $Hash.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Payload))
    } finally {
        $Hash.Dispose()
    }
    $Signature = [Convert]::ToBase64String($Key.Ecdsa.SignHash($Digest))
    return [pscustomobject]@{ Files = $Info.Files; Digest = $Info.Digest; Signature = $Signature }
}

function Set-ManifestSignature2([string]$ManifestPath, [string[]]$Files, [string]$Signature) {
    $Text = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8)
    $Eol = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $FileJson = (@($Files) | ForEach-Object { '"' + $_ + '"' }) -join ', '
    $Object = '{"files": [' + $FileJson + '], "sig": "' + $Signature + '"}'
    if ($Text -match '"signature2"\s*:') {
        $Text = [regex]::Replace($Text, '"signature2"\s*:\s*\{[^}]*\}', ('"signature2": ' + $Object))
    } else {
        $Index = $Text.LastIndexOf('}')
        if ($Index -lt 0) { Stop-With '清单没有结尾的 }，无法写入 v2 签名。' }
        $Head = $Text.Substring(0, $Index).TrimEnd()
        $Tail = $Text.Substring($Index)
        $Text = $Head + ',' + $Eol + '    "signature2": ' + $Object + $Eol + $Tail
    }
    [System.IO.File]::WriteAllText($ManifestPath, $Text, (New-Object System.Text.UTF8Encoding($false)))
    [void]([System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
}
# ---------------- 入口 ----------------

if ($NewKey) {
    if ([string]::IsNullOrWhiteSpace($Name)) { Stop-With '-NewKey 需要同时指定 -Name <发布者名>。' }
    $Result = New-KeyFile $KeyFile $Name
    Write-Ok ('私钥已写入 ' + $KeyFile + '（不要提交或外发）')
    Write-Host ('公钥（Base64 SPKI，放进 publishers.json 的 "' + $Name + '"）：')
    Write-Host $Result.PublicKey
    exit 0
}

if ($VerifyAll) {
    if ([string]::IsNullOrWhiteSpace($PluginsRoot)) { Stop-With '-VerifyAll 需要 -PluginsRoot <插件根目录>。' }
    $Root = (Resolve-Path -LiteralPath $PluginsRoot).Path
    $Manifests = @(Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'plugin.json' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($Manifests.Count -eq 0) { Stop-With ('没有在 ' + $Root + ' 下找到 plugin.json。') }
    $Invalid = 0
    $Rows = @()
    foreach ($ManifestPath in $Manifests) {
        $Manifest = Read-Manifest $ManifestPath
        $PublishersPath = Get-PublishersPath $ManifestPath $Publishers
        $Status = Get-PluginStatus $Manifest $PublishersPath
        if ($Status -eq 'Invalid') { $Invalid++ }
        $Rows += [pscustomobject]@{
            插件 = [string]$Manifest.Json.id
            版本 = [string]$Manifest.Json.version
            签名 = $Status
            目录 = (Split-Path -Leaf (Split-Path -Parent $ManifestPath))
        }
    }
    $Rows | Format-Table -AutoSize
    Write-Host ('公钥表：' + (Get-PublishersPath ($Manifests[0]) $Publishers))
    if ($Invalid -gt 0) { Write-Bad ($Invalid.ToString() + ' 个插件签名无效（宿主会拒绝加载）'); exit 1 }
    Write-Ok ('签名自检通过：' + $Rows.Count.ToString() + ' 个插件，没有 Invalid。')
    exit 0
}

if ($Verify) {
    $ManifestPath = Resolve-ManifestPath $Plugin
    $Manifest = Read-Manifest $ManifestPath
    $PublishersPath = Get-PublishersPath $ManifestPath $Publishers
    $Status = Get-PluginStatus $Manifest $PublishersPath
    $Info = Get-SignPayload $Manifest
    Write-Host ('插件：' + $Manifest.Json.id + ' ' + $Manifest.Json.version)
    Write-Host ('DLL SHA-256：' + $Info.DllSha)
    Write-Host ('权限指纹：' + $Info.Fingerprint)
    Write-Host ('公钥表：' + $PublishersPath)
    switch ($Status) {
        'Signed' { Write-Ok '签名有效（Signed）' }
        'UnknownPublisher' { Write-Host '[??] 签名来自未知发布者：公钥表不存在、为空，或没有匹配的公钥。' -ForegroundColor Yellow }
        default { Write-Bad '签名无效（Invalid）：文件或清单在签名之后被改过。' }
    }
    if ($Status -eq 'Invalid') { exit 1 } else { exit 0 }
}

# 默认行为：签名
$ManifestPath = Resolve-ManifestPath $Plugin
$Key = Open-PrivateKey $KeyFile
try {
    $Signature = ''
    $PayloadInfo = Get-SignPayload (Read-Manifest $ManifestPath)
    $Hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        $Digest = $Hash.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($PayloadInfo.Payload))
    } finally {
        $Hash.Dispose()
    }
    $Signature = [Convert]::ToBase64String($Key.Ecdsa.SignHash($Digest))
    Set-ManifestSignature $ManifestPath $Signature
    #v10.6：同时写 v2 签名（覆盖整包 DLL 清单），旧宿主仍验 v1
    $V2 = New-V2Signature $Key (Read-Manifest $ManifestPath)
    Set-ManifestSignature2 $ManifestPath $V2.Files $V2.Signature
    Write-Ok ('已写入 v2 签名（' + $V2.Files.Count.ToString() + ' 个 DLL，清单摘要 ' + $V2.Digest.Substring(0, 12) + '…）')

    Write-Host ('插件：' + (Read-Manifest $ManifestPath).Json.id)
    Write-Host ('DLL SHA-256：' + $PayloadInfo.DllSha)
    Write-Host ('权限指纹：' + $PayloadInfo.Fingerprint)
    Write-Ok ('已写入签名（' + $Signature.Length + ' 个字符的 Base64）')

    $PublishersPath = Get-PublishersPath $ManifestPath $Publishers
    $Spki = Get-PublicSpki $Key.Ecdsa
    $PublicKey = [Convert]::ToBase64String($Spki)
    if ($Register) {
        $PublisherName = $Name
        if ([string]::IsNullOrWhiteSpace($PublisherName)) { $PublisherName = $Key.Name }
        if ([string]::IsNullOrWhiteSpace($PublisherName)) { Stop-With '-Register 需要 -Name <发布者名>，或在密钥文件里写入 name。' }
        Register-Publisher $PublishersPath $PublisherName $PublicKey
        Write-Ok ('已登记公钥到 ' + $PublishersPath + '（发布者：' + $PublisherName + '）')
    } else {
        Write-Host '公钥（需要时放进 publishers.json，或加 -Register 自动登记）：'
        Write-Host $PublicKey
    }

    $Manifest = Read-Manifest $ManifestPath
    $Status = Get-PluginStatus $Manifest $PublishersPath
    if ($Status -eq 'Signed') { Write-Ok '自检：签名有效（Signed）' }
    else { Write-Bad ('自检：' + $Status + '（公钥还没登记到 ' + $PublishersPath + '？）') }
} finally {
    $Key.Ecdsa.Dispose()
}