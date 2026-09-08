using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PQM.Infrastructure;
using PQM.Infrastructure.Services;

namespace PQM.Console
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                //.UseWindowsService(options =>
                //{
                //    options.ServiceName = "PQM Meter Reader";
                //})
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    string connectionString =hostContext.Configuration.GetConnectionString("DefaultConnection")?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

                    int meterCooldown = hostContext.Configuration.GetValue<int>("DlmsSettings:MeterCooldownSeconds", 8);
                    DlmsMeterReader.DefaultMeterCooldownSeconds = meterCooldown > 0 ? meterCooldown : 8;

                    // Configure typed ConsoleOptions
                    services.Configure<ConsoleOptions>(options =>
                    {
                        options.DefaultConnection = connectionString;
                        options.MeterCooldownSeconds = DlmsMeterReader.DefaultMeterCooldownSeconds;
                    });

                    // Register DataContext
                    services.AddScoped<DataContext>(sp =>new DataContext(connectionString));

                    // Register Profile Sync Service
                    services.AddSingleton<ProfileSyncService>(sp =>new ProfileSyncService(connectionString,sp.GetRequiredService<ILogger<ProfileSyncService>>()));

                    // Register Background Worker
                    services.AddHostedService<DeviceConsoleRunnerService>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}