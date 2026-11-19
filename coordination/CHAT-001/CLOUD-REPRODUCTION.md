# 云端原始像素复现：CHAT-001

执行环境：Linux / PowerShell 7.6.6。使用 v4.0 的生产 ChatColorEvidence.Sample 与 ChatVisualStylePolicy，从既有游戏截图读取真实 BGRA 像素；使用受控文字和行边界。没有运行新的 OCR 或 AI 请求。
结论限于已有素材与指定行区域，不等于最新用户截图或 Windows 游戏已实测。

| 既有图片 | 行区域 x,y,w,h | 原版分类 | 背景带检测 | 预期 |
|---|---|---|---|---|
| chat-pink-super-megaphone-video.png | 16,13,820,28 | System | false | Megaphone |
| 同图 | 16,43,550,28 | System | false | Megaphone |
| 同图 | 16,73,920,28 | System | false | Megaphone |
| 同图蓝底行 | 16,103,900,29 | Whisper | false | Megaphone |
| chat-blue-megaphone-video.png | 16,103,650,29 | Normal | false | Megaphone |

粉底三行采样背景约 (214–215,164–165,182)，采样文字约 (196,116–117,152–153)；原代码未确认背景带，继而落入字体近色的 System 分类。蓝底案例也未确认背景带，出现 Normal / Whisper。

素材位置：tests/fixtures/ 下的上述两张已有图片。用户最新补图明确展示浅粉/深粉、浅蓝/深蓝及耳朵/频道号；该最新原图的直接像素回放仍待桌面端，不得写成已执行。
