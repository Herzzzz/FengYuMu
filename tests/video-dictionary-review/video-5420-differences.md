# 视频 5420ff… 词库差异清单（TSV 修改前）

- 视频：`D:\Video\5420ff115e9d68f83d6826d48dfe1cec.mp4`
- 时长/画面：82.69 秒，1280×624
- 对照基线：2026-09-03 19:09:40 的磁盘现有 `枫语幕词库.tsv`（在原有未提交 5 行之后继续）
- 取证方法：Windows Media Composition 逐秒抽帧；任务窗裁剪放大；现有 `枫语幕.exe --benchmark` OCR；人工逐帧复核。
- 规则：视频中没有完整显示的文字只记录，不猜写进词库；在线资料仅用于确认被界面省略号截断的任务全名和任务 ID。

## 实际打开的任务详情（按出现顺序）

| 时间 | 任务名 | NPC/上下文 | 视频中可辨识英文 | 基线差异 |
|---:|---|---|---|---|
| 00s | Teo's Collection | Teo；In Progress；正文小标题为 `Teo's Weird Hobby` | `Teo in Lith Harbor told me his hobby is collecting weird things. He asked me to hunt Slimes near the town and bring back 1 Slime Bubble. He warned me that it might be hard to find, though!` | 缺任务标题别名；现有 #10010 有近义长句，但不是该实机原句 |
| 03s | [Event] Growing Leaf | Maple Administrator；Available | `The Maple Administrator in Henesys is handing out Pale Maple Leaf for a special event! Nurture yours and see what it becomes...` | 标题和短摘要均缺；资料 ID 500009 |
| 04–06s | First Greeting with Anne, the Town Resident | Community Board；Available | `It looks like Arthur, the town clerk, is looking for someone to help with their work. Let's go check out Community Board in front of Henesys Town Hall.` | 标题和短摘要均缺；资料 ID 506013 |
| 07s | Silas Irons in Need of an Apprentice | Silas Irons；Available | `The master blacksmith of Perion, Silas Irons, seems to be looking for someone worthy of becoming his apprentice.` | 标题已有 #80008；缺实机短摘要 |
| 09–12s | Vicious in Need of an Apprentice | Vicious；Available | `It seems Vicious, the carpenter of Henesys, is looking for someone worthy of becoming his apprentice.` | 标题已有 #80017；缺实机短摘要 |
| 13–14s | JM From tha Streetz Looking for a Partner! | JM From tha Streetz；Available | `The leatherworking master JM From tha Streetz of Kerning City seems to be looking for someone who could become his business partner.` | 现有标题缺叹号；#80020 长说明已有，缺实机短摘要 |
| 15–18s | Chrishrama in Need of an Apprentice | Chrishrama；Available | `The Arcforging master, Chrishrama of Sleepywood, seems to be searching for someone who could become his apprentice.` | 标题已有 #80023；缺实机短摘要 |
| 19–21s | Shadow of the World Tree | Grendel the Really Old；Available；正文小标题 `The Roots of Regret` | `Let's go see Grendel the Really Old in Ellinia. He seems deeply troubled by something...` | 标题、小标题和短摘要均缺；视频未显示资料 ID |
| 23s | Arwen and the Glass Shoe | Arwen the Fairy；Available | `Arwen the Fairy from Ellinia has lost something...` | 标题已有 #10200；缺实机短摘要 |
| 30–33s | I Need Help on My Homework! | Wing the Fairy；Available | `Whenever I walk past Wing the Fairy in Ellinia, he seems to be asking for help. I wonder why...` | 标题已有 #10210；缺实机短摘要 |
| 34s | Special Taste of Florina Beach I | Riel；Available | `A beautiful girl by the name of Riel from Florina Beach seems to be in need of help...` | 标题已有 #10700；缺实机短摘要 |
| 36s | Camila's Gem | Camila；Available | `I hear that Camila in Henesys is worried about something...` | 标题已有 #10105；缺实机短摘要 |
| 39s | Maya and the Weird Medicine | Maya；Available；正文小标题 `Maya of Henesys` | `I hear that Maya in Henesys is very sick...` | 现有 #10106 标题为 `Maya of Henesys`；缺实机标题别名和短摘要 |
| 42s | [Event] Beyond the Unknown | Arwen the Fairy；Completed | `I delivered the Glowing Mushroom from the cave entrance to Arwen the Fairy.` | 标题已有 #500001；缺完成态短摘要 |
| 46s | [Event] A New Adventure Awaits | Betty → Cherry；Completed | `You delivered Betty's research report to Cherry. Now that I've come all this way... why not hop aboard the ship bound for Ossyria?` | 标题和完成态短摘要均缺；资料 ID 500008 |
| 49s | The Bowman's Next Journey | Athena Pierce；Completed；正文小标题 `Proof of Qualification` | `Athena Pierce in Henesys welcomed me warmly, and I gained a new job, obtaining even greater power!` | 标题和 #20203 资格证明已有；缺该完成态原句 |
| 51s | The Magician's Next Journey | Grendel the Really Old；Completed；正文小标题 `Proof of Qualification` | `Grendel the Really Old in Ellinia welcomed me warmly, and I gained a new job, obtaining even greater power!` | 标题和 #20103 资格证明已有；缺该完成态原句 |
| 52s | The Thief's Next Journey | Dark Lord；Completed；正文小标题 `Proof of Qualification` | `Dark Lord in Kerning City welcomed me warmly, and I gained a new job, obtaining even greater power!` | 标题和 #20303 资格证明已有；缺该完成态原句 |
| 54s | The Warrior's Next Journey | Dances with Balrog；Completed；正文小标题 `Proof of Qualification` | `Dances with Balrog in Perion welcomed me warmly, and I gained a new job, obtaining even greater power!` | 标题和 #20003 资格证明已有；缺该完成态原句 |
| 56s | To Henesys, the Prairie Town | Arthur；Completed | `Arthur, the Town Clerk I met at Henesys Town Hall, suggested I become a resident of Henesys. Once I've made up my mind, I should speak with Arthur for more details.` | 标题和完成态摘要均缺；资料 ID 506000 |
| 60s | To the Gray City, Kerning City | City Clerk Roxy；Completed | `City Clerk Roxy, whom I met in Kerning City, suggested that I become a resident of Kerning City. Which city suits me better, Henesys or Kerning City? Once I make up my mind, I should speak with the City Clerk of that city for more details.` | 标题和完成态摘要均缺；资料 ID 506100 |
| 68s、77–81s | First Greeting with Pia, the Town Resident | Pia；Completed | `I greeted Pia, a resident of Henesys. I have a feeling we're going to get along great!` | 标题和完成态摘要均缺；资料 ID 506011 |
| 75s | Donating to Henesys | Arthur；Completed | `I gathered the items requested by Arthur, the town clerk, and brought them to him. I'm glad I could contribute to the development of our town.` | 标题和公共完成态摘要均缺；视频未显示具体捐赠物，不能判断 506019–506035 中的具体 ID |

## 只在任务列表中出现、未打开详情

这些名称可确认，但视频没有显示相应正文；不据此补写正文。

| 任务名 | 基线状态 |
|---|---|
| Olaf's Training | 仅列表可见；现有词库只有 `Olaf's Second Training`，名称不擅自合并 |
| Francois in Need of an Apprentice | 已有 #80014 |
| In Search of the Book of Ancient | 缺失；列表文字完整，但没有正文/ID |
| Alpha Platoon's Network of Communication | 缺失；列表末尾被省略号截断，不写入 |
| Father's Mushroom Stew | 现有任务说明正文中可见该短语，但没有任务名条目；视频未打开，不写入 |
| Luke the Security Guy | 已有 #10113 |
| [Event] Gift for the New Journey | 已有 #500006 |
| [Event] A Strange and Familiar Wood | 已有 #500000 |

## 不确定性边界

- 任务窗标题栏会用省略号截断长标题；`First Greeting with Anne/Pia, the Town Resident` 已用同一游戏版本的任务资料页核实全名与 ID，其他被截断且未能可靠核实的列表项不写入。
- `Donating to Henesys` 在资料库中有多个等级/物品版本。视频只显示共用完成句，没有显示具体捐赠物，因此只建立视频证据专用分组，不猜具体任务 ID。
- 词库增量只采用视频可辨识句子；不会把 OCR 错字写成新的“兼容词条”。OCR 回归只用于测量现有识别器在这些真实帧上的命中情况。

## 辅助核对来源

- https://meowdb.com/msclassic/quest-tracker/500008
- https://meowdb.com/msclassic/quest-tracker/500009
- https://meowdb.com/msclassic/quest-tracker/506000
- https://meowdb.com/msclassic/quest-tracker/506100
- https://meowdb.com/msclassic/quest-tracker/506011
- https://meowdb.com/msclassic/quest-tracker/506013
