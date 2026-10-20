using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RainReminder.Core;
using RainReminder.Service;

if (args.Contains("--smoke-test"))
{
    new WeatherConfig { Name = "Smoke test", Latitude = 38.27, Longitude = 140.87 }.Validate();
    return;
}
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "RainReminder");
builder.Services.AddHostedService<WeatherWorker>();
await builder.Build().RunAsync();
