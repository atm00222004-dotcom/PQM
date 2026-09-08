using Gurux.DLMS;
using Gurux.DLMS.Objects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;
using PQM.Infrastructure.Services;
using PQM.Server.Models;
using System.Collections.Concurrent;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/device")]
    public class LiveScanController : ControllerBase
    {
        private readonly IDeviceRepository _deviceRepository;
        private readonly APIResponse _apiResponse;
        private readonly ILogger<LiveScanController> _logger;
        private readonly string _connectionString;

    private static readonly ConcurrentDictionary<int, SemaphoreSlim>
        _deviceLocks = new();

        public LiveScanController(
            IDeviceRepository deviceRepository,
            ILogger<LiveScanController> logger,
            IConfiguration configuration)
        {
            _deviceRepository = deviceRepository;
            _apiResponse = new APIResponse();
            _logger = logger;

            _connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string DefaultConnection not found.");
        }

        private static SemaphoreSlim GetDeviceLock(int deviceId)
        {
            return _deviceLocks.GetOrAdd(
                deviceId,
                _ => new SemaphoreSlim(1, 1));
        }

        private static async Task<bool> IsDeviceReachableAsync(
            string ip,
            int port,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            try
            {
                using var client =
                    new System.Net.Sockets.TcpClient();

                var connectTask =
                    client.ConnectAsync(ip, port);

                var timeoutTask =
                    Task.Delay(
                        timeoutMs,
                        cancellationToken);

                var completedTask =
                    await Task.WhenAny(
                        connectTask,
                        timeoutTask);

                if (completedTask == connectTask)
                {
                    await connectTask;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Live scan endpoint
        [HttpPost("{id:int}/live-scan")]
        public async Task<ActionResult> LiveScan(
            int id,
            [FromBody] LiveScanRequest? request,
            CancellationToken cancellationToken)
        {
            // Overall live-scan timeout
            using var timeoutCts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(120));

            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);

            var ct = linkedCts.Token;

            var device =
                await _deviceRepository.GetByIdAsync(
                    id,
                    ct);

            if (device == null)
            {
                return NotFound(new
                {
                    error = $"Device {id} not found."
                });
            }

            var deviceLock =
                GetDeviceLock(id);

            // Do not queue another scan for the same device
            bool acquired =
                await deviceLock.WaitAsync(
                    TimeSpan.Zero,
                    cancellationToken);

            if (!acquired)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.Conflict;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    "A previous scan on this device is still in progress. If this persists, the device may be unresponsive — try again shortly."
                    };

                return StatusCode(
                    409,
                    _apiResponse);
            }

            try
            {
                bool reachable =
                    await IsDeviceReachableAsync(
                        device.IP,
                        device.PORT,
                        5000,
                        ct);

                if (!reachable)
                {
                    _apiResponse.Status = false;
                    _apiResponse.StatusCode =
                        System.Net.HttpStatusCode.BadRequest;

                    _apiResponse.Data = null;

                    _apiResponse.Errors =
                        new List<string>
                        {
                        $"Unable to connect to device at {device.IP}:{device.PORT}. Check network connectivity and Power."
                        };

                    return Ok(_apiResponse);
                }

                var items =
                    await ReadLiveValuesFromMeterAsync(
                        device,
                        request?.ProfileIds,
                        request?.ParameterIds,
                        ct);

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;

                _apiResponse.Data = new
                {
                    scannedAt =
                        DateTime.UtcNow.ToString("o"),

                    deviceId = id,

                    deviceName = device.Name,

                    items
                };

                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (OperationCanceledException)
                when (timeoutCts.IsCancellationRequested)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.RequestTimeout;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    "Meter did not respond within 120 seconds."
                    };

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[LiveScanController] Live scan failed for Device {DeviceId}.",
                    id);

                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    ex.Message
                    };

                return Ok(_apiResponse);
            }
            finally
            {
                deviceLock.Release();
            }
        }

        private async Task<List<LiveScanItemResult>>
            ReadLiveValuesFromMeterAsync(
                Device device,
                List<int>? profileIds,
                List<int>? parameterIds,
                CancellationToken ct)
        {
            var parameters =
                new List<(
                    int Id,
                    string Name,
                    string ObisCode,
                    string? ObjectType,
                    int? AttributeIndex,
                    int? Scaler,
                    string? Unit)>();

            using (var conn =
                new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);

                using var cmd =
                    conn.CreateCommand();

                if (parameterIds != null &&
                    parameterIds.Count > 0)
                {
                    var idParams =
                        string.Join(
                            ",",
                            parameterIds.Select(
                                (_, i) => $"@id{i}"));

                    cmd.CommandText = $@"
                    SELECT
                        Id,
                        Name,
                        ObisCode,
                        ObjectType,
                        AttributeIndex,
                        Scaler,
                        Unit
                    FROM Parameters
                    WHERE Id IN ({idParams})
                      AND ObisCode IS NOT NULL
                      AND IsVisible = 1;";

                    for (int i = 0;
                         i < parameterIds.Count;
                         i++)
                    {
                        cmd.Parameters.Add(
                            $"@id{i}",
                            System.Data.SqlDbType.Int)
                            .Value = parameterIds[i];
                    }
                }
                else if (profileIds != null &&
                         profileIds.Count > 0)
                {
                    var profileParams =
                        string.Join(
                            ",",
                            profileIds.Select(
                                (_, i) => $"@profileId{i}"));

                    cmd.CommandText = $@"
                    SELECT
                        Id,
                        Name,
                        ObisCode,
                        ObjectType,
                        AttributeIndex,
                        Scaler,
                        Unit
                    FROM Parameters
                    WHERE ProfileId IN ({profileParams})
                      AND ObisCode IS NOT NULL
                      AND IsVisible = 1;";

                    for (int i = 0;
                         i < profileIds.Count;
                         i++)
                    {
                        cmd.Parameters.Add(
                            $"@profileId{i}",
                            System.Data.SqlDbType.Int)
                            .Value = profileIds[i];
                    }
                }
                else
                {
                    cmd.CommandText = @"
                    SELECT TOP 50
                        Id,
                        Name,
                        ObisCode,
                        ObjectType,
                        AttributeIndex,
                        Scaler,
                        Unit
                    FROM Parameters
                    WHERE ObisCode IS NOT NULL
                      AND IsVisible = 1
                      AND (
                          MeterTypeId = @meterTypeId
                          OR MeterTypeId IS NULL
                      )
                    ORDER BY Id;";

                    cmd.Parameters.Add(
                        "@meterTypeId",
                        System.Data.SqlDbType.Int)
                        .Value =
                        (object?)device.MeterTypeId
                        ?? DBNull.Value;
                }

                using var reader =
                    await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    parameters.Add(
                        (
                            Id: reader.GetInt32(0),

                            Name: reader.GetString(1),

                            ObisCode: reader.GetString(2),

                            ObjectType:
                                reader.IsDBNull(3)
                                    ? null
                                    : reader.GetString(3),

                            AttributeIndex:
                                reader.IsDBNull(4)
                                    ? (int?)null
                                    : reader.GetInt32(4),

                            Scaler:
                                reader.IsDBNull(5)
                                    ? (int?)null
                                    : reader.GetInt32(5),

                            Unit:
                                reader.IsDBNull(6)
                                    ? null
                                    : reader.GetString(6)
                        ));
                }
            }

            if (parameters.Count == 0)
            {
                return new List<LiveScanItemResult>();
            }

            var results =
                new List<LiveScanItemResult>();

            await using var meterReader =
                new DlmsMeterReader(device);

            await meterReader.ConnectAsync(ct);

            foreach (var param in parameters)
            {
                var item =
                    new LiveScanItemResult
                    {
                        ParameterId = param.Id,
                        ParameterName = param.Name,
                        ObisCode = param.ObisCode,
                        Unit = param.Unit
                    };

                try
                {
                    GXDLMSObject dlmsObj =
                        param.ObjectType switch
                        {
                            "GXDLMSExtendedRegister" =>
                                new GXDLMSExtendedRegister(
                                    param.ObisCode),

                            "GXDLMSDemandRegister" =>
                                new GXDLMSDemandRegister(
                                    param.ObisCode),

                            _ =>
                                new GXDLMSRegister(
                                    param.ObisCode)
                        };

                    if (param.Scaler.HasValue &&
                        dlmsObj is GXDLMSRegister reg)
                    {
                        reg.Scaler =
                            param.Scaler.Value;
                    }
                    else if (param.Scaler.HasValue &&
                             dlmsObj is GXDLMSExtendedRegister extReg)
                    {
                        extReg.Scaler =
                            param.Scaler.Value;
                    }

                    int attributeIndex =
                        param.AttributeIndex ?? 2;

                    var value =
                        await meterReader.ReadObjectAsync(
                            dlmsObj,
                            attributeIndex,
                            ct);

                    item.Value =
                        value?.ToString()
                        ?? string.Empty;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item.Error = ex.Message;
                    item.Value = string.Empty;
                }

                results.Add(item);
            }

            return results;
        }
    }
}
