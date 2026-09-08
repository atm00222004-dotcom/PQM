using System.IO;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PQM.Core.Interfaces.Repositories;
using PQM.Infrastructure;
using PQM.Infrastructure.Repositories;
using PQM.Infrastructure.Services;
using Serilog;
using Serilog.Events;

string logDirectory = @"C:\PQM\Logs";
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logDirectory, "server-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

try
{
    Log.Information("[PQM.Server] Starting PQM Web API Server...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services.AddControllers().AddJsonOptions(options =>
    {   
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

    builder.Services.AddOpenApi();

    var connectionString =builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'DefaultConnection' not found."
        );

    builder.Services.AddDbContext<DataContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
    builder.Services.AddScoped<ProfileSyncService>(sp =>
    new ProfileSyncService(
        connectionString,
        sp.GetRequiredService<ILogger<ProfileSyncService>>()));
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

// Auto-apply any pending EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = new DataContext(connectionString);
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Migration error (non-fatal): {ex.Message}");
    }
}

// Auto-start PQMMeterReader Windows Service if currently stopped
try
{
    if (OperatingSystem.IsWindows())
    {
        using var sc = new System.ServiceProcess.ServiceController("PQMMeterReader");
        if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped ||
            sc.Status == System.ServiceProcess.ServiceControllerStatus.StopPending)
        {
            Console.WriteLine("[Startup] PQMMeterReader Windows service is stopped. Attempting auto-start...");
            sc.Start();
            Console.WriteLine("[Startup] PQMMeterReader service start command sent successfully.");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] Note on PQMMeterReader Windows service auto-start: {ex.Message}");
}

app.UseCors("AllowReactApp");
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PQM API v1");
    });
}

// app.UseHttpsRedirection();
app.UseAuthorization();

try
{
    app.MapControllers();
    app.MapFallbackToFile("/index.html");

    app.Run();
}
catch (System.Reflection.ReflectionTypeLoadException ex)
{
    Log.Fatal(ex, "[PQM.Server] ReflectionTypeLoadException on startup.");
    foreach (var le in ex.LoaderExceptions)
    {
        Log.Error("[LoaderException]: {Message}", le?.Message);
    }
    throw;
}
catch (Exception ex)
{
    Log.Fatal(ex, "[PQM.Server] Host terminated unexpectedly.");
    throw;
}
}
finally
{
    Log.CloseAndFlush();
}