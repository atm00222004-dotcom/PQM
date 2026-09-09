using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PQM.Infrastructure.Services;
using PQM.Core.Helpers;
using PQM.Core.DTOs;

namespace PQM.Console
{
    public class DeviceConsoleRunnerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DeviceConsoleRunnerService> _logger;
        private readonly ConsoleOptions _options;
        private readonly string _connectionString;

        public DeviceConsoleRunnerService(IServiceScopeFactory scopeFactory, IOptions<ConsoleOptions> options, ILogger<DeviceConsoleRunnerService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _connectionString = !string.IsNullOrWhiteSpace(_options.DefaultConnection) ? _options.DefaultConnection : throw new InvalidOperationException("Connection string 'DefaultConnection' not found in options.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int tickCounter = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                tickCounter++;
                if (tickCounter % 12 == 1)
                {
                    _logger.LogInformation("[PQM.Console] Service Heartbeat — Service active and polling. Time: {TimeUtc:yyyy-MM-dd HH:mm:ss UTC}.", DateTime.UtcNow);
                }

                try
                {
                    await ProcessDueSchedulesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PQM.Console] Error during sync execution cycle: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("[PQM.Console] Production Sync Runner Stopped.");
        }

        private async Task ProcessDueSchedulesAsync(CancellationToken stoppingToken)
        {
            var dueSchedules = await GetDueSchedulesAsync(stoppingToken);

            if (dueSchedules.Count == 0)
                return;

            _logger.LogInformation(
                "[PQM.Console] Found {Count} due schedule(s) to execute.",
                dueSchedules.Count);

            foreach (var schedule in dueSchedules)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;

                _logger.LogInformation(
                    "[PQM.Console] Executing Schedule {ScheduleId} for {DeviceCount} device(s).",
                    schedule.ScheduleId,
                    schedule.DeviceIds.Count);

                DateTime nowUtc = DateTime.UtcNow;

                DateTime? nextRunAtUtc =
                    ScheduleHelper.ComputeNextRunAtUtc(schedule.ScheduledTime, schedule.TimeZoneId, nowUtc);

                // Mark as Running immediately, and advance NextRunAtUtc now so this
                // schedule isn't picked up again on the next 5-second poll.
                using (var advanceCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await UpdateScheduleCompletionAsync(
                        schedule.ScheduleId, nowUtc, "Running", nextRunAtUtc, advanceCts.Token);
                }

                // Run this schedule for ALL active devices, collecting per-device outcome.
                var deviceTasks = schedule.DeviceIds.Select(deviceId => ProcessScheduledDeviceAsync(deviceId, schedule.ScheduleId, stoppingToken));

                bool[] deviceOutcomes = await Task.WhenAll(deviceTasks);

                // Determine overall schedule status from actual device results.
                int succeeded = deviceOutcomes.Count(ok => ok);
                int total = deviceOutcomes.Length;

                string finalStatus = total == 0? "Success": succeeded == total? "Success": succeeded > 0? "PartialFailure": "Failed";

                using (var completionCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await UpdateScheduleCompletionAsync(
                        schedule.ScheduleId, DateTime.UtcNow, finalStatus, nextRunAtUtc, completionCts.Token);
                }

                _logger.LogInformation(
                    "[PQM.Console] Completed Schedule {ScheduleId}. Status={Status} ({Succeeded}/{Total} devices). NextRun={NextRunAtUtc}",
                    schedule.ScheduleId,
                    finalStatus,
                    succeeded,
                    total,
                    nextRunAtUtc);
            }
        }

        private async Task<bool> ProcessScheduledDeviceAsync(int deviceId, int scheduleId, CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
                return false;

            using var scope = _scopeFactory.CreateScope();

            var profileSyncService =scope.ServiceProvider.GetRequiredService<ProfileSyncService>();

            // Prevent same device from syncing twice (e.g. Sync Now already running it).
            if (!ProfileSyncService.TryAcquireLock(deviceId))
            {
                _logger.LogInformation(
                    "[PQM.Console] Device {DeviceId} is already syncing. Skipping scheduled run.",
                    deviceId);

                return false;
            }

            try
            {
                _logger.LogInformation(
                    "[PQM.Console] Schedule {ScheduleId}: Starting sync for Device {DeviceId}.",
                    scheduleId,
                    deviceId);

                var result = await profileSyncService.SyncDeviceAllProfilesAsync(deviceId, stoppingToken);

                string finalStatus = result.Success ? "Online" : "Error";

                if (result.Success)
                {
                    _logger.LogInformation(
                        "[PQM.Console] Schedule {ScheduleId}: Device {DeviceId} completed. Status={Status}, Profiles={Succeeded}/{Attempted}",
                        scheduleId, deviceId, finalStatus, result.ProfilesSucceeded, result.ProfilesAttempted);
                }
                else
                {
                    _logger.LogWarning(
                        "[PQM.Console] Schedule {ScheduleId}: Device {DeviceId} failed. Error={Error}",
                        scheduleId, deviceId, result.ErrorMessage);
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[PQM.Console] Schedule {ScheduleId}: Device {DeviceId} sync threw an exception.",
                    scheduleId,
                    deviceId);

                return false;
            }
            finally
            {
                ProfileSyncService.ReleaseLock(deviceId);
            }
        }

        private async Task<List<DueScheduleItem>> GetDueSchedulesAsync(CancellationToken cancellationToken)
        {
            var list = new List<DueScheduleItem>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Get all due global schedules.
            using (var scheduleCmd = conn.CreateCommand())
            {
                scheduleCmd.CommandText = @"
                SELECT
                    Id,
                    ScheduledTime,
                    RepeatMode
                FROM DeviceSyncSchedule
                WHERE IsEnabled = 1
                  AND NextRunAtUtc IS NOT NULL
                  AND NextRunAtUtc <= @nowUtc
                ORDER BY Id";

                scheduleCmd.Parameters.AddWithValue(
                    "@nowUtc",
                    DateTime.UtcNow);

                using var scheduleReader =
                    await scheduleCmd.ExecuteReaderAsync(cancellationToken);

                while (await scheduleReader.ReadAsync(cancellationToken))
                {
                    list.Add(new DueScheduleItem
                    {
                        ScheduleId = scheduleReader.GetInt32(0),
                        ScheduledTime = scheduleReader.GetTimeSpan(1),
                        RepeatMode = scheduleReader.IsDBNull(2)
                            ? "Daily"
                            : scheduleReader.GetString(2),
                        TimeZoneId = "India Standard Time"
                    });
                }
            }

            // No due schedules.
            if (list.Count == 0)
                return list;

            // Get ALL active devices.
            var deviceIds = new List<int>();

            using (var deviceCmd = conn.CreateCommand())
            {
                deviceCmd.CommandText = @"
                    SELECT Id
                    FROM Devices
                    WHERE IsDeleted = 0
                       OR IsDeleted IS NULL
                    ORDER BY Id";

                using var deviceReader =
                    await deviceCmd.ExecuteReaderAsync(cancellationToken);

                while (await deviceReader.ReadAsync(cancellationToken))
                {
                    deviceIds.Add(deviceReader.GetInt32(0));
                }
            }

            // Assign all active devices to every due schedule.
            foreach (var schedule in list)
            {
                schedule.DeviceIds.AddRange(deviceIds);
            }

            return list;
        }

        private async Task UpdateScheduleCompletionAsync(int scheduleId, DateTime lastRunAtUtc, string lastRunStatus, DateTime? nextRunAtUtc, CancellationToken cancellationToken)
        {
            using var conn = new SqlConnection(_connectionString);

            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
            UPDATE DeviceSyncSchedule
            SET LastRunAtUtc = @lastRunAtUtc,
                LastRunStatus = @lastRunStatus,
                NextRunAtUtc = @nextRunAtUtc
            WHERE Id = @scheduleId";

            cmd.Parameters.AddWithValue("@scheduleId", scheduleId);
            cmd.Parameters.AddWithValue("@lastRunAtUtc", lastRunAtUtc);
            cmd.Parameters.AddWithValue("@lastRunStatus", lastRunStatus);
            cmd.Parameters.AddWithValue(
                "@nextRunAtUtc",
                (object?)nextRunAtUtc ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}