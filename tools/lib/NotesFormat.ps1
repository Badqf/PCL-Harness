# 发布说明的格式规则（历史 51 个 Release 一律是「- 类型：一句话」的中文清单）。
# 独立成文件是为了能被 tools\tests\notes-format.tests.ps1 直接 dot-source 测试。
function Get-NotesFormatProblems {
    param([string[]]$Lines)
    $kinds = '新增|优化|修复|变更|移除|说明|调整'
    $bad = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Lines -or @($Lines).Count -eq 0) {
        $bad.Add('发布说明不能为空：用 -Notes 给出一到数行「- 类型：一句话」')
        return $bad
    }
    if (($Lines -join "`n") -match '详见\s*docs|完整更新日志') {
        $bad.Add('不要自己写「详见 docs\更新记录.md …」，脚本会自动追加这一行')
    }
    foreach ($line in $Lines) {
        $s = ([string]$line).Trim()
        if ($s -eq '') { continue }
        if ($s -match '^\s{0,3}#{1,6}\s') { $bad.Add("不要用 Markdown 标题：$s"); continue }
        if ($s -match '\*\*|__|`|!\[|\]\(|^>|^\|') { $bad.Add("不要用 Markdown 强调/链接/引用/表格：$s"); continue }
        if ($s -match '[\uD800-\uDBFF][\uDC00-\uDFFF]') { $bad.Add("不要用 emoji：$s"); continue }
        if ($s -match "(?<!^)(新增|优化|修复|变更|移除|说明|调整):") { $bad.Add("类型词后面用全角冒号「：」，不是半角：$s"); continue }
        if (-not ($s -match "^[-·]\s*($kinds)：")) { $bad.Add("每行以「- 类型：」开头（类型限 $kinds）：$s"); continue }
        if ($s.Length -gt 240) { $bad.Add("单行过长（$($s.Length) 字），拆成两条：$($s.Substring(0, 24))…") }
    }
    $bad
}

function Assert-NotesFormat {
    param([string[]]$Lines)
    $problems = Get-NotesFormatProblems -Lines $Lines
    if (@($problems).Count -gt 0) {
        $problems | ForEach-Object { Write-Host "      FAIL $_" -ForegroundColor Red }
        throw "发布说明格式不符合历史约定（共 $(@($problems).Count) 处），已中止。参考：gh release view v3.9.7"
    }
}
