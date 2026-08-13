using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MapleOverlay
{
    internal sealed class TaskDictionaryRow
    {
        public string English;
        public string Chinese;
        public string Category;
        public string IconHash;
        public string TaskId;
        public string StartMap;
    }

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
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
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
            foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow) all[Clean(row.Cells[0].Value)] = new string[] {
                Clean(row.Cells[1].Value), Clean(row.Cells[2].Value), Clean(row.Cells[3].Value) };
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
                string preservedHash = all.ContainsKey(key) && all[key].Length > 2 ? all[key][2] : "";
                all[key] = new string[] { Clean(p[1]), category,
                    p.Length > 3 ? Clean(p[3]) : preservedHash }; count++;
            }
            grid.Rows.Clear(); foreach (KeyValuePair<string, string[]> p in all)
                grid.Rows.Add(p.Key, p.Value[0], p.Value[1], p.Value.Length > 2 ? p.Value[2] : "");
            RefreshTaskGrid();
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
