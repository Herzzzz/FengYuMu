using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MapleOverlay
{
    internal enum ChatVisualKind
    {
        Chat = 0,
        System = 1,
        Megaphone = 2,
        SuperMegaphone = 3
    }

    internal sealed class ChatVisualStyle
    {
        internal ChatVisualKind Kind;
        internal Color ForeColor;
        internal Color BackColor;
        internal bool HasBackground;

        internal static ChatVisualStyle Default
        {
            get
            {
                return new ChatVisualStyle {
                    Kind = ChatVisualKind.Chat,
                    ForeColor = Color.FromArgb(241, 245, 249),
                    BackColor = Color.FromArgb(18, 18, 22),
                    HasBackground = false
                };
            }
        }
    }

    internal static class ChatVisualStylePolicy
    {
        internal static ChatVisualStyle FromSample(string sourceText, Color sampledForeground,
            Color sampledBackground, bool coloredBackground)
        {
            bool broadcast = coloredBackground || LooksLikeBroadcast(sourceText);
            bool system = LooksLikeSystem(sourceText);
            bool blueMegaphone = coloredBackground && IsBlueMegaphoneBackground(sampledBackground);
            Color background = sampledBackground.IsEmpty
                ? Color.FromArgb(172, 46, 112) : sampledBackground;
            Color foreground = sampledForeground.IsEmpty
                ? (system ? Color.FromArgb(255, 214, 92) : Color.FromArgb(241, 245, 249))
                : sampledForeground;

            if (broadcast)
            {
                background = MakeReadableBackground(background);
                foreground = EnsureContrast(foreground, background);
            }
            else
                foreground = MakeReadableOnDark(foreground);

            ChatVisualKind kind = blueMegaphone ? ChatVisualKind.Megaphone :
                (broadcast ? ChatVisualKind.SuperMegaphone :
                    (system ? ChatVisualKind.System : ChatVisualKind.Chat));
            return new ChatVisualStyle {
                Kind = kind,
                ForeColor = foreground,
                BackColor = background,
                HasBackground = broadcast
            };
        }

        private static bool IsBlueMegaphoneBackground(Color color)
        {
            return !color.IsEmpty && color.R >= 105 && color.G >= 125 && color.B >= 130 &&
                color.G >= color.R + 12 && color.B >= color.R + 20;
        }

        internal static bool LooksLikeBroadcast(string value)
        {
            string text = (value ?? "").ToLowerInvariant();
            return text.IndexOf("megaphone", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("gift-filled message", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("super message", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("[world]", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("喇叭", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("广播", StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeSystem(string value)
        {
            string text = (value ?? "").ToLowerInvariant();
            return text.IndexOf("[notice]", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("system", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("系统公告", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("公告", StringComparison.Ordinal) == 0;
        }

        private static Color MakeReadableOnDark(Color color)
        {
            int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            if (maximum < 24) return Color.FromArgb(226, 232, 240);
            if (maximum >= 168) return Color.FromArgb(color.R, color.G, color.B);
            double factor = 168.0 / Math.Max(1, maximum);
            return Color.FromArgb(Math.Min(255, (int)Math.Round(color.R * factor)),
                Math.Min(255, (int)Math.Round(color.G * factor)),
                Math.Min(255, (int)Math.Round(color.B * factor)));
        }

        private static Color MakeReadableBackground(Color color)
        {
            int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            if (maximum < 58) return Color.FromArgb(132, 38, 91);
            if (maximum <= 205) return Color.FromArgb(color.R, color.G, color.B);
            double factor = 205.0 / maximum;
            return Color.FromArgb((int)Math.Round(color.R * factor),
                (int)Math.Round(color.G * factor), (int)Math.Round(color.B * factor));
        }

        private static Color EnsureContrast(Color foreground, Color background)
        {
            double foregroundLight = Luminance(foreground);
            double backgroundLight = Luminance(background);
            if (Math.Abs(foregroundLight - backgroundLight) >= 58)
                return Color.FromArgb(foreground.R, foreground.G, foreground.B);
            if (backgroundLight >= 128)
            {
                double target = Math.Max(22, backgroundLight - 68);
                double factor = target / Math.Max(1, foregroundLight);
                return Color.FromArgb(Math.Max(0, Math.Min(255, (int)Math.Round(foreground.R * factor))),
                    Math.Max(0, Math.Min(255, (int)Math.Round(foreground.G * factor))),
                    Math.Max(0, Math.Min(255, (int)Math.Round(foreground.B * factor))));
            }
            double blend = Math.Min(1.0, (backgroundLight + 82 - foregroundLight) /
                Math.Max(1, 255 - foregroundLight));
            return Color.FromArgb((int)Math.Round(foreground.R + (255 - foreground.R) * blend),
                (int)Math.Round(foreground.G + (255 - foreground.G) * blend),
                (int)Math.Round(foreground.B + (255 - foreground.B) * blend));
        }

        private static double Luminance(Color value)
        {
            return value.R * 0.299 + value.G * 0.587 + value.B * 0.114;
        }
    }

    internal sealed class TaskDictionaryRow
    {
        public string English;
        public string Chinese;
        public string Category;
        public string IconHash;
        public string TaskId;
        public string StartMap;
    }

    internal static class AiTranslationWindowContent
    {
        internal static string Append(string existing, string translation)
        {
            string next = Normalize(translation);
            if (next.Length == 0) return existing ?? "";
            string current = existing ?? "";
            if (current.Length > 12000)
            {
                int cut = current.IndexOf('\n', Math.Max(0, current.Length - 8000));
                current = cut >= 0 ? current.Substring(cut + 1) : current.Substring(current.Length - 8000);
            }
            return current.Length == 0 ? next : current.TrimEnd() + Environment.NewLine + next;
        }

        private static string Normalize(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            StringBuilder result = new StringBuilder();
            bool spacing = false;
            foreach (char valueChar in value.Trim())
            {
                if (Char.IsWhiteSpace(valueChar)) { spacing = true; continue; }
                if (spacing && result.Length > 0) result.Append(' ');
                spacing = false;
                result.Append(valueChar);
            }
            return result.ToString();
        }
    }

    internal sealed class AiTranslationWindowForm : Form
    {
        private const int ResizeBorder = 7;
        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtCaption = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private readonly OverlayForm overlay;
        private readonly RichTextBox content = new RichTextBox();
        private readonly Label status = new Label();
        private readonly Panel header = new Panel();
        private readonly Label closeButton = new Label();
        private readonly Font contentRegularFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Regular);
        private readonly Font contentBroadcastFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
        private bool allowClose;
        private bool dragging;
        private Point dragPointerOrigin;
        private Point dragWindowOrigin;
        private Control dragCaptureTarget;
        private string displayedText = "";
        private bool hiddenByUser;

        internal AiTranslationWindowForm(OverlayForm owner)
        {
            overlay = owner;
            Text = "枫语幕 · AI实时翻译";
            Icon = Program.AppIcon;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(300, 150);
            Size = new Size(560, 280);
            Opacity = 0.92d;
            Padding = new Padding(1);
            BackColor = Color.FromArgb(91, 111, 131);
            Font = new Font("Microsoft YaHei UI", 9.0f);

            header.Dock = DockStyle.Top;
            header.Height = 44;
            header.BackColor = Color.FromArgb(43, 57, 72);
            header.Cursor = Cursors.SizeAll;
            Label title = new Label {
                Text = "AI实时聊天翻译", AutoSize = true,
                Location = new Point(12, 5), ForeColor = Color.FromArgb(244, 247, 250),
                Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold),
                Cursor = Cursors.SizeAll
            };
            status.Text = "等待聊天区出现新消息";
            status.AutoSize = false;
            status.Location = new Point(13, 24);
            status.Size = new Size(Width - 58, 17);
            status.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            status.AutoEllipsis = true;
            status.ForeColor = Color.FromArgb(178, 193, 207);
            status.Cursor = Cursors.SizeAll;
            closeButton.Text = "×";
            closeButton.TabStop = false;
            closeButton.BackColor = header.BackColor;
            closeButton.ForeColor = Color.FromArgb(235, 240, 245);
            closeButton.Font = new Font("Segoe UI", 11.0f, FontStyle.Regular);
            closeButton.TextAlign = ContentAlignment.MiddleCenter;
            closeButton.Cursor = Cursors.Hand;
            closeButton.Size = new Size(36, 30);
            closeButton.Dock = DockStyle.Right;
            closeButton.MouseEnter += delegate { closeButton.BackColor = Color.FromArgb(184, 72, 72); };
            closeButton.MouseLeave += delegate { closeButton.BackColor = header.BackColor; };
            closeButton.Click += delegate { Close(); };
            header.Resize += delegate {
                status.Width = Math.Max(80, header.ClientSize.Width - closeButton.Width - 20);
            };
            header.MouseDown += BeginWindowDrag;
            header.MouseMove += ContinueWindowDrag;
            header.MouseUp += EndWindowDrag;
            title.MouseDown += BeginWindowDrag;
            title.MouseMove += ContinueWindowDrag;
            title.MouseUp += EndWindowDrag;
            status.MouseDown += BeginWindowDrag;
            status.MouseMove += ContinueWindowDrag;
            status.MouseUp += EndWindowDrag;
            header.Controls.Add(title); header.Controls.Add(status); header.Controls.Add(closeButton);

            content.Dock = DockStyle.Fill;
            content.Multiline = true;
            content.ReadOnly = true;
            content.ScrollBars = RichTextBoxScrollBars.Vertical;
            content.BorderStyle = BorderStyle.None;
            content.BackColor = Color.FromArgb(38, 46, 55);
            content.ForeColor = Color.FromArgb(241, 245, 249);
            content.Font = contentRegularFont;
            content.Margin = new Padding(14);
            content.WordWrap = true;
            content.TabStop = false;

            Panel body = new Panel {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 9, 8, 10),
                BackColor = Color.FromArgb(38, 46, 55)
            };
            body.Controls.Add(content);
            Controls.Add(body); Controls.Add(header);
            LoadSavedBounds();
            FormClosing += HandleFormClosing;
            ResizeEnd += delegate { SaveBounds(); };
            Disposed += delegate { contentRegularFont.Dispose(); contentBroadcastFont.Dispose(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmNcHitTest || (int)message.Result != HtClient ||
                WindowState != FormWindowState.Normal) return;
            int packed = unchecked((int)(long)message.LParam);
            Point screenPoint = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
            Point clientPoint = PointToClient(screenPoint);
            bool left = clientPoint.X <= ResizeBorder;
            bool right = clientPoint.X >= ClientSize.Width - ResizeBorder;
            bool top = clientPoint.Y <= ResizeBorder;
            bool bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;
            if (left && top) message.Result = (IntPtr)HtTopLeft;
            else if (right && top) message.Result = (IntPtr)HtTopRight;
            else if (left && bottom) message.Result = (IntPtr)HtBottomLeft;
            else if (right && bottom) message.Result = (IntPtr)HtBottomRight;
            else if (left) message.Result = (IntPtr)HtLeft;
            else if (right) message.Result = (IntPtr)HtRight;
            else if (top) message.Result = (IntPtr)HtTop;
            else if (bottom) message.Result = (IntPtr)HtBottom;
            else if (header.Bounds.Contains(clientPoint) &&
                !closeButton.ClientRectangle.Contains(closeButton.PointToClient(screenPoint)))
                message.Result = (IntPtr)HtCaption;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: keep game focus.
                return parameters;
            }
        }

        internal string DisplayedText { get { return displayedText; } }
        internal bool HiddenByUser { get { return hiddenByUser; } }

        internal void AppendTranslation(string translation)
        {
            AppendTranslation(translation, ChatVisualStyle.Default);
        }

        internal void AppendTranslation(string translation, ChatVisualStyle style)
        {
            string next = AiTranslationWindowContent.Append("", translation);
            if (next.Length == 0) return;
            if (style == null) style = ChatVisualStyle.Default;
            if (content.TextLength > 12000)
            {
                int cut = content.Text.IndexOf('\n', Math.Max(0, content.TextLength - 8000));
                content.Select(0, cut >= 0 ? cut + 1 : Math.Max(0, content.TextLength - 8000));
                content.SelectedText = "";
            }
            content.SelectionStart = content.TextLength;
            content.SelectionLength = 0;
            if (content.TextLength > 0) content.AppendText(Environment.NewLine);
            content.SelectionStart = content.TextLength;
            content.SelectionColor = style.ForeColor;
            content.SelectionBackColor = style.HasBackground ? style.BackColor : content.BackColor;
            bool megaphone = style.Kind == ChatVisualKind.Megaphone ||
                style.Kind == ChatVisualKind.SuperMegaphone;
            content.SelectionFont = megaphone ? contentBroadcastFont : contentRegularFont;
            content.AppendText(style.HasBackground ? "  " + next + "  " : next);
            content.SelectionBackColor = content.BackColor;
            content.SelectionColor = content.ForeColor;
            displayedText = content.Text;
            content.ScrollToCaret();
            status.Text = style.Kind == ChatVisualKind.Megaphone
                ? "普通喇叭译文已按蓝底显示"
                : (style.Kind == ChatVisualKind.SuperMegaphone
                    ? "超级喇叭译文已按粉底显示"
                    : "AI新译文已按聊天来源颜色显示");
        }

        internal void ShowPassive()
        {
            hiddenByUser = false;
            ShowPassiveIfAllowed();
        }

        internal void ShowPassiveIfAllowed()
        {
            if (hiddenByUser) return;
            if (!Visible) Show();
        }

        internal void HideForStop()
        {
            hiddenByUser = false;
            Hide();
        }

        internal void ClosePermanently()
        {
            allowClose = true;
            Close();
        }

        private void BeginWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            StartWindowDrag(sender as Control, Control.MousePosition);
        }

        private void ContinueWindowDrag(object sender, MouseEventArgs e)
        {
            if (!dragging || (e.Button & MouseButtons.Left) == 0) return;
            MoveWindowDrag(Control.MousePosition);
        }

        private void EndWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) FinishWindowDrag();
        }

        private void StartWindowDrag(Control captureTarget, Point pointerPosition)
        {
            if (captureTarget == null) return;
            dragging = true;
            dragPointerOrigin = pointerPosition;
            dragWindowOrigin = Location;
            dragCaptureTarget = captureTarget;
            captureTarget.Capture = true;
        }

        private void MoveWindowDrag(Point pointerPosition)
        {
            if (!dragging) return;
            Location = new Point(dragWindowOrigin.X + pointerPosition.X - dragPointerOrigin.X,
                dragWindowOrigin.Y + pointerPosition.Y - dragPointerOrigin.Y);
        }

        private void FinishWindowDrag()
        {
            if (!dragging) return;
            dragging = false;
            if (dragCaptureTarget != null) dragCaptureTarget.Capture = false;
            dragCaptureTarget = null;
            SaveBounds();
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveBounds();
            if (allowClose || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            hiddenByUser = true;
            Hide();
        }

        private void LoadSavedBounds()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Rectangle fallback = new Rectangle(work.Right - Width - 24,
                work.Top + Math.Max(24, (work.Height - Height) / 2), Width, Height);
            Rectangle saved = fallback;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FengYuMu"))
                {
                    if (key != null)
                    {
                        int x = Convert.ToInt32(key.GetValue("AiChatWindowX",
                            key.GetValue("TranslationWindowX", fallback.X)));
                        int y = Convert.ToInt32(key.GetValue("AiChatWindowY",
                            key.GetValue("TranslationWindowY", fallback.Y)));
                        int layoutVersion = Convert.ToInt32(key.GetValue("AiChatWindowLayoutVersion", 0));
                        int width = layoutVersion >= 2
                            ? Convert.ToInt32(key.GetValue("AiChatWindowWidth", fallback.Width))
                            : fallback.Width;
                        int height = layoutVersion >= 2
                            ? Convert.ToInt32(key.GetValue("AiChatWindowHeight", fallback.Height))
                            : fallback.Height;
                        saved = new Rectangle(x, y, Math.Max(300, width), Math.Max(150, height));
                    }
                }
            }
            catch { saved = fallback; }
            bool visible = false;
            foreach (Screen screen in Screen.AllScreens)
                if (Rectangle.Intersect(screen.WorkingArea, saved).Width >= 100 &&
                    Rectangle.Intersect(screen.WorkingArea, saved).Height >= 80)
                { visible = true; break; }
            Rectangle selected = visible ? saved : fallback;
            Rectangle targetWork = Screen.FromRectangle(selected).WorkingArea;
            int selectedWidth = Math.Min(selected.Width, targetWork.Width);
            int selectedHeight = Math.Min(selected.Height, targetWork.Height);
            int selectedX = Math.Max(targetWork.Left,
                Math.Min(selected.X, targetWork.Right - selectedWidth));
            int selectedY = Math.Max(targetWork.Top,
                Math.Min(selected.Y, targetWork.Bottom - selectedHeight));
            Bounds = new Rectangle(selectedX, selectedY, selectedWidth, selectedHeight);
        }

        private void SaveBounds()
        {
            if (WindowState != FormWindowState.Normal || Width < 300 || Height < 150) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                {
                    key.SetValue("AiChatWindowX", Left, RegistryValueKind.DWord);
                    key.SetValue("AiChatWindowY", Top, RegistryValueKind.DWord);
                    key.SetValue("AiChatWindowWidth", Width, RegistryValueKind.DWord);
                    key.SetValue("AiChatWindowHeight", Height, RegistryValueKind.DWord);
                    key.SetValue("AiChatWindowLayoutVersion", 2, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
    }

    internal sealed class MainPanelForm : Form
    {
        private readonly OverlayForm overlay;
        private readonly Label status = new Label();
        private readonly Label hotkeys = new Label();
        private readonly TrackBar range = new TrackBar();
        private readonly Label rangeValue = new Label();
        private readonly CheckBox continuousTranslation = new CheckBox();

        public MainPanelForm(OverlayForm owner)
        {
            overlay = owner;
            Text = "枫语幕";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowIcon = true;
            Icon = Program.AppIcon;
            ClientSize = new Size(560, 576);
            BackColor = Color.FromArgb(244, 247, 251);
            Font = new Font("Microsoft YaHei UI", 9.0f);

            Panel header = new Panel {
                Dock = DockStyle.Top, Height = 94,
                BackColor = Color.FromArgb(31, 41, 55), Padding = new Padding(22, 16, 22, 10)
            };
            Label title = new Label {
                Text = "枫语幕", ForeColor = Color.White, AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 20.0f, FontStyle.Bold), Location = new Point(20, 12)
            };
            Label subtitle = new Label {
                Text = "稳定识别模式 · 外部截图 OCR · 不修改游戏", ForeColor = Color.FromArgb(191, 219, 254),
                AutoSize = true, Font = new Font("Microsoft YaHei UI", 9.5f), Location = new Point(22, 58)
            };
            header.Controls.Add(title); header.Controls.Add(subtitle);

            Panel card = new Panel {
                Location = new Point(20, 110), Size = new Size(520, 76),
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            status.AutoSize = false; status.Location = new Point(16, 11); status.Size = new Size(486, 25);
            status.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold);
            status.ForeColor = Color.FromArgb(22, 101, 52);
            hotkeys.AutoSize = false; hotkeys.Location = new Point(16, 42); hotkeys.Size = new Size(486, 22);
            hotkeys.ForeColor = Color.FromArgb(75, 85, 99);
            card.Controls.Add(status); card.Controls.Add(hotkeys);

            GroupBox rangeCard = new GroupBox {
                Text = "翻译范围与显示", Location = new Point(20, 196), Size = new Size(520, 224),
                BackColor = Color.White, ForeColor = Color.FromArgb(55, 65, 81)
            };
            range.Minimum = 1; range.Maximum = 3; range.TickStyle = TickStyle.TopLeft;
            range.TickFrequency = 1; range.SmallChange = 1; range.LargeChange = 1;
            range.Location = new Point(15, 19); range.Size = new Size(340, 42);
            range.Value = overlay == null ? 2 : (int)overlay.TranslationRangeMode;
            rangeValue.Location = new Point(370, 23); rangeValue.Size = new Size(130, 26);
            rangeValue.TextAlign = ContentAlignment.MiddleCenter;
            rangeValue.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            rangeValue.ForeColor = Color.FromArgb(37, 99, 235);
            Label rangeLabels = new Label {
                Text = "兼容                              推荐                              精简",
                Location = new Point(19, 60), Size = new Size(332, 20),
                ForeColor = Color.FromArgb(107, 114, 128)
            };
            range.ValueChanged += delegate {
                if (overlay != null) overlay.ApplyTranslationRangeMode((TranslationRangeMode)range.Value);
                RefreshRangeText();
            };
            continuousTranslation.Text = "持续自动翻译（检测到游戏面板即显示）";
            continuousTranslation.AutoSize = false;
            continuousTranslation.Location = new Point(18, 87);
            continuousTranslation.Size = new Size(480, 28);
            continuousTranslation.ForeColor = Color.FromArgb(31, 41, 55);
            continuousTranslation.Checked = overlay != null && overlay.ContinuousTranslationEnabled;
            continuousTranslation.CheckedChanged += delegate {
                if (overlay != null) overlay.ApplyContinuousTranslation(continuousTranslation.Checked);
            };
            Label continuousHint = new Label {
                Text = "默认关闭；开启后智能降频，F8翻译开关、F9对齐聊天框。",
                Location = new Point(35, 119), Size = new Size(460, 24),
                ForeColor = Color.FromArgb(107, 114, 128)
            };
            Label independentWindow = new Label {
                Text = "AI实时翻译会自动打开独立浮窗（跟随聊天颜色）",
                AutoSize = false, Location = new Point(18, 148), Size = new Size(480, 28),
                ForeColor = Color.FromArgb(31, 41, 55),
                Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold)
            };
            Label independentHint = new Label {
                Text = "只显示AI聊天译文；F8仍独立控制游戏原位置覆盖。",
                Location = new Point(35, 180), Size = new Size(460, 24),
                ForeColor = Color.FromArgb(107, 114, 128)
            };
            rangeCard.Controls.Add(independentHint); rangeCard.Controls.Add(independentWindow);
            rangeCard.Controls.Add(continuousHint); rangeCard.Controls.Add(continuousTranslation);
            rangeCard.Controls.Add(rangeValue); rangeCard.Controls.Add(rangeLabels); rangeCard.Controls.Add(range);

            Button ready = MakeButton("缩到托盘，开始使用", new Point(20, 434), new Size(520, 42), true);
            ready.Click += delegate { Hide(); };
            Button dictionary = MakeButton("词库", new Point(20, 488), new Size(124, 38), false);
            dictionary.Click += delegate { overlay.ShowDictionaryEditor(); };
            Button shortcut = MakeButton("快捷键", new Point(152, 488), new Size(124, 38), false);
            shortcut.Click += delegate { overlay.ShowHotkeyEditor(); };
            Button ai = MakeButton("AI 翻译", new Point(284, 488), new Size(124, 38), false);
            ai.Click += delegate { overlay.ShowChatTranslator(); };
            Button update = MakeButton("一键更新", new Point(416, 488), new Size(124, 38), false);
            update.Enabled = overlay != null;
            update.Click += async delegate { await overlay.InstallLatestApplicationAsync(update); };
            Label hint = new Label {
                Text = "F8 翻译开/关；F9 自动对齐聊天框；关闭后仍在托盘。",
                Location = new Point(22, 534), Size = new Size(516, 32),
                ForeColor = Color.FromArgb(107, 114, 128), TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.Add(hint); Controls.Add(update); Controls.Add(ai); Controls.Add(shortcut); Controls.Add(dictionary);
            Controls.Add(ready); Controls.Add(rangeCard); Controls.Add(card); Controls.Add(header);
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        private static Button MakeButton(string text, Point location, Size size, bool primary)
        {
            Button button = new Button {
                Text = text, Location = location, Size = size, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold),
                BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(31, 41, 55)
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(209, 213, 219);
            return button;
        }

        internal void RefreshStatus()
        {
            int dictionaryCount = overlay == null ? 50000 : overlay.DictionaryEntryCount;
            int taskCount = overlay == null ? 12000 : overlay.TaskEntryCount;
            string showKey = overlay == null ? "F8" : overlay.ShowHotkeyDescription;
            string hideKey = overlay == null ? "F9" : overlay.HideHotkeyDescription;
            status.Text = "已就绪 · 词库 " + dictionaryCount + " 条 · 任务文本 " + taskCount + " 条";
            string gamepadShow = overlay == null ? "未绑定" : overlay.ShowGamepadShortcutDescription;
            string gamepadHide = overlay == null ? "未绑定" : overlay.HideGamepadShortcutDescription;
            hotkeys.Text = "键盘 " + showKey + " 翻译 / " + hideKey + " 对齐聊天框" +
                "    手柄 " + gamepadShow + " 翻译 / " + gamepadHide + " 隐藏";
            if (overlay != null && overlay.HotkeyRegistrationStatus.Length > 0)
                hotkeys.Text = overlay.HotkeyRegistrationStatus;
            if (overlay != null && range.Value != (int)overlay.TranslationRangeMode)
                range.Value = (int)overlay.TranslationRangeMode;
            if (overlay != null && continuousTranslation.Checked != overlay.ContinuousTranslationEnabled)
                continuousTranslation.Checked = overlay.ContinuousTranslationEnabled;
            RefreshRangeText();
        }

        private void RefreshRangeText()
        {
            rangeValue.Text = range.Value == 1 ? "兼容最大" :
                (range.Value == 2 ? "推荐均衡" : "精简最小");
        }
    }

    internal sealed class HotkeyForm : Form
    {
        private sealed class GamepadButtonItem
        {
            public readonly GamepadButton Button;
            private readonly string text;

            public GamepadButtonItem(GamepadButton button, string label)
            {
                Button = button;
                text = label;
            }

            public override string ToString() { return text; }
        }

        private readonly OverlayForm overlay;
        private readonly ComboBox showKey = new ComboBox();
        private readonly ComboBox hideKey = new ComboBox();
        private readonly TextBox showModifiers = new TextBox();
        private readonly TextBox hideModifiers = new TextBox();
        private readonly ComboBox showGamepadFirst = new ComboBox();
        private readonly ComboBox showGamepadSecond = new ComboBox();
        private readonly ComboBox hideGamepadFirst = new ComboBox();
        private readonly ComboBox hideGamepadSecond = new ComboBox();

        public HotkeyForm(OverlayForm owner)
        {
            overlay = owner;
            Text = "更改快捷键";
            Icon = SystemIcons.Information;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(590, 380);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BuildUi();
            showKey.SelectedItem = overlay == null ? "F8" : overlay.ShowKey.ToString();
            hideKey.SelectedItem = overlay == null ? "F9" : overlay.HideKey.ToString();
            showModifiers.Text = overlay == null ? "" : OverlayForm.ModifiersText(overlay.ShowModifiers);
            hideModifiers.Text = overlay == null ? "" : OverlayForm.ModifiersText(overlay.HideModifiers);
            SelectGamepadButton(showGamepadFirst, overlay == null ? GamepadButton.None : overlay.ShowGamepadShortcut.First);
            SelectGamepadButton(showGamepadSecond, overlay == null ? GamepadButton.None : overlay.ShowGamepadShortcut.Second);
            SelectGamepadButton(hideGamepadFirst, overlay == null ? GamepadButton.None : overlay.HideGamepadShortcut.First);
            SelectGamepadButton(hideGamepadSecond, overlay == null ? GamepadButton.None : overlay.HideGamepadShortcut.Second);
            UpdateGamepadSecondBoxes();
        }

        private void BuildUi()
        {
            FillKeys(showKey); FillKeys(hideKey);
            GroupBox keyboard = new GroupBox { Text = "键盘快捷键", Location = new Point(16, 12), Size = new Size(558, 142) };
            Label showLabel = new Label { Text = "翻译开/关：", Location = new Point(14, 29), AutoSize = true };
            showKey.Location = new Point(115, 25);
            showModifiers.Location = new Point(230, 25); showModifiers.Width = 300;
            Label hideLabel = new Label { Text = "自动对齐聊天框：", Location = new Point(14, 70), AutoSize = true };
            hideKey.Location = new Point(115, 66);
            hideModifiers.Location = new Point(230, 66); hideModifiers.Width = 300;
            Label hint = new Label { Text = "右侧可留空；组合键用 + 连接，例如 CTRL+ALT。", Location = new Point(14, 102), Size = new Size(520, 28), ForeColor = Color.DimGray };
            keyboard.Controls.AddRange(new Control[] { showLabel, showKey, showModifiers, hideLabel, hideKey, hideModifiers, hint });

            GroupBox gamepad = new GroupBox { Text = "手柄快捷键（独立于键盘）", Location = new Point(16, 162), Size = new Size(558, 150) };
            FillGamepadButtons(showGamepadFirst, false); FillGamepadButtons(showGamepadSecond, true);
            FillGamepadButtons(hideGamepadFirst, false); FillGamepadButtons(hideGamepadSecond, true);
            Label gamepadShowLabel = new Label { Text = "翻译开/关：", Location = new Point(14, 30), AutoSize = true };
            Label gamepadHideLabel = new Label { Text = "隐藏翻译：", Location = new Point(14, 72), AutoSize = true };
            showGamepadFirst.Location = new Point(115, 26); showGamepadFirst.Width = 170;
            showGamepadSecond.Location = new Point(320, 26); showGamepadSecond.Width = 210;
            hideGamepadFirst.Location = new Point(115, 68); hideGamepadFirst.Width = 170;
            hideGamepadSecond.Location = new Point(320, 68); hideGamepadSecond.Width = 210;
            Label plus1 = new Label { Text = "+", Location = new Point(296, 30), AutoSize = true };
            Label plus2 = new Label { Text = "+", Location = new Point(296, 72), AutoSize = true };
            Label gamepadHint = new Label {
                Text = "支持单键或双键同时按；松开后才能再次触发。兼容 XInput。",
                Location = new Point(14, 110), Size = new Size(520, 28), ForeColor = Color.DimGray
            };
            showGamepadFirst.SelectedIndexChanged += delegate { UpdateGamepadSecondBoxes(); };
            hideGamepadFirst.SelectedIndexChanged += delegate { UpdateGamepadSecondBoxes(); };
            gamepad.Controls.AddRange(new Control[] { gamepadShowLabel, showGamepadFirst, plus1, showGamepadSecond,
                gamepadHideLabel, hideGamepadFirst, plus2, hideGamepadSecond, gamepadHint });

            Button save = new Button { Text = "保存并生效", Location = new Point(410, 326), Size = new Size(164, 38) };
            save.Click += delegate { SaveSettings(); };
            Controls.AddRange(new Control[] { keyboard, gamepad, save });
        }

        private static void FillKeys(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = 88;
            box.Items.AddRange(new object[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Home", "End", "Insert", "Delete", "PageUp", "PageDown" });
        }

        private static void FillGamepadButtons(ComboBox box, bool second)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.Add(new GamepadButtonItem(GamepadButton.None, second ? "无（单键）" : "未绑定"));
            foreach (GamepadButton button in GamepadButtonNames.SelectableButtons())
                box.Items.Add(new GamepadButtonItem(button, GamepadButtonNames.Display(button)));
            box.SelectedIndex = 0;
        }

        private static void SelectGamepadButton(ComboBox box, GamepadButton wanted)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                GamepadButtonItem item = box.Items[i] as GamepadButtonItem;
                if (item != null && item.Button == wanted) { box.SelectedIndex = i; return; }
            }
            box.SelectedIndex = 0;
        }

        private static GamepadButton SelectedGamepadButton(ComboBox box)
        {
            GamepadButtonItem item = box.SelectedItem as GamepadButtonItem;
            return item == null ? GamepadButton.None : item.Button;
        }

        private void UpdateGamepadSecondBoxes()
        {
            showGamepadSecond.Enabled = SelectedGamepadButton(showGamepadFirst) != GamepadButton.None;
            hideGamepadSecond.Enabled = SelectedGamepadButton(hideGamepadFirst) != GamepadButton.None;
            if (!showGamepadSecond.Enabled) showGamepadSecond.SelectedIndex = 0;
            if (!hideGamepadSecond.Enabled) hideGamepadSecond.SelectedIndex = 0;
        }

        private void SaveSettings()
        {
            try
            {
                Keys sk = (Keys)Enum.Parse(typeof(Keys), Convert.ToString(showKey.SelectedItem), true);
                Keys hk = (Keys)Enum.Parse(typeof(Keys), Convert.ToString(hideKey.SelectedItem), true);
                uint sm = OverlayForm.ParseModifiers(showModifiers.Text);
                uint hm = OverlayForm.ParseModifiers(hideModifiers.Text);
                if (sk == hk && sm == hm) { MessageBox.Show("两个功能不能用完全相同的快捷键。", "快捷键"); return; }
                GamepadButton showFirst = SelectedGamepadButton(showGamepadFirst);
                GamepadButton showSecond = SelectedGamepadButton(showGamepadSecond);
                GamepadButton hideFirst = SelectedGamepadButton(hideGamepadFirst);
                GamepadButton hideSecond = SelectedGamepadButton(hideGamepadSecond);
                if ((showSecond != GamepadButton.None && showFirst == showSecond) ||
                    (hideSecond != GamepadButton.None && hideFirst == hideSecond))
                {
                    MessageBox.Show("双键组合不能选择两个相同按键。", "快捷键");
                    return;
                }
                GamepadShortcut gs = new GamepadShortcut(showFirst, showSecond);
                GamepadShortcut gh = new GamepadShortcut(hideFirst, hideSecond);
                if (gs.ConflictsWith(gh))
                {
                    MessageBox.Show("手柄呼出和缩回组合会同时触发，请换成不重叠的按键。", "快捷键");
                    return;
                }
                if (!overlay.ApplyHotkeys(sk, sm, hk, hm)) return;
                overlay.ApplyGamepadShortcuts(gs, gh);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                {
                    key.SetValue("ShowKey", sk.ToString());
                    key.SetValue("ShowModifiers", OverlayForm.ModifiersText(sm));
                    key.SetValue("HideKey", hk.ToString());
                    key.SetValue("HideModifiers", OverlayForm.ModifiersText(hm));
                    key.SetValue("GamepadShowShortcut", gs.Serialize());
                    key.SetValue("GamepadHideShortcut", gh.Serialize());
                }
                Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败"); }
        }
    }

    internal sealed class DictionaryOnlyForm : Form
    {
        private readonly OverlayForm overlay;
        private readonly string path;
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox search = new TextBox();
        private readonly Label status = new Label();
        private readonly TabControl categoryPages = new TabControl();
        private readonly ComboBox skillJobFilter = new ComboBox();
        private readonly DataGridView taskGrid = new DataGridView();
        private readonly TextBox taskSearch = new TextBox();
        private readonly ComboBox taskMapFilter = new ComboBox();
        private readonly List<TaskDictionaryRow> taskRows = new List<TaskDictionaryRow>();

        public DictionaryOnlyForm(OverlayForm owner, string baseDir)
        {
            overlay = owner;
            path = Path.Combine(baseDir, "枫语幕词库.tsv");
            Text = "打开并更改词库";
            Icon = SystemIcons.Information;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(680, 480);
            Size = new Size(900, 650);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BuildUi();
            LoadRows();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage normalTab = new TabPage("常规词库");
            TabPage taskTab = new TabPage("任务词库");
            TabPage thanksTab = new TabPage("鸣谢与资料来源");
            normalTab.Controls.Add(root);
            tabs.TabPages.Add(normalTab);
            tabs.TabPages.Add(taskTab);
            tabs.TabPages.Add(thanksTab);
            Controls.Add(tabs);

            TableLayoutPanel thanks = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 3 };
            thanks.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            thanks.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            thanks.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            thanks.Controls.Add(new Label { Text = "鸣谢", Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold), AutoSize = true }, 0, 0);
            TextBox thanksText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window, Font = new Font("Microsoft YaHei UI", 10),
                Text = "枫语幕的怀旧服任务、地图、道具和术语整理参考了玩家社区公开资料。\r\n\r\n" +
                    "特别鸣谢 MSCW Guidebook（冒险岛怀旧服资料站）及其制作者/群主，为怀旧服玩家整理并维护资料。\r\n" +
                    "资料站：https://mscw-guidebook.com/\r\n\r\n" +
                    "同时感谢：冒险岛小册子、NiaMeowDB、参与校对词库的QQ群玩家。\r\n" +
                    "实时AI翻译功能来自 @奇怪小鸭。\r\n\r\n" +
                    "本站和社区资料仅用于查询、术语核对与翻译适配；若游戏实际内容发生变化，以当前客户端显示为准。" };
            thanks.Controls.Add(thanksText, 0, 1);
            LinkLabel guideLink = new LinkLabel { Text = "打开 MSCW Guidebook", AutoSize = true, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Underline) };
            guideLink.LinkClicked += delegate { try { Process.Start("https://mscw-guidebook.com/"); } catch { } };
            thanks.Controls.Add(guideLink, 0, 2);
            thanksTab.Controls.Add(thanks);

            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            top.Controls.Add(new Label { Text = "搜索：", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
            search.Width = 260; search.TextChanged += delegate { FilterRows(); }; top.Controls.Add(search);
            Button add = new Button { Text = "新增", AutoSize = true };
            add.Click += delegate { int i = grid.Rows.Add("", "", "自定义", ""); grid.CurrentCell = grid.Rows[i].Cells[0]; grid.BeginEdit(true); };
            Button delete = new Button { Text = "删除选中", AutoSize = true };
            delete.Click += delegate { foreach (DataGridViewRow row in grid.SelectedRows) if (!row.IsNewRow) grid.Rows.Remove(row); };
            Button save = new Button { Text = "保存并载入内存", AutoSize = true };
            save.Click += delegate { SaveRows(); };
            top.Controls.Add(add); top.Controls.Add(delete); top.Controls.Add(save);
            status.AutoSize = true; status.Padding = new Padding(10, 8, 0, 0); status.ForeColor = Color.DarkGreen; top.Controls.Add(status);
            root.Controls.Add(top, 0, 0);

            categoryPages.Dock = DockStyle.Fill;
            string[] pageNames = { "全部", "界面", "地图/NPC", "怪物", "职业技能", "装备", "消耗品", "任务/其他物品", "详情说明", "图标/其他" };
            foreach (string pageName in pageNames) categoryPages.TabPages.Add(new TabPage(pageName));
            categoryPages.SelectedIndexChanged += delegate { UpdateSkillJobFilter(); FilterRows(); };
            root.Controls.Add(categoryPages, 0, 1);

            skillJobFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            skillJobFilter.Width = 130;
            skillJobFilter.Items.AddRange(new object[] { "全部职业", "新手", "战士", "剑客", "准骑士", "枪战士", "魔法师", "火毒法师", "冰雷法师", "牧师", "弓箭手", "猎人", "弩弓手", "飞侠", "刺客", "侠客" });
            skillJobFilter.SelectedIndex = 0;
            skillJobFilter.SelectedIndexChanged += delegate { FilterRows(); };
            top.Controls.Add(skillJobFilter);
            UpdateSkillJobFilter();

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("English", "英文原文");
            grid.Columns.Add("Chinese", "中文翻译");
            grid.Columns.Add("Category", "分类/版本备注");
            grid.Columns.Add("IconHash", "图标指纹");
            grid.Columns[3].Visible = false;
            grid.Columns[0].FillWeight = 42; grid.Columns[1].FillWeight = 38; grid.Columns[2].FillWeight = 20;
            root.Controls.Add(grid, 0, 2);

            GroupBox qq = new GroupBox { Text = "QQ群共享", Dock = DockStyle.Fill };
            FlowLayoutPanel share = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), WrapContents = false };
            Button import = new Button { Text = "导入群友词库", AutoSize = true };
            import.Click += delegate { ImportRows(); };
            Button export = new Button { Text = "导出QQ群分享包", AutoSize = true };
            export.Click += delegate { ExportRows(); };
            share.Controls.Add(import); share.Controls.Add(export);
            share.Controls.Add(new Label { Text = "导入后先检查，点击上方“保存并载入内存”才会生效。", AutoSize = true, Padding = new Padding(12, 7, 0, 0), ForeColor = Color.DimGray });
            qq.Controls.Add(share); root.Controls.Add(qq, 0, 3);

            taskGrid.Dock = DockStyle.Fill;
            taskGrid.AllowUserToAddRows = false;
            taskGrid.AllowUserToDeleteRows = false;
            taskGrid.ReadOnly = true;
            taskGrid.RowHeadersVisible = false;
            taskGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            taskGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            taskGrid.Columns.Add("StartMap", "接取地图/区域");
            taskGrid.Columns.Add("TaskId", "任务代码");
            taskGrid.Columns.Add("English", "英文任务名");
            taskGrid.Columns.Add("Chinese", "中文任务名");
            taskGrid.Columns.Add("TextCount", "说明/对白数量");
            taskGrid.Columns[0].FillWeight = 22;
            taskGrid.Columns[1].FillWeight = 13;
            taskGrid.Columns[2].FillWeight = 28;
            taskGrid.Columns[3].FillWeight = 28;
            taskGrid.Columns[4].FillWeight = 13;
            taskGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) {
                if (e.RowIndex < 0) return;
                string taskId = Convert.ToString(taskGrid.Rows[e.RowIndex].Cells[1].Value);
                using (TaskEditorForm editor = new TaskEditorForm(taskId, taskRows)) editor.ShowDialog(this);
                RefreshTaskGrid();
            };
            Panel taskPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            FlowLayoutPanel taskTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false };
            taskTop.Controls.Add(new Label { Text = "搜索任务代码或名称：", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
            taskSearch.Width = 190;
            taskSearch.TextChanged += delegate { FilterTaskRows(); };
            taskTop.Controls.Add(taskSearch);
            taskTop.Controls.Add(new Label { Text = "接取地图：", AutoSize = true, Padding = new Padding(12, 8, 0, 0) });
            taskMapFilter.DropDownStyle = ComboBoxStyle.DropDownList; taskMapFilter.Width = 130;
            taskMapFilter.SelectedIndexChanged += delegate { FilterTaskRows(); };
            taskTop.Controls.Add(taskMapFilter);
            Button addTask = new Button { Text = "新增任务", AutoSize = true };
            addTask.Click += delegate { AddTask(); };
            taskTop.Controls.Add(addTask);
            Button taskSave = new Button { Text = "保存全部并载入内存", AutoSize = true };
            taskSave.Click += delegate { SaveRows(); };
            taskTop.Controls.Add(taskSave);
            Label taskHint = new Label { Dock = DockStyle.Bottom, Height = 32,
                Text = "任务按接取地图/区域分类。可选择地图筛选；双击任务可编辑地图、名称、说明和完整对白。",
                ForeColor = Color.DimGray };
            taskPanel.Controls.Add(taskGrid);
            taskPanel.Controls.Add(taskHint);
            taskPanel.Controls.Add(taskTop);
            taskTab.Controls.Add(taskPanel);
        }

        private void AddTask()
        {
            using (NewTaskForm dialog = new NewTaskForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (taskRows.Exists(delegate(TaskDictionaryRow row) { return row.TaskId == dialog.TaskId; }))
                {
                    MessageBox.Show("任务代码 " + dialog.TaskId + " 已经存在，可在列表中双击编辑。", "新增任务");
                    return;
                }
                taskRows.Add(new TaskDictionaryRow { TaskId = dialog.TaskId,
                    English = dialog.EnglishName, Chinese = dialog.ChineseName,
                    Category = "怀旧服-任务#" + dialog.TaskId, IconHash = "", StartMap = dialog.StartMap });
                RefreshTaskGrid();
                using (TaskEditorForm editor = new TaskEditorForm(dialog.TaskId, taskRows)) editor.ShowDialog(this);
                RefreshTaskGrid();
                status.Text = "已新增任务 " + dialog.TaskId + "，请点击保存全部并载入内存";
            }
        }

        private void LoadRows()
        {
            grid.Rows.Clear();
            taskRows.Clear();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] p = raw.Split('\t');
                if (p.Length >= 2)
                {
                    string category = p.Length > 2 ? p[2].Trim() : "";
                    if (category.StartsWith("怀旧服-任务", StringComparison.Ordinal) && category.LastIndexOf('#') >= 0)
                    {
                        taskRows.Add(new TaskDictionaryRow { English = p[0].Trim(), Chinese = p[1].Trim(),
                            Category = category, IconHash = p.Length > 3 ? p[3].Trim() : "",
                            TaskId = category.Substring(category.LastIndexOf('#') + 1),
                            StartMap = p.Length > 4 ? p[4].Trim() : "" });
                    }
                    else grid.Rows.Add(p[0].Trim(), p[1].Trim(), category, p.Length > 3 ? p[3].Trim() : "");
                }
            }
            RefreshTaskGrid();
            status.Text = (grid.Rows.Count - 1) + " 条常规，" + taskRows.Count + " 条任务";
        }

        private void RefreshTaskGrid()
        {
            taskGrid.Rows.Clear();
            Dictionary<string, string[]> tasks = new Dictionary<string, string[]>();
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (TaskDictionaryRow row in taskRows)
            {
                string[] value;
                if (!tasks.TryGetValue(row.TaskId, out value))
                {
                    value = new string[] { "", "", "" };
                    tasks[row.TaskId] = value;
                    counts[row.TaskId] = 0;
                }
                if (row.Category.StartsWith("怀旧服-任务#", StringComparison.Ordinal))
                {
                    value[0] = row.English; value[1] = row.Chinese; value[2] = row.StartMap;
                }
                else counts[row.TaskId] = counts[row.TaskId] + 1;
            }
            List<KeyValuePair<string, string[]>> ordered = new List<KeyValuePair<string, string[]>>(tasks);
            ordered.Sort(delegate(KeyValuePair<string, string[]> a, KeyValuePair<string, string[]> b) {
                int map = StringComparer.CurrentCultureIgnoreCase.Compare(a.Value[2], b.Value[2]);
                return map != 0 ? map : StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key);
            });
            string selectedMap = taskMapFilter.SelectedItem == null ? "全部地图" : Convert.ToString(taskMapFilter.SelectedItem);
            SortedSet<string> maps = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (KeyValuePair<string, string[]> task in ordered)
            {
                string map = String.IsNullOrWhiteSpace(task.Value[2]) ? "未分类" : task.Value[2];
                maps.Add(map);
                taskGrid.Rows.Add(map, task.Key, task.Value[0], task.Value[1], counts[task.Key]);
            }
            taskMapFilter.BeginUpdate(); taskMapFilter.Items.Clear(); taskMapFilter.Items.Add("全部地图");
            foreach (string map in maps) taskMapFilter.Items.Add(map);
            taskMapFilter.SelectedItem = taskMapFilter.Items.Contains(selectedMap) ? selectedMap : "全部地图";
            taskMapFilter.EndUpdate();
            FilterTaskRows();
        }

        private void FilterTaskRows()
        {
            string query = taskSearch.Text.Trim();
            string selectedMap = taskMapFilter.SelectedItem == null ? "全部地图" : Convert.ToString(taskMapFilter.SelectedItem);
            taskGrid.CurrentCell = null;
            foreach (DataGridViewRow row in taskGrid.Rows)
            {
                bool visible = query.Length == 0;
                for (int i = 0; !visible && i < 4; i++)
                    visible = Convert.ToString(row.Cells[i].Value).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                if (selectedMap != "全部地图" && Convert.ToString(row.Cells[0].Value) != selectedMap) visible = false;
                row.Visible = visible;
            }
        }

        private void FilterRows()
        {
            string q = search.Text.Trim(); grid.CurrentCell = null;
            string page = categoryPages.SelectedTab == null ? "全部" : categoryPages.SelectedTab.Text;
            string job = skillJobFilter.SelectedItem == null ? "全部职业" : Convert.ToString(skillJobFilter.SelectedItem);
            int visibleCount = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                bool visible = q.Length == 0;
                for (int i = 0; !visible && i < 3; i++) visible = Convert.ToString(row.Cells[i].Value).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                string category = Convert.ToString(row.Cells[2].Value);
                if (!MatchesCategoryPage(category, page)) visible = false;
                if (visible && page == "图标/其他" && !HasIconHash(Convert.ToString(row.Cells[3].Value))) visible = false;
                if (visible && page == "职业技能" && job != "全部职业" && SkillJob(category) != job) visible = false;
                row.Visible = visible;
                if (visible) visibleCount++;
            }
            status.Text = "当前分类 " + visibleCount + " 条；全部常规 " + Math.Max(0, grid.Rows.Count - 1) + " 条";
        }

        private void UpdateSkillJobFilter()
        {
            skillJobFilter.Visible = categoryPages.SelectedTab != null && categoryPages.SelectedTab.Text == "职业技能";
        }

        private static string CategoryId(string category)
        {
            int marker = category.LastIndexOf('#');
            return marker >= 0 && marker + 1 < category.Length ? category.Substring(marker + 1) : "";
        }

        private static bool HasIconHash(string value)
        {
            UInt64 parsed;
            return !String.IsNullOrWhiteSpace(value) && value.Trim().Length <= 16 &&
                UInt64.TryParse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool MatchesCategoryPage(string category, string page)
        {
            if (page == "全部") return true;
            if (page == "界面") return category.StartsWith("怀旧服-界面", StringComparison.Ordinal);
            if (page == "地图/NPC") return category.StartsWith("怀旧服-地图", StringComparison.Ordinal) || category.StartsWith("怀旧服-NPC", StringComparison.Ordinal) || category.StartsWith("怀旧服-地区", StringComparison.Ordinal);
            if (page == "怪物") return category.StartsWith("怀旧服-怪物", StringComparison.Ordinal);
            if (page == "职业技能") return category.StartsWith("怀旧服-技能#", StringComparison.Ordinal) || category.StartsWith("怀旧服-职业", StringComparison.Ordinal);
            if (page == "详情说明") return category.IndexOf("说明#", StringComparison.Ordinal) >= 0;
            if (category.StartsWith("怀旧服-道具#", StringComparison.Ordinal))
            {
                string id = CategoryId(category);
                if (page == "装备") return id.StartsWith("1", StringComparison.Ordinal);
                if (page == "消耗品") return id.StartsWith("2", StringComparison.Ordinal);
                if (page == "任务/其他物品") return id.StartsWith("3", StringComparison.Ordinal) || id.StartsWith("4", StringComparison.Ordinal) || id.StartsWith("5", StringComparison.Ordinal);
            }
            if (page == "图标/其他") return true;
            return false;
        }

        private static string SkillJob(string category)
        {
            long id;
            if (!Int64.TryParse(CategoryId(category), out id) || id < 1000000) return "新手";
            int job = (int)(id / 10000);
            if (job == 100) return "战士"; if (job == 110 || job == 111) return "剑客";
            if (job == 120 || job == 121) return "准骑士"; if (job == 130 || job == 131) return "枪战士";
            if (job == 200) return "魔法师"; if (job == 210 || job == 211) return "火毒法师";
            if (job == 220 || job == 221) return "冰雷法师"; if (job == 230 || job == 231) return "牧师";
            if (job == 300) return "弓箭手"; if (job == 310 || job == 311) return "猎人";
            if (job == 320 || job == 321) return "弩弓手"; if (job == 400) return "飞侠";
            if (job == 410 || job == 411) return "刺客"; if (job == 420 || job == 421) return "侠客";
            return "全部职业";
        }

        internal string RunCategorySelfTest()
        {
            StringBuilder report = new StringBuilder();
            foreach (TabPage page in categoryPages.TabPages)
            {
                categoryPages.SelectedTab = page;
                FilterRows();
                int visible = 0;
                foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow && row.Visible) visible++;
                report.Append(page.Text).Append('=').Append(visible).AppendLine();
            }
            categoryPages.SelectedIndex = 4;
            foreach (object item in skillJobFilter.Items)
            {
                skillJobFilter.SelectedItem = item;
                FilterRows();
                int visible = 0;
                foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow && row.Visible) visible++;
                report.Append("职业/").Append(Convert.ToString(item)).Append('=').Append(visible).AppendLine();
            }
            return report.ToString();
        }

        private static string Clean(object value)
        {
            return Convert.ToString(value).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private string SerializeRows()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("# 枫语幕词库：英文<Tab>中文<Tab>分类/资料ID<Tab>可选图标指纹");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string en = Clean(row.Cells[0].Value), zh = Clean(row.Cells[1].Value), cat = Clean(row.Cells[2].Value);
                string iconHash = Clean(row.Cells[3].Value);
                if (en.Length == 0 || zh.Length == 0 || !seen.Add(en + "\t" + cat)) continue;
                b.Append(en).Append('\t').Append(zh).Append('\t').Append(cat);
                if (iconHash.Length > 0) b.Append('\t').Append(iconHash);
                b.AppendLine();
            }
            foreach (TaskDictionaryRow row in taskRows)
            {
                string en = Clean(row.English), zh = Clean(row.Chinese), category = Clean(row.Category);
                if (en.Length == 0 || zh.Length == 0 || category.Length == 0) continue;
                b.Append(en).Append('\t').Append(zh).Append('\t').Append(category);
                if (!String.IsNullOrWhiteSpace(row.IconHash) || !String.IsNullOrWhiteSpace(row.StartMap))
                    b.Append('\t').Append(Clean(row.IconHash));
                if (!String.IsNullOrWhiteSpace(row.StartMap)) b.Append('\t').Append(Clean(row.StartMap));
                b.AppendLine();
            }
            return b.ToString();
        }

        private void SaveRows()
        {
            try
            {
                File.WriteAllText(path, SerializeRows(), new UTF8Encoding(true));
                overlay.ReloadDictionary(); LoadRows(); status.Text = "已保存并生效";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败"); }
        }

        private void ImportRows()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "枫语幕词库 (*.tsv)|*.tsv|文本文件 (*.txt)|*.txt";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Merge(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            }
        }

        private void Merge(string text)
        {
            Dictionary<string, string[]> all = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow)
            {
                string english = Clean(row.Cells[0].Value), category = Clean(row.Cells[2].Value);
                all[english + "\t" + category] = new string[] { english,
                    Clean(row.Cells[1].Value), category, Clean(row.Cells[3].Value) };
            }
            int count = 0;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] p = raw.Split('\t');
                if (p.Length < 2 || Clean(p[0]).Length == 0 || Clean(p[1]).Length == 0) continue;
                string key = Clean(p[0]);
                string category = p.Length > 2 ? Clean(p[2]) : "QQ群";
                if (category.StartsWith("怀旧服-任务", StringComparison.Ordinal) && category.LastIndexOf('#') >= 0)
                {
                    string taskId = category.Substring(category.LastIndexOf('#') + 1);
                    TaskDictionaryRow existing = taskRows.Find(delegate(TaskDictionaryRow row) {
                        return row.TaskId == taskId && String.Equals(row.English, key, StringComparison.OrdinalIgnoreCase);
                    });
                    if (existing == null)
                        taskRows.Add(new TaskDictionaryRow { TaskId = taskId, English = key,
                            Chinese = Clean(p[1]), Category = category, IconHash = p.Length > 3 ? Clean(p[3]) : "",
                            StartMap = p.Length > 4 ? Clean(p[4]) : "" });
                    else
                    {
                        existing.Chinese = Clean(p[1]); existing.Category = category;
                        if (p.Length > 3 && Clean(p[3]).Length > 0) existing.IconHash = Clean(p[3]);
                        if (p.Length > 4 && Clean(p[4]).Length > 0) existing.StartMap = Clean(p[4]);
                    }
                    count++;
                    continue;
                }
                string identity = key + "\t" + category;
                string preservedHash = all.ContainsKey(identity) && all[identity].Length > 3 ? all[identity][3] : "";
                all[identity] = new string[] { key, Clean(p[1]), category,
                    p.Length > 3 ? Clean(p[3]) : preservedHash }; count++;
            }
            grid.Rows.Clear(); foreach (KeyValuePair<string, string[]> p in all)
                grid.Rows.Add(p.Value[0], p.Value[1], p.Value[2], p.Value.Length > 3 ? p.Value[3] : "");
            RefreshTaskGrid();
            status.Text = "已合并 " + count + " 条，尚未保存";
        }

        private void ExportRows()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "枫语幕词库 (*.tsv)|*.tsv";
                dialog.FileName = "枫语幕词库_" + DateTime.Now.ToString("yyyyMMdd") + ".tsv";
                if (Directory.Exists(@"D:\GPT\文件")) dialog.InitialDirectory = @"D:\GPT\文件";
                if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, SerializeRows(), new UTF8Encoding(true));
            }
        }
    }

    internal sealed class NewTaskForm : Form
    {
        private readonly TextBox id = new TextBox();
        private readonly TextBox english = new TextBox();
        private readonly TextBox chinese = new TextBox();
        private readonly TextBox startMap = new TextBox();
        public string TaskId { get; private set; }
        public string EnglishName { get; private set; }
        public string ChineseName { get; private set; }
        public string StartMap { get; private set; }

        public NewTaskForm()
        {
            Text = "新增任务";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 250);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            AddField("任务代码：", id, 24);
            AddField("英文任务名：", english, 68);
            AddField("中文任务名：", chinese, 112);
            AddField("接取地图/区域：", startMap, 156);
            Button save = new Button { Text = "创建并编辑内容", Location = new Point(342, 202), Size = new Size(145, 31) };
            save.Click += delegate { AcceptTask(); };
            Controls.Add(save);
            AcceptButton = save;
        }

        private void AddField(string label, TextBox box, int y)
        {
            Controls.Add(new Label { Text = label, Location = new Point(22, y + 4), AutoSize = true });
            box.Location = new Point(126, y); box.Width = 360;
            Controls.Add(box);
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private void AcceptTask()
        {
            TaskId = Clean(id.Text); EnglishName = Clean(english.Text); ChineseName = Clean(chinese.Text); StartMap = Clean(startMap.Text);
            if (TaskId.Length == 0 || EnglishName.Length == 0 || ChineseName.Length == 0 || StartMap.Length == 0)
            {
                MessageBox.Show("任务代码、任务名和接取地图/区域都必须填写。", "新增任务");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class TaskEditorForm : Form
    {
        private readonly string taskId;
        private readonly List<TaskDictionaryRow> source;
        private readonly DataGridView grid = new DataGridView();
        private readonly ToolStripTextBox startMap = new ToolStripTextBox();

        public TaskEditorForm(string id, List<TaskDictionaryRow> rows)
        {
            taskId = id;
            source = rows;
            Text = "任务词库编辑｜任务代码 " + id;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 480);
            Size = new Size(980, 650);
            Font = new Font("Microsoft YaHei UI", 9.0f);

            ToolStrip tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            ToolStripButton addDialogue = new ToolStripButton("新增对白");
            ToolStripButton addDescription = new ToolStripButton("新增任务说明");
            ToolStripButton delete = new ToolStripButton("删除选中");
            ToolStripButton save = new ToolStripButton("保存本页并关闭");
            tools.Items.Add(new ToolStripLabel("接取地图/区域："));
            startMap.AutoSize = false; startMap.Width = 170; tools.Items.Add(startMap);
            addDialogue.Click += delegate { AddRow("怀旧服-任务对白#" + taskId); };
            addDescription.Click += delegate { AddRow("怀旧服-任务说明#" + taskId); };
            delete.Click += delegate {
                foreach (DataGridViewRow row in grid.SelectedRows) if (!row.IsNewRow) grid.Rows.Remove(row);
            };
            save.Click += delegate { SaveBack(); DialogResult = DialogResult.OK; Close(); };
            tools.Items.Add(addDialogue); tools.Items.Add(addDescription); tools.Items.Add(delete);
            tools.Items.Add(new ToolStripSeparator()); tools.Items.Add(save);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.Columns.Add("Type", "内容类型");
            grid.Columns.Add("English", "英文原文/完整句子");
            grid.Columns.Add("Chinese", "中文翻译");
            grid.Columns.Add("Source", "来源/翻译方式");
            grid.Columns[0].Width = 150;
            grid.Columns[0].ReadOnly = true;
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[3].Width = 170;
            grid.Columns[3].ReadOnly = true;
            grid.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            Controls.Add(grid);
            Controls.Add(tools);
            LoadRows();
        }

        private void LoadRows()
        {
            foreach (TaskDictionaryRow row in source)
                if (row.TaskId == taskId)
                {
                    grid.Rows.Add(row.Category, row.English, row.Chinese, row.IconHash);
                    if (startMap.Text.Length == 0 && !String.IsNullOrWhiteSpace(row.StartMap)) startMap.Text = row.StartMap;
                }
        }

        private void AddRow(string category)
        {
            int index = grid.Rows.Add(category, "", "", "玩家新增/修改");
            grid.CurrentCell = grid.Rows[index].Cells[1];
            grid.BeginEdit(true);
        }

        private static string CleanValue(object value)
        {
            return Convert.ToString(value).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private void SaveBack()
        {
            source.RemoveAll(delegate(TaskDictionaryRow row) { return row.TaskId == taskId; });
            foreach (DataGridViewRow row in grid.Rows)
            {
                string category = CleanValue(row.Cells[0].Value);
                string english = CleanValue(row.Cells[1].Value);
                string chinese = CleanValue(row.Cells[2].Value);
                string sourceNote = CleanValue(row.Cells[3].Value);
                if (category.Length == 0 || english.Length == 0 || chinese.Length == 0) continue;
                source.Add(new TaskDictionaryRow { TaskId = taskId, Category = category,
                    English = english, Chinese = chinese, IconHash = sourceNote,
                    StartMap = category.StartsWith("怀旧服-任务#", StringComparison.Ordinal) ? CleanValue(startMap.Text) : "" });
            }
        }
    }
}
