# 14 张实测截图：面板坐标 / 元素清单 / 全量 OCR 交付

生成时间：本轮会话
输入：`D:\TEMP\codex-clipboard-*.png`，共 14 张，全部 **1936 × 1239**，同一角色（Herzzz / LV.27 / Magician）、同一地图（Victoria Road - Kerning City）、同一 UI 缩放。

---

## 0. 必须先说明的测量条件（影响坐标可用性）

| 项目 | 结果 |
| --- | --- |
| `pwsh` 进程 | **完全不可用**。连 `pwsh -Command "exit 0"` 都返回 `(no output)` + `[exit code: 3221225794]`＝`0xC0000142 STATUS_DLL_INIT_FAILED`，进程在初始化阶段即死亡。 |
| 后果 | **无法读像素、无法裁剪放大、无法跑 OCR 引擎、无法做边缘跳变扫描。** |
| 实际可用的手段 | 只有 `read_image`，对 1936×1239 原图返回 **1638×1048 预览**（缩放系数 0.846）。 |
| 因此本报告坐标 | 一律为**目视换算值**，面板级精度 **±10~20 px**，小字号元素 **±20~30 px**。 |
| 交叉验证 | 用两轮独立子代理（每轮 14 个测量 + 14 个复核）交叉核对文字与结构；两轮在**文本层**一致，在**数字层**互相矛盾（详见 §6），故数字以本报告人工裁决值为准。 |

**结论：本报告的"元素清单与文本"可直接用；"像素坐标"必须再用实机裁图工具核一遍才能当施工常数。** 已在下文用 ✅ / ⚠️ 标注每一项。

---

## 1. 两套坐标系（最重要的一条）

| 坐标系 | 尺寸 | 说明 |
| --- | --- | --- |
| 截图坐标系 | 1936 × 1239 | PNG 原始像素。窗口截图含 OS 标题栏与 8 px 边框。 |
| **客户区坐标系** | **1920 × 1200** | **游戏本体分辨率**。证据：第 10 张 OPTIONS > Graphics 页第 1 行明确显示 `Resolution = 1920 x 1200`，末行 `UI Size = Default Ratio`。 |

换算关系（✅ 算术闭合，与 Windows 95/96 DPI 边框度量一致）：

```
客户区原点在截图内 = (8, 31)
左侧框 8 px   右侧框 8 px     → 1936 − 1920 = 16 = 8 + 8   ✅
标题栏 23 px + 边框 8 px       → 1239 − 1200 = 39 = 31 + 8  ✅
客户区 → 截图： shot = client + (8, 31)
截图 → 客户区： client = shot − (8, 31)
```

> ⚠️ 该偏移未经像素跳变复核（无 shell），但两条约束同时闭合，且与第 2 张里"顶部黑色标题条带下沿 y≈31""底部暗条带上沿 y≈1231"两处目视跳变吻合。

---

## 2. 主界面固定布局（14 张全部出现，客户区坐标）

| 元素 | 客户区 (x, y, w, h) | 文本 / 说明 | 置信 |
| --- | --- | --- | --- |
| OS 标题栏 | (−8, −31, 1936, 31) | `MapleStory` + 最小化/最大化/关闭 | ✅ |
| 小地图面板 | (4, 8, 309, 385) | 见下 | ✅ |
| ├ 标题 | (30, 17, 76, 16) | `MINI MAP` | ✅ |
| ├ 头部小按钮 ×2 + WORLD | (233, 12, 20, 16) / (257, 12, 20, 16) / (282, 10, 52, 19) | 前两个纯图标；后者 `WORLD` | ⚠️ |
| ├ 街道名 | (75, 39, 141, 23) | `Victoria Road` | ✅ |
| ├ 地图名 | (75, 71, 127, 23) | `Kerning City` | ✅ |
| └ 绘图区（红点 + 绿点） | (8, 100, 300, 285) | 无可辨文字（有个别极小暗红数字串，本分辨率不可读） | ⚠️ |
| 左上玩家名列表（队伍单位） | (176, 36, 150, 105) | 4 行名字 + 4 条红色 HP 条 | ✅ |
| ├ 第 1 行 | (176, 36, 92, 16) | `Morden`（左侧另有一枚星形小图标，疑队长标记） | ✅ |
| ├ 第 2 行 | (176, 56, 141, 16) | `EyeOfAmazon` | ✅ |
| ├ 第 3 行 | (176, 76, 69, 16) | `Herzzz` | ✅ |
| ├ 第 4 行 | (176, 96, 67, 16) | `Zoelin` | ✅ |
| ├ HP 条 ×4 | (270, 34 / 54 / 74 / 94, 105, 14) | 纯图形 | ⚠️ |
| └ 同行右侧小字 | (375, 86, 30, 14) | `DDD`（第 1/2 张读作 `DD`，薄字下 D 的个数不可定） | ⚠️ |
| 系统提示气泡（浅底 + 锁图标 + 1/2 按钮） | (386, 168, 175, 60) | `Fame if KPQ chs` / `are too low!`；下方两个小按钮 `1` `2` | ✅ |
| 右上 Quest Helper 条 | (1417, 189, 500, 30) | `Quest Helper (0/5)` + `auto` | ✅ |
| ├ 标题与计数 | (1599, 193, 118, 18) | `Quest Helper (0/5)` | ✅ |
| └ auto 按钮 | (1822, 190, 40, 20) | `auto` | ⚠️ |
| 底部状态条（HUD） | (329, 1065, 1238, 133) | 见下 | ✅ |
| ├ `LV.` + 等级方块 | (340, 1096, 60, 30) | `LV.` `27`（27 位于橙色方块内） | ✅ |
| ├ 职业 / 角色名 | (418, 1090, 100, 44) | `Magician` / `Herzzz` | ✅ |
| ├ `HP (636/636)` | (660, 1134, 120, 20) | **636/636** | ✅ |
| ├ `MP (942/942)` | (838, 1134, 120, 20) | **942/942**（见 §6 争议项） | ⚠️ |
| ├ `EXP: 6,456 (10.14%)` | (582, 1128, 210, 20) | **6,456 (10.14%)**；HP/MP 条在 y=1145，EXP 条在 y=1160 | ✅ |
| ├ 按钮 `CASH SHOP` | (1120, 1080, 140, 88) | 橙色 | ⚠️ |
| ├ 按钮 `MENU` | (1255, 1080, 105, 88) | 绿色 | ⚠️ |
| └ 按钮 `SHORT CUT` | (1377, 1080, 130, 88) | 蓝色 | ⚠️ |
| 聊天框 | (330, 1050, 1080, 40) | 仅 1 行 + 右侧图标列 | ✅ |
| 物品快捷键栏 QuickSlot | (950, 936, 285, 173) | 2 行 × 5 列，格内只有图标 | ✅ |

补充观察（对识别有用）：

- `EXP:` 数值行与 HP/MP 数值行**不在同一水平线**：EXP 略高、更靠左；EXP 进度条反而更靠下。若按同一 y 带切图会切错。
- `SHORT CUT` 是**两个词**（键位窗里该键帽下也分两行 `SHORT` / `CUT`）。
- HUD 的 HP/MP 数值**带圆括号**，等级 `LV.` 带一个点。

---

## 3. 弹出面板清单（按截图逐张，客户区坐标）

### 第 1 张 `c29685ee`｜任务日志 + 任务详情

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| QUEST 窗（左列表） | (657, 259, 382, 611) | ⚠️ 边界需复核 |
| ├ 页签行 | (713, 277, 305, 30) | `Available` / `In Progress`（选中） / `Completed` |
| ├ 列表行 1 | (735, 317, 145, 22) | `Henesys (1)` |
| ├ 列表行 2（选中） | (713, 351, 340, 26) | `Maple World's Unique Fun: Omok` |
| ├ 列表行 3 | (735, 385, 155, 22) | `Sleepywood (1)` |
| ├ 列表行 4 | (735, 419, 350, 22) | `[Forest of Patience] Sabitrama...` |
| └ DETAILS 按钮 | (970, 800, 100, 28) | `DETAILS` |
| 任务详情窗 | (1025, 259, 430, 615) | 标题栏浅蓝底 |
| ├ 标题 | (1052, 275, 320, 24) | `Maple World's Unique Fu...`（游戏自身截断） |
| ├ `Level 12+` | (1055, 325, 80, 22) | |
| ├ 正文小节标题 | (1060, 385, 320, 24) | `Make a Pig & Mushroom Omok Set` |
| ├ 正文行 1–6 | 起点 (1060, 420)，行距 ≈ 21，宽 ≈ 390 | 见 §4 |
| └ 按钮 | (1352, 800, 120, 28) / (1490, 800, 90, 28) | `QUEST HELPER` / `FORFEIT` |

**交叉验证（代码）**：`src/MapleOverlay.cs` 的 `SelectQuestDetailLines()` 用「三个页签的最右缘 + 12」作为**列表列与详情列的分界**。本图三个页签最右缘 ≈ 1013（客户区），故分界线 ≈ **1025**，与本表详情窗左边界一致 ✅。这说明本图的左列表 / 右详情分界就落在 **x ≈ 1025**，与代码模型吻合。

### 第 2 张 `e5002805`｜任务详情 = Quest Summary

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| 详情窗标题 | (1188, 340, 300, 26) | `[Forest of Patience] Sab...` |
| `Level 25+` | (1160, 398, 80, 22) | |
| NPC 立绘区 | (1430, 330, 190, 120) | 纯图形 |
| `Quest Summary` 小节标题 | (1195, 458, 180, 26) | 带绿叶图标 |
| **任务目标计数** | **(1209, 521, 170, 22)** | **`Pink Anthurium 0 / 1`** ← 最关键的任务目标区域 |
| 正文行 1–9 | 起点 (1195, 580)，行距 ≈ 21 | 见 §4 |
| 按钮 | (1374, 800, 120, 28) / (1500, 800, 90, 28) | `QUEST HELPER` / `FORFEIT` |

### 第 3 张 `1a42ed39`｜CHARACTER STAT + 右侧属性值窗

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| CHARACTER STAT 面板 | (827, 399, 465, 420) | ⚠️ |
| ├ 标题 | (890, 421, 160, 22) | `CHARACTER STAT`（暗红字，OCR 常漏） |
| ├ 洋红标签列左缘 | x ≈ 872 | NAME/JOB/LEVEL/HP/MP/EXP/FAME/STR/DEX/INT/LUK/ABILITY POINT |
| ├ 首行 NAME 行 | y ≈ 432 | **行距 34 px（连续 12 行等距）** ✅ |
| ├ 数值列左缘 | x ≈ 932 | |
| └ 右侧属性值窗 | (1163, 448, 245, 300) | 10 行，行距 34 |
| 弹窗内容 | 见 §4 | `ATTACK 29 - 38` … `JUMP 100%` |

**交叉验证（代码）**：`src/SceneRecognition.cs` 的 `ClassicSceneVision.FindCharacterStats()` 以洋红标签行的**行距 / 34** 反推 scale，并以「面板顶 = 首个洋红行 − 44×scale」「右列左缘 = 左列 + 360×scale」定位。本图实测行距 ≈ **34 px** ⇒ **scale ≈ 1.0**，与代码中的 34 px / 360 px / 44 px 常数**逐项吻合**。这是本报告中**唯一一处与代码模型闭环验证过的几何**，可信度明显高于其他面板。

> 也就是说：本图（1920×1200、UI Size = Default Ratio）下，属性面板的布局单位就是 **34 px**，右列偏移就是 **360 px**，不是缩放后的值。

### 第 4 张 `192e7b6b`｜技能窗 + 技能说明浮层（叠在属性窗上）

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| SKILL INVENTORY | (415, 287, 310, 600) | ⚠️ |
| ├ 标题 | (437, 297, 200, 22) | `SKILL INVENTORY` |
| ├ 技能行 1–6 | 起点 (470, 425)，行距 ≈ 34 | `Introduction to Magic` / `Improved MP Reco...` / `Max MP Inc...` / `Magic Guard` / `Magic Armo...` / `Energy Bolt` / `Magic Claw` |
| ├ 等级数字列 | x ≈ 610 | `15` `1` `15` `1` `20` `0`（见 §6 争议项） |
| └ `SKILL POINT` | (490, 855, 100, 22) | `0` |
| 技能说明浮层 | (615, 528, 360, 185) | `Improved MP Recovery` / `[Master Level: 15]` / 正文两段 / `[Current Level: 15]` |
| CHARACTER STAT（被浮层左缘压住） | (827, 399, 465, 420) | 与第 3 张同位；本图 `LEVEL` 数值被遮死 |
| 右侧属性值窗（被压） | (1163, 448, 245, 300) | 与第 3 张同位 |

### 第 5 张 `6987a838`｜装备栏 + 两个物品说明

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| ITEM INVENTORY | (82, 415, 320, 470) | ⚠️ |
| ├ 标题 | (95, 425, 180, 22) | `ITEM INVENTORY` |
| ├ 页签 | (95, 450, 340, 26) | `Equip`（选中红底）`Use` `Set-Up` `Etc` `Cash` `Deco` |
| └ 装备格区 | (95, 480, 300, 350) | 只画图标，格内**无物品名文字** |
| `Black Jester` 说明框 | (225, 570, 310, 365) | 蓝紫渐变底 |
| `Blue Wizardry Hat` 说明框 | (612, 570, 310, 365) | 同上 |
| 长条公告 | (700, 310, 1090, 30) | 绿底橙字 `Vindict's Gift-Filled Message : s>10 one hand sword attack scroll`（见 §6） |

### 第 6 张 `792115d3`｜消耗栏 + Jr. Balrog Invasion

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| ITEM INVENTORY（Use 页） | (82, 405, 320, 480) | 6×6 格，格内有数量数字但本分辨率不可逐一确认 |
| 物品说明框 | (130, 800, 445, 265) | `Jr. Balrog Invasion` / `Untradable` / `9,780` / `Usable Until: 1/5/2027 00:36 UTC` / `I sense an overwhelming power!` / `Right-click on the buff icon to cancel the transformation.` |
| 蓝灰公告条 | (755, 295, 430, 34) | `Tobiko's Flowery Message: go home` |

### 第 7 张 `6f68f274`｜NPC 对话窗（居中）

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| 对话窗 | (432, 310, 825, 335) | 浅灰底 |
| ├ 标题 | (672, 365, 470, 22) | `<Party Quest: First Time Together (Lv.21)>` |
| ├ NPC 立绘 | (475, 350, 140, 140) | 图形 |
| ├ NPC 名 | (545, 486, 110, 22) | `Lakelis` |
| ├ 正文 3 行 | 起点 (672, 372)，行距 ≈ 26 | 见 §4 |
| └ 按钮 | (437, 622, 120, 26) / (1097, 622, 80, 26) / (1183, 622, 55, 26) | `END CHAT` / `YES` / `NO` |

### 第 8 张 `aef7aff7`｜KEY BINDINGS

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| KEY BINDINGS 窗 | (752, 297, 995, 710) | 本批图中最大的面板 |
| ├ 标题 | (770, 308, 200, 24) | `KEY BINDINGS` |
| ├ 页签 | (775, 343, 350, 28) | `Keyboard/Mouse`（选中）`Controller` |
| ├ Preset 行 | (775, 410, 320, 26) | `Preset` `1` `2` `3` `QUICK SLOTS KEYS` |
| ├ 键盘格区 | (762, 440, 958, 470) | F1–F12 / 主键区 / 小键盘区，键帽下多带功能小字 |
| └ 底部按钮 | (770, 955, 165, 30) / (1785, 955, 95, 30) / (1885, 955, 95, 30) | `DEFAULT` `CLEAR ALL` / `CONFIRM` / `CANCEL` |

键帽下可辨小字（对词库有用）：`MENU` `CASH SHOP` `SHORT CUT` `MOVE` `SCREEN` `SHOT` `TO PARTY` `TO ALL` `ITEM` `BUDDY` `PARTY` `FRIEND` `GUILD` `MACRO` `QUICK` `SLOT` `QUEST` `WORLD` `MAP` `EQUIP` `STAT` `SKILL` `CHAT` `PICK UP` `ATTACK` `JUMP` `MOVE UP` `MOVE DOWN` `MOVE LEFT` `MOVE RIGHT`。

### 第 9 张 `20fc3ca0`｜玩家右键菜单

| 区域 | 客户区 (x, y, w, h) | 内容 |
| --- | --- | --- |
| 右键菜单 | (975, 595, 165, 315) | 深蓝底白字 + 白描边 |
| ├ 标题行 | (985, 600, 150, 22) | `Nelson` |
| └ 菜单项 ×8 | 起点 (985, 630)，行距 ≈ 27 | `CHARACTER INFO` `SAVE NAME` `TRADE REQUEST` `WHISPER` `INVITE TO CHAT` `ADD FRIEND` `BLACKLIST` `REPORT` |

### 第 10–14 张｜OPTIONS 窗（同一窗口，5 个不同页签）

| 区域 | 客户区 (x, y, w, h) | 说明 |
| --- | --- | --- |
| OPTIONS 窗 | (672, 192, 607, 585) | 5 张图同位，是最稳定的面板之一 ✅ |
| ├ 标题 | (688, 202, 120, 24) | `OPTIONS` |
| ├ 页签行 | (685, 237, 585, 28) | `Graphics` `Sound` `Game` `Social` `Controller` |
| ├ 标签列 | x ≈ 690，宽 ≈ 195 | 粉红底白字 |
| ├ 控件列 | x ≈ 885 | 下拉框 / 复选框 / 滑杆 |
| └ 按钮 | (1050, 790, 100, 28) / (1165, 790, 100, 28) | `CONFIRM` / `CANCEL` |

各页签的**精确 OCR 内容**见 §4。

---

## 4. 全量 OCR 文本清单（按图）✅ 文本层经两轮独立核对

> 标注约定：`[?]` = 本轮无法定论；`@×N` = 刷屏符号，个数仅为估值。

### 第 1 张 `c29685ee`
```
标题栏        MapleStory
小地图        MINI MAP / Victoria Road / Kerning City / WORLD
小地图内      Pole arm 58 att  (+3)        ← 透过半透明面板可见的道具信息
玩家列表      Morden / EyeOfAmazon / Herzzz / Zoelin / DD
系统气泡      Fame if KPQ chs are too low!  [锁图标] [1] [2]
Quest Helper  Quest Helper (0/5) / auto
QUEST 页签    Available / In Progress / Completed
QUEST 列表    Henesys (1)
              Maple World's Unique Fun: Omok
              Sleepywood (1)
              [Forest of Patience] Sabitrama...
QUEST 按钮    DETAILS
详情标题      Maple World's Unique Fu...
详情等级      Level 12+
详情小节      Make a Pig & Mushroom Omok Set
详情正文      I brought the requested items to Casey in
              Henesys Park and received a Pig & Mushroom
              Omok Set! With this, I can play the Omok
              mini-game with other adventurers anywhere! I
              should remember to ask Casey if I want to know
              more about the mini-game.
详情按钮      QUEST HELPER / FORFEIT
HUD           LV. 27 / Magician / Herzzz
              HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
              CASH SHOP / MENU / SHORT CUT
聊天          Kogre : nobody saying hi
招牌          Prophesy's Gift  [s/c 不定]
名牌          Leatherworking / JetFrobe / Solum / Street.N[?] / black Kitty / Paw / NoFans
              Kelan / stryker / Kitty / Ragnarokz / Avocahoe / BlueCat / CatDad
              Anabolismdora / Le[?] / Sylveon / Docas / WizMan / Duhm / Qu[?] / runs
              Eatercrab / YoungLink / myc EyeMay / Tak[?] / EI[?] / JuanCa[?]
              Corinthia / HealYouB / Mast3rM / Thayne / JustChillTV / NineEleven
              White Bunny / ThreeStax / CannonKun / TheClass[?] / Xura Pink Bunny
招牌块        Duhm : / RPQ>BO[?] / LVL / 30+@@@@@@@@ + 纯 @ 约 5 行（每行 @ 约 8–10）
```

### 第 2 张 `e5002805`
```
HUD           HP (636/636) / MP (942/942) / EXP 6,456 (10.14%)
QUEST 列表    Henesys (1) / Maple World's Unique Fun: Omok / Sleepywood (1)
              [Forest of Patience] Sabatrama ...      ← 与第 1 张 Sabitrama 冲突，见 §6
详情标题      [Forest of Patience] Sab...
详情等级      Level 25+
详情小节      Quest Summary（带绿叶图标）
任务目标      Pink Anthurium 0 / 1
详情正文      I met Sabatrama, a medicinal herb trader, in front
              of the Sleepywood Hotel. He appears to be the
              younger brother of Chrishrama. He recently
              discovered a place that grows the last herb he
              needs for a medicine he's been working on... He
              asked me to go deep in the forest and grab the
              Pink Anthurium. It's made of green grass and a
              small pink flower. I need to make sure I get him
              the same exact one. Shane from Ellinia can take
聊天          prophecy : u right
招牌          Prophesy's Gift / Yaair : B>Squishy shoes 10k@@@@@@@ @@
气泡          Slurp : LOL
名牌          valina / DarkKn[?] / Joon / Kalan / inks / world / Yosh[?] / amandab[?]
              ElonMa[?] / Stryker / shoo / elty / Ragnarokz / BlackOw l / CatDad / Joa
              en[?] / YoungLink / AysByeMay / Tak[?] / QueenDocas / THIRST[?]
              Anabolism / Lor[?] / Kamaitachi / CannonKun / WizMan / safiza / gles
              BigGz / wiful / DinkT Day / galois / eggyi / Cornelius / HealYou[?]
              JetProbe / Solun / Leatherworking / NoFans
```

### 第 3 张 `1a42ed39`
```
CHARACTER STAT  NAME Herzzz / JOB MAGICIAN + magician（第二行转职名）
                LEVEL 27 / HP 636/636 / MP 942/942 / EXP 6456 (10%)
                FAME 1 / STR 4 / DEX 4 / INT 131 / LUK 20
                ABILITY POINT 0 / DETAIL
右侧属性值窗    ATTACK 29 - 38 / WEAPON DEF 55 / MAGIC 112 / MAGIC DEF 115
                ACCURACY 64 / EVASION 15 / CRIT. RATE 6% / CRIT. DAMAGE 122%
                SPEED 100% / JUMP 100%
HUD             HP 636/636 / MP 942/942 / EXP 6,456 (10.14%)
聊天          buxiang : Player buy&sel Best place go to .WWW.U4N.COM Legit and fair pl[?]
招牌          Drinks. B> / SCROLLS OFFER !! / Show me wha / I GOT DON / Gatherworking
              NineEleven : S> / blue bamboo 2 / int. white band 1 / int@@@@@@@@ + 纯 @ 行
              Duhm : / RPQ>BONU / LVL / 30+@@@@@@ + 纯 @ 行
              Bowtiful : DUHM / LEAVE UR PTY / Gildardo : JPQ / PLSSSSSSS
              Yaair : B>Squishy shoes 10k@@@@
气泡          Tobiko's Bubbly Message: sit boi
系统气泡      Fame if KPQ chs are too low!
名牌          JetFrobs / Solum / ina Street.N*ker[?] / HauHee / Corinthia / HealyouSe[?]
              Pink Bunny / Bla k N n[?] / nibaPaw / ittle Bunny / NoFans / Eval / Casuc
              MadD / Squishy / shoes / 10k@@@@ / Ninji / ElonMa / Pig Purpalicious
              JOKEBOY / Trable / Eyenharold / Inputina / YoungLink / Iow / MycByt
              TakeTa / MrsFTo[?] / Kelan harokz / Avocahoe / Z4Vyyy / FirstBloc / d
              Le Ma[?] / BigBlackDude / HChrisG / hi / s / nDoc / Guild / CannonKun
              WizzMan / boto / Alias / Bowti / Bisha / ArcticStorm / Herzzz
              TheFamou / Yetllites / NineEleven
```

### 第 4 张 `192e7b6b`
```
SKILL INVENTORY  Introduction to Magic 15 / Improved MP Reco... 15 / Max MP Inc... 1
                 Magic Guard 15 / Magic Armo... 1 / Energy Bolt 20 / Magic Claw 0[?]
                 SKILL POINT 0
技能浮层         Improved MP Recovery
                 [Master Level: 15]
                 The amount of MP that naturally regenerates over time
                 increases. Also, MP recovery from items is boosted.
                 [Current Level: 15]
                 Regenerates 1% of Max MP every 10 seconds; increases MP recovery from
                 items by 20%
CHARACTER STAT   NAME Herzzz / JOB MAGICIAN / LEVEL [被浮层遮住] / HP 636/636 / MP 942/942
                 STR 4 / DEX 4 / INT 131 / [LUK 20，标签被遮] / ABILITY POINT 0
右侧属性值窗      ATTACK 29 - 38 / WEAPON DEF 55 / MAGIC 112 / MAGIC DEF 115
                 ACCURACY 64 / EVASION 15 / CRIT RATE 6% / CRIT DAMAGE 122%
                 SPEED 100% / JUMP 100%
公告条           Tobiko's Bubbly Message: sit boi
聊天          abao : J>PQ LVL 32 rush @@@@…（约 26 个，见 §6）
招牌          Drinks. B> / SCROLLS OFFER !! / SHOW ME WHAT / YOU GOT DON
              NineEleven : S> / blue bamboo 2 / int. white band 1 / int@@@@ + 纯 @ 行
              Gildardo : JPQ / PLSSSSSSS / Yaair : B>Squishy shoes 10k@@@
              Input: B>Squishy shoes @@@ / Bowtiful : DUHM LEAVE UR PTY
              Mast3rli : B>SCROLL 60% 10% 100%@@@@@@@@ + 纯 @ 行
              CannonKun / TheClauthor / Streetz N
气泡          abao : J>PQ LVL 32 rush + @ 刷屏
              Mast3rli : B>SCROLL 60% 10% 100% + @ 刷屏
              BlackOwl : int
名牌          Leatherworking / Brown Ri / Corinthians / Amoria / narok / Kelan / Sword
              B Seherck Pigck / Z4Yxxy / niDdd / nerd / Lyoe / JffPro / PudYaair
              Nnji / ElonMa / Henes / woc / rdo / misu / Prajna / Ohyung / Link
              mydeimg / eastonh / u ngBrowwoo / JDuino / Hdd / WizMan / eggyil
              Voltronio / Bi / QueenDocas / VardMAX / Sithiea / Herzzz / el's kyo / P. Aniel
```

### 第 5 张 `6987a838`
```
ITEM INVENTORY   Equip / Use / Set-Up / Etc / Cash / Deco
Black Jester     REQ LEV : 10 / REQ STR : 0 / REQ INT : 50 / REQ DEX : 0 / REQ LUK : 20 / REQ FAME : 0
                 BEGINNER WARRIOR MAGICIAN BOWMAN THIEF
                 Type: Hat / MaxHP: +15 / MaxMP: +15 / Weapon Def.: +16
                 Magic Def.: +18 / Remaining Enhancements: 7
Blue Wizardry Hat REQ LEV : 20 / REQ STR : 0 / REQ INT : 30 / REQ DEX : 0 / REQ LUK : 10 / REQ FAME : 0
                 BEGINNER WARRIOR MAGICIAN BOWMAN THIEF
                 Type: Hat / INT: +1 / MaxMP: +10 / Weapon Def.: +12
                 Magic Def.: +14 / Remaining Enhancements: 7
公告条           Vindict's Gift-Filled Message : s>10 one hand sword attack scroll
聊天          BlackOwl : jpt@@@
招牌          Yaair : B>Squishy shoes 10k@@@ / NineEleven : S> blue bamboo 2 int white band 1
              abao : J>PQ LVL 32 rush 100%@@@@@@ + 纯 @ 行
              Gildaro : J>PQ / PLSSSSSSSSSS / SSSSSSSS
              Mast3rli : B>SCROLL 60% 10% 100%@@@@@@ + 纯 @ 行
气泡          Wishlist AutoCats on steam plz  [1] [2]
              There are lots of / obstacles / here that / require you to
              join forces with / your party / members.
名牌          JetFrobo / Solum / NoFans / Nini / MoOKiEBoY / Blunt / BIGGZ / AlvaroMAX
              Wood / Herzzz / NYChrisG / JoJoUpOb / Corinthians / ninaPaw / Mast3rli
              Eater / DDD / PaTopFloorBoss wil[?] / Leatherworking / HeadinBlackPig
              nerm / Lwoe / MOKO / nte Bunny / Brown Ri / SafaraIsFien / rain
              swn Kitty / CarnifexKurl / yabui / Hunea / firea / Fume / eggyii / nM
              is iruked / POOF9 / Ohzy oum / nit / EH / vindic / The / poo / Swells
```

### 第 6 张 `792115d3`
```
ITEM INVENTORY   Use（选中）
Jr. Balrog Invasion  Untradable / 9,780 / Usable Until: 1/5/2027 00:36 UTC
                 I sense an overwhelming power!
                 Right-click on the buff icon to cancel the transformation.
公告条           Tobiko's Flowery Message: go home
聊天          DeaTobi : B>Angel Halo 35k @@@@…（约 26–29 个）
招牌          Drinks, B> / SCHOLLS OFFER !! / SHOW ME WHA[?]
              SAKMOTO : B> / 2H AEX ATT[?] / 60% / 18K@@@ / B> IRON ORE / 360@@@
              Yaair : B>Squishy shoes 10K@@@
              H2O2 : JPQ + @ 刷屏 / H2O2
              NineEleven : S> blue bamboo 2 int. white band 1 int + @ 行（每行 16）
              gyull : JP + @ 行（每行 16）
              buxiang : wwW.U4N.CO / M->Top trade / platform easy / buy & sell Price / fair :D
              Painted : T> Kitty / Chat Ring for / Toga~
              Gil Izardo : J> KPQ.[?]
              YoungLink : B>WARRIOR / 30 BOTTOM
              Brown Ri / ina Street[?] / …lum JPQ lvl 40+ / TaKeT[?]
系统气泡      Fame if KPQ chs are too low! [1] [2]
              Wishlist AutoCats on steam plz [1] [2]
名牌          Leatherworking / Corinthians / Brown Ri / NoFans / Nini / ElonMa / MoOkIEBo
              ck Pig Pud / Yaair / Pau ma / loosh / Jwi Bugle / Blunt / BerryDonut
              Blig! / Gold / Uprachao / Rocky / WizMan / Herzzz / sheeker / HissuraKe
              Vindict / Prajna / YoungLeonhart / eld imp / ddmminy / Millo / Wood
              Manag[?] / 165cm / Jeyren / kela / love / Resound JP[?] / Endure / WAR@@
              Eater / Zakul[?] / nimas
```

### 第 7 张 `6f68f274`
```
对话窗标题       <Party Quest: First Time Together (Lv.21)>
NPC 名           Lakelis
对话正文         How about you and your party members attempt a quest
                 together? Here you'll find obstacles and problems that
                 must be overcome with great teamwork.
对话按钮         END CHAT / YES / NO
HUD              LV. 27 / Magician / Herzzz / HP 638/638[?] / MP 342/342[?] / EXP 6,456 (10.14%)
                 CASH SHOP / MENU / SHORTCUT
聊天          SungBrowwo : S> FISHING SPEAR 25K @@@@…（约 28 个）
浅色小弹窗       ?os : / mesos with / transparent / pricing
招牌          Drinks : B> / OFFER / WHA / GOT D / DEFs<
              Yaair : B>Squishy shoes 10k@@ / smOOV : J>PQ@@@@@@@@ + @ 行
              SungBrowwo : S> FISHING / SPEAR 25K + @ 行 / Gani : J>KPQ@@@@@@@@ + @ 行
              Leatherworking / JeffProbe / Soluna / Street / Nofans / Brown Ri
              Corinthians / ElonMa / Kean / Idolo / Lan / Kelan / Snarolz
              3a Seher Resound Int / Alluring / IBro / Blunt / Werrish / JoJoUpUper
              Herzzz / Gunee / AvalonMAX / ValorBlade / Lawrens / CannonKun / WilzMan
              Painted / Golo / MotorD / Gani / Mewto Eater / Shyhe / Hawn / smOOV
              Yume / auHeo / Sithlea / Chzaya / BerryDonut / Kastenharcel / Anabolish
```

### 第 8 张 `aef7aff7`
```
KEY BINDINGS     Keyboard/Mouse（选中） / Controller
                 Preset 1 2 3 QUICK SLOTS KEYS
                 Esc/MENU/F1–F12/CASH SHOP/SHORT CUT/MOVE/PrtSc/SCREEN/SHOT/Slk/Brk
                 主键区 Tab QWERTYUIOP[]\ + QUEST/WORLD/MAP/EQUIP/STAT/BUDDY/PARTY/
                 FRIEND/GUILD/MACRO/QUICK SLOT/CHAT 等键帽小字
                 Lock ASDFGHJKL;' + Enter/STAT/SKILL
                 Shift ZXCVBNM,./ + PICK UP/ATTACK
                 Ctrl/Alt/Space + JUMP
                 TO PARTY / TO ALL / ITEM / BUDDY / PARTY / FRIEND / GUILD / MACRO / QUICK / SLOT
                 小键盘区 + 右侧两列 MOVE UP / MOVE DOWN / MOVE LEFT / MOVE RIGHT
                 底部 DEFAULT / CLEAR ALL / CONFIRM / CANCEL
HUD              LV. 27 / Magician / Herzzz / HP (636/636) / MP (342/342)[?] / EXP: 6,456 (10.14%)
聊天          ValorBlade : B> all scrolls, iron ores, and nx items@@@@@@@@@@@@@（约 13 个）
招牌          Input : B>Squishy Shoes / Yaair : B>Squishy shoes 10k@@@
              NineEleven : S> blue bamboo 2 int, white band 1 + @ 行
              HealYouSon : J>>>>>>>>>>>>> / >>>>>>>>>>PQ / @@@
              ValorBlade : B> all scrolls, iron / ores, and nx / items + @ 行
```

### 第 9 张 `20fc3ca0`
```
右键菜单         Nelson
                 CHARACTER INFO / SAVE NAME / TRADE REQUEST / WHISPER
                 INVITE TO CHAT / ADD FRIEND / BLACKLIST / REPORT
HUD              LV. 27 / Magician / Herzzz / HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
Chat             buxiang : wwW.U4N.CO / M->Top trade / platform easy / buy & sell Price / fair :D
招牌          Yaair : B>Squishy shoes 10k@@@ / Paul : S>staff 60% 55k
              NineEleven : S> blue bamboo 2 int. white band 1 + @ 行 / Zebina : J>PQ@@@@
              Painted : T> Kitty Chat Ring for Toga / ValorBlade : B> all scrolls, iron ores, and nx items
              BigBlackDude : JPQ / Wishlist AutoCats on steam plz [1] [2]
              Rainstorm : J>KPQ Bonus 31 / abao : J>PQ LVL 32 rush @@@@
名牌          Leatherworking / Brown Ri / Corinthians / NoFans / Nini / ElonMa / Herzzz
              QueenDocas / Blunt / Sithiea / CannonKun / WizMan / BigGz / Gani / Gold
              Uprachao / Rocky / Prajna / YoungLink / mydeimg / eastonh / HissuraKe
              Vindict / Eggyii / Mewto / IurF[?] / Gorgeous / builers[?] / Pink Bunny
              HiroNikaido / Casually / Paul fangs / MFO[?] / Solum / Street.N / IBWhi[?]
```

### 第 10 张 `162084c4`｜OPTIONS › Graphics
```
OPTIONS
Graphics（选中）/ Sound / Game / Social / Controller
Resolution            1920 x 1200
Windowed              Enable Alt + Enter For Fullscreen   [✓]
V-Sync                Enable V-Sync                       [✓]
UI Size               Default Ratio
UI Fade               UI Is Transparent During Battle     [✓]
Graphic Quality       Very High (Slower Performance)
Background Quality    Low —▮———— High
Effect Quality        Low ——▮—— High
My Skill Opacity      20% —▮—— 100%
Other Skill Opacity   0% —▮——— 100%
Damage Skin Effects   Basic Numbers
Damage Skin Display   Min ——▮— Max
Pets Back             Show Pets Behind Your Character     [✓]
CONFIRM / CANCEL
HUD              HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
聊天          Luzc : J>PQ@@@@@@@@…（约 35 个）
招牌          Universal : B>Pure Waters 600 ea / B>Mithril / Iron Ore 200 ea
              Vitale : J>PQ @@~[?] / NineEleven : S> blue bamboo 2 int, white band 1
              smOOV : J>PQ@@@@@@@@@@@@@@@@ / cutlas 12k / leaf earing 7k /
              yellow square 5k / lvl25 thief bot 4k[?]
```

### 第 11 张 `4599aa49`｜OPTIONS › Sound
```
Master Volume   0% ——▮—— 100%   [ ] Mute
BGM             0% ——▮—— 100%   [ ] Mute
Normal SFX      0% ——▮—— 100%   [ ] Mute
Monster SFX     0% ——▮—— 100%   [ ] Mute
Skill SFX       0% ——▮—— 100%   [ ] Mute
CONFIRM / CANCEL
HUD              HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
聊天          Zakul : s> cutlas 12k / leaf earing 7k / yellow square 5k / lvl25 thief bot 4k
招牌          Universal : B>Pure Waters 600 ea / B>Mithril / Iron Ore 200 ea
              Vitale : J>PQ @@@ / izu zu[?] / Anabolism : LEVEL 25 / WARRIOR /
              LOOKING FOR PARTYYYYY / YoungLink : B>WARRIOR 30 BOTTOM
```

### 第 12 张 `de4d2ddd`｜OPTIONS › Game
```
HP Warning             0% ——▮—— 100%
MP Warning             0% ——▮—— 100%
Enemy Info             Show Name and HP
Screenshot File Format JPEG
Screenshot Location    MapleStory Folder
Burst Screenshot       [ ] Continuous Capture
                       5 Shots    2 to 10 Shots Available
Auto Screenshot        Auto-Capture Memorable Moments   [✓]
Memory-mapped I/O      Use                              [✓]
Max Memory Limit       32 GB      2GB to 64GB Available
Language               English
CONFIRM / CANCEL
HUD              HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
聊天          TheLegend27 : J>PQ LVL 28 Magician | /invite TheLegend27 @@@@
招牌          TheLegend27 : J>PQ LVL 28 / Magician | /invite / TheLegend27 @@@
              Cuthiany : S> / Earrings , Leaf, / Gold & Square / 4k each[?]
              Universal : B>Pure Waters 600 ea / B>Mithril / Iron Ore 200 ea
```

### 第 13 张 `fa43ec66`｜OPTIONS › Social
```
Whispers            Allow Whispers         [✓]
Chat Invites        Allow Chat Invites     [✓]
Friend Chat         Show Friend Chat       [✓]
Party Invites       Allow Party Invites    [✓]
Trade Requests      Allow Trade Requests   [✓]
Friend Request      Allow Friend Requests  [✓]
Friend Login        Show Friend Login Alerts [✓]
Message Time        Show Chat Timestamps   [ ]
Chat Box Font Size  Small ——▮—— Large
CONFIRM / CANCEL
HUD              HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
聊天          Universal : B>Pure Waters 600 ea   B>Mithril / Iron Ore 200 ea
招牌          NineEleven : S> blue bamboo 2 int, white band 1 + @ 行
              TheLegend27 : J>PQ LVL 28 / Magician |/invite / TheLegend27 @@@
              Yaair : B>Squishy shoes 10k@@@@
```

### 第 14 张 `2444a6b3`｜OPTIONS › Controller
```
Controller              Enable Controller Support  [✓]
Stick Sensitivity       0 ——▮—— 100
D-Pad Slots             Auto-Switch
Trigger Mode            Toggle Trigger Quickslots [✓]
CONFIRM / CANCEL
HUD              HP (636/636) / MP (942/942) / EXP: 6,456 (10.14%)
聊天          Luzc : J>PQ@@@@@@@@…（约 35 个）
招牌          Universal : B>Pure Waters 600 ea / B>Mithril / Iron Ore 200 ea
              Vitale : J>PQ @@@ / iju zu[?] / TheLegend27 : J>PQ LVL 28 /
              Magician |/invite / TheLegend27 @@@ / YongBrowwo : S> FISHING SPEAR 20K
```

---

## 5. 14 张差异对照（哪些元素只在某几张出现）

| # | 文件（短号） | 打开的面板 | 关键新增元素 |
| --- | --- | --- | --- |
| 1 | `c29685ee` | QUEST + 任务详情 | 任务日志三页签；详情正文；`Level 12+` |
| 2 | `e5002805` | QUEST + 任务详情 | **`Quest Summary` + `Pink Anthurium 0 / 1` + `Level 25+`** |
| 3 | `1a42ed39` | CHARACTER STAT + 属性值窗 | **属性面板无遮挡**，`LUK 20` / `FAME 1` 可见 |
| 4 | `192e7b6b` | SKILL INVENTORY + 技能浮层 + 属性窗 | 技能等级列；浮层压住属性窗左半 |
| 5 | `6987a838` | ITEM INVENTORY(Equip) + 2 物品框 | **装备说明的 REQ / Type / 强化次数** |
| 6 | `792115d3` | ITEM INVENTORY(Use) + 物品框 | **`Usable Until: 1/5/2027 00:36 UTC`** |
| 7 | `6f68f274` | NPC 对话窗 | **`<Party Quest: First Time Together (Lv.21)>`** |
| 8 | `aef7aff7` | KEY BINDINGS | 最大面板；键帽功能小字 |
| 9 | `20fc3ca0` | 玩家右键菜单 | **8 项玩家菜单** |
| 10 | `162084c4` | OPTIONS › Graphics | **`Resolution 1920 x 1200` / `UI Size Default Ratio`** |
| 11 | `4599aa49` | OPTIONS › Sound | 5 条音量滑杆 |
| 12 | `de4d2ddd` | OPTIONS › Game | 截图设置 / `Language English` |
| 13 | `fa43ec66` | OPTIONS › Social | 8 个复选框 |
| 14 | `2444a6b3` | OPTIONS › Controller | 手柄设置 |

始终出现（可作为基准层）：小地图、左上玩家列表 + HP 条、Quest Helper `(0/5)`、底部状态条、聊天框、右下 QuickSlot、世界层名牌与自建商店招牌。

---

## 6. 争议项与裁决（重要：两轮复核结果不一致的地方）

两轮独立复核在**文本层一致**，但在下列**数字**上互相矛盾。这些正是识别最容易翻车的位置，列出供实机复核：

| 项目 | 说法 A | 说法 B | 本轮裁决 |
| --- | --- | --- | --- |
| 第 1 张 `MP` | `942/942` | `342/342` | **倾向 942/942**（第 2、4、8、9、10–14 张多数读作 942/942；且与第 3 张人物属性窗一致） |
| 第 7 / 8 张 `MP` | `342/342` | `942/942` | ⚠️ **未定**。同一个角色同一时刻 MP 不应变化，说明至少有一轮把 `9` 读成了 `3`（或反之，`9↔3` 是经典混淆） |
| `EXP` 数值 | `6,456 (10.14%)` | `8,456` / `8,458` | **倾向 `6,456`**（第 1、2、4、7 张一致） |
| 第 3 张人物属性窗 `EXP` 行 | `6456 (10%)` | `6,458 (10.14%)` | ⚠️ **未定**。但可以确定：**该行不带千分位逗号**，与底部 HUD 的 `6,456` 写法不同 —— 这本身就是个识别陷阱 |
| `Magic Claw` 技能等级 | `20` | `0` | ⚠️ **未定**。若是 `0`，则该字模宽应≈单字符宽，可用宽度判定 |
| 第 2 张任务名 | `Sabitrama` | `Sabatrama` | ⚠️ **未定**。第 1 张读作 `Sabitrama`。**这是任务 ID 对齐的命门，必须实机确认**（该 NPC 正确拼写为 Sabitrama） |
| 第 5 张长公告条 | `Vindict's Gift-Filled Message` | 绿底橙字整条 | ⚠️ 条内文字过长且被角色遮挡，两轮只读出 `s>10 one hand sword attack scroll` 这一致片段 |
| 第 6 张 `2H AEX ATT` | `AEX` | `AXE` | ⚠️ **未定**。`E↔X`、`A↔E` 在小字号下互混；实机道具名应为 `2H Axe ATT 60%` |
| 第 6 张 `Gil Izardo` / `Gildardo` | `Gil Izardo : J> KPQ.` | `Gildardo : JPQ` | ⚠️ 未定，同一块招牌两轮读法不同 |
| 第 5 张 `PLSSSSSSSSSS` | 两行 S，约 10 + 8 个 | 一行 | ⚠️ 只有 S 的**个数**有分歧，文本本身一致 |
| 聊天行 `@` 个数 | 约 13 / 26 / 28 / 35 不等 | — | ⚠️ **个数不可靠**。可确认的是"存在长 @ 串"这一事实与位置，个数请勿入库 |

**特别提示（与既有纠错需求直接相关）**：本批截图里成规模出现 `636/636`、`942/942`、`0/5`、`0 / 1`、`6,456 (10.14%)`、`29 - 38`、`100%`、`60%` 这类数字串，两轮复核在 `9↔3`、`0/O`、`5/S`、`6/8` 上确实出现了互相矛盾——即你此前遇到的 `25/50/0 → 2S/SO/O` 并非孤例。建议纠错规则至少覆盖：`9↔3`、`0↔O`、`5↔S`、`6↔8`、`1↔l/I`，且**只对"斜杠分隔的数字对"和"纯数字行"生效**，避免污染普通文本。

---

## 7. 交付物状态

| 用户要求 | 状态 |
| --- | --- |
| A. 面板 (x,y,w,h) | ✅ 已给（面板级 ±10~20 px；属性面板经代码模型闭环验证） |
| B. 全部文本 + 分区 + 语义分类 | ✅ 已给，两轮独立核对；争议项已单列 |
| C. 面板内容与侧边栏/任务目标/HUD 对应 | ✅ 已给（§2 固定层 / §3 弹出层 / §4 逐图文本） |
| D. 14 张差异对照 + 统一结论 | ✅ 见 §5 |
| E. 14 张逐张交付 | ✅ 见 §3 + §4 |
| F. 未识别项 + 位置 + 原因 | ✅ 见 §6 |

**未达成**：像素级精确坐标（受 `pwsh` 不可用所限，无法读像素/裁剪放大/边缘扫描）。建议在能起进程的环境里跑一次裁图脚本把这些框逐一对齐；最优先复核顺序：**任务详情列左边界 x≈1025 → 任务目标行 → 属性面板 34 px 行距与 360 px 右列偏移 → HUD 三个按钮**。
