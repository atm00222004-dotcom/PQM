using Microsoft.AspNetCore.SignalR;

namespace PQM.Server.Hubs
{
    public class DeviceHub : Hub
    {
        /// <summary>
        /// Called by PQM.Console (SignalR client) to broadcast status changes to all connected web clients.
        /// </summary>
        public async Task BroadcastDeviceStatus(int deviceId, string status, string? lastSync, string? lastError)
        {
            await Clients.All.SendAsync("DeviceStatusChanged", new
            {
                deviceId,
                status,
                lastSync,
                lastError
            });
        }
    }
}