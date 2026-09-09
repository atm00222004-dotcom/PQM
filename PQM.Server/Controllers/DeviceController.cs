using Microsoft.AspNetCore.Mvc;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;
using PQM.Infrastructure.Services;
using PQM.Server.Models;
using System.Net;
using System.Net.Sockets;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/device")]
    public class DeviceController : ControllerBase
    {
        private readonly IDeviceRepository _deviceRepository;
        private readonly ILogger<DeviceController> _logger;
        private readonly ProfileSyncService _profileSyncService;
        public DeviceController(IDeviceRepository deviceRepository,ILogger<DeviceController> logger,ProfileSyncService profileSyncService)
        {
            _deviceRepository = deviceRepository;
            _logger = logger;
            _profileSyncService = profileSyncService;
        }

        [HttpGet]
        public async Task<ActionResult> GetAllDevices(CancellationToken cancellationToken)
        {
            try
            {
                var devices = await _deviceRepository.GetAllAsync(cancellationToken);

                return Ok(new APIResponse
                {
                    Status = true,
                    StatusCode = HttpStatusCode.OK,
                    Data = devices,
                    Errors = new List<string>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] GetAllDevices failed.");

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult> GetDeviceById(int id,CancellationToken cancellationToken)
        {
            try
            {
                var device =
                    await _deviceRepository.GetByIdAsync(
                        id,
                        cancellationToken);

                if (device == null)
                {
                    return NotFound(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.NotFound,
                        Data = null,
                        Errors = new List<string>
                        {
                            $"Device {id} not found."
                        }
                    });
                }

                return Ok(new APIResponse
                {
                    Status = true,
                    StatusCode = HttpStatusCode.OK,
                    Data = device,
                    Errors = new List<string>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] GetDeviceById failed for Device {DeviceId}.",
                    id);

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        [HttpPost]
        public async Task<ActionResult> CreateDevice([FromBody] Device device,CancellationToken cancellationToken)
        {
            try
            {
                if (device == null)
                {
                    return BadRequest(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.BadRequest,
                        Data = null,
                        Errors = new List<string>
                        {
                            "Device data is required."
                        }
                    });
                }

                // Schedule is optional at repository level.
                // Frontend can enforce "Add schedule first" validation.

                var deviceId =
                    await _deviceRepository.AddAsync(
                        device,
                        cancellationToken);

                return CreatedAtAction(
                    nameof(GetDeviceById),
                    new { id = deviceId },
                    new APIResponse
                    {
                        Status = true,
                        StatusCode = HttpStatusCode.Created,
                        Data = new
                        {
                            id = deviceId,
                            message = "Device created successfully."
                        },
                        Errors = new List<string>()
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] CreateDevice failed.");

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult> UpdateDevice(int id,[FromBody] Device device,CancellationToken cancellationToken)
        {
            try
            {
                if (device == null)
                {
                    return BadRequest(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.BadRequest,
                        Data = null,
                        Errors = new List<string>
                        {
                            "Device data is required."
                        }
                    });
                }

                if (id != device.Id)
                {
                    return BadRequest(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.BadRequest,
                        Data = null,
                        Errors = new List<string>
                        {
                            "Device ID in URL and request body do not match."
                        }
                    });
                }

                var updated =
                    await _deviceRepository.UpdateAsync(
                        device,
                        cancellationToken);

                if (!updated)
                {
                    return NotFound(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.NotFound,
                        Data = null,
                        Errors = new List<string>
                        {
                            $"Device {id} not found."
                        }
                    });
                }

                return Ok(new APIResponse
                {
                    Status = true,
                    StatusCode = HttpStatusCode.OK,
                    Data = new
                    {
                        id,
                        message = "Device updated successfully."
                    },
                    Errors = new List<string>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] UpdateDevice failed for Device {DeviceId}.",
                    id);

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> DeleteDevice(int id,CancellationToken cancellationToken)
        {
            try
            {
                var deleted =
                    await _deviceRepository.DeleteAsync(
                        id,
                        cancellationToken);

                if (!deleted)
                {
                    return NotFound(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.NotFound,
                        Data = null,
                        Errors = new List<string>
                        {
                            $"Device {id} not found."
                        }
                    });
                }

                return Ok(new APIResponse
                {
                    Status = true,
                    StatusCode = HttpStatusCode.OK,
                    Data = new
                    {
                        id,
                        message = "Device deleted successfully."
                    },
                    Errors = new List<string>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] DeleteDevice failed for Device {DeviceId}.",
                    id);

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        [HttpGet("meterTypes")]
        public async Task<ActionResult> GetAllMeterTypes(CancellationToken cancellationToken)
        {
            try
            {
                var meterTypes =await _deviceRepository.GetMeterTypesAsync(cancellationToken);

                return Ok(new APIResponse
                {
                    Status = true,
                    StatusCode = HttpStatusCode.OK,
                    Data = meterTypes,
                    Errors = new List<string>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] GetAllMeterTypes failed.");

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        [HttpPost("{id:int}/sync")]
        public async Task<ActionResult> TriggerDeviceSync(int id,CancellationToken cancellationToken)
        {
            using var timeoutCts =new CancellationTokenSource(TimeSpan.FromMinutes(5));

            using var linkedCts =CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,timeoutCts.Token);

            var ct = linkedCts.Token;

            try
            {
                // ----------------------------------------------------
                // 1. Get device
                // ----------------------------------------------------

                var device =await _deviceRepository.GetByIdAsync(id,ct);

                if (device == null)
                {
                    return NotFound(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.NotFound,
                        Data = null,
                        Errors = new List<string>
                        {
                            $"Device {id} not found."
                        }
                    });
                }

                // ----------------------------------------------------
                // 2. Check device network connectivity
                // ----------------------------------------------------

                var reachable =await IsDeviceReachableAsync(device.IP,device.PORT,5000,ct);

                if (!reachable)
                {
                    return Ok(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.BadRequest,
                        Data = null,
                        Errors = new List<string>
                        {
                            $"Unable to connect to device at {device.IP}:{device.PORT}. " +
                            "Check network connectivity and Power."
                        }
                    });
                }

                // ----------------------------------------------------
                // 3. Start Sync
                // ----------------------------------------------------

                _logger.LogInformation(
                    "[DeviceController] Sync Now started for Device {DeviceId}.",
                    id);

                var result =
                    await _profileSyncService
                        .SyncDeviceAllProfilesAsync(id, ct);

                // ----------------------------------------------------
                // 4. Return result
                // ----------------------------------------------------

                if (!result.Success)
                {
                    return Ok(new APIResponse
                    {
                        Status = false,
                        StatusCode = HttpStatusCode.BadRequest,
                        Data = null,
                        Errors = new List<string>
                        {
                            result.ErrorMessage ?? "Sync failed."
                        }
                    });
                }

                _logger.LogInformation(
                    "[DeviceController] Sync Now completed for Device {DeviceId}.",
                    id);

                return Ok(new APIResponse
                {
                    Status = true,
                    StatusCode = HttpStatusCode.OK,
                    Data = new
                    {
                        deviceId = id,
                        status = "Completed",
                        completedAt = DateTime.UtcNow,
                        message =
                            $"Sync completed successfully for device {id}."
                    },
                    Errors = new List<string>()
                });
            }
            catch (OperationCanceledException)
                when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "[DeviceController] Sync timeout for Device {DeviceId}.",
                    id);

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.RequestTimeout,
                    Data = null,
                    Errors = new List<string>
                    {
                        "Sync did not complete within 5 minutes."
                    }
                });
            }
            catch (OperationCanceledException)
            {
                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        "Sync operation was cancelled."
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DeviceController] TriggerDeviceSync failed for Device {DeviceId}.",
                    id);

                return Ok(new APIResponse
                {
                    Status = false,
                    StatusCode = HttpStatusCode.BadRequest,
                    Data = null,
                    Errors = new List<string>
                    {
                        ex.Message
                    }
                });
            }
        }

        private static async Task<bool> IsDeviceReachableAsync(string ip,int port,int timeoutMs,CancellationToken cancellationToken)
        {
            try
            {
                using var client = new TcpClient();

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

                if (completedTask != connectTask)
                    return false;

                await connectTask;

                return client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}