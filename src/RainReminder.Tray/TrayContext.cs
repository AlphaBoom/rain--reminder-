using RainReminder.Core;

namespace RainReminder.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon icon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2_000 };
    private SettingsForm? settings;
    private bool showingReminder;
    private readonly EventWaitHandle showSettings = new(false, EventResetMode.AutoReset, @"Local\RainReminder.ShowSettings.v1");

    public TrayContext(bool setup)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add("服务状态…", null, (_, _) => ShowStatus());
        menu.Items.Add("测试提醒…", null, (_, _) => { using var form = new ReminderForm("测试提醒", "这是一条测试提醒。实际提醒只在工作日早晨、有降水风险时出现。"); form.ShowDialog(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出托盘（服务继续运行）", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = SystemIcons.Information, Text = "工作日降雨提醒", ContextMenuStrip = menu, Visible = false };
        icon.DoubleClick += (_, _) => OpenSettings();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        UpdateIcon();
        if (setup || !File.Exists(Store.ConfigPath)) OpenSettings();
        Tick();
    }

    private void OpenSettings()
    {
        if (settings is null || settings.IsDisposed)
        {
            settings = new SettingsForm();
            settings.FormClosed += (_, _) => UpdateIcon();
        }
        settings.Show();
        settings.Activate();
    }

    private void ShowStatus()
    {
        try
        {
            var status = Store.Read<ServiceStatus>(Store.StatusPath);
            var text = status is null ? "尚无天气检查记录。请先设置地点；服务只在设定的工作日早晨检查天气。"
                : $"最近检查：{status.CheckedAt.LocalDateTime:yyyy-MM-dd HH:mm}\n"
                + (status.Error ?? (status.IsHoliday ? "今天是日本假日，已跳过提醒" : status.Outlook?.Wet == true ? "有降水风险" : "未达到提醒阈值"));
            text += "\n\n服务名称：RainReminder\n可在 Windows“服务”中查看是否正在运行。";
            MessageBox.Show(text, "降雨提醒 · 服务状态");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "读取状态失败"); }
    }

    private void Tick()
    {
        if (showSettings.WaitOne(0)) OpenSettings();
        if (showingReminder || !Desktop.IsUnlocked()) return;
        try
        {
            var config = Store.Read<WeatherConfig>(Store.ConfigPath)?.Validate();
            if (config is null) return;
            var status = Store.Read<ServiceStatus>(Store.StatusPath);
            var state = Store.Read<UserState>(Store.UserStatePath) ?? new UserState();
            var now = DateTimeOffset.UtcNow;
            if (!Policy.ShouldNotify(config, status, state, now)) return;
            var outlook = status!.Outlook!;
            var first = TimeZoneInfo.ConvertTime(outlook.FirstIntervalEnd!.Value.AddHours(-1), config.Zone);
            var text = $"{config.Name} 今天可能有雨，出门前考虑带伞。\n\n"
                + $"预计从 {first:HH:mm} 左右有降水风险\n"
                + $"剩余时段最高降水概率：{outlook.MaxProbability}%\n"
                + $"最大小时降水量：{outlook.MaxPrecipitation:g} mm\n\n"
                + "数据：Open-Meteo（包含雨、阵雨和雪）";
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(config.Name + " 今日 天气");
            showingReminder = true;
            try
            {
                using var form = new ReminderForm("今天可能有雨", text, url);
                form.ShowDialog(); // Remains visible until acknowledged; independent of Do Not Disturb.
                Store.Write(Store.UserStatePath, new UserState(Policy.LocalDate(config, now)));
            }
            finally { showingReminder = false; }
        }
        catch (Exception ex) { Store.Log(Path.Combine(Store.UserRoot, "tray.log"), ex.ToString()); }
    }

    private void UpdateIcon()
    {
        try { icon.Visible = Store.Read<WeatherConfig>(Store.ConfigPath)?.ShowTrayIcon ?? false; }
        catch (Exception ex) { Store.Log(Path.Combine(Store.UserRoot, "tray.log"), ex.Message); }
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        timer.Dispose();
        icon.Visible = false;
        icon.Dispose();
        settings?.Dispose();
        showSettings.Dispose();
        base.ExitThreadCore();
    }
}
