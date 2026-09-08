using Microsoft.AspNetCore.Mvc;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;
using PQM.Infrastructure.Services;
using PQM.Server.Models;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/device")]
    public class DeviceController : ControllerBase
    {
        private readonly IDeviceRepository _deviceRepository;
        private readonly APIResponse _apiResponse;
        private readonly ILogger<DeviceController> _logger;
        private readonly string _connectionString;
        private readonly PQM.Infrastructure.Services.ProfileSyncService _profileSyncService;

        public DeviceController(IDeviceRepository deviceRepository,ILogger<DeviceController> logger,IConfiguration configuration,ProfileSyncService profileSyncService)
        {
            _deviceRepository = deviceRepository;
            _apiResponse = new APIResponse();
            _logger = logger;
            _profileSyncService = profileSyncService;
            _connectionString = configuration.GetConnectionString("DefaultConnection")?? throw new InvalidOperationException("Connection string DefaultConnection not found.");
        }

        // ============================================================
        // GET: api/device
        // Get all devices
        // ============================================================

        [HttpGet]
        public async Task<ActionResult> GetAllDevices(CancellationToken cancellationToken)
        {
            try
            {
                var devices = await _deviceRepository.GetAllAsync(cancellationToken);

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;
                _apiResponse.Data = devices;
                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    ex.Message
                };

                return Ok(_apiResponse);
            }
        }

        // ============================================================
        // GET: api/device/{id}
        // Get device by ID
        // ============================================================

        [HttpGet("{id:int}")]
        public async Task<ActionResult> GetDeviceById(int id,CancellationToken cancellationToken)
        {
            try
            {
                var device = await _deviceRepository.GetByIdAsync(id,cancellationToken);

                if (device == null)
                    return NotFound();

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;
                _apiResponse.Data = device;
                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    ex.Message
                };

                return Ok(_apiResponse);
            }
        }

        // ============================================================
        // POST: api/device
        // Add device
        // ============================================================

        [HttpPost]
        public async Task<ActionResult> CreateDevice([FromBody] Device device,CancellationToken cancellationToken)
        {
            try
            {
                var created = await _deviceRepository.AddAsync(
                    device,
                    cancellationToken);

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.Created;
                _apiResponse.Data = created;
                _apiResponse.Errors.Clear();

                return CreatedAtAction(
                    nameof(GetDeviceById),
                    new { id = created },
                    _apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    ex.Message
                };

                return Ok(_apiResponse);
            }
        }

        // ============================================================
        // PUT: api/device/{id}
        // Update device
        // ============================================================

        [HttpPut("{id:int}")]
        public async Task<ActionResult> UpdateDevice(int id,[FromBody] Device device,CancellationToken cancellationToken)
        {
            try
            {
                if (id != device.Id)
                    return BadRequest();

                var updated = await _deviceRepository.UpdateAsync(
                    device,
                    cancellationToken);

                if (!updated)
                    return NotFound();

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;
                _apiResponse.Data = updated;
                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    ex.Message
                };

                return Ok(_apiResponse);
            }
        }

        // ============================================================
        // DELETE: api/device/{id}
        // Delete device
        // ============================================================

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> DeleteDevice(int id,CancellationToken cancellationToken)
        {
            try
            {
                var deleted = await _deviceRepository.DeleteAsync(
                    id,
                    cancellationToken);

                if (!deleted)
                    return NotFound();

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;
                _apiResponse.Data = true;
                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    ex.Message
                };

                return Ok(_apiResponse);
            }
        }

        // ============================================================
        // GET: api/device/meterTypes
        // Get all meter types
        // ============================================================

        [HttpGet("meterTypes")]
        public async Task<ActionResult> GetAllMeterTypes(CancellationToken cancellationToken)
        {
            try
            {
                var list = new List<object>();

                using var conn =
                    new Microsoft.Data.SqlClient.SqlConnection(
                        _connectionString);

                await conn.OpenAsync(cancellationToken);

                using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                    SELECT
                        Id,
                        Name
                    FROM MeterType
                    ORDER BY Name ASC;";

                using var reader =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    list.Add(new
                    {
                        id = reader.GetInt32(0),
                        name = reader.GetString(1)
                    });
                }

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;
                _apiResponse.Data = list;
                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    ex.Message
                };

                return Ok(_apiResponse);
            }
        }

        // ============================================================
        // Helper: Check whether device is reachable
        // ============================================================

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

                var completed =
                    await Task.WhenAny(
                        connectTask,
                        timeoutTask);

                if (completed == timeoutTask ||
                    !client.Connected)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }


        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _syncLocks = new();

        private static SemaphoreSlim GetSyncLock(int deviceId) =>_syncLocks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));


        // ============================================================
        // POST: api/device/{id}/sync
        // Trigger sync for a device
        // ============================================================

        [HttpPost("{id:int}/sync")]
        public async Task<ActionResult> TriggerDeviceSync(int id,CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linkedCts.Token;

            var device = await _deviceRepository.GetByIdAsync(id, ct);
            if (device == null)
                return NotFound(new { error = $"Device {id} not found." });

            var deviceLock = GetSyncLock(id);

            bool acquired = await deviceLock.WaitAsync(TimeSpan.Zero, cancellationToken);
            if (!acquired)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.Conflict;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string>
                {
                    "A sync is already in progress for this device. Please wait for it to finish."
                };
                return StatusCode(409, _apiResponse);
            }

            try
            {
                bool reachable = await IsDeviceReachableAsync(device.IP, device.PORT, 5000, ct);
                if (!reachable)
                {
                    _apiResponse.Status = false;
                    _apiResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
                    _apiResponse.Data = null;
                    _apiResponse.Errors = new List<string>
                    {
                        $"Unable to connect to device at {device.IP}:{device.PORT}. Check network connectivity and Power."
                    };
                    return Ok(_apiResponse);
                }

                _logger.LogInformation("[DeviceController] Sync Now started for Device {DeviceId}.", id);

                var result = await _profileSyncService.SyncDeviceAllProfilesAsync(id, ct);

                _apiResponse.Status = result.Success;
                _apiResponse.StatusCode = result.Success? System.Net.HttpStatusCode.OK: System.Net.HttpStatusCode.BadRequest;

                if (result.Success)
                {
                    _apiResponse.Data = new
                    {
                        deviceId = id,
                        status = "Completed",
                        completedAt = DateTime.UtcNow.ToString("o"),
                        message = $"Sync completed successfully for device {id}."
                    };
                    _apiResponse.Errors.Clear();
                }
                else
                {
                    _apiResponse.Data = null;
                    _apiResponse.Errors = new List<string> { result.ErrorMessage ?? "Sync failed." };
                }

                return Ok(_apiResponse);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.RequestTimeout;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string> { "Sync did not complete within 5 minutes." };
                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DeviceController] TriggerSync failed for Device {DeviceId}.", id);

                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string> { ex.Message };
                return Ok(_apiResponse);
            }
            finally
            {
                deviceLock.Release();
            }
        }
    }
}