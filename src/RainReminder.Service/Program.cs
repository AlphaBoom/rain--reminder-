using System.ServiceProcess;
using RainReminder.Core;
using RainReminder.Service;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Contains("--smoke-test"))
        {
            new WeatherConfig { Name = "Smoke test", Latitude = 38.27, Longitude = 140.87 }.Validate();
            return;
        }
        if (args.Contains("--console")) new WeatherWorker().RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        else ServiceBase.Run(new RainService());
    }
}
internal sealed class RainService : ServiceBase
{
    private readonly CancellationTokenSource cancellation = new();
    private Task? worker;
    public RainService() { ServiceName = "RainReminder"; CanStop = true; AutoLog = false; }
    protected override void OnStart(string[] args)
    {
        worker = Task.Run(() => new WeatherWorker().RunAsync(cancellation.Token));
        _ = worker.ContinueWith(t =>
        {
            Store.Log(Path.Combine(Store.SharedRoot, "status", "service.log"), t.Exception!.ToString());
            Environment.Exit(1);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
    protected override void OnStop()
    {
        cancellation.Cancel();
        try { worker?.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException ex) when (ex.InnerExceptions.All(x => x is OperationCanceledException)) { }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) cancellation.Dispose();
        base.Dispose(disposing);
    }
}

