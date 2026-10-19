# ITEM-001 CP2 云端验收材料

## 待验收结论

CP2 已完成本地实现、验证和云端 6.1 最终复验，结论为 `CP2 PASS`，阻断项为 `0`。首轮实现提交为 `f7f58214f1715b7113040c36c46d9ced70b23c19`；云端首轮指出分句路径没有把已解析的 `Shared` 标记传给最终结果，唯一修复提交 `7f63041` 已通过复验。基线为 `f6d59efbb9e0974c8ac4e29a062a87c9a6d9d104`。

本轮只处理三件事：完全相同的共享道具说明可安全翻译；同英文不同中文或其他冲突继续保留原文；查询返回候选副本，调用方筛选不再污染持久索引。没有修改 OCR、面板、合成器、技能、任务、聊天或发布逻辑。

## 本地验证

- Windows 构建退出码：`0`
- EXE SHA-256：`76ACA20148B1CC085B3C6C0A94247B721C541F7EF2CF851CFADF0C241347A83A`
- ITEM 完整语义、规划、合成测试：`91/91` 通过
- CP1 防误归属回归：`8/8` 通过
- 道具重载与索引回归：`23/23` 通过
- 合成与迟到 generation 回归：`33/33` 通过
- 词库 SHA-256：`82CE7D66350B53C4A1A38BAA327E875EF38096829946892CFF468AEDF948BDF7`

91 项测试包含以下 CP2 正例和反例：

1. 多 ID、相同英文、相同中文：翻译成功，`Shared=true`，`ItemId=""`；
2. 可靠 ID 已知时：只保留该 ID，不声明共享；
3. 单 ID 普通说明：只允许完整精确匹配；
4. 相同英文、不同中文：保持歧义，不绘制；
5. 先按 ID 查询再无 ID 查询：结果稳定；
6. 清空返回候选列表：不影响索引中的原始候选；
7. 重载词库：结果一致；
8. `Omok Table` 共享说明进入生产规划器与 `PrepareSimpleFusionAsync`，名称和正文共用一个 owner，两个标签都 ready。
9. 两个不同道具完整说明中的相同分句 `A crimson tonic.` 返回 `Matched=true`、`Shared=true`、`ItemId=""`。

## CP1 实测帧回归

CP2 EXE 对 F064/F065 原始实测帧重新执行 Windows OCR 与生产绘制路径，退出码均为 `0`：

- F064：`prepared=2 painted=2 preserved=0`；输出 SHA-256 `3DDF6EF3191F9F83E3B0972515B5AD61E7FABAD9F0DF39894A9B5FEF87289F7F`
- F065：`prepared=1 painted=1 preserved=0`；输出 SHA-256 `4BDDDF7076609715213AAB92ED0381230F9808CC6F6770F51C38EEFA5CB937B0`

两张输出哈希与 CP1 已验收结果相同，说明 CP2 没有改变既有绘制结果。

## 跨功能回归边界

设备、任务布局、技能覆盖、技能语义、聊天分类、喇叭、DPI 和发布安全的主要动态测试均通过。以下五个脚本仍失败，但都属于基线已有或冻结模块问题；从 CP2 基线到本提交，`src` 下只有 `ItemTextIndex.cs` 和 `ItemTextSemantics.cs` 发生变化：

- `logic-quest-body-and-summary.ps1`：过期的精确源码字符串断言；
- `logic-skill-panel-001.ps1`：`full panel plans skill-name` 既有 fixture 失败；
- `logic-continuous-translation.ps1`：过期的精确源码字符串断言；
- `logic-overlay-paint-safety.ps1`：过期的精确源码字符串断言；
- `logic-panel-context-isolation.ps1`：过期的精确源码字符串断言。

本轮没有为了让这些跨范围脚本变绿而修改冻结模块或削弱断言。

## 验证边界

- 源码、Windows 构建、受控 Windows OCR 重放：已验证；
- 原生 OCR + 实际游戏动态交互：`UNVERIFIED`；
- 正式发布：未执行；
- 未触碰、未提交 3 张 `main_ui_live_*.png`。

## 证据

- `evidence/cp2-build.log`
- `evidence/cp2-item-text.log`
- `evidence/cp2-cp1-regression.log`
- `evidence/cp2-journal-item.log`
- `evidence/cp2-simple-flow.log`
- `evidence/cp2-picture-064.log`
- `evidence/cp2-picture-065.log`
