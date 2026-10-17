# CHAT-001 结果回传模板

- 任务号：CHAT-001
- 提交人 / 工具：DeepSeek Harness（DSH）；Windows / PowerShell 7.6.5；.NET SDK 6.0.410 Roslyn
- 分支和提交 SHA：`dsh/chat-001`，实施提交 `51f22ae867f8bd3fa785e7f8704800ca96953e31`；结果文件提交见回传消息中的最终 SHA
- 代码基线是否与指令一致：是。基线 `c4dc19ea26e6a5611786ae7a6bd80acf33d6e858`；本分支在 `d05625e`（仅协作文件）之上实施，`git diff c4dc19e..51f22ae` 未触碰任务、装备、技能、AI 请求或 OCR 轮次
- 修改文件与修改原因：
  - `src/ChatColorEvidence.cs`：按行联合采样背景带与深色笔画；支持连续同色块中间行、顶底裁剪、蓝/粉相邻、缩放/轻微偏色和 28% 左右场景纹理透出；深蓝/深粉笔画不再因亮度阈值被丢弃；粉色/蓝色带分别输出 `PinkBroadcast`/`BlueBroadcast`
  - `src/SimpleForms.cs`：新增 `ChatSourceCategory.Unknown`；带背景只在已确认的粉/蓝广播带上进入喇叭；普通文字叠在粉/蓝场景上不被升级；中性深灰不再被锚点误判为家族；未可靠来源落 Unknown（只在“全部”）
  - `src/MapleOverlay.cs`：消息按说话者身份与物理行顺序关联，不再用 5 字符公共片段；同一文本的不同物理行分别取色；强 OCR 文本缓存增加来源样式一致性检查，样式变化时不复用旧分类；基准报告记录采样证据
  - `src/OfflineChat.cs`：逐行样式随解析、帧差分、队列、异步发布、历史筛选传递；同文本跨频道/背景变化作为新来源入队；缓存只缓存译文文本，不缓存来源
  - `src/build.ps1`：本机无 Visual Studio 2022 时回退到 .NET SDK 自带 Roslyn `csc.dll`，仍使用原 .NET Framework 4.8 引用程序集和同一组源文件
  - 测试：新增 `tests/logic-chat-megaphone-pixels.ps1`、`logic-chat-megaphone-pipeline.ps1`、`logic-chat-megaphone-ocr.ps1`；更新过时源码断言、117ms 轮询期望，并把 `logic-chat-ui-r15` 的硬编码工作目录改为仓库内 `.bench` 以在本机运行
- EXE SHA-256：`5BBA332CED4D0B7143D726E73237632CFD5380891E1D2749E3CE89509A444296`（`枫语幕.exe`，663,040 bytes）
- 构建输入记录路径及哈希：`D:\GPT\文件\枫语幕\work\dsh-chat-001\枫语幕.exe.build.json`，SHA-256 `615E46447464066D2DC9D618A8E896146562A39EAF3A16B47010A44CD16BD841`；记录 64 个构建输入，`head=51f22ae867f8bd3fa785e7f8704800ca96953e31`，`worktree=[]`，编译器 `dotnet ...\sdk\6.0.410\Roslyn\bincore\csc.dll`
- 构建命令、环境、退出码：`pwsh -NoProfile -File src/build.ps1`；Windows，.NET Framework 4.8 引用程序集，Roslyn csc.dll；退出码 0；输出 `编译完成 ... SHA-256=5BBA...`
- 检查命令、各自退出码、结果文件（均在实施提交 `51f22ae` 的干净构建上重跑）：
  - `pwsh -NoProfile -File tests/logic-chat-megaphone-pixels.ps1` → 0；161 项；`.bench/evidence-commit/megaphone-pixels.log`
  - `pwsh -NoProfile -File tests/logic-chat-megaphone-pipeline.ps1` → 0；33 项；`.bench/evidence-commit/megaphone-pipeline.log`
  - `pwsh -NoProfile -File tests/logic-chat-megaphone-ocr.ps1` → 0；14 项；连续 4 次 0；`.bench/evidence-commit/megaphone-ocr*.log`
  - `pwsh -NoProfile -File tests/logic-chat-category-filtering.ps1` → 0；`.bench/evidence-commit/chat-category-filtering.log`
  - `pwsh -NoProfile -File tests/logic-chat-continuity.ps1` → 0；9/9；`.bench/evidence-commit/chat-continuity.log`
  - `pwsh -NoProfile -File tests/logic-chat-feedback-correctness.ps1` → 0；39 项；`.bench/evidence-commit/chat-feedback-correctness.log`
  - `pwsh -NoProfile -File tests/logic-independent-window.ps1` → 0；`.bench/evidence-commit/independent-window.log`
  - `pwsh -NoProfile -File tests/logic-chat-ui-r15.ps1` → 0；20 项；`.bench/evidence-commit/chat-ui-r15.log`
  - `pwsh -NoProfile -File tests/logic-video-bv1l8yc67et4-chat.ps1 -ExePath .\枫语幕.exe` → 0；32 条；`.bench/evidence-commit/video-chat.log`
  - `pwsh -NoProfile -File tests/logic-ai-chat-performance.ps1 -ExePath .\枫语幕.exe` → 0；`.bench/evidence-commit/ai-chat-performance.log`
  - `pwsh -NoProfile -File tests/logic-chat-region-mouse-lifecycle.ps1 -ExePath .\枫语幕.exe` → 0；24/30/36/39/40px 生命期；`.bench/evidence-commit/region-mouse-lifecycle.log`
  - 既有环境敏感项：`tests/logic-ai-chat-accuracy.ps1` → 1（仅在最后 `qq-220.0` 连续帧断言失败：该机 Windows OCR 把首行读成 `Xx.AzNKiDxX• repot'`，无冒号，`ParseChatLines` 未收；`ParseChatLines` 本提交未改；前面解析/修正/词汇各项均通过）；`tests/logic-chat-region-first-use.ps1` → 1（既有源码断言 `RecognitionWasCancelled(automatic, requestId, generation)` 在基线和本提交均不存在，与本改动无关）
- 旧版失败 → 新版通过的对照：
  | 样本/行 | v4.0 分类 | 新版分类 | 新版证据 |
  |---|---|---|---|
  | pink (16,13,820,28) 第一行 | System，band=false | SuperMegaphone / Megaphone | PinkBroadcast，cov=0.651，row=0.559，inkMatch=true |
  | pink (16,43,550,28) 中间行 | System，band=false | SuperMegaphone / Megaphone | PinkBroadcast，cov=0.746，row=0.680，inkMatch=true；无上下边界仍通过 |
  | pink (16,73,920,28) 最后一行 | System，band=false | SuperMegaphone / Megaphone | PinkBroadcast，cov=0.741，row=0.700，inkMatch=true |
  | pink 图蓝底行 (16,103,900,29) | Whisper，band=false | Megaphone / Megaphone | BlueBroadcast，cov=0.674，row=0.670，inkMatch=true |
  | blue 图 (16,103,650,29) | Normal，band=false | Megaphone / Megaphone | BlueBroadcast，cov=0.637，row=0.638，inkMatch=true |
  旧实现把 16 级单桶覆盖≥0.60、外圈一致率≥78%、±12px 边界作为强制条件，且亮度<125 直接丢弃深色笔画；粉底三行和相邻蓝行因此无背景带，再被最近字体锚点判成 System/Whisper/Normal。新实现改为“行内背景带（含适度纹理）+ 深粉/深蓝笔画族”联合证据；中间行无边界时由 inkMatch 通过，上下裁剪/相邻行不再依赖 12px 边界。
- 原始像素样本：已执行。`tests/fixtures/chat-pink-super-megaphone-video.png` SHA-256 `B22E63FFE2DB4E1CFC7141E2E70A33395180F22F95CEFAA2FC3C26A509CCEF27`；`tests/fixtures/chat-blue-megaphone-video.png` SHA-256 `C868C4425F021B9A297BC46D1ACD6664C9AC705283DB418CC9EFB3901AA3E32C`。行区域见上表。用户本次提供的粉蓝交错原图：UNVERIFIED（未在本工作区，未拿原文件，未用 AI 窗口截图代替）
- 连续粉底中间行、粉蓝交错、深色笔画、裁剪缩放：已执行。粉底三行分别做第一/中间/最后行；图片 0.75×/1.25× 缩放；粉图 +8/+2/-3、蓝图 -6/+2/+8 偏色；行高裁剪到 10–12px；整图顶裁/底裁；粉蓝相邻行；28% 场景纹理透出；深粉/深蓝笔画均保留。全部判为对应 Pink/BlueBroadcast；普通白字叠在粉/蓝/纹理场景上判 Normal，不升级为喇叭。
- 普通/好友/组队/家族/联盟/私聊隔离：已执行。六种色在统一蓝色场景上分别保持 Normal/Buddy/Party/Guild/Alliance/Whisper；`[Party]` 等显式前缀不被粉带提升；有效家族色和 `[Guild]` 仍为 Guild；未匹配深蓝/中性灰/无证据片段为 Unknown，只出现在“全部”，不制造家族/系统；`[Notice]`/`System:` 保持公告/系统。
- 解析、跨行关联、异步返回、缓存、主窗/浮窗：已执行。`FindStyles` 对解析后的每一行按物理行顺序取一次；Alice/Bob 同正文不同昵称分别命中；缺席 Carol 和短公共片段为 Unknown；同玩家两条相同文本分别取 Megaphone/Guild；bullet、CHxx、OCR 噪声名、换行尾行均关联到说话者行；同文本粉转蓝/普通转喇叭判为新来源，Normal→Unknown 抖动不重复发布；强 OCR 缓存样式不一致时返回新采样帧而不复用旧分类；发布路径携带 `Pending.Style` 到历史，同一文本粉/蓝两条记录各保留来源；主窗口和浮窗的“喇叭”筛选只显示喇叭，“全部”保留 Unknown；翻译缓存只缓存文本，不缓存来源。
- Windows 游戏实测：UNVERIFIED。本机没有运行中的游戏客户端/游戏画面采集，未执行真实游戏内逐条点击过滤器，也不把“编译通过/公开像素通过”写成“所有玩家实测通过”。已执行的 Windows 项目是：同一提交构建 EXE、两套公开游戏原图 Windows OCR 基准、WinForms 主窗/浮窗控件级筛选和聊天区事件生命期。
- OCR/AI 调用次数是否变化，检测耗时：`AnalyzeChatBitmapAsync` 仍只有 1 次快速 `RecognizeAsync` 和 1 次 `strongOcr.RecognizeAsync` 调用点；`OnlineAiClient.TranslateAsync` 调用点为 2（与 v4.0 相同）；未增加 OCR 轮次或 AI 请求。Windows OCR 基准耗时：粉图 83–95ms、蓝图 87–98ms（4 轮，1080×140，含 OCR）；像素证据 `Analyze` 约 2.16–2.46ms/行（920×28，100 次平均）。
- 未验证项、残留问题：
  - 用户最新粉蓝交错原图未在本工作区；桌面端需私下回放并回传结果
  - 真实游戏内过滤器逐条点击/实际频道隔离未测；不存在的频道按 UNVERIFIED
  - `tests/logic-ai-chat-accuracy.ps1` 的 `qq-220.0` 连续帧断言在该机 OCR 变体下失败（`Xx.AzNKiDxX• repot'` 无冒号）；`ParseChatLines` 未改，属既有 OCR/解析边界，需云端或桌面端在目标 OCR 主机复核
  - `tests/logic-chat-region-first-use.ps1` 的 `RecognitionWasCancelled(...)` 源码断言在基线也不存在，属既有失效测试，未在本任务修改
  - 构建机无 Visual Studio 2022，`build.ps1` 增加 .NET SDK Roslyn 回退；产物仍为 .NET Framework 4.8 winexe，输入记录 64 项
  - 主窗/浮窗测试使用真实 WinForms 控件，但为控件级断言，非物理鼠标/DPI 验收
  - `枫语幕.exe` 和 `*.build.json` 为本地构建产物，未提交；以路径和 SHA-256 回传
- 桌面端 5.6 收集和核对结论：DSH 已完成源代码、公开像素、Windows OCR、控件级链路和干净提交构建；请桌面端在本机用同一 EXE `5BBA332C...` 复核实机粉/蓝喇叭过滤器，并把未执行项如实记 UNVERIFIED。DSH 不代替桌面端宣称实机通过。
- 云端最终验收：PENDING（由云端填写）

不要粘贴密钥或原始玩家聊天记录。原始用户图片和日志只走用户允许的私有通道。
