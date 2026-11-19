using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MapleOverlay
{
    internal sealed class EmbeddedFilePicker : Form
    {
        private readonly TextBox directory = new TextBox(), filename = new TextBox();
        private readonly ListBox entries = new ListBox();
        private readonly Label status = new Label();
        private readonly Button accept = new BlueGlassButton();
        private readonly bool save;
        private string overwriteTarget = "";
        internal string SelectedPath { get; private set; }

        internal EmbeddedFilePicker(string folder, bool save, string defaultName)
        {
            this.save = save;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = save ? "导出词库" : "导入词库";
            ClientSize = new Size(390, 540);
            Font = new Font("Microsoft YaHei UI", 9.0f);
            BackColor = Color.FromArgb(238, 245, 255);
            Label hint = new Label { Dock = DockStyle.Top, Height = 44,
                Text = "粘贴文件夹路径并按回车；双击文件夹进入。\r\n可选择 .tsv 或 .txt，不会打开额外窗口。" };
            directory.Dock = DockStyle.Top; directory.Height = 30; directory.Text = folder;
            directory.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RefreshEntries(); }
            };
            Button up = new BlueGlassButton { Text = "上一级", Dock = DockStyle.Top, Height = 32 };
            up.Click += delegate {
                try { DirectoryInfo parent = Directory.GetParent(directory.Text);
                    if (parent != null) { directory.Text = parent.FullName; RefreshEntries(); } }
                catch (Exception ex) { status.Text = ex.Message; }
            };
            entries.Dock = DockStyle.Fill;
            entries.SelectedIndexChanged += delegate {
                string selected = entries.SelectedItem as string;
                if (selected != null && !selected.StartsWith("[目录] ", StringComparison.Ordinal)) filename.Text = selected;
            };
            entries.DoubleClick += delegate {
                string selected = entries.SelectedItem as string;
                if (selected == null) return;
                if (selected.StartsWith("[目录] ", StringComparison.Ordinal)) {
                    directory.Text = Path.Combine(directory.Text, selected.Substring(5)); RefreshEntries();
                } else AcceptSelection();
            };
            filename.Dock = DockStyle.Bottom; filename.Text = defaultName ?? "";
            status.Dock = DockStyle.Bottom; status.Height = 76; status.ForeColor = Color.FromArgb(185, 28, 28);
            accept.Dock = DockStyle.Bottom; accept.Height = 40; accept.Text = save ? "导出到选定路径" : "导入选定文件";
            accept.Click += delegate { AcceptSelection(); };
            Controls.Add(entries); Controls.Add(up); Controls.Add(directory); Controls.Add(hint);
            Controls.Add(filename); Controls.Add(accept); Controls.Add(status);
            Shown += delegate { RefreshEntries(); };
        }
        private void RefreshEntries()
        {
            try {
                string folder = Path.GetFullPath(directory.Text.Trim());
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("文件夹不存在。");
                entries.Items.Clear();
                foreach (string child in Directory.GetDirectories(folder)) entries.Items.Add("[目录] " + Path.GetFileName(child));
                foreach (string file in Directory.GetFiles(folder)) {
                    string extension = Path.GetExtension(file);
                    if (extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) entries.Items.Add(Path.GetFileName(file));
                }
                directory.Text = folder; overwriteTarget = ""; status.Text = "";
            } catch (Exception ex) { status.Text = "无法打开目录：" + ex.Message; }
        }
        private void AcceptSelection()
        {
            try {
                if (String.IsNullOrWhiteSpace(filename.Text)) throw new ArgumentException("请填写文件名。");
                string target = Path.GetFullPath(Path.Combine(directory.Text.Trim(), filename.Text.Trim()));
                string extension = Path.GetExtension(target);
                if (!extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("仅接受 .tsv 或 .txt 文件。");
                if (!Directory.Exists(Path.GetDirectoryName(target))) throw new DirectoryNotFoundException("目标目录不存在。");
                if (!save && !File.Exists(target)) throw new FileNotFoundException("选定文件不存在。");
                if (save && File.Exists(target) && overwriteTarget != target) {
                    overwriteTarget = target; status.Text = "文件已存在。再次点击确认覆盖，或更改文件名。"; return;
                }
                SelectedPath = target; DialogResult = DialogResult.OK; Close();
            } catch (Exception ex) { status.Text = ex.Message; }
        }
    }
}
