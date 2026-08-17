param(
    [string]$Source = (Join-Path $PSScriptRoot '..\data\henesys-skills-2026-08-17.json'),
    [string]$Dictionary = (Join-Path $PSScriptRoot '..\枫语幕词库.tsv'),
    [string]$Cache = (Join-Path $PSScriptRoot '..\data\henesys-skills-zh.json'),
    [string]$Endpoint = 'http://127.0.0.1:17891/v1/chat/completions'
)

$ErrorActionPreference = 'Stop'

function Test-Chinese([string]$Text) {
    return -not [string]::IsNullOrWhiteSpace($Text) -and $Text -match '[\u3400-\u9fff]'
}

function Invoke-LocalTranslation([string]$System, [string]$User, [int]$MaxTokens = 420) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $body = @{
            model = 'local-qwen3'
            temperature = if ($attempt -eq 1) { 0.1 } else { 0.0 }
            top_p = 0.8
            max_tokens = $MaxTokens
            messages = @(
                @{ role = 'system'; content = $System }
                @{ role = 'user'; content = $User + "`n/no_think" }
            )
        } | ConvertTo-Json -Depth 8
        try {
            $reply = (Invoke-RestMethod -Uri $Endpoint -Method Post -ContentType 'application/json' `
                -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 60).choices[0].message.content
            $reply = ($reply -replace '(?s)<think>.*?</think>', '').Trim()
            if ($reply) { return $reply }
        } catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Milliseconds 400
        }
    }
    throw '本地模型未返回译文'
}

function Convert-JsonReply([string]$Reply) {
    $clean = $Reply.Trim() -replace '^```(?:json)?\s*', '' -replace '\s*```$', ''
    $start = $clean.IndexOf('{')
    $end = $clean.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) { throw "模型没有返回JSON：$Reply" }
    return $clean.Substring($start, $end - $start + 1) | ConvertFrom-Json
}

function Convert-ToNumberTemplate([string]$Text) {
    $script:index = 0
    return [regex]::Replace($Text, '\d+(?:\.\d+)?', {
        param($match)
        $script:index++
        return '{n' + $script:index + '}'
    })
}

function Expand-NumberTemplate([string]$English, [string]$ChineseTemplate) {
    $values = [regex]::Matches($English, '\d+(?:\.\d+)?') | ForEach-Object { $_.Value }
    $expanded = $ChineseTemplate
    for ($i = $values.Count; $i -ge 1; $i--) {
        $expanded = $expanded.Replace('{n' + $i + '}', $values[$i - 1])
    }
    return $expanded
}

$skills = Get-Content -LiteralPath $Source -Raw -Encoding UTF8 | ConvertFrom-Json
$dvgPath = Join-Path $PSScriptRoot '..\data\dvg-079-skills-2026-08-17.json'
$dvgById = @{}
if (Test-Path -LiteralPath $dvgPath) {
    foreach ($row in (Get-Content -LiteralPath $dvgPath -Raw -Encoding UTF8 | ConvertFrom-Json)) {
        $dvgById[[string]$row.skillID] = [string]$row.skillName
    }
}
$canonicalNames = @{
    'Maple Warrior' = '冒险岛勇士'; 'Power Stance' = '稳如泰山'; "Hero's Will" = '勇士的意志'
    'Power Guard' = '伤害反击'; 'Ice Charge' = '寒冰充能'; 'Fire Charge' = '火焰充能'
    'Lightning Charge' = '雷电充能'; 'Charged Blow' = '属性攻击'; 'Blunt Weapon Booster' = '快速钝器'
    'Advanced Combo Attack' = '进阶斗气'; 'Achilles' = '阿基里斯'; 'Guardian' = '寒冰掌'
    'Monster Magnet' = '磁石'; 'Brandish' = '轻舞飞扬'; 'Enrage' = '葵花宝典'
    "Heaven's Hammer" = '圣域'; 'Berserk' = '恶龙附身'
}
$existingByEnglish = @{}
$existingBySkillId = @{}
foreach ($line in Get-Content -LiteralPath $Dictionary -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    $parts = $line.Split("`t")
    if ($parts.Count -lt 2) { continue }
    $existingByEnglish[$parts[0].Trim().ToLowerInvariant()] = $parts[1].Trim()
    if ($parts.Count -ge 3 -and $parts[2] -match '^怀旧服-技能#(\d+)$') {
        $existingBySkillId[$matches[1]] = $parts[1].Trim()
    }
}

$cacheObject = if (Test-Path -LiteralPath $Cache) {
    Get-Content -LiteralPath $Cache -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    [pscustomobject]@{ skills = @(); templates = @() }
}
$translatedSkills = @{}
foreach ($row in @($cacheObject.skills)) { $translatedSkills[[string]$row.id] = $row }
$translatedTemplates = @{}
foreach ($row in @($cacheObject.templates)) { $translatedTemplates[[string]$row.english] = [string]$row.chinese }

$skillSystem = '你是冒险岛国际服怀旧版的简体中文本地化译者。准确翻译技能名和完整机制说明，使用冒险岛常用术语：Weapon/Magic Defense=物理/魔法防御，Accuracy=命中率，Evasion=回避率，Mastery=熟练度，Critical Rate/Damage=暴击率/暴击伤害，Attack Power=攻击力，Movement Speed=移动速度，Freeze=冻结，Stun=眩晕，Seal=封印，Meso=金币。不得删减条件、概率、持续时间或限制；语言自然简洁。只输出严格JSON：{"nameZh":"...","descriptionZh":"..."}。'

$done = 0
foreach ($skill in $skills) {
    $id = [string]$skill.id
    $officialName = ''
    if ($canonicalNames.ContainsKey([string]$skill.name)) { $officialName = $canonicalNames[[string]$skill.name] }
    elseif (([int]$skill.jobId % 10) -eq 2 -and $dvgById.ContainsKey($id)) { $officialName = $dvgById[$id] }
    if ($translatedSkills.ContainsKey($id)) {
        if ($officialName) { $translatedSkills[$id].nameZh = $officialName }
        $done++; continue
    }
    $preferred = if ($existingBySkillId.ContainsKey($id)) { $existingBySkillId[$id] }
        elseif ($existingByEnglish.ContainsKey($skill.name.ToLowerInvariant())) { $existingByEnglish[$skill.name.ToLowerInvariant()] }
        else { '' }
    $hint = if ($preferred) { "`n旧版可参考中文名：$preferred（仅在含义一致时沿用）" } else { '' }
    $user = "职业：$($skill.jobName)`n英文技能名：$($skill.name)$hint`n英文说明：$($skill.description)"
    $parsed = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $parsed = Convert-JsonReply (Invoke-LocalTranslation $skillSystem $user 460)
            if ((($officialName -and (Test-Chinese $parsed.descriptionZh)) -or
                ((Test-Chinese $parsed.nameZh) -and (Test-Chinese $parsed.descriptionZh)))) { break }
            $parsed = $null
        } catch { $parsed = $null }
    }
    if ($null -eq $parsed) {
        $plainDesc = Invoke-LocalTranslation '把冒险岛技能说明完整翻译成自然准确的简体中文，不得删减机制，只输出译文。' ([string]$skill.description) 420
        $plainName = $officialName
        if (-not $plainName) {
            $plainName = Invoke-LocalTranslation '把冒险岛技能名称翻译成简洁的简体中文技能名，只输出中文名称。' ([string]$skill.name) 60
        }
        if ((Test-Chinese $plainDesc) -and (Test-Chinese $plainName)) {
            $parsed = [pscustomobject]@{ nameZh = $plainName; descriptionZh = $plainDesc }
        } else { throw "技能翻译失败：$id $($skill.name)" }
    }
    $translatedSkills[$id] = [pscustomobject]@{
        id = [int]$skill.id; name = [string]$skill.name
        nameZh = if ($officialName) { $officialName } else { [string]$parsed.nameZh }
        description = [string]$skill.description; descriptionZh = [string]$parsed.descriptionZh
        jobName = [string]$skill.jobName
    }
    $done++
    $cacheObject = [pscustomobject]@{
        skills = @($translatedSkills.Values | Sort-Object id)
        templates = @($translatedTemplates.GetEnumerator() | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ english = $_.Key; chinese = $_.Value }
        })
    }
    $cacheObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Cache -Encoding UTF8
    Write-Host ("技能 {0}/{1}: {2} -> {3}" -f $done, $skills.Count, $skill.name, $parsed.nameZh)
}

$allEffects = @($skills | ForEach-Object { @($_.levels) | ForEach-Object { [string]$_.effect } } | Where-Object { $_ })
$templates = @($allEffects | ForEach-Object { Convert-ToNumberTemplate $_ } | Sort-Object -Unique)
$templateSystem = '你是冒险岛怀旧服数值词条本地化译者。把英文技能等级效果翻成简洁准确的简体中文；保留并原样输出所有{n1}、{n2}等占位符，不得增删、合并或改名。术语：Damage=伤害，Attack Power=攻击力，Weapon/Magic Defense=物理/魔法防御，Accuracy=命中率，Evasion=回避率，Mastery=熟练度，Critical Rate/Damage=暴击率/暴击伤害，sec=秒，Meso=金币。只输出译文，不解释。'
$templateDone = 0
foreach ($template in $templates) {
    if ($translatedTemplates.ContainsKey($template)) { $templateDone++; continue }
    $expected = @([regex]::Matches($template, '\{n\d+\}') | ForEach-Object { $_.Value })
    $translated = ''
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $candidate = (Invoke-LocalTranslation $templateSystem $template 220).Trim().Trim('"')
        $actual = @([regex]::Matches($candidate, '\{n\d+\}') | ForEach-Object { $_.Value })
        if ((Test-Chinese $candidate) -and ((@($expected | Sort-Object) -join '|') -eq (@($actual | Sort-Object) -join '|'))) {
            $translated = $candidate; break
        }
    }
    if (-not $translated) {
        $sentinel = $template
        for ($i = $expected.Count; $i -ge 1; $i--) { $sentinel = $sentinel.Replace('{n' + $i + '}', [string]($i * 11)) }
        $candidate = Invoke-LocalTranslation '你必须把英文冒险岛技能效果翻译成简体中文。所有数字原样保留，必须出现中文，禁止照抄英文，只输出一行中文译文。' ($sentinel + "`n只输出中文译文。") 220
        $valid = Test-Chinese $candidate
        for ($i = 1; $i -le $expected.Count; $i++) {
            $number = [string]($i * 11)
            if (-not $candidate.Contains($number)) { $valid = $false; break }
            $candidate = $candidate.Replace($number, '{n' + $i + '}')
        }
        if ($valid) { $translated = $candidate.Trim() }
    }
    if (-not $translated) { throw "等级词条翻译失败：$template" }
    $translatedTemplates[$template] = $translated
    $templateDone++
    $cacheObject = [pscustomobject]@{
        skills = @($translatedSkills.Values | Sort-Object id)
        templates = @($translatedTemplates.GetEnumerator() | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ english = $_.Key; chinese = $_.Value }
        })
    }
    $cacheObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Cache -Encoding UTF8
    Write-Host ("等级模板 {0}/{1}" -f $templateDone, $templates.Count)
}

$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($line in Get-Content -LiteralPath $Dictionary -Encoding UTF8) {
    $parts = $line.Split("`t")
    if ($parts.Count -ge 3) { [void]$seen.Add(($parts[0].Trim().ToLowerInvariant() + "`t" + $parts[2].Trim())) }
}
$append = New-Object 'System.Collections.Generic.List[string]'
foreach ($skill in $skills) {
    $translated = $translatedSkills[[string]$skill.id]
    $categoryName = '怀旧服-技能#' + $skill.id
    $categoryText = '怀旧服-技能说明#' + $skill.id
    $nameKey = $skill.name.Trim().ToLowerInvariant() + "`t" + $categoryName
    if ($seen.Add($nameKey)) {
        $append.Add(([string]$skill.name).Replace("`t", ' ') + "`t" + ([string]$translated.nameZh).Replace("`t", ' ') + "`t" + $categoryName + "`tHenesys.gg当前技能数据")
    }
    if ($skill.description) {
        $descriptionKey = $skill.description.Trim().ToLowerInvariant() + "`t" + $categoryText
        if ($seen.Add($descriptionKey)) {
            $append.Add(([string]$skill.description).Replace("`t", ' ') + "`t" + ([string]$translated.descriptionZh).Replace("`t", ' ') + "`t" + $categoryText + "`tHenesys.gg当前技能说明")
        }
    }
    foreach ($level in @($skill.levels)) {
        if (-not $level.effect) { continue }
        $english = [string]$level.effect
        $template = Convert-ToNumberTemplate $english
        $chinese = Expand-NumberTemplate $english $translatedTemplates[$template]
        $effectKey = $english.Trim().ToLowerInvariant() + "`t" + $categoryText
        if ($seen.Add($effectKey)) {
            $append.Add($english.Replace("`t", ' ') + "`t" + $chinese.Replace("`t", ' ') + "`t" + $categoryText + "`tHenesys.gg等级" + $level.level)
        }
    }
}
if ($append.Count -gt 0) {
    $outputLines = New-Object 'System.Collections.Generic.List[string]'
    $outputLines.Add('# Henesys.gg 当前怀旧服技能数据快照：2026-08-17；完整技能名、说明及各等级效果')
    $outputLines.AddRange($append)
    Add-Content -LiteralPath $Dictionary -Value $outputLines -Encoding UTF8
}
Write-Host "完成：技能 $($skills.Count)，等级效果 $($allEffects.Count)，新增词库行 $($append.Count)。"
