using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PQM.Core.Entities;

namespace PQM.Infrastructure.Services
{
    public class SyncResult
    {
        public bool Success { get; set; }
        public int RowsRead { get; set; }
        public int RowsWritten { get; set; }
        public int RowsSkipped { get; set; }
        public DateTime? NewWatermarkUtc { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DeviceSyncResult
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool AlreadyInProgress { get; set; }
        public int ProfilesAttempted { get; set; }
        public int ProfilesSucceeded { get; set; }
        public int TotalRowsRead { get; set; }
        public int TotalRowsWritten { get; set; }
        public int TotalRowsSkipped { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, SyncResult> ProfileResults { get; set; } = new();
    }

    public class ProfileSyncService
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _activeDeviceSyncs = new();

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _lockAcquiredTimes = new();

        private readonly string _connectionString;

        private readonly ILogger<ProfileSyncService> _logger;

        public ProfileSyncService(string connectionString, ILogger<ProfileSyncService> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public static bool TryAcquireLock(int deviceId)
        {
            if (_lockAcquiredTimes.TryGetValue(deviceId, out var acquiredAt))
            {
                if (DateTime.UtcNow - acquiredAt > TimeSpan.FromMinutes(45))
                {
                    _activeDeviceSyncs.TryRemove(deviceId, out _);
                    _lockAcquiredTimes.TryRemove(deviceId, out _);
                }
            }

            if (_activeDeviceSyncs.TryAdd(deviceId, 1))
            {
                _lockAcquiredTimes[deviceId] = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        public static void ReleaseLock(int deviceId)
        {
            _activeDeviceSyncs.TryRemove(deviceId, out _);
            _lockAcquiredTimes.TryRemove(deviceId, out _);
        }

        public async Task<DeviceSyncResult> SyncDeviceAllProfilesAsync(int deviceId, System.Threading.CancellationToken cancellationToken = default)
        {
            var deviceResult = new DeviceSyncResult { DeviceId = deviceId };

            if (!TryAcquireLock(deviceId))
            {
                deviceResult.Success = false;
                deviceResult.AlreadyInProgress = true;
                deviceResult.ErrorMessage = $"Sync already in progress for device {deviceId}.";
                _logger.LogInformation("[ProfileSyncService] Device {DeviceId} is already undergoing a sync. Concurrent request skipped.", deviceId);
                return deviceResult;
            }

            _logger.LogInformation("[ProfileSyncService] Concurrency lock ACQUIRED for Device {DeviceId}.", deviceId);

            DateTime syncExecutionTimeUtc = DateTime.UtcNow;
            bool isTimedOut = false;

            // Generous 45-minute outer safety timeout for complete per-device sweep
            using var hardCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            hardCts.CancelAfter(TimeSpan.FromMinutes(45));
            var syncToken = hardCts.Token;

            try
            {
                Device? device = await LoadDeviceAsync(deviceId);
                if (device == null)
                {
                    deviceResult.Success = false;
                    deviceResult.ErrorMessage = $"Device with Id={deviceId} not found.";
                    _logger.LogError("[ProfileSyncService] {ErrorMessage}", deviceResult.ErrorMessage);
                    return deviceResult;
                }

                deviceResult.DeviceName = device.Name;
                TimeZoneInfo deviceTz = GetDeviceTimeZone(device.TimeZoneId);

                _logger.LogInformation("[ProfileSyncService] Starting single-session profile sweep for Device {DeviceId} ('{DeviceName}')...", deviceId, device.Name);

                await using (var reader = new DlmsMeterReader(device, verboseLogging: false))
                {
                    try
                    {
                        await reader.ConnectAsync(syncToken);
                        await reader.ReadAssociationViewAsync(syncToken);

                        // Loop through all catalog profiles under the SAME open session
                        foreach (var kvp in ProfileCatalog.AllProfiles)
                        {
                            syncToken.ThrowIfCancellationRequested();

                            string obisCode = kvp.Key;
                            deviceResult.ProfilesAttempted++;

                            using var profileCts = CancellationTokenSource.CreateLinkedTokenSource(syncToken);
                            TimeSpan timeout = obisCode switch
                            {
                                "1.0.99.1.0.255" => TimeSpan.FromMinutes(30), // Block Load Profile
                                "1.0.99.2.0.255" => TimeSpan.FromMinutes(15), // Daily Load Profile
                                _ => TimeSpan.FromMinutes(10)
                            };
                            profileCts.CancelAfter(timeout);
                            var profileToken = profileCts.Token;

                            try
                            {
                                var profileSyncRes = await SyncSingleProfileOnOpenReaderAsync(reader, device, obisCode, deviceTz, syncExecutionTimeUtc, profileToken);
                                deviceResult.ProfileResults[obisCode] = profileSyncRes;

                                if (profileSyncRes.Success)
                                {
                                    deviceResult.ProfilesSucceeded++;
                                    deviceResult.TotalRowsRead += profileSyncRes.RowsRead;
                                    deviceResult.TotalRowsWritten += profileSyncRes.RowsWritten;
                                    deviceResult.TotalRowsSkipped += profileSyncRes.RowsSkipped;
                                }
                            }
                            catch (OperationCanceledException) when (profileCts.IsCancellationRequested && !syncToken.IsCancellationRequested)
                            {
                                _logger.LogWarning("[ProfileSyncService] Profile '{ObisCode}' timed out after {Minutes} minutes for Device {DeviceId}. Continuing remaining profiles...", obisCode, Math.Round(timeout.TotalMinutes), deviceId);
                                deviceResult.ProfileResults[obisCode] = new SyncResult
                                {
                                    Success = false,
                                    ErrorMessage = $"Profile read timed out after {Math.Round(timeout.TotalMinutes)} minutes"
                                };
                            }
                            catch (OperationCanceledException) when (syncToken.IsCancellationRequested)
                            {
                                isTimedOut = true;
                                throw; // Outer safety timeout
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[ProfileSyncService] Profile '{ObisCode}' failed for Device {DeviceId}. Continuing remaining profiles...", obisCode, deviceId);
                                deviceResult.ProfileResults[obisCode] = new SyncResult
                                {
                                    Success = false,
                                    ErrorMessage = ex.Message
                                };
                            }
                        }

                        deviceResult.Success = deviceResult.ProfilesSucceeded > 0;
                    }
                    catch (OperationCanceledException) when (syncToken.IsCancellationRequested)
                    {
                        isTimedOut = true;
                        _logger.LogWarning("[ProfileSyncService] Sync timed out after 5 minutes for Device {DeviceId}.", deviceId);
                        deviceResult.Success = false;
                        deviceResult.ErrorMessage = "Sync timed out after 5 minutes";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ProfileSyncService] Failed to establish DLMS session with Device {DeviceId} ('{DeviceName}').", deviceId, device.Name);
                        deviceResult.Success = false;
                        deviceResult.ErrorMessage = $"Connection failure: {ex.Message}";
                    }
                } // DisconnectAsync() executes here automatically, sending WRAPPER RLRQ frame!

                await UpdateDeviceStatusInDbAsync(deviceId, syncExecutionTimeUtc);

                return deviceResult;
            }
            catch (OperationCanceledException) when (syncToken.IsCancellationRequested)
            {
                isTimedOut = true;
                _logger.LogWarning("[ProfileSyncService] Hard cancellation timeout reached for Device {DeviceId}.", deviceId);
                deviceResult.Success = false;
                deviceResult.ErrorMessage = "Sync timed out after 5 minutes";
                await UpdateDeviceStatusInDbAsync(deviceId, syncExecutionTimeUtc);
                return deviceResult;
            }
            finally
            {
                ReleaseLock(deviceId);
                _logger.LogInformation("[ProfileSyncService] Concurrency lock RELEASED for Device {DeviceId}.", deviceId);
            }
        }

        private async Task<SyncResult> SyncSingleProfileOnOpenReaderAsync(DlmsMeterReader reader,Device device,string obisCode,TimeZoneInfo deviceTz,DateTime syncExecutionTimeUtc,System.Threading.CancellationToken cancellationToken = default)
        {
            var result = new SyncResult();
            bool isTimeSeries = ProfileCatalog.TimeSeriesProfiles.ContainsKey(obisCode);
            bool isStaticOrMetadata = ProfileCatalog.StaticOrMetadataProfiles.ContainsKey(obisCode);

            int profileId = await EnsureProfileAsync(obisCode, isTimeSeries);

            DateTime? startTimeLocal = null;
            DateTime? currentWatermarkUtc = null;

            if (isTimeSeries)
            {
                currentWatermarkUtc = await GetLastReadWatermarkUtcAsync(device.Id, profileId);
                if (currentWatermarkUtc.HasValue)
                {
                    DateTime watermarkWithSafetyUtc = currentWatermarkUtc.Value.AddHours(-1);
                    startTimeLocal = TimeZoneInfo.ConvertTimeFromUtc(watermarkWithSafetyUtc, deviceTz);
                }
            }

            IReadOnlyList<ProfileColumnInfo> columns;
            var profileObj = reader.GetProfileObjects().FirstOrDefault(p => p.LogicalName == obisCode);
            if (profileObj != null)
            {
                columns = await reader.ReadCaptureObjectsAsync(profileObj, cancellationToken);
            }
            else
            {
                columns = new List<ProfileColumnInfo>();
            }

            var parameterMap = await EnsureParametersAsync(profileId, columns);
            var rows = await reader.ReadProfileAllEntriesAsync(obisCode, startTimeLocal, cancellationToken);
            result.RowsRead = rows.Count;

            if (rows.Count == 0)
            {
                result.Success = true;
                return result;
            }

            return await SaveReadingSessionAsync(device.Id, profileId, obisCode, isTimeSeries, deviceTz, rows, columns, parameterMap, currentWatermarkUtc, syncExecutionTimeUtc);
        }

        private async Task<int> GetOrCreateParameterForColumnAsync(SqlConnection conn,SqlTransaction tx,int profileId,int colIndex,IReadOnlyList<ProfileColumnInfo> columns)
        {
            string obis = (colIndex < columns.Count && !string.IsNullOrEmpty(columns[colIndex].LogicalName))
                ? columns[colIndex].LogicalName
                : $"Param_{profileId}_{colIndex}";

            string name = (colIndex < columns.Count && !string.IsNullOrEmpty(columns[colIndex].Description))
                ? columns[colIndex].Description
                : obis;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Id FROM Parameters WHERE ProfileId = @pid AND ObisCode = @obis";
                cmd.Parameters.AddWithValue("@pid", profileId);
                cmd.Parameters.AddWithValue("@obis", obis);
                var existing = await cmd.ExecuteScalarAsync();
                if (existing != null && existing != DBNull.Value)
                {
                    return Convert.ToInt32(existing);
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO Parameters (ProfileId, Name, ObisCode, AttributeIndex, IsHistorical, IsVisible, CreatedAt)
                                    VALUES (@pid, @name, @obis, 2, 1, 1, GETUTCDATE());
                                    SELECT SCOPE_IDENTITY();";
                cmd.Parameters.AddWithValue("@pid", profileId);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@obis", obis);
                var newId = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(newId);
            }
        }

        private async Task UpdateDeviceStatusInDbAsync(int deviceId, DateTime lastSyncUtc)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        UPDATE Devices 
        SET LastSync = @lastSync
        WHERE Id = @id";
            cmd.Parameters.AddWithValue("@lastSync", lastSyncUtc);
            cmd.Parameters.AddWithValue("@id", deviceId);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<SyncResult> SaveReadingSessionAsync(int deviceId,int profileId,string obisCode,bool isTimeSeries,TimeZoneInfo deviceTz,IReadOnlyList<ProfileRow> rows,IReadOnlyList<ProfileColumnInfo> columns,Dictionary<int, int> parameterMap,DateTime? currentWatermarkUtc,DateTime syncExecutionTimeUtc)
        {
            var result = new SyncResult { RowsRead = rows.Count };

            if (rows.Count == 0)
            {
                result.Success = true;
                return result;
            }

            DateTime? maxWrittenEntryUtc = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var tx = conn.BeginTransaction();

                try
                {
                    var existingTimestamps = await GetExistingEntryTimestampsUtcAsync(conn, tx, deviceId, profileId);

                    for (int rIdx = 0; rIdx < rows.Count; rIdx++)
                    {
                        var row = rows[rIdx];

                        DateTime? entryTimestampUtc = null;
                        if (row.Timestamp.HasValue && row.Timestamp.Value.Year > 1)
                        {
                            try
                            {
                                var localDt = DateTime.SpecifyKind(row.Timestamp.Value, DateTimeKind.Unspecified);
                                entryTimestampUtc = TimeZoneInfo.ConvertTimeToUtc(localDt, deviceTz);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[ProfileSyncService] Row {RIdx}: failed to convert local timestamp {LocalTs} to UTC.", rIdx, row.Timestamp);
                                entryTimestampUtc = null;
                            }
                        }

                        if (entryTimestampUtc.HasValue && existingTimestamps.Contains(entryTimestampUtc.Value))
                        {
                            result.RowsSkipped++;
                            continue;
                        }

                        long sessionId = await InsertReadingSessionAsync(conn, tx, deviceId, profileId, syncExecutionTimeUtc, entryTimestampUtc);

                        for (int cIdx = 0; cIdx < row.Values.Count; cIdx++)
                        {
                            int parameterId = 0;
                            if (parameterMap.TryGetValue(cIdx, out int pid) && pid > 0)
                            {
                                parameterId = pid;
                            }
                            else
                            {
                                parameterId = await GetOrCreateParameterForColumnAsync(conn, tx, profileId, cIdx, columns);
                                parameterMap[cIdx] = parameterId;
                            }

                            var cellObj = row.Values[cIdx];
                            string formattedVal = ValueFormatter.FormatValue(cellObj);
                            string? rawVal = cellObj?.ToString();
                            double? numericVal = TryParseDouble(formattedVal);

                            await InsertReadingValueAsync(conn, tx, sessionId, parameterId, formattedVal, rawVal, numericVal);
                        }

                        result.RowsWritten++;
                        if (entryTimestampUtc.HasValue)
                        {
                            existingTimestamps.Add(entryTimestampUtc.Value);
                            if (!maxWrittenEntryUtc.HasValue || entryTimestampUtc.Value > maxWrittenEntryUtc.Value)
                            {
                                maxWrittenEntryUtc = entryTimestampUtc.Value;
                            }
                        }
                    }

                    if (isTimeSeries)
                    {
                        DateTime? watermarkToSave = maxWrittenEntryUtc ?? currentWatermarkUtc;
                        if (watermarkToSave.HasValue)
                        {
                            await UpsertDeviceProfileSyncStateAsync(conn, tx, deviceId, profileId, watermarkToSave.Value, syncExecutionTimeUtc);
                            result.NewWatermarkUtc = watermarkToSave;
                        }
                    }

                    await tx.CommitAsync();
                    result.Success = true;
                    return result;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "[ProfileSyncService] Transaction failed for device {DeviceId}, profile '{ObisCode}'. Rollback executed.", deviceId, obisCode);
                    result.Success = false;
                    result.ErrorMessage = $"Database transaction error: {ex.Message}";
                    return result;
                }
            }
        }

        private TimeZoneInfo GetDeviceTimeZone(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ProfileSyncService] Invalid TimeZoneId '{TimeZoneId}' on Device. Falling back to 'India Standard Time'.", timeZoneId);
                }
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private async Task<Device?> LoadDeviceAsync(int deviceId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Name, IP, PORT, ClientAddress, ServerAddress,
                                       AuthenticationTypeId, Password, Timeout, TimeZoneId
                                FROM Devices WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", deviceId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Device
                {
                    Id                   = reader.GetInt32(reader.GetOrdinal("Id")),
                    Name                 = reader.GetString(reader.GetOrdinal("Name")),
                    IP                   = reader.GetString(reader.GetOrdinal("IP")),
                    PORT                 = reader.GetInt32(reader.GetOrdinal("PORT")),
                    ClientAddress        = reader.IsDBNull(reader.GetOrdinal("ClientAddress"))        ? 16    : reader.GetInt32(reader.GetOrdinal("ClientAddress")),
                    ServerAddress        = reader.IsDBNull(reader.GetOrdinal("ServerAddress"))        ? 1     : reader.GetInt32(reader.GetOrdinal("ServerAddress")),
                    AuthenticationTypeId = reader.IsDBNull(reader.GetOrdinal("AuthenticationTypeId")) ? null  : reader.GetInt32(reader.GetOrdinal("AuthenticationTypeId")),
                    Password             = reader.IsDBNull(reader.GetOrdinal("Password"))             ? null  : reader.GetString(reader.GetOrdinal("Password")),
                    Timeout              = reader.IsDBNull(reader.GetOrdinal("Timeout"))              ? 30000 : reader.GetInt32(reader.GetOrdinal("Timeout")),
                    TimeZoneId           = reader.IsDBNull(reader.GetOrdinal("TimeZoneId"))           ? null  : reader.GetString(reader.GetOrdinal("TimeZoneId"))
                };
            }

            return null;
        }

        private async Task<int> EnsureProfileAsync(string obisCode, bool isTimeSeries)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Profiles WHERE ObisCode = @obis";
                cmd.Parameters.AddWithValue("@obis", obisCode);
                var existingId = await cmd.ExecuteScalarAsync();
                if (existingId != null && existingId != DBNull.Value)
                {
                    return Convert.ToInt32(existingId);
                }
            }

            // Insert missing profile
            using (var cmd = conn.CreateCommand())
            {
                string friendlyName = ProfileCatalog.AllProfiles.GetValueOrDefault(obisCode, obisCode);
                string category = isTimeSeries ? "TimeSeries" : "Static";

                cmd.CommandText = @"INSERT INTO Profiles (ObisCode, FriendlyName, Category)
                                    VALUES (@obis, @name, @cat);
                                    SELECT SCOPE_IDENTITY();";
                cmd.Parameters.AddWithValue("@obis", obisCode);
                cmd.Parameters.AddWithValue("@name", friendlyName);
                cmd.Parameters.AddWithValue("@cat", category);

                var newId = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(newId);
            }
        }

        private async Task<DateTime?> GetLastReadWatermarkUtcAsync(int deviceId, int profileId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT LastReadTimestampUtc FROM DeviceProfileSyncState
                                WHERE DeviceId = @did AND ProfileId = @pid";
            cmd.Parameters.AddWithValue("@did", deviceId);
            cmd.Parameters.AddWithValue("@pid", profileId);

            var val = await cmd.ExecuteScalarAsync();
            if (val != null && val != DBNull.Value)
            {
                return Convert.ToDateTime(val);
            }
            return null;
        }

        private async Task<Dictionary<int, int>> EnsureParametersAsync(int profileId, IReadOnlyList<ProfileColumnInfo> columns)
        {
            var map = new Dictionary<int, int>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Load existing parameters for this profile
            var existingParams = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, ObisCode FROM Parameters WHERE ProfileId = @pid";
                cmd.Parameters.AddWithValue("@pid", profileId);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    int pId = rdr.GetInt32(0);
                    string? obis = rdr.IsDBNull(1) ? null : rdr.GetString(1);
                    if (!string.IsNullOrEmpty(obis) && !existingParams.ContainsKey(obis))
                    {
                        existingParams[obis] = pId;
                    }
                }
            }

            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                string obis = !string.IsNullOrEmpty(col.LogicalName) ? col.LogicalName : $"Col_{col.Index}";

                if (existingParams.TryGetValue(obis, out int paramId))
                {
                    map[i] = paramId;

                    // Update metadata if Scaler/Unit was previously missing
                    if (col.Scaler.HasValue || col.UnitCode.HasValue || !string.IsNullOrEmpty(col.Unit))
                    {
                        using var updateCmd = conn.CreateCommand();
                        updateCmd.CommandText = @"
                            UPDATE Parameters
                            SET Scaler = ISNULL(Scaler, @scaler),
                                UnitCode = ISNULL(UnitCode, @unitCode),
                                Unit = ISNULL(Unit, @unit)
                            WHERE Id = @id AND (Scaler IS NULL OR Unit IS NULL OR UnitCode IS NULL);";
                        updateCmd.Parameters.AddWithValue("@scaler", (object?)col.Scaler ?? DBNull.Value);
                        updateCmd.Parameters.AddWithValue("@unitCode", (object?)col.UnitCode ?? DBNull.Value);
                        updateCmd.Parameters.AddWithValue("@unit", (object?)col.Unit ?? DBNull.Value);
                        updateCmd.Parameters.AddWithValue("@id", paramId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Create missing Parameter with full DLMS metadata
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"INSERT INTO Parameters (ProfileId, Name, ObisCode, ObjectType, AttributeIndex, Scaler, UnitCode, Unit, IsHistorical, IsVisible, CreatedAt)
                                        VALUES (@pid, @name, @obis, @objType, @attrIdx, @scaler, @unitCode, @unit, 1, 1, GETUTCDATE());
                                        SELECT SCOPE_IDENTITY();";
                    cmd.Parameters.AddWithValue("@pid", profileId);
                    cmd.Parameters.AddWithValue("@name", obis);
                    cmd.Parameters.AddWithValue("@obis", obis);
                    cmd.Parameters.AddWithValue("@objType", (object?)col.ObjectType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@attrIdx", col.AttributeIndex);
                    cmd.Parameters.AddWithValue("@scaler", (object?)col.Scaler ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@unitCode", (object?)col.UnitCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@unit", (object?)col.Unit ?? DBNull.Value);

                    var newId = await cmd.ExecuteScalarAsync();
                    int newParamId = Convert.ToInt32(newId);
                    existingParams[obis] = newParamId;
                    map[i] = newParamId;
                }
            }

            return map;
        }

        private async Task<HashSet<DateTime>> GetExistingEntryTimestampsUtcAsync(SqlConnection conn, SqlTransaction tx, int deviceId, int profileId)
        {
            var set = new HashSet<DateTime>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT EntryTimestampUtc FROM ReadingSessions
                                WHERE DeviceId = @did AND ProfileId = @pid AND EntryTimestampUtc IS NOT NULL";
            cmd.Parameters.AddWithValue("@did", deviceId);
            cmd.Parameters.AddWithValue("@pid", profileId);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var dt = rdr.GetDateTime(0);
                set.Add(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            }

            return set;
        }

        private async Task<long> InsertReadingSessionAsync(SqlConnection conn, SqlTransaction tx, int deviceId, int profileId, DateTime readTime, DateTime? entryTimestampUtc)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO ReadingSessions (DeviceId, ProfileId, ReadTime, EntryTimestampUtc)
                                VALUES (@did, @pid, @rt, @et);
                                SELECT SCOPE_IDENTITY();";
            cmd.Parameters.AddWithValue("@did", deviceId);
            cmd.Parameters.AddWithValue("@pid", profileId);
            cmd.Parameters.AddWithValue("@rt", readTime);
            cmd.Parameters.AddWithValue("@et", (object?)entryTimestampUtc ?? DBNull.Value);

            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(id);
        }

        private async Task InsertReadingValueAsync(SqlConnection conn, SqlTransaction tx, long sessionId, int parameterId, string value, string? rawValue, double? numericValue)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO ReadingValues (SessionId, ParameterId, Value, RawValue, ValueNumeric)
                                VALUES (@sid, @pid, @val, @raw, @num)";
            cmd.Parameters.AddWithValue("@sid", sessionId);
            cmd.Parameters.AddWithValue("@pid", parameterId);
            cmd.Parameters.AddWithValue("@val", (object?)ValueFormatter.CleanValue(value) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@raw", (object?)rawValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@num", (object?)numericValue ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task UpsertDeviceProfileSyncStateAsync(SqlConnection conn, SqlTransaction tx, int deviceId, int profileId, DateTime lastReadTimestampUtc, DateTime lastSyncedAt)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                MERGE DeviceProfileSyncState AS target
                USING (SELECT @did AS DeviceId, @pid AS ProfileId) AS source
                ON (target.DeviceId = source.DeviceId AND target.ProfileId = source.ProfileId)
                WHEN MATCHED THEN
                    UPDATE SET target.LastReadTimestampUtc = @lr, target.LastSyncedAt = @ls
                WHEN NOT MATCHED THEN
                    INSERT (DeviceId, ProfileId, LastReadTimestampUtc, LastSyncedAt)
                    VALUES (@did, @pid, @lr, @ls);";

            cmd.Parameters.AddWithValue("@did", deviceId);
            cmd.Parameters.AddWithValue("@pid", profileId);
            cmd.Parameters.AddWithValue("@lr", lastReadTimestampUtc);
            cmd.Parameters.AddWithValue("@ls", lastSyncedAt);

            await cmd.ExecuteNonQueryAsync();
        }

        private static double? TryParseDouble(string input)
        {
            if (double.TryParse(input, out var val)) return val;
            return null;
        }
    }
}
