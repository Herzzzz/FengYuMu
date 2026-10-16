# 视频 5420ff… 词库增量验证

- 验证日期：2026-09-04
- 输入视频：`D:\Video\5420ff115e9d68f83d6826d48dfe1cec.mp4`
- 目标词库：仓库根目录 `枫语幕词库.tsv`
- 差异依据：`video-5420-differences.md`

## 变更边界

- 在磁盘已有未提交词库末尾继续追加 35 行，没有改写或排序旧词条。
- 修改前词库前缀共 2,174,775 字节，SHA-256 为 `DF9B5B83A97F7647DCB08D8978C380F346251EF56C2D1123B76EB3B5ED2CF5C8`；追加并统一新增段换行后，该前缀逐字节保持不变。
- 最终词库共 2,182,856 字节，SHA-256 为 `AED0F550D9E875EDE0D997B58EE4083B5383C6F1A2EB3EDB7E600B8C654F82C5`。
- 新增行仍使用旧版三列 TSV 格式；没有删除旧词条，也没有写入 OCR 错字。
- `Donating to Henesys` 因视频未显示具体捐赠物，只采用视频证据专用分组 `视频5420-75s`，没有猜填 506019–506035 中的任务 ID。

## 真实视频帧 OCR 回归

使用现有 `枫语幕.exe --benchmark` 对逐秒抽取的真实视频帧任务面板进行回归：

- 任务面板样本：23 个
- 词库本次新增内容的关键实帧命中：9 个
- 单样本最大耗时：1,063 毫秒
- 单样本平均耗时：848 毫秒
- 2 秒目标：23/23 通过

关键命中覆盖 `Teo's Collection`、`[Event] Growing Leaf`、`JM From tha Streetz Looking for a Partner!`、`The Roots of Regret`、`Maya and the Weird Medicine`、`To Henesys, the Prairie Town`、`To the Gray City, Kerning City`、`First Greeting with Pia, the Town Resident` 和 `Donating to Henesys`。模糊或界面省略号截断且无法从视频确认的内容未加入词库。

专用验证脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\video-dictionary-review\validate-video-5420-dictionary.ps1
```

结果：35/35 增量词条存在且唯一，9 个关键实帧命中，23 个 OCR 样本均在 2 秒内，验证通过。

## 兼容性回归

- `tests\logic-regression-v2.1.0.ps1`：20/20 通过。
- `tests\logic-regression-v3.0.ps1`：场景分类、结构化 OCR 数值纠错与安全边界通过。
- `git diff --check`：没有新增空白错误；输出中的换行提示来自工作区既有文件状态。

本词库任务没有修改 `src\SceneRecognition.cs`、`src\MapleOverlay.cs` 或 `src\build.ps1`，没有生成发布包，也没有发布版本。最终完整回归、安装目录同步、ZIP 与 GitHub 发布由“枫语幕v3.0接管”任务执行。
