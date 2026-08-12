using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MapleOverlay
{
    internal sealed class HotkeyForm : Form
    {
        private readonly OverlayForm overlay;
        private readonly ComboBox showKey = new ComboBox();
        private readonly ComboBox hideKey = new ComboBox();
        private readonly TextBox showModifiers = new TextBox();
        private readonly TextBox hideModifiers = new TextBox();

        public HotkeyForm(OverlayForm owner)
        {
            overlay = owner;
            Text = "更改快捷键";
            Icon = SystemIcons.Information;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 178);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BuildUi();
            showKey.SelectedItem = overlay.ShowKey.ToString();
            hideKey.SelectedItem = overlay.HideKey.ToString();
            showModifiers.Text = OverlayForm.ModifiersText(overlay.ShowModifiers);
            hideModifiers.Text = OverlayForm.ModifiersText(overlay.HideModifiers);
        }

        private void BuildUi()
        {
            FillKeys(showKey); FillKeys(hideKey);
            Label showLabel = new Label { Text = "呼出翻译：", Location = new Point(24, 25), AutoSize = true };
            showKey.Location = new Point(112, 21);
            showModifiers.Location = new Point(215, 21); showModifiers.Width = 180;
            Label hideLabel = new Label { Text = "缩回后台：", Location = new Point(24, 68), AutoSize = true };
            hideKey.Location = new Point(112, 64);
            hideModifiers.Location = new Point(215, 64); hideModifiers.Width = 180;
            Label hint = new Label { Text = "右侧可留空，或写 CTRL、ALT、SHIFT、WIN，多个用 + 连接。", Location = new Point(24, 105), AutoSize = true, ForeColor = Color.DimGray };
            Button save = new Button { Text = "保存并立即生效", Location = new Point(260, 134), Size = new Size(135, 30) };
            save.Click += delegate { SaveSettings(); };
            Controls.AddRange(new Control[] { showLabel, showKey, showModifiers, hideLabel, hideKey, hideModifiers, hint, save });
        }

        private static void FillKeys(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = 88;
            box.Items.AddRange(new object[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Home", "End", "Insert", "Delete", "PageUp", "PageDown" });
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
                overlay.ApplyHotkeys(sk, sm, hk, hm);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\FengYuMu"))
                {
                    key.SetValue("ShowKey", sk.ToString());
                    key.SetValue("ShowModifiers", OverlayForm.ModifiersText(sm));
                    key.SetValue("HideKey", hk.ToString());
                    key.SetValue("HideModifiers", OverlayForm.ModifiersText(hm));
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
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            Controls.Add(root);

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
            root.Controls.Add(grid, 0, 1);

            GroupBox qq = new GroupBox { Text = "QQ群共享", Dock = DockStyle.Fill };
            FlowLayoutPanel share = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), WrapContents = false };
            Button import = new Button { Text = "导入群友词库", AutoSize = true };
            import.Click += delegate { ImportRows(); };
            Button export = new Button { Text = "导出QQ群分享包", AutoSize = true };
            export.Click += delegate { ExportRows(); };
            share.Controls.Add(import); share.Controls.Add(export);
            share.Controls.Add(new Label { Text = "导入后先检查，点击上方“保存并载入内存”才会生效。", AutoSize = true, Padding = new Padding(12, 7, 0, 0), ForeColor = Color.DimGray });
            qq.Controls.Add(share); root.Controls.Add(qq, 0, 2);
        }

        private void LoadRows()
        {
            grid.Rows.Clear();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] p = raw.Split('\t');
                if (p.Length >= 2) grid.Rows.Add(p[0].Trim(), p[1].Trim(),
                    p.Length > 2 ? p[2].Trim() : "", p.Length > 3 ? p[3].Trim() : "");
            }
            status.Text = (grid.Rows.Count - 1) + " 条";
        }

        private void FilterRows()
        {
            string q = search.Text.Trim(); grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                bool visible = q.Length == 0;
                for (int i = 0; !visible && i < 3; i++) visible = Convert.ToString(row.Cells[i].Value).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Visible = visible;
            }
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
                if (en.Length == 0 || zh.Length == 0 || !seen.Add(en)) continue;
                b.Append(en).Append('\t').Append(zh).Append('\t').Append(cat);
                if (iconHash.Length > 0) b.Append('\t').Append(iconHash);
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
            foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow) all[Clean(row.Cells[0].Value)] = new string[] {
                Clean(row.Cells[1].Value), Clean(row.Cells[2].Value), Clean(row.Cells[3].Value) };
            int count = 0;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (String.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                string[] p = raw.Split('\t');
                if (p.Length < 2 || Clean(p[0]).Length == 0 || Clean(p[1]).Length == 0) continue;
                string key = Clean(p[0]);
                string preservedHash = all.ContainsKey(key) && all[key].Length > 2 ? all[key][2] : "";
                all[key] = new string[] { Clean(p[1]), p.Length > 2 ? Clean(p[2]) : "QQ群",
                    p.Length > 3 ? Clean(p[3]) : preservedHash }; count++;
            }
            grid.Rows.Clear(); foreach (KeyValuePair<string, string[]> p in all)
                grid.Rows.Add(p.Key, p.Value[0], p.Value[1], p.Value.Length > 2 ? p.Value[2] : "");
            status.Text = "已合并 " + count + " 条，尚未保存";
        }

        private void ExportRows()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "枫语幕词库 (*.tsv)|*.tsv";
                dialog.FileName = "枫语幕词库_" + DateTime.Now.ToString("yyyyMMdd") + ".tsv";
                if (Directory.Exists(@"D:\GTP\文件")) dialog.InitialDirectory = @"D:\GTP\文件";
                if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, SerializeRows(), new UTF8Encoding(true));
            }
        }
    }
}
