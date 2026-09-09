using Microsoft.EntityFrameworkCore;
using PQM.Core.Interfaces.Repositories;
using PQM.Infrastructure;
using PQM.Infrastructure.Repositories;
using PQM.Infrastructure.Services;
using PQM.Server.Hubs;
using Serilog;
using Serilog.Events;
using System.Text.Json.Serialization;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override(
        "Microsoft.EntityFrameworkCore.Database.Command",
        LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("[PQM.Server] Starting PQM Web API Server...");

    var builder = WebApplication.CreateBuilder(args);

    // Allow PQM Server to be accessed from office Wi-Fi
    builder.WebHost.UseUrls("http://0.0.0.0:5135");

    builder.Host.UseSerilog();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler =
                ReferenceHandler.IgnoreCycles;
        });

    builder.Services.AddOpenApi();

    var connectionString =
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'DefaultConnection' not found."
        );

    builder.Services.AddDbContext<DataContext>(
        options => options.UseSqlServer(connectionString));

    builder.Services.AddScoped<IAuthRepository, AuthRepository>();
    builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
    builder.Services.AddScoped<IScheduleRepository, ScheduleRepository>();
    builder.Services.AddScoped<ILiveRepository, LiveRepository>();

    builder.Services.AddScoped<ProfileSyncService>(sp =>new ProfileSyncService(connectionString,sp.GetRequiredService<ILogger<ProfileSyncService>>()));

    builder.Services.AddSignalR();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowReactApp", policy =>
            policy.SetIsOriginAllowed(origin => true)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials());
    });

    var app = builder.Build();

    // ============================================================
    // Auto-apply pending EF Core migrations on startup
    // ============================================================

    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var db = new DataContext(connectionString);
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Startup] Migration error (non-fatal): {ex.Message}");
        }
    }

    // ============================================================
    // Auto-start PQMMeterReader Windows Service if stopped
    // ============================================================

    try
    {
        if (OperatingSystem.IsWindows())
        {
            using var sc =
                new System.ServiceProcess.ServiceController(
                    "PQMMeterReader");

            if (sc.Status ==
                    System.ServiceProcess.ServiceControllerStatus.Stopped ||
                sc.Status ==
                    System.ServiceProcess.ServiceControllerStatus.StopPending)
            {
                Console.WriteLine(
                    "[Startup] PQMMeterReader Windows service is stopped. " +
                    "Attempting auto-start...");

                sc.Start();

                Console.WriteLine(
                    "[Startup] PQMMeterReader service start command " +
                    "sent successfully.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"[Startup] Note on PQMMeterReader Windows service " +
            $"auto-start: {ex.Message}");
    }

    // ============================================================
    // Middleware
    // ============================================================

    app.UseCors("AllowReactApp");

    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(
                "/openapi/v1.json",
                "PQM API v1");
        });
    }

    // app.UseHttpsRedirection();

    app.UseAuthorization();

    // ============================================================
    // Endpoints
    // ============================================================

    try
    {
        app.MapControllers();

        app.MapHub<DeviceHub>("/hubs/device");

        app.MapFallbackToFile("/index.html");

        app.Run();
    }
    catch (System.Reflection.ReflectionTypeLoadException ex)
    {
        Log.Fatal(
            ex,
            "[PQM.Server] ReflectionTypeLoadException on startup.");

        foreach (var le in ex.LoaderExceptions)
        {
            Log.Error(
                "[LoaderException]: {Message}",
                le?.Message);
        }

        throw;
    }
    catch (Exception ex)
    {
        Log.Fatal(
            ex,
            "[PQM.Server] Host terminated unexpectedly.");

        throw;
    }
}
finally
{
    Log.CloseAndFlush();
}