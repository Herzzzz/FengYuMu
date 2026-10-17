# CHAT-001 结果回传模板

- 任务号：CHAT-001（第二次 NEEDS_CHANGES：R1 精确昵称、R2 保留行对齐修复轮）
- 提交人 / 工具：DeepSeek Harness（DSH）；Windows / PowerShell 7.6.5；.NET SDK 6.0.410 Roslyn
- 分支和提交 SHA：`dsh/chat-001`，实施提交 `690e2c1bb9011109ee12dc031f55406496d6d973`；结果文件提交见回传消息中的最终 SHA
- 代码基线是否与指令一致：是。基线 `c4dc19ea26e6a5611786ae7a6bd80acf33d6e858`；本分支在 `d05625e`（仅协作文件）之上实施，`src` 只改聊天分类/关联/队列/样式与构建工具，未改任务、装备、技能算法、AI 请求内容或 OCR 轮次
- 修改文件与修改原因：
  - `src/ChatColorEvidence.cs`：按行联合采样背景带与深色笔画；支持连续同色块中间行、顶底裁剪、蓝/粉相邻、缩放/轻微偏色和约 28% 场景纹理透出；深蓝/深粉笔画不再因亮度阈值被丢弃
  - `src/SimpleForms.cs`：新增 `ChatSourceCategory.Unknown`；R3 下无前景/背景/物理行证据时返回 Unknown，显式频道前缀仍优先；中性深灰不再被锚点误判为家族；粉/蓝广播带才进入喇叭
  - `src/MapleOverlay.cs`：第二次复验 R1：只有规范化昵称完全相等且正文完全相等才置 exact；`SameSpeaker` 仍可 OCR 容错关联，但不会因正文相等把模糊昵称升级为 exact。回归 `Alice`(Party)/`Alicf`(Guild) 同帧时，精确 `Alicf` 在单次和 occurrence API 都选 Guild，模糊 `Alic` 保持 Unknown。新增 `StyleForRow` 供 R2 按当前物理行取色
  - `src/ChatFrameContinuity.cs`：新增 `ResolveIndices`，与字符串版本共用确认状态，但返回当前行索引
  - `src/OfflineChat.cs`：R2 解析时产出当前物理行索引；行差分、连续性、OCR 复现抑制、来源变化全部按索引；队列携带 `RowIndex + Style`，发布使用 `Prepared.Style`。第二次复验 R2 删除了 same-text occurrence-from-start 的来源变化方法，改为 previous/current 有序动态规划对齐：区分 retained / new / true source change；retained duplicate 从候选剔除，未对齐新行保留，真来源变化补入，文本差分漏掉的同文本跨频道新消息补入
  - `src/build.ps1`：无 Visual Studio 2022 时回退到 .NET SDK Roslyn `csc.dll`，仍使用原 .NET Framework 4.8 引用程序集和同一组源文件
  - 测试：新增/加强 `logic-chat-megaphone-pixels.ps1`、`logic-chat-megaphone-pipeline.ps1`、`logic-chat-megaphone-ocr.ps1`；新增 Alice/Alicf、retained-duplicate scroll、new same-text cross-channel、true source-change 回归；更新 R3 期望、`ParseChatLines` 重载反射、反馈测试提取方法和过时源码断言
- EXE SHA-256：`0D0565BFBA9B851B84B423175B68DA75D99C088486845B3BAC0997AE69E2473F`
- 构建输入记录路径及哈希：`D:\GPT\文件\枫语幕\work\dsh-chat-001\枫语幕.exe.build.json`，SHA-256 `274A5C8561268597481F90892D36BB268B7F13173A2AB86750109303D6652EE9`；记录 64 个构建输入，`head=690e2c1bb9011109ee12dc031f55406496d6d973`，`worktree=[]`，编译器 `dotnet ...\sdk\6.0.410\Roslyn\bincore\csc.dll`
- 构建命令、环境、退出码：`pwsh -NoProfile -File src/build.ps1`；Windows，.NET Framework 4.8 引用程序集，Roslyn csc.dll；退出码 0
- 检查命令、各自退出码、结果文件（均在实施提交 `690e2c1` 的干净构建上重跑）：
  - `pwsh -NoProfile -File tests/logic-chat-megaphone-pixels.ps1` → 0；164 项；`.bench/evidence-r2-megaphone-pixels.log`
  - `pwsh -NoProfile -File tests/logic-chat-megaphone-pipeline.ps1` → 0；72 项；`.bench/evidence-r2-megaphone-pipeline.log`
  - `pwsh -NoProfile -File tests/logic-chat-megaphone-ocr.ps1` → 0；14 项；连续 3 次 0；`.bench/evidence-r2-megaphone-ocr.log`、`-run1.log`、`-run2.log`
  - `pwsh -NoProfile -File tests/logic-chat-category-filtering.ps1` → 0；`.bench/evidence-r2-chat-category-filtering.log`
  - `pwsh -NoProfile -File tests/logic-chat-continuity.ps1` → 0；9/9；`.bench/evidence-r2-chat-continuity.log`
  - `pwsh -NoProfile -File tests/logic-chat-feedback-correctness.ps1` → 0；39 项；`.bench/evidence-r2-chat-feedback-correctness.log`
  - `pwsh -NoProfile -File tests/logic-independent-window.ps1` → 0；`.bench/evidence-r2-independent-window.log`
  - `pwsh -NoProfile -File tests/logic-chat-ui-r15.ps1` → 0；20 项；`.bench/evidence-r2-chat-ui-r15.log`
  - `pwsh -NoProfile -File tests/logic-video-bv1l8yc67et4-chat.ps1 -ExePath .\枫语幕.exe` → 0；32 条；`.bench/evidence-r2-video-chat.log`
  - `pwsh -NoProfile -File tests/logic-ai-chat-performance.ps1 -ExePath .\枫语幕.exe` → 0；`.bench/evidence-r2-ai-chat-performance.log`
  - `pwsh -NoProfile -File tests/logic-chat-region-mouse-lifecycle.ps1 -ExePath .\枫语幕.exe` → 0；24/30/36/39/40px 生命期；`.bench/evidence-r2-region-mouse-lifecycle.log`
  - 既有环境敏感项：`tests/logic-ai-chat-accuracy.ps1` → 1（仅最后 `qq-220.0` 连续帧断言失败：该机 Windows OCR 把首行读成无冒号的 `Xx.AzNKiDxX• repot'`；`ParseChatLines` 文本解析逻辑未改，只增加行索引输出；前面解析/修正/词汇各项均通过）；`tests/logic-chat-region-first-use.ps1` → 1（既有源码断言 `RecognitionWasCancelled(automatic, requestId, generation)` 在基线和本提交均不存在，与本改动无关）
- 旧版失败 → 新版通过的对照：
  | 样本/行 | v4.0 分类 | 新版分类 | 新版证据 |
  |---|---|---|---|
  | pink (16,13,820,28) 第一行 | System，band=false | SuperMegaphone / Megaphone | PinkBroadcast，cov=0.651，row=0.559，inkMatch=true |
  | pink (16,43,550,28) 中间行 | System，band=false | SuperMegaphone / Megaphone | PinkBroadcast，cov=0.746，row=0.680，inkMatch=true；无上下边界仍通过 |
  | pink (16,73,920,28) 最后一行 | System，band=false | SuperMegaphone / Megaphone | PinkBroadcast，cov=0.741，row=0.700，inkMatch=true |
  | pink 图蓝底行 (16,103,900,29) | Whisper，band=false | Megaphone / Megaphone | BlueBroadcast，cov=0.674，row=0.670，inkMatch=true |
  | blue 图 (16,103,650,29) | Normal，band=false | Megaphone / Megaphone | BlueBroadcast，cov=0.637，row=0.638，inkMatch=true |
  旧实现把 16 级单桶覆盖≥0.60、外圈一致率≥78%、±12px 边界作为强制条件，且亮度<125 直接丢弃深色笔画；粉底三行和相邻蓝行因此无背景带，再被最近字体锚点判成 System/Whisper/Normal。新实现改为“行内背景带（含适度纹理）+ 深粉/深蓝笔画族”联合证据；中间行无边界时由 inkMatch 通过。
- R1 精确昵称复验证据：
  - 同帧 `Alice: hello` Party、`Alicf: hello` Guild；`FindStyle("Alicf: hello")` 与 `FindStyle("Alicf: hello", 0)` 都返回 Guild。
  - `FindStyle("Alice: hello")` → Party；`FindStyle("Alic: hello")` 与 occurrence API 都返回 Unknown；缺失昵称 `hello` → Unknown。
  - 实现判定：`exact = ExactSpeaker && parsedBody == candidateBody`；`SameSpeaker` 只用于 OCR 容错关联，不参与 exact 判定。
  - 文本 API 下多条同等精确/模糊行保持 Unknown；显式 occurrence 仍可解析重复精确行。
- R2 保留行对齐复验证据：
  - previous `[same normal, same megaphone]`、current `[retained same megaphone, new other]`、候选 `[0,1]` → 只入队 `[1]`，`changed=false`；旧 retained duplicate 不再因 occurrence 0 被当作来源变化重入。
  - 仅 retained duplicate 候选 `[0]` → 结果为空；真来源变化同一行 → 入队 `[0]` 且 `changed=true`。
  - 文本差分漏掉的同文本新跨频道消息 → 入队新行；候选已包含该新行时仍保留。
  - 三行同文本不同频道按乱序异步完成发布，历史仍分别保留各自来源。
- R3 无证据来源：`FromSample("Alice: hello", Empty, Empty, false)` → Unknown；`[Party]`、`[Guild]`、`[Notice]`、`System:` 等显式前缀仍正确；白字前景仍为 Normal。
- 原始像素样本：已执行。`tests/fixtures/chat-pink-super-megaphone-video.png` SHA-256 `B22E63FFE2DB4E1CFC7141E2E70A33395180F22F95CEFAA2FC3C26A509CCEF27`；`tests/fixtures/chat-blue-megaphone-video.png` SHA-256 `C868C4425F021B9A297BC46D1ACD6664C9AC705283DB418CC9EFB3901AA3E32C`。行区域见上表。用户本次提供的粉蓝交错原图：UNVERIFIED（未在本工作区）
- 连续粉底中间行、粉蓝交错、深色笔画、裁剪缩放：已执行。粉底三行第一/中间/最后行；0.75×/1.25× 缩放；粉图 +8/+2/-3、蓝图 -6/+2/+8 偏色；行高裁剪 10–12px；整图顶裁/底裁；粉蓝相邻；28% 场景纹理透出；深粉/深蓝笔画均保留。全部判为对应 Pink/BlueBroadcast；普通白字叠在粉/蓝/纹理场景上判 Normal，不升级为喇叭。
- 普通/好友/组队/家族/联盟/私聊隔离：已执行。六种色在统一蓝色场景上分别保持 Normal/Buddy/Party/Guild/Alliance/Whisper；`[Party]` 等显式前缀不被粉带提升；有效家族色和 `[Guild]` 仍为 Guild；未匹配深蓝/中性灰/无证据片段为 Unknown，只出现在“全部”，不制造家族/系统；`[Notice]`/`System:` 保持公告/系统。
- 解析、跨行关联、异步返回、缓存、主窗/浮窗：已执行。解析→行索引→样式→对齐→队列→发布→历史全链路；主窗和浮窗“喇叭”筛选只显示喇叭，“全部”保留 Unknown；同文本粉/蓝两条记录各保留来源；翻译缓存只缓存文本，不缓存来源；异步测试用乱序完成发布验证样式/行号不串。
- Windows 游戏实测：UNVERIFIED。本机没有运行中的游戏客户端/游戏画面采集，未执行真实游戏内逐条点击过滤器。已执行的是：同一提交构建 EXE、两套公开游戏原图 Windows OCR 基准、WinForms 主窗/浮窗控件级筛选和聊天区事件生命期。
- OCR/AI 调用次数是否变化，检测耗时：`AnalyzeChatBitmapAsync` 仍只有 1 次快速 `RecognizeAsync` 和 1 次 `strongOcr.RecognizeAsync` 调用点；`OnlineAiClient.TranslateAsync` 调用点为 2（与 v4.0 相同）；未增加 OCR 轮次或 AI 请求。Windows OCR 基准耗时：91–102ms（3 轮，1080×140，含 OCR）；像素证据 `Analyze` 约 2.56ms/行（920×28，100 次平均）。
- 未验证项、残留问题：
  - 用户最新粉蓝交错原图未在本工作区；桌面端需私下回放并回传
  - 真实游戏内过滤器逐条点击/实际频道隔离未测；不存在的频道按 UNVERIFIED
  - `tests/logic-ai-chat-accuracy.ps1` 的 `qq-220.0` 连续帧断言在该机 OCR 变体下失败（无冒号 `Xx.AzNKiDxX• repot'`）；文本解析逻辑未改，属既有 OCR/解析边界
  - `tests/logic-chat-region-first-use.ps1` 的 `RecognitionWasCancelled(...)` 源码断言在基线也不存在，属既有失效测试
  - 构建机无 Visual Studio 2022，`build.ps1` 增加 .NET SDK Roslyn 回退；产物仍为 .NET Framework 4.8 winexe
  - 主窗/浮窗测试使用真实 WinForms 控件，但为控件级断言，非物理鼠标/DPI 验收
  - `枫语幕.exe` 和 `*.build.json` 为本地构建产物，未提交；以路径和 SHA-256 回传
- 桌面端 5.6 收集和核对结论：DSH 已完成 R1/R2 第二次复验修复、R3 保留、公开像素、Windows OCR、控件级链路和干净提交构建；请桌面端用同一 EXE `0D0565BF...` 复核实机粉/蓝喇叭过滤器，未执行项记 UNVERIFIED。
- 云端最终验收：PENDING（由云端填写）

不要粘贴密钥或原始玩家聊天记录。原始用户图片和日志只走用户允许的私有通道。
