using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    internal enum ChatSourceCategory
    {
        Normal = 0,
        Buddy = 1,
        Party = 2,
        Guild = 3,
        Alliance = 4,
        Whisper = 5,
        System = 6,
        Notice = 7,
        Megaphone = 8
    }

    internal sealed class ChatVisualStyle
    {
        internal ChatVisualKind Kind;
        internal Color ForeColor;
        internal Color BackColor;
        internal bool HasBackground;
        internal ChatSourceCategory Category;

        internal static ChatVisualStyle Default
        {
            get
            {
                return new ChatVisualStyle {
                    Kind = ChatVisualKind.Chat,
                    ForeColor = Color.FromArgb(241, 245, 249),
                    BackColor = Color.FromArgb(18, 18, 22),
                    HasBackground = false,
                    Category = ChatSourceCategory.Normal
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
            bool blueMegaphone = coloredBackground && IsCoolMegaphoneBackground(sampledBackground);
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
                HasBackground = broadcast,
                Category = ClassifySource(sourceText, sampledForeground, kind)
            };
        }

        internal static ChatSourceCategory ClassifySource(string sourceText, Color foreground,
            ChatVisualKind visualKind)
        {
            if (visualKind == ChatVisualKind.Megaphone ||
                visualKind == ChatVisualKind.SuperMegaphone)
                return ChatSourceCategory.Megaphone;

            string text = StripTimePrefix(sourceText).TrimStart();
            if (Regex.IsMatch(text, @"^(\[\s*notice\s*\]|notice\s*[:：]|公告\s*[:：])",
                RegexOptions.IgnoreCase)) return ChatSourceCategory.Notice;
            if (Regex.IsMatch(text, @"^(\[\s*system\s*\]|system\s*[:：]|系统\s*[:：])",
                RegexOptions.IgnoreCase)) return ChatSourceCategory.System;
            if (HasChannelPrefix(text, "buddy", "好友")) return ChatSourceCategory.Buddy;
            if (HasChannelPrefix(text, "party", "组队")) return ChatSourceCategory.Party;
            if (HasChannelPrefix(text, "guild", "家族")) return ChatSourceCategory.Guild;
            if (HasChannelPrefix(text, "alliance", "联盟")) return ChatSourceCategory.Alliance;
            if (HasChannelPrefix(text, "whisper", "悄悄话")) return ChatSourceCategory.Whisper;

            // GMS chat colors stay stable while antialiasing, UI scale and capture scaling
            // shift individual pixels. Classify by the nearest color family, not one exact RGB.
            if (!foreground.IsEmpty)
            {
                Color[] anchors = {
                    Color.FromArgb(235, 235, 235), Color.FromArgb(235, 151, 62),
                    Color.FromArgb(194, 143, 235), Color.FromArgb(120, 70, 160),
                    Color.FromArgb(126, 205, 126), Color.FromArgb(50, 150, 80),
                    Color.FromArgb(225, 115, 145), Color.FromArgb(220, 190, 90)
                };
                ChatSourceCategory[] categories = {
                    ChatSourceCategory.Normal, ChatSourceCategory.Buddy,
                    ChatSourceCategory.Party, ChatSourceCategory.Guild,
                    ChatSourceCategory.Alliance, ChatSourceCategory.Whisper,
                    ChatSourceCategory.System, ChatSourceCategory.Notice
                };
                int best = Int32.MaxValue;
                int second = Int32.MaxValue;
                int bestIndex = 0;
                for (int index = 0; index < anchors.Length; index++)
                {
                    int distance = ColorDistance(foreground, anchors[index]);
                    if (distance < best)
                    {
                        second = best; best = distance; bestIndex = index;
                    }
                    else if (distance < second) second = distance;
                }
                // Conservative fallback: uncertain colors remain ordinary chat instead of
                // disappearing under a wrong filter.
                if (best <= 5200 && second - best >= 650) return categories[bestIndex];
            }
            return visualKind == ChatVisualKind.System
                ? ChatSourceCategory.System : ChatSourceCategory.Normal;
        }

        private static bool HasChannelPrefix(string text, string english, string chinese)
        {
            string pattern = "^(?:\\[\\s*(?:" + Regex.Escape(english) + "|" +
                Regex.Escape(chinese) + ")\\s*\\]|(?:" + Regex.Escape(english) + "|" +
                Regex.Escape(chinese) + ")\\s*[:：])";
            return Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase);
        }

        private static string StripTimePrefix(string value)
        {
            return Regex.Replace(value ?? "", @"^\s*\[\s*\d{1,2}:\d{2}\s*\]\s*", "");
        }

        private static int ColorDistance(Color left, Color right)
        {
            int red = left.R - right.R;
            int green = left.G - right.G;
            int blue = left.B - right.B;
            return red * red + green * green + blue * blue;
        }

        private static bool IsCoolMegaphoneBackground(Color color)
        {
            return !color.IsEmpty && color.G >= 125 && color.B >= 120 &&
                Math.Max(color.G, color.B) >= color.R + 20;
        }

        internal static bool LooksLikePlayerChatLine(string value)
        {
            string text = StripTimePrefix(value).TrimStart();
            // GMS character names contain no spaces. OCR may append CHxx before the colon.
            if (Regex.IsMatch(text,
                @"^[A-Za-z0-9_]{2,20}(?:\s+(?:(?:CH\s*)?[0-9Oo]{1,2}|[©®]))?\s*[:：•·]",
                RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(text,
                @"^[A-Za-z][A-Za-z0-9_]{2,23}\s+(?:C(?:H|W)?[O0A]?\d{1,3}\s*[:：•·]?|C[A-Z0-9]{0,4}\s*[:：•·])",
                RegexOptions.IgnoreCase)) return true;
            // At low video resolution the tiny channel badge can become «HÖl 0. The angle glyph
            // is a reliable separator after the character name, so this is still a player row.
            return Regex.IsMatch(text,
                @"^[A-Za-z][A-Za-z0-9_]{2,23}\s*[«»‹›]\s*[^\s:：•·]{0,8}\s+[0-9O@±]{1,4}\s*[:：•·]",
                RegexOptions.IgnoreCase);
        }

        internal static bool IsLikelyColoredBand(Color color)
        {
            if (color.IsEmpty) return false;
            int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            int minimum = Math.Min(color.R, Math.Min(color.G, color.B));
            return maximum >= 80 && maximum - minimum >= 28;
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

    internal sealed class ChatTranslationRecord
    {
        internal string Original = "";
        internal string Translation = "";
        internal string OutputLabel = "中文";
        internal ChatVisualStyle Style = ChatVisualStyle.Default;
    }

    internal sealed class ChatTranslationHistory
    {
        private readonly List<ChatTranslationRecord> records = new List<ChatTranslationRecord>();
        private readonly int maximumCharacters;

        internal ChatTranslationHistory(int characterLimit)
        {
            maximumCharacters = Math.Max(2000, characterLimit);
        }

        internal void Add(ChatTranslationRecord record)
        {
            if (record == null || String.IsNullOrWhiteSpace(record.Translation)) return;
            if (record.Style == null) record.Style = ChatVisualStyle.Default;
            records.Add(record);
            int characters = 0;
            for (int index = records.Count - 1; index >= 0; index--)
            {
                ChatTranslationRecord current = records[index];
                characters += (current.Original ?? "").Length +
                    (current.Translation ?? "").Length + 16;
                if (characters <= maximumCharacters) continue;
                records.RemoveRange(0, index + 1);
                break;
            }
        }

        internal List<ChatTranslationRecord> Select(ChatSourceCategory? category)
        {
            List<ChatTranslationRecord> result = new List<ChatTranslationRecord>();
            foreach (ChatTranslationRecord record in records)
                if (!category.HasValue || record.Style.Category == category.Value)
                    result.Add(record);
            return result;
        }
    }

    internal sealed class ChatCategoryFilterBar : TableLayoutPanel
    {
        private readonly Dictionary<Button, ChatSourceCategory?> filters =
            new Dictionary<Button, ChatSourceCategory?>();
        private ChatSourceCategory? selectedCategory;

        internal event EventHandler SelectedCategoryChanged;
        internal ChatSourceCategory? SelectedCategory { get { return selectedCategory; } }

        internal ChatCategoryFilterBar()
        {
            Dock = DockStyle.Fill;
            Height = 33;
            ColumnCount = 10;
            RowCount = 1;
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            BackColor = Color.FromArgb(13, 34, 59);
            Margin = Padding.Empty;
            Padding = new Padding(0, 2, 0, 2);
            for (int index = 0; index < ColumnCount; index++)
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10.0f));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            AddFilter("全部", null);
            AddFilter("普通", ChatSourceCategory.Normal);
            AddFilter("好友", ChatSourceCategory.Buddy);
            AddFilter("组队", ChatSourceCategory.Party);
            AddFilter("家族", ChatSourceCategory.Guild);
            AddFilter("联盟", ChatSourceCategory.Alliance);
            AddFilter("悄悄", ChatSourceCategory.Whisper);
            AddFilter("系统", ChatSourceCategory.System);
            AddFilter("公告", ChatSourceCategory.Notice);
            AddFilter("喇叭", ChatSourceCategory.Megaphone);
            RefreshSelection();
        }

        internal void SelectCategory(ChatSourceCategory? category)
        {
            if (selectedCategory == category) return;
            selectedCategory = category;
            RefreshSelection();
            EventHandler changed = SelectedCategoryChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        private void AddFilter(string text, ChatSourceCategory? category)
        {
            Button button = new FlatPillButton {
                Text = text, AutoSize = false, Dock = DockStyle.Fill,
                Margin = new Padding(1, 0, 1, 0), Padding = Padding.Empty,
                FlatStyle = FlatStyle.Flat,
                TabStop = false, Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            filters[button] = category;
            button.Click += delegate { SelectCategory(filters[button]); };
            Controls.Add(button, filters.Count - 1, 0);
        }

        private void RefreshSelection()
        {
            foreach (KeyValuePair<Button, ChatSourceCategory?> item in filters)
            {
                bool active = item.Value == selectedCategory;
                item.Key.BackColor = active ? Color.FromArgb(42, 103, 238) : BackColor;
                item.Key.ForeColor = active ? Color.White : Color.FromArgb(181, 194, 207);
                item.Key.Invalidate();
            }
        }
    }

    internal sealed class FlatPillButton : Button
    {
        internal FlatPillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath path =
                BlueGlassButton.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen border = new Pen(Color.FromArgb(96, 126, 164)))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
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
        private readonly TextBox outboundInput = new TextBox();
        private readonly Button outboundTranslate = new Button();
        private readonly ChatCategoryFilterBar categoryFilter = new ChatCategoryFilterBar();
        private readonly ChatTranslationHistory history = new ChatTranslationHistory(12000);
        private readonly Func<string, Task<string>> translateOutbound;
        private readonly Font contentRegularFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Regular);
        private readonly Font contentBroadcastFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
        private bool allowClose;
        private bool dragging;
        private Point dragPointerOrigin;
        private Point dragWindowOrigin;
        private Control dragCaptureTarget;
        private string displayedText = "";
        private bool hiddenByUser;
        private bool outboundBusy;

        internal AiTranslationWindowForm(OverlayForm owner) : this(owner, null) { }

        internal AiTranslationWindowForm(OverlayForm owner,
            Func<string, Task<string>> outboundTranslator)
        {
            overlay = owner;
            translateOutbound = outboundTranslator;
            AutoScaleDimensions = new SizeF(96.0f, 96.0f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "枫语幕 · AI实时翻译";
            Icon = Program.AppIcon;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(360, 200);
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
            categoryFilter.SelectedCategoryChanged += delegate { RenderHistory(); };

            Panel body = new Panel {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 9, 8, 10),
                BackColor = Color.FromArgb(38, 46, 55)
            };
            TableLayoutPanel bodyLayout = new TableLayoutPanel {
                Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
                BackColor = body.BackColor, Margin = Padding.Empty, Padding = Padding.Empty
            };
            bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,
                translateOutbound == null ? 0 : 40));
            bodyLayout.Controls.Add(categoryFilter, 0, 0);
            bodyLayout.Controls.Add(content, 0, 1);
            if (translateOutbound != null)
            {
                TableLayoutPanel outbound = new TableLayoutPanel {
                    Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                    Margin = new Padding(0, 5, 0, 0), BackColor = body.BackColor
                };
                outbound.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
                outbound.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                outbound.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
                Label inputLabel = new Label {
                    Text = "中文：", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(205, 216, 226)
                };
                outboundInput.Dock = DockStyle.Fill;
                outboundInput.Font = new Font("Microsoft YaHei UI", 9.5f);
                outboundInput.BackColor = Color.FromArgb(248, 250, 252);
                outboundInput.KeyDown += async delegate(object sender, KeyEventArgs e) {
                    if (e.KeyCode != Keys.Enter) return;
                    e.Handled = true; e.SuppressKeyPress = true;
                    await SubmitOutboundAsync();
                };
                outboundTranslate.Text = "翻译并复制";
                outboundTranslate.Dock = DockStyle.Fill;
                outboundTranslate.Click += async delegate { await SubmitOutboundAsync(); };
                outbound.Controls.Add(inputLabel, 0, 0);
                outbound.Controls.Add(outboundInput, 1, 0);
                outbound.Controls.Add(outboundTranslate, 2, 0);
                bodyLayout.Controls.Add(outbound, 0, 2);
            }
            body.Controls.Add(bodyLayout);
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

        internal string DisplayedText { get { return displayedText; } }
        internal bool HiddenByUser { get { return hiddenByUser; } }
        internal ChatSourceCategory? SelectedCategory { get { return categoryFilter.SelectedCategory; } }

        internal void SelectCategory(ChatSourceCategory? category)
        {
            categoryFilter.SelectCategory(category);
        }

        internal void AppendTranslation(string translation)
        {
            AppendTranslation(translation, ChatVisualStyle.Default);
        }

        internal void AppendTranslation(string translation, ChatVisualStyle style)
        {
            string next = AiTranslationWindowContent.Append("", translation);
            if (next.Length == 0) return;
            if (style == null) style = ChatVisualStyle.Default;
            history.Add(new ChatTranslationRecord { Translation = next, Style = style });
            if (categoryFilter.SelectedCategory.HasValue &&
                style.Category != categoryFilter.SelectedCategory.Value)
            {
                status.Text = "新译文已归入“" + CategoryName(style.Category) + "”";
                return;
            }
            if (content.TextLength > 12000) RenderHistory();
            else AppendVisibleTranslation(next, style);
            UpdateStatus(style);
        }

        private void AppendVisibleTranslation(string next, ChatVisualStyle style)
        {
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
        }

        private void RenderHistory()
        {
            bool wasReadOnly = content.ReadOnly;
            content.ReadOnly = false;
            content.Clear();
            content.ReadOnly = wasReadOnly;
            foreach (ChatTranslationRecord record in history.Select(categoryFilter.SelectedCategory))
                AppendVisibleTranslation(record.Translation, record.Style);
            displayedText = content.Text;
            status.Text = categoryFilter.SelectedCategory.HasValue
                ? "正在显示“" + CategoryName(categoryFilter.SelectedCategory.Value) + "”聊天"
                : "正在显示全部聊天";
        }

        private void UpdateStatus(ChatVisualStyle style)
        {
            status.Text = style.Kind == ChatVisualKind.Megaphone
                ? "普通喇叭译文已按蓝底显示"
                : (style.Kind == ChatVisualKind.SuperMegaphone
                    ? "超级喇叭译文已按粉底显示"
                    : "AI新译文已归入“" + CategoryName(style.Category) + "”");
        }

        internal static string CategoryName(ChatSourceCategory category)
        {
            switch (category)
            {
                case ChatSourceCategory.Buddy: return "好友";
                case ChatSourceCategory.Party: return "组队";
                case ChatSourceCategory.Guild: return "家族";
                case ChatSourceCategory.Alliance: return "联盟";
                case ChatSourceCategory.Whisper: return "悄悄话";
                case ChatSourceCategory.System: return "系统";
                case ChatSourceCategory.Notice: return "公告";
                case ChatSourceCategory.Megaphone: return "喇叭";
                default: return "普通";
            }
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

        private async Task SubmitOutboundAsync()
        {
            if (translateOutbound == null || outboundBusy) return;
            string source = (outboundInput.Text ?? "").Trim();
            if (source.Length == 0)
            {
                status.Text = "先输入一句中文";
                outboundInput.Focus();
                return;
            }
            outboundBusy = true;
            outboundTranslate.Enabled = false;
            status.Text = "正在翻译成英文…";
            try
            {
                string translated = await translateOutbound(source);
                if (!String.IsNullOrWhiteSpace(translated))
                {
                    AppendTranslation("English  " + translated.Trim(), ChatVisualStyle.Default);
                    status.Text = "已翻译并复制，可直接回游戏粘贴";
                    outboundInput.SelectAll();
                }
            }
            catch (Exception ex)
            {
                status.Text = "翻译失败：" + ex.Message;
            }
            finally
            {
                outboundBusy = false;
                outboundTranslate.Enabled = true;
            }
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

    internal sealed class BlueGlassButton : Button
    {
        private bool hovered;
        private bool pressed;
        internal bool PrimaryCard { get; set; }

        internal BlueGlassButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.FromArgb(247, 250, 255);
            ForeColor = Color.FromArgb(30, 48, 78);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true; Invalidate(); base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false; Invalidate(); base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.FromArgb(238, 245, 255) : Parent.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            Rectangle faceBounds = new Rectangle(1, pressed ? 2 : 1,
                Math.Max(1, Width - 3), Math.Max(1, Height - (pressed ? 4 : 3)));
            int radius = Math.Min(18, Math.Max(7, faceBounds.Height / 2));
            Color top = PrimaryCard ? Color.FromArgb(96, 151, 255) :
                (hovered ? Color.FromArgb(244, 249, 255) : Color.White);
            Color bottom = PrimaryCard ? Color.FromArgb(42, 103, 238) :
                (hovered ? Color.FromArgb(226, 238, 255) : BackColor);
            using (System.Drawing.Drawing2D.GraphicsPath face = RoundedPath(faceBounds, radius))
            using (System.Drawing.Drawing2D.LinearGradientBrush fill =
                new System.Drawing.Drawing2D.LinearGradientBrush(faceBounds, top, bottom,
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                e.Graphics.FillPath(fill, face);
                using (Pen border = new Pen(PrimaryCard ? Color.FromArgb(90, 144, 236) :
                    Color.FromArgb(184, 205, 233), 1.0f))
                    e.Graphics.DrawPath(border, face);
            }
            using (Pen highlight = new Pen(Color.FromArgb(120, 255, 255, 255), 1.0f))
                e.Graphics.DrawLine(highlight, radius, faceBounds.Top + 1,
                    Math.Max(radius, faceBounds.Right - radius), faceBounds.Top + 1);
            Rectangle textBounds = new Rectangle(faceBounds.X + Padding.Left,
                faceBounds.Y + Padding.Top, Math.Max(1, faceBounds.Width - Padding.Horizontal),
                Math.Max(1, faceBounds.Height - Padding.Vertical));
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            if (Text.IndexOf('\n') >= 0 || Text.IndexOf('\r') >= 0)
                flags |= TextFormatFlags.WordBreak;
            else flags |= TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (TextAlign == ContentAlignment.MiddleLeft) flags |= TextFormatFlags.Left;
            else if (TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
                Enabled ? (PrimaryCard ? Color.White : ForeColor) :
                    Color.FromArgb(140, 155, 177), flags);
        }

        internal static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds,
            int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath path =
                new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2,
                Math.Min(bounds.Width, bounds.Height)));
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BlueGlassPanel : Panel
    {
        internal BlueGlassPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(244, 248, 255);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.FromArgb(238, 245, 255) : Parent.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            Rectangle faceBounds = new Rectangle(1, 1, Math.Max(1, Width - 3),
                Math.Max(1, Height - 3));
            using (System.Drawing.Drawing2D.GraphicsPath face =
                BlueGlassButton.RoundedPath(faceBounds, 18))
            using (System.Drawing.Drawing2D.LinearGradientBrush fill =
                new System.Drawing.Drawing2D.LinearGradientBrush(faceBounds,
                    Color.White, BackColor,
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                e.Graphics.FillPath(fill, face);
                using (Pen border = new Pen(Color.FromArgb(190, 210, 238)))
                    e.Graphics.DrawPath(border, face);
            }
            using (Pen highlight = new Pen(Color.FromArgb(120, 255, 255, 255)))
                e.Graphics.DrawLine(highlight, 18, 2, Math.Max(18, Width - 18), 2);
        }
    }

    internal sealed class MainPanelForm : Form
    {
        private sealed class BlueSurfacePanel : Panel
        {
            internal BlueSurfacePanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush background =
                    new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle,
                        Color.FromArgb(250, 252, 255), Color.FromArgb(232, 241, 255),
                        System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    e.Graphics.FillRectangle(background, ClientRectangle);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle glow = new Rectangle(-Width / 3, -Height / 4,
                    Math.Max(1, Width * 4 / 3), Math.Max(1, Height * 3 / 4));
                using (System.Drawing.Drawing2D.GraphicsPath path =
                    new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(glow);
                    using (System.Drawing.Drawing2D.PathGradientBrush brush =
                        new System.Drawing.Drawing2D.PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(65, 118, 167, 255);
                        brush.SurroundColors = new Color[] { Color.FromArgb(0, 232, 241, 255) };
                        e.Graphics.FillPath(brush, path);
                    }
                }
            }
        }

        private sealed class CircleActionButton : Control
        {
            internal CircleActionButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                    ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                TabStop = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaintBackground(e);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle circle = new Rectangle(2, 2, Math.Max(1, Width - 5),
                    Math.Max(1, Height - 5));
                using (System.Drawing.Drawing2D.LinearGradientBrush fill =
                    new System.Drawing.Drawing2D.LinearGradientBrush(circle,
                        Color.FromArgb(71, 135, 255), Color.FromArgb(29, 85, 220),
                        System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal))
                    e.Graphics.FillEllipse(fill, circle);
                using (Pen ring = new Pen(Color.FromArgb(150, 157, 195, 255), 1.25f))
                    e.Graphics.DrawEllipse(ring, circle);
                TextRenderer.DrawText(e.Graphics, Text, Font, circle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                ForeColor = Color.FromArgb(211, 216, 255);
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                ForeColor = Color.White;
                Invalidate();
            }
        }

        private sealed class LogicalFont
        {
            internal readonly string Family;
            internal readonly float Size;
            internal readonly FontStyle Style;

            internal LogicalFont(Font font)
            {
                Family = font.FontFamily.Name;
                Size = font.SizeInPoints;
                Style = font.Style;
            }
        }

        private const int BaseDpi = 96;
        private readonly Size logicalClientSize = new Size(430, 680);
        private readonly Dictionary<Control, Rectangle> logicalBounds =
            new Dictionary<Control, Rectangle>();
        private readonly Dictionary<Control, Padding> logicalPaddings =
            new Dictionary<Control, Padding>();
        private readonly Dictionary<Control, AnchorStyles> logicalAnchors =
            new Dictionary<Control, AnchorStyles>();
        private readonly Dictionary<Control, LogicalFont> logicalFonts =
            new Dictionary<Control, LogicalFont>();
        private readonly OverlayForm overlay;
        private readonly Label status = new Label();
        private readonly Label hotkeys = new Label();
        private readonly TrackBar range = new TrackBar();
        private readonly Label rangeValue = new Label();
        private readonly Panel startPage = new BlueSurfacePanel();
        private readonly Panel dashboardPage = new BlueSurfacePanel();
        private readonly Panel aiPage = new BlueSurfacePanel();
        private readonly Panel aiHost = new Panel();
        private readonly Button continuousCard = new BlueGlassButton();
        private readonly Button floatingCard = new BlueGlassButton();
        private readonly Button snapModeButton = new BlueGlassButton();
        private readonly System.Windows.Forms.Timer snapTimer = new System.Windows.Forms.Timer();
        private bool snapEnabled;
        private bool dpiLayoutReady;
        private bool applyingDpiLayout;
        private float currentLayoutScale = 1.0f;
        private Action arrangeDashboardCards;

        // Capture-time restoration must not steal keyboard focus back from the game.
        // Explicit entry points still call Activate() when the user asks to open the panel.
        protected override bool ShowWithoutActivation { get { return true; } }

        public MainPanelForm(OverlayForm owner)
        {
            overlay = owner;
            AutoScaleDimensions = new SizeF(96.0f, 96.0f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "枫语幕 v4.0 测试版";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowIcon = true;
            Icon = Program.AppIcon;
            ClientSize = logicalClientSize;
            MinimumSize = new Size(446, 560);
            AutoScroll = true;
            BackColor = Color.FromArgb(238, 245, 255);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            snapEnabled = LoadSnapPreference();
            BuildStartPage();
            BuildDashboardPage();
            BuildAiPage();
            Controls.Add(aiPage);
            Controls.Add(dashboardPage);
            Controls.Add(startPage);
            ShowPage(startPage);
            snapTimer.Interval = 500;
            snapTimer.Tick += delegate { ApplySnapToGameWindow(); };
            snapTimer.Start();
            Resize += delegate { LayoutPageSurfaces(); };
            PerformLayout();
            CaptureLogicalLayout(this);
            dpiLayoutReady = true;
            Shown += delegate {
                ApplyDpiLayout(GetCurrentDpi(), true, false);
                ApplySnapToGameWindow();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    if (overlay != null) overlay.MinimizeMainPanelToTray();
                    else Hide();
                }
            };
        }

        private void BuildStartPage()
        {
            startPage.Location = Point.Empty;
            startPage.Size = logicalClientSize;
            startPage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            startPage.BackColor = Color.FromArgb(238, 245, 255);
            Button update = MakeButton("v4.0 测试版   ·   检查更新", Point.Empty,
                new Size(250, 44), false);
            update.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            update.Click += async delegate {
                if (overlay != null) await overlay.InstallLatestApplicationAsync(update);
            };
            CircleActionButton start = new CircleActionButton {
                Text = "开始翻译", ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 20.0f, FontStyle.Bold),
                Cursor = Cursors.Hand, TabStop = false
            };
            start.Click += delegate { ShowDashboardPage(); };
            EventHandler centerStart = delegate {
                int diameter = Math.Max(190, Math.Min(280,
                    (int)(Math.Min(startPage.ClientSize.Width, startPage.ClientSize.Height) * 0.54f)));
                start.Size = new Size(diameter, diameter);
                start.Location = new Point((startPage.ClientSize.Width - diameter) / 2,
                    (startPage.ClientSize.Height - diameter) / 2 + 28);
                update.Location = new Point((startPage.ClientSize.Width - update.Width) / 2, 34);
            };
            startPage.Resize += centerStart;
            startPage.Controls.Add(update);
            startPage.Controls.Add(start);
            centerStart(startPage, EventArgs.Empty);
        }

        private void BuildDashboardPage()
        {
            dashboardPage.Location = Point.Empty;
            dashboardPage.Size = logicalClientSize;
            dashboardPage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            dashboardPage.BackColor = Color.FromArgb(238, 245, 255);

            Panel header = CreateHeader("枫语幕", false);
            Button back = MakeButton("←", new Point(14, 13), new Size(40, 38), false);
            back.Font = new Font("Segoe UI Symbol", 13.0f, FontStyle.Regular);
            back.Click += delegate { ShowPage(startPage); };
            header.Controls.Add(back);
            foreach (Control child in header.Controls)
            {
                Label label = child as Label;
                if (label != null) label.Location = new Point(66, label.Top);
            }
            status.AutoSize = false;
            status.Location = new Point(66, 34);
            status.Size = new Size(260, 22);
            status.ForeColor = Color.FromArgb(91, 111, 140);
            status.AutoEllipsis = true;
            snapModeButton.Text = snapEnabled ? "吸附" : "自由";
            snapModeButton.Location = new Point(342, 16);
            snapModeButton.Size = new Size(70, 34);
            snapModeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            snapModeButton.FlatStyle = FlatStyle.Flat;
            snapModeButton.FlatAppearance.BorderColor = Color.FromArgb(190, 210, 238);
            snapModeButton.BackColor = Color.FromArgb(244, 248, 255);
            snapModeButton.ForeColor = Color.FromArgb(38, 71, 118);
            snapModeButton.Cursor = Cursors.Hand;
            snapModeButton.Click += delegate { SetSnapEnabled(!snapEnabled); };
            header.Controls.Add(status);
            header.Controls.Add(snapModeButton);

            Panel rangeCard = CreateCardPanel(new Point(16, 80), new Size(398, 112));
            rangeCard.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Label rangeTitle = CreateCardTitle("翻译范围", new Point(18, 14), new Size(180, 25));
            rangeValue.Location = new Point(278, 13);
            rangeValue.Size = new Size(102, 25);
            rangeValue.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            rangeValue.TextAlign = ContentAlignment.MiddleRight;
            rangeValue.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            rangeValue.ForeColor = Color.FromArgb(42, 103, 238);
            rangeValue.BackColor = Color.Transparent;
            range.Minimum = 1; range.Maximum = 3; range.TickStyle = TickStyle.TopLeft;
            range.TickFrequency = 1; range.SmallChange = 1; range.LargeChange = 1;
            range.Location = new Point(14, 44); range.Size = new Size(370, 38);
            range.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            range.Value = overlay == null ? 2 : (int)overlay.TranslationRangeMode;
            Label rangeLabels = new Label {
                Text = "更多                         推荐                         重点",
                Location = new Point(19, 84), Size = new Size(360, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(103, 124, 153),
                BackColor = Color.Transparent
            };
            range.ValueChanged += delegate {
                if (overlay != null) overlay.ApplyTranslationRangeMode((TranslationRangeMode)range.Value);
                RefreshRangeText();
            };
            rangeCard.Controls.Add(rangeTitle); rangeCard.Controls.Add(rangeValue);
            rangeCard.Controls.Add(rangeLabels); rangeCard.Controls.Add(range);

            ConfigureCardButton(continuousCard, "持续翻译\r\n按变化自动识别", new Point(16, 206));
            continuousCard.Click += delegate {
                if (overlay != null) overlay.ApplyContinuousTranslation(!overlay.ContinuousTranslationEnabled);
                RefreshStatus();
            };
            ConfigureCardButton(floatingCard, "悬浮窗\r\n默认关闭", new Point(220, 206));
            floatingCard.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            floatingCard.Click += delegate {
                if (overlay != null) overlay.ApplyAiChatFloatingWindow(!overlay.AiChatFloatingWindowEnabled);
                RefreshStatus();
            };
            Button dictionary = CreateCardButton("打开词库\r\n补充专用译名", new Point(16, 286));
            dictionary.Click += delegate { overlay.ShowDictionaryEditor(); };
            Button shortcut = CreateCardButton("快捷键\r\n键盘与手柄", new Point(220, 286));
            shortcut.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            shortcut.Click += delegate { overlay.ShowHotkeyEditor(); };
            Button ai = MakeButton("AI 实时翻译\r\n聊天分类、译文记录与中译英  →",
                new Point(16, 380), new Size(398, 112), true);
            ai.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            ai.TextAlign = ContentAlignment.MiddleLeft;
            ai.Padding = new Padding(20, 0, 12, 0);
            ai.Font = new Font("Microsoft YaHei UI", 13.0f, FontStyle.Bold);
            ai.BackColor = Color.FromArgb(38, 105, 238);
            ai.FlatAppearance.BorderColor = Color.FromArgb(38, 105, 238);
            ai.Click += delegate { ShowAiPage(); };
            Button trayButton = MakeButton("最小化到托盘", new Point(16, 516),
                new Size(398, 42), false);
            trayButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            trayButton.Click += delegate {
                if (overlay != null) overlay.MinimizeMainPanelToTray();
                else Hide();
            };
            hotkeys.Location = new Point(16, 574);
            hotkeys.Size = new Size(398, 36);
            hotkeys.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            hotkeys.ForeColor = Color.FromArgb(103, 124, 153);
            hotkeys.TextAlign = ContentAlignment.MiddleCenter;
            hotkeys.AutoEllipsis = true;
            dashboardPage.Controls.Add(hotkeys); dashboardPage.Controls.Add(trayButton);
            dashboardPage.Controls.Add(ai);
            dashboardPage.Controls.Add(shortcut); dashboardPage.Controls.Add(dictionary);
            dashboardPage.Controls.Add(floatingCard); dashboardPage.Controls.Add(continuousCard);
            dashboardPage.Controls.Add(rangeCard); dashboardPage.Controls.Add(header);
            arrangeDashboardCards = delegate {
                int cardWidth = ScaleValue(398, currentLayoutScale);
                int left = Math.Max(0, (dashboardPage.ClientSize.Width - cardWidth) / 2);
                rangeCard.Left = left;
                continuousCard.Left = left;
                floatingCard.Left = left + ScaleValue(204, currentLayoutScale);
                dictionary.Left = left;
                shortcut.Left = left + ScaleValue(204, currentLayoutScale);
                ai.Left = left;
                trayButton.Left = left;
                hotkeys.Left = left;
            };
            dashboardPage.Resize += delegate {
                if (arrangeDashboardCards != null) arrangeDashboardCards();
            };
            arrangeDashboardCards();
        }

        private void BuildAiPage()
        {
            aiPage.Location = Point.Empty;
            aiPage.Size = logicalClientSize;
            aiPage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            aiPage.BackColor = Color.FromArgb(238, 245, 255);
            Panel header = CreateHeader("AI 实时翻译", true);
            Button back = MakeButton("←", new Point(14, 13), new Size(40, 38), false);
            back.Font = new Font("Segoe UI Symbol", 13.0f, FontStyle.Regular);
            back.Click += delegate { ShowDashboardPage(); };
            header.Controls.Add(back);
            foreach (Control child in header.Controls)
            {
                Label label = child as Label;
                if (label != null) label.Location = new Point(66, label.Top);
            }
            aiHost.Location = new Point(0, 64);
            aiHost.Size = new Size(430, 616);
            aiHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            aiHost.BackColor = Color.FromArgb(238, 245, 255);
            aiPage.Controls.Add(aiHost);
            aiPage.Controls.Add(header);
            aiPage.Resize += delegate {
                int preferredWidth = ScaleValue(logicalClientSize.Width, currentLayoutScale);
                aiHost.Width = Math.Min(preferredWidth, aiPage.ClientSize.Width);
                aiHost.Left = Math.Max(0, (aiPage.ClientSize.Width - aiHost.Width) / 2);
            };
        }

        private Panel CreateHeader(string titleText, bool compact)
        {
            Panel header = new Panel {
                Location = Point.Empty, Size = new Size(430, 64),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(244, 248, 255)
            };
            Label title = new Label {
                Text = titleText, ForeColor = Color.FromArgb(23, 46, 79), AutoSize = true,
                Font = new Font("Microsoft YaHei UI", compact ? 14.0f : 17.0f,
                    FontStyle.Bold), Location = new Point(18, compact ? 20 : 9)
            };
            header.Controls.Add(title);
            return header;
        }

        private static Panel CreateCardPanel(Point location, Size size)
        {
            return new BlueGlassPanel { Location = location, Size = size };
        }

        private static Label CreateCardTitle(string text, Point location, Size size)
        {
            return new Label {
                Text = text, Location = location, Size = size,
                Font = new Font("Microsoft YaHei UI", 11.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(26, 48, 79),
                BackColor = Color.Transparent
            };
        }

        private Button CreateCardButton(string text, Point location)
        {
            Button button = new BlueGlassButton();
            ConfigureCardButton(button, text, location);
            return button;
        }

        private static void ConfigureCardButton(Button button, string text, Point location)
        {
            button.Text = text;
            button.Location = location;
            button.Size = new Size(194, 68);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 210, 238);
            button.BackColor = Color.FromArgb(244, 248, 255);
            button.ForeColor = Color.FromArgb(30, 48, 78);
            button.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(14, 0, 6, 0);
            button.Cursor = Cursors.Hand;
        }

        private void ShowPage(Control page)
        {
            startPage.Visible = page == startPage;
            dashboardPage.Visible = page == dashboardPage;
            aiPage.Visible = page == aiPage;
            page.BringToFront();
        }

        private void LayoutPageSurfaces()
        {
            if (applyingDpiLayout) return;
            int contentWidth = ScaleValue(logicalClientSize.Width, currentLayoutScale);
            int contentHeight = ScaleValue(logicalClientSize.Height, currentLayoutScale);
            Size surfaceSize = new Size(Math.Max(contentWidth, ClientSize.Width),
                Math.Max(contentHeight, ClientSize.Height));
            startPage.Size = surfaceSize;
            dashboardPage.Size = surfaceSize;
            aiPage.Size = surfaceSize;
            if (arrangeDashboardCards != null) arrangeDashboardCards();
        }

        internal void ShowDashboardPage()
        {
            ShowPage(dashboardPage);
            RefreshStatus();
        }

        internal void ShowStartPage()
        {
            ShowPage(startPage);
        }

        internal void ShowAiPage()
        {
            ShowPage(aiPage);
            if (overlay != null) overlay.AttachChatTranslator(aiHost);
        }

        internal void ShowDashboardForTest()
        {
            ShowDashboardPage();
        }

        internal void ShowAiPageForTest()
        {
            ShowPage(aiPage);
            if (aiHost.Controls.Count == 0)
            {
                OfflineChatForm testChat = new OfflineChatForm(null,
                    AppDomain.CurrentDomain.BaseDirectory);
                testChat.PrepareForEmbeddedHost();
                aiHost.Controls.Add(testChat);
                testChat.Show();
            }
        }

        private bool LoadSnapPreference()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                    return Convert.ToInt32(key.GetValue("MainWindowSnapEnabled", 1),
                        CultureInfo.InvariantCulture) != 0;
            }
            catch { return true; }
        }

        private void SetSnapEnabled(bool enabled)
        {
            snapEnabled = enabled;
            snapModeButton.Text = enabled ? "吸附" : "自由";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                    key.SetValue("MainWindowSnapEnabled", enabled ? 1 : 0,
                        RegistryValueKind.DWord);
            }
            catch { }
            if (enabled)
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                ApplySnapToGameWindow();
            }
        }

        private void ApplySnapToGameWindow()
        {
            if (!snapEnabled || overlay == null || !Visible ||
                WindowState != FormWindowState.Normal || applyingDpiLayout) return;
            Rectangle game;
            if (!overlay.TryGetMapleWindowBounds(out game) || game.Width < 160 || game.Height < 300)
                return;
            Rectangle work = Screen.FromRectangle(game).WorkingArea;
            int targetHeight = Math.Min(game.Height, work.Height);
            int targetWidth = Math.Max(MinimumSize.Width, Math.Min(536,
                (int)Math.Round(targetHeight * 0.44f)));
            targetWidth = Math.Min(targetWidth, work.Width);
            int left;
            if (game.Right + targetWidth <= work.Right) left = game.Right;
            else if (game.Left - targetWidth >= work.Left) left = game.Left - targetWidth;
            else left = Math.Max(work.Left, work.Right - targetWidth);
            int top = Math.Max(work.Top, Math.Min(game.Top, work.Bottom - targetHeight));
            Rectangle target = new Rectangle(left, top, targetWidth, targetHeight);
            if (Bounds != target) Bounds = target;
        }

        protected override void WndProc(ref Message message)
        {
            const int WmDpiChanged = 0x02E0;
            int newDpi = 0;
            if (message.Msg == WmDpiChanged)
                newDpi = (int)(message.WParam.ToInt64() & 0xffff);
            base.WndProc(ref message);
            if (newDpi > 0 && dpiLayoutReady && IsHandleCreated && !IsDisposed)
            {
                BeginInvoke((MethodInvoker)delegate {
                    if (!IsDisposed) ApplyDpiLayout(newDpi, true, false);
                });
            }
        }

        internal string RunDpiLayoutSelfTest(int scalePercent)
        {
            List<string> problems = new List<string>();
            CheckDpiLayout(this, problems);
            return problems.Count == 0
                ? "OK: " + scalePercent + "% DPI layout"
                : scalePercent + "% DPI layout: " + String.Join(" | ", problems.ToArray());
        }

        internal string RunExpandedLayoutSelfTest()
        {
            foreach (Control child in dashboardPage.Controls)
            {
                if (!child.Text.StartsWith("AI 实时翻译", StringComparison.Ordinal)) continue;
                int childCenter = child.Left + child.Width / 2;
                int pageCenter = dashboardPage.ClientSize.Width / 2;
                return Math.Abs(childCenter - pageCenter) <= 3
                    ? "OK: expanded layout centered"
                    : "Expanded layout is not centered: " + child.Bounds + " / " +
                        dashboardPage.ClientSize;
            }
            return "Expanded layout AI card missing";
        }

        internal bool SnapEnabledForTest { get { return snapEnabled; } }

        internal void ApplyDpiTestScale(int scalePercent)
        {
            int dpi = (int)Math.Round(BaseDpi * Math.Max(1.0f, scalePercent / 100.0f));
            ApplyDpiLayout(dpi, false, true);
        }

        private void CaptureLogicalLayout(Control parent)
        {
            if (!logicalFonts.ContainsKey(parent))
                logicalFonts.Add(parent, new LogicalFont(parent.Font));
            foreach (Control child in parent.Controls)
            {
                logicalBounds[child] = child.Bounds;
                logicalPaddings[child] = child.Padding;
                logicalAnchors[child] = child.Anchor;
                CaptureLogicalLayout(child);
            }
        }

        private int GetCurrentDpi()
        {
            try
            {
                using (Graphics graphics = CreateGraphics())
                    return Math.Max(BaseDpi, (int)Math.Round(graphics.DpiX));
            }
            catch { return BaseDpi; }
        }

        private void ApplyDpiLayout(int dpi, bool constrainToScreen, bool simulateFontScale)
        {
            if (!dpiLayoutReady || applyingDpiLayout) return;
            applyingDpiLayout = true;
            bool layoutSuspended = false;
            bool completed = false;
            try
            {
                float scale = Math.Max(1.0f, dpi / (float)BaseDpi);
                currentLayoutScale = scale;
                SuspendLayout();
                layoutSuspended = true;
                AutoScrollPosition = Point.Empty;
                // Clear the previous DPI's virtual canvas before shrinking. Otherwise WinForms
                // can collapse anchored page panels to 0x0 while the stale larger canvas exists.
                AutoScrollMinSize = Size.Empty;

                Size contentSize = new Size(ScaleValue(logicalClientSize.Width, scale),
                    ScaleValue(logicalClientSize.Height, scale));
                Size windowSize = contentSize;
                if (constrainToScreen && IsHandleCreated)
                {
                    Rectangle work = Screen.FromControl(this).WorkingArea;
                    int nonClientWidth = Math.Max(0, Width - ClientSize.Width);
                    int nonClientHeight = Math.Max(0, Height - ClientSize.Height);
                    int maximumWidth = Math.Max(320, work.Width - nonClientWidth - 16);
                    int maximumHeight = Math.Max(260, work.Height - nonClientHeight - 16);
                    windowSize.Width = Math.Min(contentSize.Width, maximumWidth);
                    windowSize.Height = Math.Min(contentSize.Height, maximumHeight);
                    if (windowSize.Height < contentSize.Height &&
                        windowSize.Width + SystemInformation.VerticalScrollBarWidth <= maximumWidth)
                        windowSize.Width += SystemInformation.VerticalScrollBarWidth;
                }

                // Anchors otherwise react to ClientSize and parent-bound changes after the
                // controls have already been scaled, effectively applying part of the scale twice.
                foreach (Control control in logicalBounds.Keys) control.Anchor = AnchorStyles.None;
                ClientSize = windowSize;
                foreach (KeyValuePair<Control, Rectangle> item in logicalBounds)
                {
                    Rectangle logical = item.Value;
                    item.Key.Bounds = new Rectangle(ScaleValue(logical.X, scale),
                        ScaleValue(logical.Y, scale), ScaleValue(logical.Width, scale),
                        ScaleValue(logical.Height, scale));
                    Padding padding;
                    if (logicalPaddings.TryGetValue(item.Key, out padding))
                        item.Key.Padding = new Padding(ScaleValue(padding.Left, scale),
                            ScaleValue(padding.Top, scale), ScaleValue(padding.Right, scale),
                            ScaleValue(padding.Bottom, scale));
                }
                if (simulateFontScale)
                {
                    foreach (KeyValuePair<Control, LogicalFont> item in logicalFonts)
                    {
                        LogicalFont font = item.Value;
                        item.Key.Font = new Font(font.Family, font.Size * scale,
                            font.Style, GraphicsUnit.Point);
                    }
                }

                AutoScrollMinSize = contentSize;
                completed = true;
            }
            finally
            {
                foreach (KeyValuePair<Control, AnchorStyles> item in logicalAnchors)
                    if (!item.Key.IsDisposed) item.Key.Anchor = item.Value;
                if (layoutSuspended) ResumeLayout(true);
                applyingDpiLayout = false;
            }
            if (completed)
            {
                LayoutPageSurfaces();
                KeepInsideCurrentScreen();
            }
        }

        private void KeepInsideCurrentScreen()
        {
            if (!IsHandleCreated) return;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int left = Math.Max(work.Left, Math.Min(Left, work.Right - Width));
            int top = Math.Max(work.Top, Math.Min(Top, work.Bottom - Height));
            if (left != Left || top != Top) Location = new Point(left, top);
        }

        private static int ScaleValue(int value, float scale)
        {
            return (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }

        private static void CheckDpiLayout(Control parent, List<string> problems)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                if (child.Left < -2 || child.Top < -2 ||
                    child.Right > parent.ClientSize.Width + 2 ||
                    child.Bottom > parent.ClientSize.Height + 2)
                {
                    problems.Add(child.GetType().Name + "越界(" + child.Text + ") " +
                        child.Bounds + " / parent " + parent.ClientSize);
                }
                Label label = child as Label;
                if (label != null && !label.AutoSize && !label.AutoEllipsis &&
                    label.Text.Length > 0)
                {
                    Size measured = TextRenderer.MeasureText(label.Text, label.Font,
                        new Size(Math.Max(1, label.Width), Int32.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    if (measured.Height > label.Height + 3)
                        problems.Add("文字被截断(" + label.Text + ")");
                }
                if (problems.Count < 12 && child.HasChildren) CheckDpiLayout(child, problems);
                if (problems.Count >= 12) return;
            }
        }

        private static Button MakeButton(string text, Point location, Size size, bool primary)
        {
            BlueGlassButton button = new BlueGlassButton {
                Text = text, Location = location, Size = size, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold),
                BackColor = primary ? Color.FromArgb(42, 103, 238) : Color.FromArgb(244, 248, 255),
                ForeColor = primary ? Color.White : Color.FromArgb(30, 48, 78),
                PrimaryCard = primary
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
            string floatingKey = overlay == null ? "F10" : overlay.FloatingWindowHotkeyDescription;
            status.Text = "已就绪 · 词库 " + dictionaryCount + " · 任务 " + taskCount;
            string gamepadShow = overlay == null ? "未绑定" : overlay.ShowGamepadShortcutDescription;
            string gamepadHide = overlay == null ? "未绑定" : overlay.HideGamepadShortcutDescription;
            hotkeys.Text = "键盘 " + showKey + " 翻译 / " + hideKey + " 对齐 / " +
                floatingKey + " AI浮窗" +
                "    手柄 " + gamepadShow + " 翻译 / " + gamepadHide + " 隐藏";
            if (overlay != null && overlay.HotkeyRegistrationStatus.Length > 0)
                hotkeys.Text = overlay.HotkeyRegistrationStatus;
            if (overlay != null && range.Value != (int)overlay.TranslationRangeMode)
                range.Value = (int)overlay.TranslationRangeMode;
            bool continuous = overlay != null && overlay.ContinuousTranslationEnabled;
            continuousCard.Text = "持续翻译\r\n" + (continuous ? "已开启" : "默认关闭");
            continuousCard.BackColor = continuous ? Color.FromArgb(218, 233, 255) : Color.FromArgb(244, 248, 255);
            bool floating = overlay != null && overlay.AiChatFloatingWindowEnabled;
            floatingCard.Text = "悬浮窗\r\n" + (floating ? "已开启" : "默认关闭");
            floatingCard.BackColor = floating ? Color.FromArgb(218, 233, 255) : Color.FromArgb(244, 248, 255);
            RefreshRangeText();
        }

        private void RefreshRangeText()
        {
            rangeValue.Text = range.Value == 1 ? "翻译更多" :
                (range.Value == 2 ? "日常推荐" : "只翻重点");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                snapTimer.Stop();
                snapTimer.Dispose();
            }
            base.Dispose(disposing);
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
        private readonly ComboBox floatingWindowKey = new ComboBox();
        private readonly TextBox showModifiers = new TextBox();
        private readonly TextBox hideModifiers = new TextBox();
        private readonly TextBox floatingWindowModifiers = new TextBox();
        private readonly ComboBox showGamepadFirst = new ComboBox();
        private readonly ComboBox showGamepadSecond = new ComboBox();
        private readonly ComboBox hideGamepadFirst = new ComboBox();
        private readonly ComboBox hideGamepadSecond = new ComboBox();

        public HotkeyForm(OverlayForm owner)
        {
            overlay = owner;
            AutoScaleDimensions = new SizeF(96.0f, 96.0f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "更改快捷键";
            Icon = SystemIcons.Information;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(590, 425);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BuildUi();
            showKey.SelectedItem = overlay == null ? "F8" : overlay.ShowKey.ToString();
            hideKey.SelectedItem = overlay == null ? "F9" : overlay.HideKey.ToString();
            floatingWindowKey.SelectedItem = overlay == null ? "F10" : overlay.FloatingWindowKey.ToString();
            showModifiers.Text = overlay == null ? "" : OverlayForm.ModifiersText(overlay.ShowModifiers);
            hideModifiers.Text = overlay == null ? "" : OverlayForm.ModifiersText(overlay.HideModifiers);
            floatingWindowModifiers.Text = overlay == null ? "" :
                OverlayForm.ModifiersText(overlay.FloatingWindowModifiers);
            SelectGamepadButton(showGamepadFirst, overlay == null ? GamepadButton.None : overlay.ShowGamepadShortcut.First);
            SelectGamepadButton(showGamepadSecond, overlay == null ? GamepadButton.None : overlay.ShowGamepadShortcut.Second);
            SelectGamepadButton(hideGamepadFirst, overlay == null ? GamepadButton.None : overlay.HideGamepadShortcut.First);
            SelectGamepadButton(hideGamepadSecond, overlay == null ? GamepadButton.None : overlay.HideGamepadShortcut.Second);
            UpdateGamepadSecondBoxes();
        }

        private void BuildUi()
        {
            FillKeys(showKey); FillKeys(hideKey); FillKeys(floatingWindowKey);
            GroupBox keyboard = new GroupBox { Text = "键盘快捷键", Location = new Point(16, 12), Size = new Size(558, 182) };
            Label showLabel = new Label { Text = "翻译开/关：", Location = new Point(14, 29), AutoSize = true };
            showKey.Location = new Point(115, 25);
            showModifiers.Location = new Point(230, 25); showModifiers.Width = 300;
            Label hideLabel = new Label { Text = "自动对齐聊天框：", Location = new Point(14, 70), AutoSize = true };
            hideKey.Location = new Point(115, 66);
            hideModifiers.Location = new Point(230, 66); hideModifiers.Width = 300;
            Label floatingLabel = new Label { Text = "呼出AI悬浮窗：", Location = new Point(14, 111), AutoSize = true };
            floatingWindowKey.Location = new Point(115, 107);
            floatingWindowModifiers.Location = new Point(230, 107); floatingWindowModifiers.Width = 300;
            Label hint = new Label { Text = "误关悬浮窗时用第三项重新呼出；右侧组合键可留空。", Location = new Point(14, 143), Size = new Size(520, 28), ForeColor = Color.DimGray };
            keyboard.Controls.AddRange(new Control[] { showLabel, showKey, showModifiers,
                hideLabel, hideKey, hideModifiers, floatingLabel, floatingWindowKey,
                floatingWindowModifiers, hint });

            GroupBox gamepad = new GroupBox { Text = "手柄快捷键（独立于键盘）", Location = new Point(16, 202), Size = new Size(558, 150) };
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

            Button save = new Button { Text = "保存并生效", Location = new Point(410, 371), Size = new Size(164, 38) };
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
                Keys fk = (Keys)Enum.Parse(typeof(Keys), Convert.ToString(floatingWindowKey.SelectedItem), true);
                uint sm = OverlayForm.ParseModifiers(showModifiers.Text);
                uint hm = OverlayForm.ParseModifiers(hideModifiers.Text);
                uint fm = OverlayForm.ParseModifiers(floatingWindowModifiers.Text);
                bool showMatchesAlign = sk == hk && sm == hm;
                bool showMatchesFloating = sk == fk && sm == fm;
                bool alignMatchesFloating = hk == fk && hm == fm;
                if (showMatchesAlign || showMatchesFloating || alignMatchesFloating)
                {
                    MessageBox.Show("三个功能不能使用完全相同的快捷键。", "快捷键");
                    return;
                }
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
                if (!overlay.ApplyHotkeys(sk, sm, hk, hm, fk, fm)) return;
                overlay.ApplyGamepadShortcuts(gs, gh);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                {
                    key.SetValue("ShowKey", sk.ToString());
                    key.SetValue("ShowModifiers", OverlayForm.ModifiersText(sm));
                    key.SetValue("HideKey", hk.ToString());
                    key.SetValue("HideModifiers", OverlayForm.ModifiersText(hm));
                    key.SetValue("FloatingWindowKey", fk.ToString());
                    key.SetValue("FloatingWindowModifiers", OverlayForm.ModifiersText(fm));
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
            AutoScaleDimensions = new SizeF(96.0f, 96.0f);
            AutoScaleMode = AutoScaleMode.Dpi;
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
                WriteDictionaryAtomically(path, SerializeRows());
                overlay.ReloadDictionary(); LoadRows(); status.Text = "已保存并生效";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败"); }
        }

        private static void WriteDictionaryAtomically(string targetPath, string contents)
        {
            string directory = Path.GetDirectoryName(targetPath);
            if (String.IsNullOrEmpty(directory)) directory = Environment.CurrentDirectory;
            string temporaryPath = Path.Combine(directory,
                Path.GetFileName(targetPath) + ".saving-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(temporaryPath, contents, new UTF8Encoding(true));
                if (File.Exists(targetPath))
                    File.Replace(temporaryPath, targetPath, null, true);
                else
                    File.Move(temporaryPath, targetPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
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
            AutoScaleDimensions = new SizeF(96.0f, 96.0f);
            AutoScaleMode = AutoScaleMode.Dpi;
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
            AutoScaleDimensions = new SizeF(96.0f, 96.0f);
            AutoScaleMode = AutoScaleMode.Dpi;
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
