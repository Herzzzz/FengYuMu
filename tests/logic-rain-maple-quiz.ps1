$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dictionary = Get-Content (Join-Path $repoRoot '枫语幕词库.tsv') -Encoding UTF8

$expected = @(
    @('1013', 'How do you climb a ladder or a rope?', '怎样爬上梯子或绳子？'),
    @('1013', 'Press the up arrow key while standing on it.', '站在梯子或绳子前按方向键上。'),
    @('1014', 'How do you put an item on?', '怎样穿戴一件装备？'),
    @('1014', 'Double-click it in your inventory.', '在背包里双击它。'),
    @('1015', 'Where do you see what you are wearing, and how do you take it off?', '在哪里查看已经穿戴的装备，又该怎样脱下来？'),
    @('1015', 'The Equipment Inventory - double-click a piece to unequip it.', '在装备栏中查看；双击装备即可脱下。'),
    @('1016', 'How do you recover HP?', '怎样恢复生命值？'),
    @('1016', 'Drink a potion, or sit down on a chair or bench and rest.', '喝药水，或者坐在椅子、长椅上休息。'),
    @('1017', 'What level do you need for your first job advancement?', '第一次转职需要达到多少级？'),
    @('1017', 'Level 10 - for Warrior, Magician, Bowman and Thief alike.', '10级——战士、魔法师、弓箭手和飞侠都一样。'),
    @('1018', 'What do you get for levelling up, and what is it for?', '升级后会获得什么？它有什么用途？'),
    @('1018', 'AP - spend it on the stats you want to raise.', '获得能力值点数（AP），可以加到想提升的属性上。'),
    @('1019', 'Where do you go to leave Maple Island?', '要从哪里离开冒险岛？'),
    @('1019', 'Southperry - the boat there sails to Victoria Island.', '前往南港，那里的船会驶往维多利亚岛。')
)

foreach ($entry in $expected) {
    $prefix = $entry[1] + [char]9 + $entry[2] + [char]9 + '怀旧服-任务对白#' + $entry[0] + [char]9
    $matches = @($dictionary | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        throw "瑞恩问答词条缺失或重复：任务#$($entry[0]) $($entry[1])"
    }
}

$quizQuestions = @(
    @('1013', 'What key should you press in front of a ladder or rope to hang from it?'),
    @('1014', 'Can you wear an item just by double-clicking it with your mouse?'),
    @('1015', "What window can you use to check the items you're wearing?"),
    @('1016', 'Which of the following is not a correct way to recover health?'),
    @('1017', 'In order to make the job advancement as a Warrior, Bowman, or Thief, you have to be at least level 10. What level do you have to be to make the job advancement as a Magician?'),
    @('1018', 'If you keep up with your adventures, you can level up and earn AP to boost your stats. Do you know how much AP you get with each level?'),
    @('1019', "This is important! You can only make the job advancement at Victoria Island, but you're currently on Maple Island. Where do you go to get a ride to Victoria Island?"),
    @('10301', "Who are you? I don't think we've met."),
    @('10301', 'Alex? Ha, I no longer have a son with that name. Get out of here!'),
    @('10301', "Eh? Confronting me now? Yelling at me in public? Hmm, you must be somebody important. Fine. How's Alex doing?"),
    @('10301', 'Ask him myself? Hahahaha! I like you. That makes me wonder. What do YOU think of ME?'),
    @('10301', "Yes, like you said, I'm the chief, but ever since Alex left, I've been losing face!"),
    @('10301', "You know THAT much? I suppose it's true that Alex has been more rebellious since Anna passed away. I've been busy with my chiefly duties, and perhaps I've neglected him. Maybe this is my fault..."),
    @('10315', 'Which of these monsters will you NOT see near Kerning City?'),
    @('10315', 'Which of these NPCs will you NOT see in Kerning City?'),
    @('10315', "Where are the places you can't go to by cab from Kerning City?"),
    @('10317', "Wouldn't it be incredible to swim in those clouds and fly your way through freedom? I wonder what it's like to fly, free as a bird. Even if it's only for a few minutes, I'd be more than happy to just be up there!"),
    @('10318', "Hey, you're the one that got me the materials for the hang glider..."),
    @('10319', "Hey, you're the one that got me the materials for the flying balloon..."),
    @('10320', "What's a human doing here? I have nothing to say to you. Please leave."),
    @('10320', "Hey, how does a human like you know about that? Well, even if you know of it, I don't think I feel like talking about it with someone like you!"),
    @('10320', 'What?!! A kid? Trust me, I am NOT a kid!! !!'),
    @('10320', "Whoa, you got them all? What's this? The leaves are dry! How am I going to make the Flying Medicine with crappy ingredients like this?"),
    @('10408', 'Can I... help you??'),
    @('10408', 'My mom sent you here? For what?'),
    @('10408', "I know, I have it. I'll have to check and see if you were actually sent by my mom, though. What's her name?"),
    @('10408', 'Where did you meet my mom?'),
    @('10408', "What's the color of her shoes?"),
    @('10408', "What's the color of the book that my mom holds?"),
    @('10408', 'Last question. What color are the earrings my mom wears?'),
    @('10701', "I'm sure you know this and all, but I'll still ask just in case. Which of these monsters will you not be seeing at Florina Beach?")
)

foreach ($entry in $quizQuestions) {
    $prefix = $entry[1] + [char]9
    $matches = @($dictionary | Where-Object {
        $_.StartsWith($prefix, [StringComparison]::Ordinal) -and
        $_.Contains(([char]9 + '怀旧服-任务对白#' + $entry[0] + [char]9))
    })
    if ($matches.Count -ne 1 -or -not $matches[0].Contains('<br>正确答案为：') -or
        -not $matches[0].Contains('（') -or -not $matches[0].Contains('）')) {
        throw "问答题提示缺失、重复或格式不完整：任务#$($entry[0]) $($entry[1])"
    }
}

$assemblyPath = Join-Path $repoRoot '枫语幕.exe'
$assembly = [Reflection.Assembly]::LoadFile($assemblyPath)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor($flags, $null, [Type[]]@([string]), $null)
$store = $ctor.Invoke(@([string](Join-Path $repoRoot '枫语幕词库.tsv')))
$storeType.GetMethod('Load', $flags).Invoke($store, @()) | Out-Null
$findDialogue = $storeType.GetMethod('FindDialogueTextMatches', $flags)
$runtimeMatches = @($findDialogue.Invoke($store, @([string]'Where did you meet my mom?')))
if ($runtimeMatches.Count -ne 1) { throw '运行时没有唯一命中问答题：Where did you meet my mom?' }
$entryField = $runtimeMatches[0].GetType().GetField('Entry', $flags)
$runtimeEntry = $entryField.GetValue($runtimeMatches[0])
$chinese = [string]$runtimeEntry.GetType().GetField('Chinese', $flags).GetValue($runtimeEntry)
$expectedRuntime = '你在哪里见到我妈妈的？' + [Environment]::NewLine + '正确答案为：魔法密林（Ellinia）'
if ($chinese -ne $expectedRuntime -or $chinese.Contains('<br>')) {
    throw "运行时问答换行或答案格式错误：$($chinese.Replace([Environment]::NewLine, '<NL>'))"
}

Write-Output "现版本15个问答任务页、31道题完整；运行时会另起一行显示：正确答案为：中文（English）"
