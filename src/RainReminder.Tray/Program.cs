using RainReminder.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--smoke-test"))
        {
            using var settings = new SettingsForm();
            _ = settings.Handle;
            using var reminder = new ReminderForm("测试提醒", "今天可能有雨。");
            _ = reminder.Handle;
            return;
        }
        using var mutex = new Mutex(true, @"Local\RainReminder.Tray.v1", out var first);
        if (!first)
        {
            // A second launch can arrive while the first is still creating its
            // context. Give the named settings event a short time to appear.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (EventWaitHandle.TryOpenExisting(@"Local\RainReminder.ShowSettings.v1", out var show))
                {
                    using (show) show.Set();
                    return;
                }
                Thread.Sleep(50);
            }
            MessageBox.Show("程序正在启动，请稍后从开始菜单重新打开。", "降雨提醒");
            return;
        }
        Application.Run(new TrayContext(args.Contains("--settings") || args.Contains("--setup")));
    }
}
