using Microsoft.Extensions.Hosting;
using RainReminder.Core;

namespace RainReminder.Service;

public sealed class WeatherWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var http = WeatherClient.CreateHttpClient();
        var client = new WeatherClient(http);
        var calendarPath = Path.Combine(Store.SharedRoot, "status", "holidays.json");
        HolidayCache? calendar = null;
        try { calendar = Store.Read<HolidayCache>(calendarPath); }
        catch (Exception ex) { Store.Log(Path.Combine(Store.SharedRoot, "status", "service.log"), ex.Message); }
        if (calendar is null)
        {
            try { calendar = JapanCalendar.Parse(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "japan-holidays.csv"), stoppingToken), DateTimeOffset.MinValue); }
            catch (Exception ex) { Store.Log(Path.Combine(Store.SharedRoot, "status", "service.log"), ex.Message); }
        }
        var nextCalendarAttempt = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            WeatherConfig? config = null;
            try
            {
                config = Store.Read<WeatherConfig>(Store.ConfigPath)?.Validate();
                if (config is null) continue;
                ServiceStatus? status;
                try { status = Store.Read<ServiceStatus>(Store.StatusPath); }
                catch (System.Text.Json.JsonException) { status = null; }
                var now = DateTimeOffset.UtcNow;
                if (!Policy.IsMorning(config, now)) continue;
                // Refresh weekly. On a network error retain any complete cached
                // calendar for this year, including the bundled official snapshot.
                if (now >= nextCalendarAttempt && (calendar is null || now - calendar.UpdatedAt >= TimeSpan.FromDays(7)))
                {
                    nextCalendarAttempt = now.AddMinutes(5);
                    try
                    {
                        var downloaded = JapanCalendar.Parse(await http.GetByteArrayAsync(JapanCalendar.Source, stoppingToken), now);
                        if (!downloaded.Covers(int.Parse(Policy.LocalDate(config, now)[..4])))
                            throw new InvalidDataException("内阁府尚未提供当年完整日历。");
                        Store.Write(calendarPath, downloaded);
                        calendar = downloaded;
                        nextCalendarAttempt = now.AddDays(7);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Store.Log(Path.Combine(Store.SharedRoot, "status", "service.log"), "日历更新失败，尝试使用缓存：" + ex.Message); }
                }
                if (calendar is null || !calendar.Covers(int.Parse(Policy.LocalDate(config, now)[..4])))
                    throw new InvalidDataException("当年日本假日日历不可用，5 分钟后重试。");
                if (calendar.IsHoliday(Policy.LocalDate(config, now)))
                {
                    if (status?.IsHoliday != true || status.LocalDate != Policy.LocalDate(config, now) || status.ConfigId != Policy.ConfigId(config))
                        Store.Write(Store.StatusPath, new ServiceStatus(now, Policy.ConfigId(config), Policy.LocalDate(config, now), null, null, true));
                    continue;
                }
                if (!Policy.ShouldFetch(config, status, now)) continue;
                var forecast = await client.ForecastAsync(config, stoppingToken);
                now = DateTimeOffset.UtcNow;
                // The desktop independently checks the morning window again.
                Store.Write(Store.StatusPath, new ServiceStatus(now, Policy.ConfigId(config),
                    Policy.LocalDate(config, now), Policy.Analyze(forecast, config, now), null));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Store.Log(Path.Combine(Store.SharedRoot, "status", "service.log"), ex.ToString());
                if (config is not null)
                {
                    try
                    {
                        var now = DateTimeOffset.UtcNow;
                        Store.Write(Store.StatusPath, new ServiceStatus(now, Policy.ConfigId(config),
                            Policy.LocalDate(config, now), null, "天气检查失败，5 分钟后自动重试。"));
                    }
                    catch (Exception writeError) { Store.Log(Path.Combine(Store.SharedRoot, "status", "service.log"), writeError.Message); }
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
