using Gurux.DLMS.Objects;
using Microsoft.AspNetCore.Mvc;
using PQM.Core.DTOs;
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
        private readonly ILiveRepository _liveRepository;
        private readonly APIResponse _apiResponse;
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _deviceLocks = new();
        public LiveScanController(IDeviceRepository deviceRepository,ILiveRepository liveRepository,ILogger<LiveScanController> logger)
        {
            _deviceRepository = deviceRepository?? throw new ArgumentNullException(nameof(deviceRepository));
            _liveRepository = liveRepository?? throw new ArgumentNullException(nameof(liveRepository));
            _apiResponse = new APIResponse();
        }
        private static SemaphoreSlim GetDeviceLock(int deviceId)
        {
            return _deviceLocks.GetOrAdd(deviceId,_ => new SemaphoreSlim(1, 1));
        }
        private static async Task<bool> IsDeviceReachableAsync(string ip,int port,int timeoutMs,CancellationToken cancellationToken)
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

        [HttpPost("{id:int}/live-scan")]
        public async Task<ActionResult> LiveScan(int id,[FromBody] LiveScanRequest? request,CancellationToken cancellationToken)
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
        private async Task<List<LiveScanItemResult>>ReadLiveValuesFromMeterAsync(Device device,List<int>? profileIds,List<int>? parameterIds,CancellationToken ct)
        {
            List<LiveScanParameterInfo> parameters =
                await _liveRepository.GetParametersForLiveScanAsync(
                    profileIds,
                    parameterIds,
                    device.MeterTypeId,
                    ct);

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

        [HttpGet("profiles")]
        public async Task<ActionResult> GetProfiles(CancellationToken cancellationToken)
        {
            try
            {
                var profiles = await _liveRepository.GetProfilesAsync(
                    cancellationToken);

                var data = profiles
                    .Select(p => new
                    {
                        p.ProfileId,
                        FriendlyName = string.IsNullOrWhiteSpace(p.FriendlyName)
                            ? p.ObisCode
                            : p.FriendlyName,
                        p.ObisCode,
                        p.Category
                    })
                    .OrderBy(p => p.FriendlyName)
                    .ToList();

                _apiResponse.Status = true;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.OK;
                _apiResponse.Data = data;
                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string> { ex.Message };
                return Ok(_apiResponse);
            }
        }

        [HttpGet("parameters")]
        public async Task<ActionResult> GetParameters([FromQuery] int? deviceId,[FromQuery] int? profileId,[FromQuery] int? meterTypeId,CancellationToken cancellationToken)
        {
            try
            {
                // If deviceId is provided but meterTypeId is not, resolve meterTypeId from the device
                if (deviceId.HasValue && deviceId.Value > 0 && !meterTypeId.HasValue)
                {
                    var device = await _deviceRepository.GetByIdAsync(
                        deviceId.Value,
                        cancellationToken);

                    if (device != null && device.MeterTypeId.HasValue)
                    {
                        meterTypeId = device.MeterTypeId.Value;
                    }
                }

                var parameters = await _liveRepository.GetVisibleParametersAsync(
                    profileId,
                    meterTypeId,
                    cancellationToken);

                var data = parameters
                    .Select(p => new
                    {
                        p.Id,
                        p.ProfileId,
                        p.MeterTypeId,
                        p.Name,
                        p.ObisCode,
                        p.Description,
                        p.Unit,
                        p.DataType,
                        p.ObjectType,
                        p.AttributeIndex,
                        p.IsHistorical,
                        p.IsVisible,
                        p.Scaler
                    })
                    .ToList();

                _apiResponse.Status = true;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.OK;
                _apiResponse.Data = data;
                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string> { ex.Message };
                return Ok(_apiResponse);
            }
        }
    }
}