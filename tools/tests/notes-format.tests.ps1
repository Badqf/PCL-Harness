# 发布说明格式闸门的单元测试：dot-source 规则文件，逐条断言。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\tests\notes-format.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\NotesFormat.ps1')

$failed = 0
function Check([string]$name, [string[]]$lines, [bool]$shouldPass) {
    $problems = @(Get-NotesFormatProblems -Lines $lines)
    $passed = ($problems.Count -eq 0)
    if ($passed -eq $shouldPass) {
        Write-Host ("      OK  " + $name) -ForegroundColor DarkGray
    } else {
        $script:failed++
        Write-Host ("      FAIL " + $name + " -> " + ($problems -join ' | ')) -ForegroundColor Red
    }
}

Write-Host '[1] 合规格式' -ForegroundColor Cyan
Check '单条修复'      @('- 修复：菜单收起后按钮底色不恢复。') $true
Check '多条混合'      @('- 新增：内嵌网页。','- 优化：窄窗口连续收缩。','- 修复：底色残留。') $true
Check '长句但未超限'  @('- 优化：' + ('很长的说明' * 12)) $true
Check '圆点前缀(3.9.x 旧写法)' @('· 优化：改由启动器直接下载。') $true

Write-Host '[2] 违规格式必须被拦下' -ForegroundColor Cyan
Check 'Markdown 标题'  @('## 更新内容') $false
Check '三级标题'       @('### 更新内容') $false
Check '加粗'           @('- 修复：**重要** 的修复') $false
Check '半角冒号'       @('- 修复: 冒号写错了') $false
Check '缺少类型词'     @('- 菜单收起后底色不恢复') $false
Check '未知类型词'     @('- 重构：内部整理') $false
Check 'emoji'          @('- 修复：修好了 😀') $false
Check '自己写详见'     @('- 修复：x','· 详见 docs\更新记录.md 与完整更新日志') $false
Check '超长单行'       @('- 优化：' + ('字' * 260)) $false
Check '空输入'         @() $false

Write-Host '[3] 整体流程' -ForegroundColor Cyan
$release = Join-Path $PSScriptRoot '..\release.ps1'
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'   # 子进程把 stderr 写成 ErrorRecord，Stop 下会直接抛
$out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $release -Version 9.9.9 -DryRun -Notes '- 修复：格式闸门的用例。' 2>&1 | Out-String
$ErrorActionPreference = $prevEap
if ($out -match '格式不符合历史约定') {
    $failed++
    Write-Host '      FAIL 合规说明被闸门误拦' -ForegroundColor Red
} else {
    Write-Host '      OK  合规说明通过闸门（随后被 preflight 按版本号拦下，符合预期）' -ForegroundColor DarkGray
}
if ($out -match 'PREFLIGHT FAIL|预检') {
    Write-Host '      OK  脚本继续跑到 preflight（未被闸门挡住）' -ForegroundColor DarkGray
} else {
    $failed++
    Write-Host '      FAIL 没有跑到 preflight，流程被意外中断' -ForegroundColor Red
}

Write-Host ''
if ($failed -eq 0) { Write-Host 'NOTES-FORMAT TESTS PASS' -ForegroundColor Green; exit 0 }
Write-Host "NOTES-FORMAT TESTS FAIL ($failed)" -ForegroundColor Red
exit 1
