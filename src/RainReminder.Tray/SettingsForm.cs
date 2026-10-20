using RainReminder.Core;

namespace RainReminder.Tray;

internal sealed class SettingsForm : Form
{
    private readonly TextBox query = new() { Dock = DockStyle.Fill, PlaceholderText = "输入城市，例如 Sendai、仙台、Tokyo" };
    private readonly ListBox locations = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox start = new() { Text = "06:00", Width = 80 };
    private readonly TextBox end = new() { Text = "10:00", Width = 80 };
    private readonly NumericUpDown probability = new() { Minimum = 1, Maximum = 100, Value = 40, Width = 80 };
    private readonly TextBox exclusions = new() { Dock = DockStyle.Fill, PlaceholderText = "例如 2026-11-03,2026-11-23" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Button search = new() { Text = "搜索地点", AutoSize = true };
    private readonly CheckBox showIcon = new() { Text = "在通知区域显示托盘图标", AutoSize = true, Checked = false };
    private readonly HttpClient http = WeatherClient.CreateHttpClient();
    private readonly CancellationTokenSource lifetime = new();
    private WeatherConfig? previous;

    public SettingsForm()
    {
        Text = "降雨提醒 · 设置";
        ClientSize = new Size(620, 625);
        MinimumSize = new Size(640, 665);
        Font = new Font("Microsoft YaHei UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 11 };
        foreach (var height in new[] { 32, 40, 140, 34, 40, 36, 38, 60, 38, 55, 40 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Text = "先选地点，早晨有降水风险时再提醒你", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.Controls.Add(query, 0, 0);
        searchRow.Controls.Add(search, 1, 0);
        layout.Controls.Add(searchRow, 0, 1);
        layout.Controls.Add(locations, 0, 2);
        layout.Controls.Add(new Label { Text = "提醒时段（地点当地时间，自动跳过日本假日）", AutoSize = true }, 0, 3);
        var timeRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        timeRow.Controls.AddRange([start, new Label { Text = "至", AutoSize = true, Padding = new Padding(4, 5, 4, 0) }, end,
            new Label { Text = "  降水概率 ≥", AutoSize = true, Padding = new Padding(4, 5, 0, 0) }, probability,
            new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }]);
        layout.Controls.Add(timeRow, 0, 4);
        layout.Controls.Add(new Label { Text = "额外不提醒的日期（公司休假等，逗号分隔）", AutoSize = true }, 0, 5);
        layout.Controls.Add(exclusions, 0, 6);
        layout.Controls.Add(new Label { Text = "概率达到阈值，或小时降水量 ≥ 0.1 mm 时提醒，每天仅一次。\n周末、日本法定假日及振替休日自动跳过（内阁府日历）。\n天气：Open-Meteo / 地点：GeoNames。", AutoSize = true }, 0, 7);
        layout.Controls.Add(showIcon, 0, 8);
        layout.Controls.Add(status, 0, 9);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "保存设置", AutoSize = true };
        save.Click += (_, _) => Save();
        buttons.Controls.Add(save);
        var test = new Button { Text = "测试提醒", AutoSize = true };
        test.Click += (_, _) => { using var sample = new ReminderForm("测试提醒", "今天可能有雨，出门前记得查看天气。\n\n这是一条测试提醒，不影响今天的正式提醒。"); sample.ShowDialog(this); };
        buttons.Controls.Add(test);
        layout.Controls.Add(buttons, 0, 10);
        Controls.Add(layout);
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(); } };
        LoadPrevious();
    }

    private void LoadPrevious()
    {
        try
        {
            previous = Store.Read<WeatherConfig>(Store.ConfigPath)?.Validate();
            if (previous is null) return;
            locations.Items.Add(new Location { Name = previous.Name, Latitude = previous.Latitude, Longitude = previous.Longitude, TimeZone = previous.TimeZone });
            locations.SelectedIndex = 0;
            start.Text = previous.MorningStart;
            end.Text = previous.MorningEnd;
            probability.Value = previous.ProbabilityThreshold;
            exclusions.Text = string.Join(",", previous.ExcludedDates);
            showIcon.Checked = previous.ShowTrayIcon;
        }
        catch (Exception ex) { status.Text = "读取设置失败：" + ex.Message; }
    }

    private async Task SearchAsync()
    {
        if (!search.Enabled || string.IsNullOrWhiteSpace(query.Text)) return;
        search.Enabled = false;
        status.Text = "正在搜索…";
        try
        {
            var results = await new WeatherClient(http).SearchAsync(query.Text, lifetime.Token);
            if (IsDisposed) return;
            locations.Items.Clear();
            locations.Items.AddRange(results.Cast<object>().ToArray());
            if (results.Length > 0) locations.SelectedIndex = 0;
            status.Text = results.Length > 0 ? "请选择正确的地区。" : "没有结果，可以试试英文地名。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) status.Text = "搜索失败：" + ex.Message; }
        finally { if (!IsDisposed) search.Enabled = true; }
    }

    private void Save()
    {
        try
        {
            if (locations.SelectedItem is not Location location) throw new ArgumentException("请先搜索并选择地点。");
            var config = new WeatherConfig
            {
                Name = location.Name, Latitude = location.Latitude, Longitude = location.Longitude,
                TimeZone = location.TimeZone, MorningStart = start.Text.Trim(), MorningEnd = end.Text.Trim(),
                ProbabilityThreshold = (int)probability.Value, PrecipitationThreshold = previous?.PrecipitationThreshold ?? 0.1,
                ExcludedDates = exclusions.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ShowTrayIcon = showIcon.Checked,
            }.Validate();
            Store.Write(Store.ConfigPath, config);
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
