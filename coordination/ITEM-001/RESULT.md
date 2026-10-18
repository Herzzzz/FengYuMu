# ITEM-001 CP1 结果

## 结论

CP1 已关闭“已规划但未真正绘制”和已知误归属两个问题，云端 6.1 已给出 `CP1 PASS`，未发现 CP1 范围内可复现阻断项。没有发布正式版本，也没有把受控 OCR 重放冒充成实机游戏验证。

- 实现提交：`ea7439a`（`fix(item): close CP1 drawing pipeline`）
- 构建退出码：`0`
- EXE SHA-256：`21F53A2F0FC2AC2544BAA2C47F57E4A9EB2DE6A89A17327015E4337712C7E0BC`
- CP1 误归属烟测：`8/8` 通过
- 合成/迟到结果：`33/33` 通过
- F064：`prepared=2 painted=2 preserved=0`
- F065：`prepared=1 painted=1 preserved=0`

云端独立核对了实现 `ea7439a`、证据 `cf98099` / `7a6634e`、四张前后裁图和阶段日志，并独立重跑 `simple-flow 33/33`。云端复算 F065 裁图区域变化为 `1578` 像素；本文的 `3440` 是完整 1920x1080 帧的变化总数，两者统计范围不同。

## 实际绘制证据

F064 和 F065 都通过当前 Windows EXE、Windows OCR 与生产图片翻译路径重放。日志中同一 generation/label 依次出现：

`prepareAttempt -> ready -> drawAttempt -> drawSuccess -> item-painted`

F064 药水正文与限制行：

- 输入 SHA-256：`BA8649093287E2AF8E183DFAC242DC1599E68669C1E9EBFE2309221C5648BEBE`
- 输出 SHA-256：`3DDF6EF3191F9F83E3B0972515B5AD61E7FABAD9F0DF39894A9B5FEF87289F7F`
- 改变像素：`9330`
- 变化边界：`726,551,998,612`
- 输出内容：`红色药水`、`账号绑定。可在世界内交易。`、`由红色草药制成的药水。恢复100 HP。`

F065 回城卷正文：

- 输入 SHA-256：`27F86BC082BE645D5A6B0956FD508B3BF4714B89966007EE1ED480AFE29F3F1F`
- 输出 SHA-256：`4BDDDF7076609715213AAB92ED0381230F9808CC6F6770F51C38EEFA5CB937B0`
- 改变像素：`3440`
- 变化边界：`795,287,1160,694`
- 输出内容：`勇士部落回城卷`、`返回勇士部落。`

对应前后裁图在 `evidence/F064-*-crop.png` 与 `evidence/F065-*-crop.png`；完整阶段日志在 `evidence/runtime-item-events-r2.log`。

## 防误归属与迟到结果

`tests/logic-item-cp1.ps1` 固定验证：

1. 完整可信 body-only 返回句可规划且只有一个 owner；
2. 任务摘要、技能面板、玩家聊天不得借用道具正文；
3. 截断尾句不得绘制；
4. 裸道具名不得被当成提示框；
5. 相邻多个道具名时保留歧义块。

`tests/logic-simple-flow-r11.ps1` 的 33 项继续通过，其中包括 stale generation 不绘制、取消中的合成不得覆盖已提交位图、F8 取消后的迟到 OCR 不得发布。

## 已知边界

- 完整 `logic-item-text-001.ps1` 在 CP2 语义项 `identical description shared by several ids is translatable` 失败；此前 30 余项已通过。CP1 没有修改这一冻结语义问题。
- `ItemTextIndex.cs`、`ItemTextSemantics.cs` 为可复现构建所需的既有 WIP 依赖，包含在实现提交中，但不声明 CP2 已完成。
- 原生 OCR + 实际游戏交互仍为 `UNVERIFIED`；本次证据是原始实测帧上的受控 Windows OCR 重放。
- 未触碰、未提交 `main_ui_live_ai.png`、`main_ui_live_dashboard.png`、`main_ui_live_start.png`。

## 证据索引

- `evidence/cp1-build-r2.log`
- `evidence/cp1-smoke-r2.log`
- `evidence/simple-flow-r2.log`
- `evidence/picture-064-r2.log`
- `evidence/picture-065-r2.log`
- `evidence/runtime-item-events-r2.log`
- `evidence/pixel-and-hashes-r2.txt`
- `evidence/cp1-logic-r1.log`
