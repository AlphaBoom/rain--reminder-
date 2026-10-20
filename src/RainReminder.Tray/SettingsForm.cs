using RainReminder.Core;
using System.Globalization;
using System.Net.Http;

namespace RainReminder.Tray;

internal sealed class SettingsForm : Form
{
    private readonly TextBox query = new() { Dock = DockStyle.Fill };
    private readonly CheckBox japanOnly = new() { Text = "仅日本", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left };
    private readonly ListView locations = new() { Dock = DockStyle.Fill, Height = 164, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly Label chosen = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 10), Text = "当前提醒地点：尚未选择" };
    private readonly DateTimePicker start = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.AddHours(6), Width = 85 };
    private readonly DateTimePicker end = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.AddHours(10), Width = 85 };
    private readonly NumericUpDown probability = new() { Minimum = 1, Maximum = 100, Value = 40, Width = 65 };
    private readonly TextBox exclusions = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 6) };
    private readonly Button search = new() { Text = "搜索", AutoSize = true };
    private readonly CheckBox showIcon = new() { Text = "在通知区域显示托盘图标", AutoSize = true, Checked = false };
    private readonly HttpClient http = WeatherClient.CreateHttpClient();
    private readonly CancellationTokenSource lifetime = new();
    private WeatherConfig? previous;
    private Location? selectedLocation;
    private readonly TableLayoutPanel layout;

    public SettingsForm()
    {
        Text = "降雨提醒 · 设置";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(660, 690);
        MinimumSize = new Size(600, 530);
        StartPosition = FormStartPosition.CenterScreen;
        layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(18) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, row);
        }
        Label Caption(string text) => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 5) };
        var title = Caption("先选地点，早晨有雨时再提醒你");
        title.Font = new Font(Font, FontStyle.Bold);
        Add(title);
        Add(Caption("提醒地点"));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, MinimumSize = new Size(0, 36) };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.Controls.Add(query, 0, 0);
        searchRow.Controls.Add(japanOnly, 1, 0);
        searchRow.Controls.Add(search, 2, 0);
        Add(searchRow);
        var hint = Caption("输入城市名，如仙台市、東京或 Sendai；选择结果确认地区。");
        hint.ForeColor = SystemColors.GrayText;
        Add(hint);
        locations.Columns.Add("地点");
        locations.Columns.Add("都道府县 / 地区");
        locations.Columns.Add("国家");
        locations.SizeChanged += (_, _) => ResizeColumns();
        locations.SelectedIndexChanged += (_, _) =>
        {
            if (locations.SelectedItems.Count == 1 && locations.SelectedItems[0].Tag is Location place) Choose(place);
        };
        Add(locations);
        Add(chosen);
        Add(Caption("早晨提醒时段（地点当地时间，自动跳过日本假日）"));
        var timeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        timeRow.Controls.AddRange([start, Caption("至"), end, Caption("  降水概率 ≥"), probability, Caption("%")]);
        Add(timeRow);
        Add(Caption("额外休假日期（可留空；例如 2026-11-03，多个用逗号分隔）"));
        Add(exclusions);
        Add(showIcon);
        var attribution = Caption("小时降水概率达到阈值或降水量 ≥ 0.1 mm 时提醒，每天仅一次。\n天气：Open-Meteo；地名：国土地理院 / GeoNames。");
        attribution.ForeColor = SystemColors.GrayText;
        Add(attribution);
        Add(status);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(14, 8, 14, 12), BackColor = SystemColors.Control };
        var save = new Button { Text = "保存设置", AutoSize = true };
        save.Click += (_, _) => Save();
        buttons.Controls.Add(save);
        var test = new Button { Text = "测试提醒", AutoSize = true };
        test.Click += (_, _) => { using var sample = new ReminderForm("测试提醒", "今天可能有雨，出门前记得查看天气。\n\n这是一条测试提醒，不影响今天的正式提醒。"); sample.ShowDialog(this); };
        buttons.Controls.Add(test);
        var scrolling = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scrolling.Controls.Add(layout);
        Controls.Add(scrolling);
        Controls.Add(buttons);
        AcceptButton = search;
        search.Click += async (_, _) => await SearchAsync();
        LoadPrevious();
        SizeChanged += (_, _) => WrapLabels();
        WrapLabels();
    }

    private void WrapLabels()
    {
        var width = Math.Max(300, ClientSize.Width - layout.Padding.Horizontal - 24);
        foreach (var label in layout.Controls.OfType<Label>()) label.MaximumSize = new Size(width, 0);
    }
    private void ResizeColumns()
    {
        var width = Math.Max(300, locations.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        locations.Columns[0].Width = width * 40 / 100;
        locations.Columns[1].Width = width * 40 / 100;
        locations.Columns[2].Width = width - locations.Columns[0].Width - locations.Columns[1].Width;
    }
    private void Choose(Location place)
    {
        selectedLocation = place;
        chosen.Text = "当前提醒地点：" + place;
    }
    private void Populate(Location[] places)
    {
        locations.BeginUpdate();
        try
        {
            locations.Items.Clear();
            foreach (var place in places)
            {
                var item = new ListViewItem(place.Name) { Tag = place, ToolTipText = place + $"\n{place.Latitude:F4}, {place.Longitude:F4}" };
                item.SubItems.Add(place.Region);
                item.SubItems.Add(place.Country);
                locations.Items.Add(item);
            }
            if (locations.Items.Count > 0)
            {
                locations.Items[0].Selected = true;
                Choose(places[0]);
            }
        }
        finally { locations.EndUpdate(); }
        ResizeColumns();
    }
    private void LoadPrevious()
    {
        try
        {
            previous = Store.Read<WeatherConfig>(Store.ConfigPath)?.Validate();
            if (previous is null) return;
            Populate([new Location { Name = previous.Name, Region = previous.Region, Country = previous.Country, Latitude = previous.Latitude, Longitude = previous.Longitude, TimeZone = previous.TimeZone }]);
            query.Text = previous.Name;
            japanOnly.Checked = previous.TimeZone == "Asia/Tokyo" || previous.TimeZone == "Tokyo Standard Time";
            start.Value = DateTime.Today.Add(previous.Start);
            end.Value = DateTime.Today.Add(previous.End);
            probability.Value = previous.ProbabilityThreshold;
            exclusions.Text = string.Join(", ", previous.ExcludedDates);
            showIcon.Checked = previous.ShowTrayIcon;
        }
        catch (Exception ex) { status.Text = "读取设置失败：" + ex.Message; }
    }
    private async Task SearchAsync()
    {
        if (!search.Enabled) return;
        var text = query.Text.Trim();
        if (text.Length == 0) { status.Text = "请先输入城市名，例如仙台市或 Sendai。"; query.Focus(); return; }
        var jp = japanOnly.Checked;
        search.Enabled = false;
        search.Text = "搜索中…";
        status.Text = "正在查找地点…";
        try
        {
            var results = await new WeatherClient(http).SearchAsync(text, lifetime.Token, jp ? "JP" : null);
            if (IsDisposed) return;
            if (query.Text.Trim() != text || japanOnly.Checked != jp) { status.Text = "搜索条件已改变，请重新搜索。"; return; }
            Populate(results);
            status.Text = results.Length > 0 ? $"找到 {results.Length} 个地点，请确认当前选中的地区。" : "没有找到。可尝试完整市名、罗马字，或取消“仅日本”。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "搜索超时，请稍后重试；也可尝试 Sendai 等罗马字地名。"; }
        catch (Exception) { if (!IsDisposed) status.Text = "地点搜索暂时不可用，请检查网络后重试。"; }
        finally { if (!IsDisposed) { search.Enabled = true; search.Text = "搜索"; } }
    }
    private WeatherConfig ReadControls()
    {
        var place = selectedLocation ?? throw new ArgumentException("请先搜索并选择地点。");
        return new WeatherConfig
        {
            Name = place.Name, Region = place.Region, Country = place.Country, Latitude = place.Latitude, Longitude = place.Longitude, TimeZone = place.TimeZone,
            MorningStart = start.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
            MorningEnd = end.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
            ProbabilityThreshold = (int)probability.Value, PrecipitationThreshold = previous?.PrecipitationThreshold ?? 0.1,
            ExcludedDates = WeatherConfig.ParseExcludedDates(exclusions.Text), ShowTrayIcon = showIcon.Checked,
        }.Validate();
    }
    internal void VerifySettingsRoundTrip()
    {
        Populate([new Location { Name = "仙台市", Region = "宮城県", Country = "日本", Latitude = 38.27, Longitude = 140.87, TimeZone = "Asia/Tokyo" }]);
        start.Value = DateTime.Today.AddHours(6);
        end.Value = DateTime.Today.AddHours(10);
        exclusions.Text = "2026/11/3， 2026-11-23";
        var path = Path.Combine(Path.GetTempPath(), "rain-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Store.Write(path, ReadControls());
            var result = Store.Read<WeatherConfig>(path)!.Validate();
            if (result.MorningStart != "06:00" || result.MorningEnd != "10:00" || result.ExcludedDates.Length != 2 || result.Region != "宮城県")
                throw new InvalidDataException("设置窗口保存验证失败。");
            PerformLayout();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private void Save()
    {
        try
        {
            Store.Write(Store.ConfigPath, ReadControls());
            MessageBox.Show(this, "已保存。服务会在 30 秒内读取设置，在指定的早晨时段检查天气。", "降雨提醒");
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存设置"); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { lifetime.Cancel(); lifetime.Dispose(); http.Dispose(); }
        base.Dispose(disposing);
    }
}

