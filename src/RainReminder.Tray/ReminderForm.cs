using System.Diagnostics;

namespace RainReminder.Tray;

internal sealed class ReminderForm : Form
{
    public ReminderForm(string title, string message, string? weatherUrl = null)
    {
        Text = title;
        ClientSize = new Size(520, 350);
        Font = new Font("Microsoft YaHei UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Text = "今天出门，记得留意天气", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill, AutoSize = false }, 0, 1);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var close = new Button { Text = "知道了", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(close);
        if (weatherUrl is not null)
        {
            var weather = new Button { Text = "查看天气", AutoSize = true };
            weather.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(weatherUrl) { UseShellExecute = true }); Close(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开浏览器"); }
            };
            buttons.Controls.Add(weather);
        }
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }
}
