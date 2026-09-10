using System.Collections.Concurrent;
using System.Text;
using Gurux.Common;
using Gurux.DLMS;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using Gurux.Net;
using PQM.Core.Entities;
// Alias required: Gurux.DLMS.Enums also defines a 'Task' type which collides
// with System.Threading.Tasks.Task in this namespace.
using SysTask = System.Threading.Tasks.Task;

namespace PQM.Infrastructure.Services
{
    /// <summary>
    /// Batch/incremental-sync DLMS meter reader.
    ///
    /// This class is the ported and adapted version of the validated prototype at
    /// D:\event_reading\meter_reading. It is intentionally a separate class from
    /// DLMSReader (the interactive/discover reader) for the following reasons:
    ///
    ///   1. Different output model: produces structured ProfileRow objects rather than
    ///      JSON strings, which is what the sync pipeline (Stage 4+) needs.
    ///
    ///   2. AllData = false fix: the critical bug in DLMSReader (AllData = true causes
    ///      Receive() to block for the full WaitTime even after a complete frame arrives)
    ///      is fixed here without touching DLMSReader.cs.
    ///
    ///   3. Incremental sync support: ReadProfileAllEntriesAsync accepts a startTime
    ///      watermark parameter that DLMSReader does not support.
    ///
    ///   4. _isAssociated guard: prevents NullReferenceException from ReleaseRequest()
    ///      when a connection never fully completed association.
    ///
    /// Registration: Transient in DI (stateful per-connection, same pattern as DLMSReader
    /// via DLMSSessionManager — instantiate, use, dispose within one sync operation).
    /// </summary>
    public class DlmsMeterReader : IDisposable, IAsyncDisposable
    {
        private readonly Device _device;
        private readonly GXDLMSClient _client;
        private readonly GXNet _media;
        private readonly bool _verboseLogging;
        private int _lastRequestBytesReceived;

        private bool _connected;

        // Guard flag set to true only after a successful ParseAAREResponse().
        // Used in Disconnect() to avoid calling ReleaseRequest() on a connection
        // that never completed association — which causes a NullReferenceException
        // inside the Gurux library.
        private bool _isAssociated;

        // =========================================================
        // PER-METER SESSION COOLDOWN
        //
        // Some DLMS meters (including this model) do not immediately release
        // their application-layer association after RLRQ. If a new AARQ arrives
        // before the meter's session cleanup completes, the meter responds with
        // "Service Unsupported" (GXDLMSConfirmedServiceError).
        //
        // The cooldown tracker records the last disconnect time for each meter
        // (keyed by IP:PORT). ConnectAsync checks this and waits out any remaining
        // cooldown before attempting Open(). This is transparent in production where
        // syncs are minutes apart, and handles back-to-back syncs (e.g., multiple
        // profiles per device in Stage 5) correctly.
        // =========================================================
        private static readonly ConcurrentDictionary<string, DateTime> _meterLastDisconnect
            = new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// Default conservative 8-second settling cooldown between sessions on the same meter.
        /// Configurable via appsettings.json (DlmsSettings:MeterCooldownSeconds).
        /// Value chosen conservatively to ensure DLMS meter firmware association layers reset.
        /// </summary>
        public static int DefaultMeterCooldownSeconds { get; set; } = 8;
        private readonly int _meterCooldownSeconds;

        public GXDLMSObjectCollection Objects => _client.Objects;

        // =========================================================
        // CONSTRUCTOR — takes Device entity directly
        // =========================================================

        /// <param name="device">
        ///   The Device entity from PQM.Core. Connection parameters are read directly
        ///   from it: IP, PORT, ClientAddress, ServerAddress, Authentication (via
        ///   AuthenticationTypeId shim), Password, Timeout.
        /// </param>
        /// <param name="verboseLogging">
        ///   When true, traces every sent/received frame byte to Console. Useful for
        ///   diagnosing protocol issues; disable in production.
        /// </param>
        public DlmsMeterReader(Device device, bool verboseLogging = false, int meterCooldownSeconds = 0)
        {
            _device = device;
            _verboseLogging = verboseLogging;
            _meterCooldownSeconds = meterCooldownSeconds > 0 ? meterCooldownSeconds : DefaultMeterCooldownSeconds;
            _connected = false;
            _isAssociated = false;

            // Resolve Authentication enum:
            // If AuthenticationTypeId is set (0=None, 1=Low, etc.), use it.
            // If AuthenticationTypeId is null, check if Password is provided:
            // meters with a Password (e.g. "lnt1") require Low authentication (1).
            Authentication authEnum;
            if (device.AuthenticationTypeId.HasValue && device.AuthenticationTypeId.Value > 0 && Enum.IsDefined(typeof(Authentication), device.AuthenticationTypeId.Value))
            {
                authEnum = (Authentication)device.AuthenticationTypeId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(device.Password))
            {
                authEnum = Authentication.Low;
            }
            else
            {
                authEnum = Authentication.None;
            }

            // ServerAddress: use Device.ServerAddress if set, fall back to 1.
            var serverAddress = (device.ServerAddress ?? 1);

            // ClientAddress: Default to 16 for Authentication.None, but auto-correct to 32
            // for Low/High authentication if left at unconfigured/default 16, preventing meter AARQ rejection.
            int clientAddress = device.ClientAddress ?? (authEnum != Authentication.None ? 32 : 16);
            if (authEnum != Authentication.None && clientAddress == 16)
            {
                Console.WriteLine($"[DLMS_SAFETY] Device {device.Id} ('{device.Name}') is configured with {authEnum} auth but ClientAddress=16 (Public). Auto-correcting to ClientAddress=32 for DLMS session.");
                clientAddress = 32;
            }

            _client = new GXDLMSClient(true)
            {
                UseLogicalNameReferencing = true,

                ClientAddress = clientAddress,

                ServerAddress = serverAddress,

                Authentication = authEnum,

                Password = Encoding.ASCII.GetBytes(device.Password ?? string.Empty),

                // WrapperType (TCP/IP wrapper) must be set explicitly.
                // This is the interface type used by this meter over TCP.
                InterfaceType = InterfaceType.WRAPPER,

                // Conformance flags required for selective access (range/entry reads)
                // and standard Get requests. Keep these exactly as in the prototype —
                // changing them can silently break selective access on some meters.
                MaxReceivePDUSize = 1024
            };

            _media = new GXNet(
                NetworkType.Tcp,
                device.IP,
                device.PORT);

            // WaitTimeMilliseconds from Device.Timeout (int? property, default 30000).
            _media.WaitTime = device.Timeout ?? 30000;

            if (_verboseLogging)
            {
                _media.Trace = System.Diagnostics.TraceLevel.Verbose;
                _media.OnTrace += (sender, e) =>
                {
                    Console.WriteLine($"[GURUX TRACE] {e}");
                };
            }
        }

        // =========================================================
        // CONNECT
        // =========================================================

        public async SysTask ConnectAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                if (_connected)
                {
                    return;
                }

                // ── PER-METER COOLDOWN ──────────────────────────────────────
                // Enforce minimum gap between sessions on the same meter to prevent
                // "Service Unsupported" AARE rejections during rapid re-connects.
                string meterKey = $"{_device.IP}:{_device.PORT}";
                if (_meterLastDisconnect.TryGetValue(meterKey, out var lastDisconnectUtc))
                {
                    var elapsed = (DateTime.UtcNow - lastDisconnectUtc).TotalSeconds;
                    int targetCooldown = _meterCooldownSeconds > 0 ? _meterCooldownSeconds : DefaultMeterCooldownSeconds;
                    if (elapsed < targetCooldown)
                    {
                        var waitMs = (int)((targetCooldown - elapsed) * 1000);
                        Console.WriteLine($"[COOLDOWN] Meter {meterKey} disconnected {elapsed:F1}s ago. Waiting {waitMs}ms before reconnecting (cooldown = {targetCooldown}s)...");
                        await System.Threading.Tasks.Task.Delay(waitMs, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Retry connection up to 2 times with a short 3-second delay on failure.
                int connectRetries = 2;
                while (connectRetries > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        Console.WriteLine($"[DIAGNOSTIC_CONNECT] Device={_device.Id} ('{_device.Name}'), IP={_device.IP}:{_device.PORT}, ClientAddr={_client.ClientAddress}, ServerAddr={_client.ServerAddress}, Auth={_client.Authentication} (TypeId={_device.AuthenticationTypeId}), PW_Len={_client.Password?.Length ?? 0}");

                        _media.Open();
                        var aarqRequests = _client.AARQRequest();

                        if (_verboseLogging)
                        {
                            Console.WriteLine($"[CLIENT STATE] Auth: {_client.Authentication}, PW Length: {_client.Password?.Length ?? 0}, PW Hex: {(_client.Password != null ? BitConverter.ToString(_client.Password) : "null")}");
                            Console.WriteLine($"[AARQ REQUEST] Count: {aarqRequests.Length}");
                            for (int i = 0; i < aarqRequests.Length; i++)
                                Console.WriteLine($"[AARQ REQUEST] [{i}] ({aarqRequests[i].Length} bytes): {BitConverter.ToString(aarqRequests[i])}");
                        }

                        foreach (var request in aarqRequests)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var reply = await SendAndReceiveAsync(request, cancellationToken);
                            if (reply.Error != 0)
                                throw new InvalidOperationException($"Meter rejected AARQ. Error Code: {reply.Error}");

                            var buffer = new GXByteBuffer();
                            buffer.Set(reply.Data);
                            _client.ParseAAREResponse(buffer);
                        }

                        break; // Connection & association succeeded!
                    }
                    catch (Exception ex) when (connectRetries > 1 && !cancellationToken.IsCancellationRequested)
                    {
                        connectRetries--;
                        if (_verboseLogging)
                            Console.WriteLine($"[CONNECT RETRY] Connection attempt failed ({ex.Message}), retrying in 3 seconds...");

                        try { _media.Close(); } catch { }
                        await System.Threading.Tasks.Task.Delay(3000, cancellationToken);
                    }
                }

                // Association is now confirmed. Only set _isAssociated = true here.
                _isAssociated = true;
                _connected = true;

                Console.WriteLine($"[DlmsMeterReader] Connected to device '{_device.Name}' ({_device.IP}:{_device.PORT}).");
            }
            catch (Exception ex)
            {
                _isAssociated = false;
                _connected = false;
                if (_verboseLogging)
                    Console.WriteLine($"[CONNECT ERROR] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        // =========================================================
        // ASSOCIATION VIEW
        // =========================================================

        public async System.Threading.Tasks.Task<IReadOnlyList<string>> ReadAssociationViewAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();

            var requests = _client.GetObjectsRequest();
            GXReplyData? finalReply = null;
            int totalBytesReceived = 0;

            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                finalReply = await SendAndReceiveAsync(request, cancellationToken);
                totalBytesReceived += _lastRequestBytesReceived;
            }

            if (finalReply == null)
                throw new InvalidOperationException("No association response received.");

            _client.ParseObjects(finalReply.Data, true);

            Console.WriteLine($"[ASSOCIATION READ] Total bytes received: {totalBytesReceived}");
            Console.WriteLine($"[ASSOCIATION READ] Total objects parsed: {_client.Objects.Count}");

            // Ensure all known profile objects exist in the client's object collection,
            // even if the meter's association view didn't report them explicitly.
            EnsureKnownProfileObjects();

            var result = new List<string>();
            foreach (var obj in _client.Objects)
            {
                if (obj.ObjectType == ObjectType.ProfileGeneric)
                    result.Add(obj.LogicalName);
            }

            return result;
        }

        /// <summary>
        /// Adds any ProfileCatalog entries that the meter did not return in its
        /// association view. Required because some meters omit certain profiles from
        /// the association view even though they are fully readable.
        /// </summary>
        private void EnsureKnownProfileObjects()
        {
            var addedProfiles = new List<string>();

            foreach (var kvp in ProfileCatalog.AllProfiles)
            {
                var obis = kvp.Key;
                var description = kvp.Value;

                var existing = _client.Objects.FirstOrDefault(o => o.LogicalName == obis);
                if (existing == null)
                {
                    var profile = new GXDLMSProfileGeneric(obis) { Description = description };
                    _client.Objects.Add(profile);
                    addedProfiles.Add($"{obis} ({description})");
                }
            }

            if (addedProfiles.Count > 0)
            {
                Console.WriteLine("[FALLBACK] Manually added missing profile generic objects:");
                foreach (var added in addedProfiles)
                    Console.WriteLine($"  - {added}");
            }
            else
            {
                Console.WriteLine("[FALLBACK] All known profile objects were present in the meter's association view.");
            }
        }

        // =========================================================
        // READ SINGLE OBJECT
        // =========================================================

        public async System.Threading.Tasks.Task<object?> ReadObjectAsync(
            GXDLMSObject obj,
            int attributeIndex = 2,
            System.Threading.CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();

            var requests = _client.Read(obj, attributeIndex);
            GXReplyData? reply = null;

            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reply = await SendAndReceiveAsync(request, cancellationToken);
            }

            if (reply == null)
                return null;

            return _client.UpdateValue(obj, attributeIndex, reply.Value);
        }

        public GXDLMSObject? FindObjectByObis(string obisCode)
        {
            return _client.Objects.FirstOrDefault(o => o.LogicalName == obisCode);
        }

        // =========================================================
        // GET PROFILE OBJECTS
        // =========================================================

        public List<GXDLMSProfileGeneric> GetProfileObjects()
        {
            return _client.Objects.OfType<GXDLMSProfileGeneric>().ToList();
        }

        // =========================================================
        // READ PROFILE INVENTORY (diagnostic-only, minimal reads)
        //
        // Used exclusively by the MeterInventoryCheck diagnostic tool.
        // Reads only:
        //   • Attribute 3 (CaptureObjects)  — needed to populate schema so
        //     ReadRowsByEntry can send the right selective-access descriptor.
        //   • Attribute 7 (EntriesInUse)    — the total count without pulling data.
        //   • ReadRowsByEntry(1, 1)          — the very first entry (earliest ts).
        //   • ReadRowsByEntry(N, 1)          — the very last entry (latest ts).
        //
        // This does NOT read the full buffer and writes NOTHING to the database.
        // =========================================================

        /// <summary>
        /// Lightweight profile inventory: returns entry count, earliest timestamp,
        /// and latest timestamp for a ProfileGeneric object identified by its OBIS code.
        /// Reads the absolute minimum from the meter (attr 3 + attr 7 + 2 entry reads).
        /// Throws if the profile is not found in the association view.
        /// </summary>
        public async System.Threading.Tasks.Task<ProfileInventoryResult> ReadProfileInventoryAsync(
            string obisCode,
            System.Threading.CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();

            var profile = _client.Objects
                .OfType<GXDLMSProfileGeneric>()
                .FirstOrDefault(o => o.LogicalName == obisCode)
                ?? throw new InvalidOperationException(
                       $"Profile object ({obisCode}) not found in meter objects. Call ReadAssociationViewAsync() first.");

            var result = new ProfileInventoryResult { ObisCode = obisCode };

            // ── Attribute 3: CaptureObjects (needed before entry reads) ──────
            await ReadObjectAsync(profile, 3, cancellationToken);

            // ── Attribute 7: EntriesInUse ────────────────────────────────────
            try
            {
                await ReadObjectAsync(profile, 7, cancellationToken);
                result.EntriesInUse = (int)profile.EntriesInUse;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.EntriesInUseError = ex.Message;
            }

            if (result.EntriesInUse is null or 0)
                return result; // Can't do boundary reads without a valid count.

            uint n = (uint)result.EntriesInUse.Value;

            // ── First entry (index 1, count 1) ───────────────────────────────
            try
            {
                var firstReqs = _client.ReadRowsByEntry(profile, 1, 1);
                GXReplyData? firstReply = null;
                foreach (var req in firstReqs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    firstReply = await SendAndReceiveAsync(req, cancellationToken);
                }
                if (firstReply?.Error == 0 && firstReply.Value != null)
                {
                    var parsed = _client.UpdateValue(profile, 2, firstReply.Value);
                    var rows = ConvertProfileRows(parsed ?? firstReply.Value);
                    result.Earliest = rows.FirstOrDefault(r => r.Timestamp.HasValue)?.Timestamp;
                }
                else if (firstReply != null && firstReply.Error != 0)
                {
                    result.EarliestError = $"DLMS Error {firstReply.Error}";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.EarliestError = ex.Message;
            }

            // ── Last entry (index N, count 1) ────────────────────────────────
            try
            {
                var lastReqs = _client.ReadRowsByEntry(profile, n, 1);
                GXReplyData? lastReply = null;
                foreach (var req in lastReqs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastReply = await SendAndReceiveAsync(req, cancellationToken);
                }
                if (lastReply?.Error == 0 && lastReply.Value != null)
                {
                    var parsed = _client.UpdateValue(profile, 2, lastReply.Value);
                    var rows = ConvertProfileRows(parsed ?? lastReply.Value);
                    result.Latest = rows.FirstOrDefault(r => r.Timestamp.HasValue)?.Timestamp;
                }
                else if (lastReply != null && lastReply.Error != 0)
                {
                    result.LatestError = $"DLMS Error {lastReply.Error}";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.LatestError = ex.Message;
            }

            // If entry-access selective access failed for earliest/latest, try range-access fallback (Attempt 2)
            if (!result.Earliest.HasValue || !result.Latest.HasValue)
            {
                try
                {
                    var start = new GXDateTime(new DateTime(2000, 1, 1));
                    var end = new GXDateTime(DateTime.Now);
                    start.Skip = DateTimeSkips.Deviation | DateTimeSkips.Status;
                    end.Skip = DateTimeSkips.Deviation | DateTimeSkips.Status;

                    var reqs = _client.ReadRowsByRange(profile, start, end);
                    GXReplyData? rangeReply = null;
                    foreach (var req in reqs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rangeReply = await SendAndReceiveAsync(req, cancellationToken);
                        if (rangeReply != null && rangeReply.Error != 0) break;
                    }

                    if (rangeReply?.Error == 0 && rangeReply.Value != null)
                    {
                        var parsed = _client.UpdateValue(profile, 2, rangeReply.Value);
                        var rows = ConvertProfileRows(parsed ?? rangeReply.Value);
                        if (!result.Earliest.HasValue)
                            result.Earliest = rows.FirstOrDefault(r => r.Timestamp.HasValue)?.Timestamp;
                        if (!result.Latest.HasValue)
                            result.Latest = rows.LastOrDefault(r => r.Timestamp.HasValue)?.Timestamp;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            // Ultimate Fallback: if selective access by entry and range both failed (e.g. meter returned DLMS Error 2),
            // call ReadProfileAllEntriesAsync which handles full buffer attribute 2 fallback.
            if (!result.Earliest.HasValue || !result.Latest.HasValue)
            {
                try
                {
                    var allRows = await ReadProfileAllEntriesAsync(obisCode, cancellationToken: cancellationToken);
                    if (!result.Earliest.HasValue)
                        result.Earliest = allRows.FirstOrDefault(r => r.Timestamp.HasValue)?.Timestamp;
                    if (!result.Latest.HasValue)
                        result.Latest = allRows.LastOrDefault(r => r.Timestamp.HasValue)?.Timestamp;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (!result.Earliest.HasValue) result.EarliestError = ex.Message;
                    if (!result.Latest.HasValue) result.LatestError = ex.Message;
                }
            }

            return result;
        }

        // =========================================================
        // GET PROFILE OBJECTS
        // =========================================================

        // =========================================================
        // READ CAPTURE OBJECTS (attribute 3)
        // =========================================================

        public async System.Threading.Tasks.Task<IReadOnlyList<ProfileColumnInfo>> ReadCaptureObjectsAsync(
            GXDLMSProfileGeneric profile,
            System.Threading.CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();

            // Read CaptureObjects (attribute 3) — populates profile.CaptureObjects
            await ReadObjectAsync(profile, 3, cancellationToken);

            // Read EntriesInUse (attribute 7) — needed for ReadRowsByEntry fallback
            try
            {
                await ReadObjectAsync(profile, 7, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Attribute 7 is optional on some meter firmware versions; ignore if absent.
            }

            var result = new List<ProfileColumnInfo>();
            var index = 0;

            foreach (var captureObject in profile.CaptureObjects)
            {
                var targetObj = captureObject.Key;
                int? scaler = null;
                int? unitCode = null;
                string? unitStr = null;

                if (targetObj is GXDLMSRegister reg)
                {
                    try
                    {
                        // Attribute 3 contains Scaler and Unit metadata
                        await ReadObjectAsync(reg, 3, cancellationToken);
                        scaler = Convert.ToInt32(reg.Scaler);
                        unitCode = (int)reg.Unit;
                        unitStr = reg.Unit != Unit.None ? reg.Unit.ToString() : null;
                    }
                    catch
                    {
                        // Fallback if attribute 3 is not supported by meter for this specific register
                    }
                }
                else if (targetObj is GXDLMSDemandRegister demandReg)
                {
                    try
                    {
                        await ReadObjectAsync(demandReg, 3, cancellationToken);
                        scaler = Convert.ToInt32(demandReg.Scaler);
                        unitCode = (int)demandReg.Unit;
                        unitStr = demandReg.Unit != Unit.None ? demandReg.Unit.ToString() : null;
                    }
                    catch
                    {
                        // Fallback if attribute 3 is not supported
                    }
                }

                result.Add(new ProfileColumnInfo
                {
                    Index = index++,
                    LogicalName = targetObj.LogicalName,
                    ObjectType = targetObj.GetType().Name,
                    AttributeIndex = captureObject.Value?.AttributeIndex ?? 2,
                    Scaler = scaler,
                    UnitCode = unitCode,
                    Unit = unitStr,
                    Description = string.Empty
                });
            }

            return result;
        }

        // =========================================================
        // READ PROFILE ALL ENTRIES
        // =========================================================

        public async System.Threading.Tasks.Task<IReadOnlyList<ProfileRow>> ReadProfileAllEntriesAsync(
            string obisCode,
            DateTime? startTime = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();

            var profile = _client.Objects
                .OfType<GXDLMSProfileGeneric>()
                .FirstOrDefault(o => o.LogicalName == obisCode);

            if (profile == null)
                throw new InvalidOperationException($"Profile object ({obisCode}) not found in meter objects. Call ReadAssociationViewAsync() first.");

            await ReadCaptureObjectsAsync(profile, cancellationToken);

            // --- Attempt 1: Selective access by date range ---
            try
            {
                var start = new GXDateTime(startTime ?? new DateTime(2000, 1, 1));
                var end = new GXDateTime(DateTime.Now);

                start.Skip = DateTimeSkips.Deviation | DateTimeSkips.Status;
                end.Skip = DateTimeSkips.Deviation | DateTimeSkips.Status;

                var requests = _client.ReadRowsByRange(profile, start, end);
                GXReplyData? reply = null;

                foreach (var request in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reply = await SendAndReceiveAsync(request, cancellationToken);
                    if (reply != null && reply.Error != 0)
                    {
                        Console.WriteLine($"[INFO] Range access request for {obisCode} returned DLMS error {reply.Error}. Breaking range read...");
                        break;
                    }
                }

                if (reply != null && reply.Error == 0)
                {
                    var rows = ConvertProfileRows(reply.Value);
                    Console.WriteLine($"[ReadProfileAllEntriesAsync] Range access succeeded for {obisCode}: {rows.Count} rows.");
                    return rows;
                }
                else if (reply != null && reply.Error != 0)
                {
                    Console.WriteLine($"[INFO] Range access for {obisCode} returned error {reply.Error}. Falling back to entry access...");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] Selective access by range failed for {obisCode}: {ex.Message}. Falling back to entry access...");
            }

            // --- Attempt 2: ReadRowsByEntry (full buffer by entry index) ---
            uint entryCount = profile.EntriesInUse > 0 ? profile.EntriesInUse : 100;
            Console.WriteLine($"[INFO] Reading {obisCode} by entry index (1 to {entryCount})...");

            try
            {
                var entryRequests = _client.ReadRowsByEntry(profile, 1, entryCount);
                GXReplyData? entryReply = null;

                foreach (var request in entryRequests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entryReply = await SendAndReceiveAsync(request, cancellationToken);
                }

                if (entryReply != null && entryReply.Error == 0)
                {
                    var rows = ConvertProfileRows(entryReply.Value);
                    if (rows.Count > 0)
                    {
                        Console.WriteLine($"[ReadProfileAllEntriesAsync] Entry access succeeded for {obisCode}: {rows.Count} rows.");
                        return rows;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] ReadRowsByEntry failed for {obisCode}: {ex.Message}. Falling back to raw attribute-2 buffer read...");
            }

            // --- Attempt 3: Raw attribute-2 buffer read (last resort) ---
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"[INFO] Fallback: reading raw attribute-2 buffer for {obisCode}...");
            var value = await ReadObjectAsync(profile, 2, cancellationToken);
            return ConvertProfileRows(value);
        }

        /// <summary>Groups a flat list of ProfileRows into a dictionary keyed by date (midnight UTC).
        /// Rows without a valid Timestamp are excluded.</summary>
        public IReadOnlyDictionary<DateTime, List<ProfileRow>> GroupRowsByDay(IReadOnlyList<ProfileRow> rows)
        {
            return rows
                .Where(r => r.Timestamp.HasValue)
                .GroupBy(r => r.Timestamp!.Value.Date)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // =========================================================
        // CONVERT PROFILE ROWS
        // =========================================================

        private static IReadOnlyList<ProfileRow> ConvertProfileRows(object? value)
        {
            var rows = new List<ProfileRow>();

            if (value is not System.Collections.IEnumerable enumerable)
                return rows;

            int rowIndex = 0;
            foreach (var item in enumerable)
            {
                if (item is not System.Collections.IEnumerable rowEnumerable)
                    continue;

                var values = rowEnumerable.Cast<object?>().ToList();

                var row = new ProfileRow
                {
                    Timestamp = ExtractTimestamp(values.ToArray()),
                    Values = values
                };

                var contentStr = string.Join(", ", values.Select(v => v?.ToString() ?? "null"));
                Console.WriteLine($"[DEBUG ROW {rowIndex++}] Timestamp: {row.Timestamp:yyyy-MM-dd HH:mm:ss} | Content: {contentStr}");

                rows.Add(row);
            }

            return rows;
        }

        // =========================================================
        // SEND AND RECEIVE
        // =========================================================

        private async System.Threading.Tasks.Task<GXReplyData> SendAndReceiveAsync(
            byte[] request,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastRequestBytesReceived = 0;

            var reply = new GXReplyData();
            var notify = new GXReplyData();

            await SendAndReceiveAsync(request, reply, notify, cancellationToken);

            while (reply.IsMoreData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nextRequest = _client.ReceiverReady(reply);
                await SendAndReceiveAsync(nextRequest, reply, notify, cancellationToken);
            }

            return reply;
        }

        private async SysTask SendAndReceiveAsync(
            byte[] request,
            GXReplyData reply,
            GXReplyData notify,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = new GXByteBuffer();

            // CRITICAL: AllData = false.
            // The meter's reply arrives as a complete frame in a single receive. With
            // AllData = true, GXNet.Receive() would wait for all bytes to arrive as
            // one contiguous read — but the frame is already complete after the first
            // Receive() call. AllData = true therefore blocks for the full WaitTime
            // even though a valid frame has already arrived, causing a false
            // TimeoutException every time. Do not change this back to true.
            var receiveParameters = new ReceiveParameters<byte[]>
            {
                WaitTime = _media.WaitTime,
                AllData = false
            };

            if (_client.InterfaceType == InterfaceType.HDLC)
            {
                receiveParameters.Eop = (byte)0x7E;
            }
            else if (_client.InterfaceType == InterfaceType.WRAPPER)
            {
                // For WRAPPER, seed with 8 bytes (the fixed header size) so Receive()
                // knows when a minimal frame header has arrived.
                receiveParameters.Count = 8;
            }

            bool received = false;
            int retries = 3;
            while (retries > 0)
            {
                lock (_media.Synchronous)
                {
                    _media.Send(request, null);

                    if (_verboseLogging)
                        Console.WriteLine($"[SEND] ({request.Length} bytes): {BitConverter.ToString(request)}");

                    received = _media.Receive(receiveParameters);
                }

                if (received) break;

                retries--;
                if (retries > 0)
                {
                    if (_verboseLogging)
                        Console.WriteLine("[RECV RETRY] No reply received. Retrying frame request...");
                    System.Threading.Thread.Sleep(500);
                }
            }

            if (receiveParameters.Reply != null)
                _lastRequestBytesReceived += receiveParameters.Reply.Length;

            if (_verboseLogging)
                Console.WriteLine($"[RECV] Received: {received}, Length: {receiveParameters.Reply?.Length ?? 0}, Bytes: {(receiveParameters.Reply != null ? BitConverter.ToString(receiveParameters.Reply) : "null")}");

            if (!received)
                throw new TimeoutException("Failed to receive reply from meter.");

            if (receiveParameters.Reply == null)
                throw new InvalidOperationException("Meter returned an empty response.");

            buffer.Set(receiveParameters.Reply);

            // WRAPPER-specific: the first Receive() only fetches the 8-byte header.
            // We parse the header to find the payload size, then issue a second
            // Receive() to collect exactly the remaining bytes.
            if (_client.InterfaceType == InterfaceType.WRAPPER)
            {
                int payloadSize = _client.GetFrameSize(buffer);
                int totalSize = 8 + payloadSize;

                if (_verboseLogging)
                    Console.WriteLine($"[FRAME SIZE INFO] Header size: {buffer.Size}, PayloadSize: {payloadSize}, TotalSize: {totalSize}");

                if (buffer.Size < totalSize)
                {
                    // CRITICAL: AllData = false here too (same reason as above).
                    // We know exactly how many bytes remain (Count = totalSize - buffer.Size),
                    // so AllData = true would be redundant AND cause the same blocking problem.
                    var remainingParams = new ReceiveParameters<byte[]>
                    {
                        WaitTime = _media.WaitTime,
                        Count = totalSize - buffer.Size,
                        AllData = false
                    };

                    lock (_media.Synchronous)
                    {
                        received = _media.Receive(remainingParams);
                    }

                    if (remainingParams.Reply != null)
                        _lastRequestBytesReceived += remainingParams.Reply.Length;

                    if (_verboseLogging)
                        Console.WriteLine($"[RECV REMAINING] Success: {received}, Length: {remainingParams.Reply?.Length ?? 0}");

                    if (received && remainingParams.Reply != null)
                        buffer.Set(remainingParams.Reply);
                }
            }

            if (_verboseLogging)
                Console.WriteLine($"[BEFORE GETDATA] Buffer Size: {buffer.Size}");

            // GetData loop: parses the buffer and accumulates blocks. If not complete,
            // sends a ReceiverReady and reads the next block until GetData returns true.
            while (true)
            {
                var complete = _client.GetData(buffer, reply, notify);

                if (_verboseLogging)
                    Console.WriteLine($"[GETDATA RESULT] Complete: {complete}, Reply Error: {reply.Error}");

                if (complete)
                    break;

                var receiverReady = _client.ReceiverReady(reply);

                var nextParams = new ReceiveParameters<byte[]>
                {
                    WaitTime = _media.WaitTime
                };

                if (_client.InterfaceType == InterfaceType.HDLC)
                    nextParams.Eop = (byte)0x7E;
                else if (_client.InterfaceType == InterfaceType.WRAPPER)
                    nextParams.Count = 8;

                lock (_media.Synchronous)
                {
                    _media.Send(receiverReady, null);
                    received = _media.Receive(nextParams);
                }

                if (nextParams.Reply != null)
                    _lastRequestBytesReceived += nextParams.Reply.Length;

                if (!received)
                    throw new TimeoutException("Failed to receive next DLMS block.");

                if (nextParams.Reply == null)
                    throw new InvalidOperationException("Meter returned an empty block.");

                buffer.Clear();
                buffer.Set(nextParams.Reply);

                if (_client.InterfaceType == InterfaceType.WRAPPER)
                {
                    int payloadSize = _client.GetFrameSize(buffer);
                    int totalSize = 8 + payloadSize;

                    if (buffer.Size < totalSize)
                    {
                        var nextRemainingParams = new ReceiveParameters<byte[]>
                        {
                            WaitTime = _media.WaitTime,
                            Count = totalSize - buffer.Size
                        };

                        lock (_media.Synchronous)
                        {
                            received = _media.Receive(nextRemainingParams);
                        }

                        if (nextRemainingParams.Reply != null)
                            _lastRequestBytesReceived += nextRemainingParams.Reply.Length;

                        if (received && nextRemainingParams.Reply != null)
                            buffer.Set(nextRemainingParams.Reply);
                    }
                }
            }
        }

        // =========================================================
        // EXTRACT TIMESTAMP
        //
        // Scans the row's values array for the first DateTime-like value.
        // Applies the Year <= 1 guard: meters occasionally return GXDateTime
        // with Year=0 or Year=1 for wildcarded/invalid clock entries.
        // Returning null instead of a garbage DateTime prevents corrupt
        // watermarks and display values in Stage 4.
        // =========================================================

        private static DateTime? ExtractTimestamp(object?[] values)
        {
            foreach (var value in values)
            {
                if (value == null)
                    continue;

                if (value is DateTime dateTime)
                {
                    if (dateTime.Year <= 1 || dateTime.Year >= 9999)
                        continue; // Invalid/wildcarded clock entry guard; check next element
                    return dateTime;
                }

                if (value is DateTimeOffset dateTimeOffset)
                {
                    if (dateTimeOffset.Year <= 1 || dateTimeOffset.Year >= 9999)
                        continue;
                    return dateTimeOffset.DateTime;
                }

                if (value is GXDateTime gxDateTime)
                {
                    var dt = gxDateTime.Value.DateTime;
                    if (dt.Year <= 1 || dt.Year >= 9999)
                        continue; // Invalid/wildcarded clock entry guard
                    return dt;
                }

                if (value is byte[] bytes)
                {
                    if (bytes.Length == 12)
                    {
                        try
                        {
                            int year = (bytes[0] << 8) | bytes[1];
                            int month = bytes[2];
                            int day = bytes[3];
                            int hour = bytes[5];
                            int minute = bytes[6];
                            int second = bytes[7];
                            if (year >= 2000 && year <= 2099 && month >= 1 && month <= 12 && day >= 1 && day <= 31 && hour < 24 && minute < 60 && second < 60)
                            {
                                return new DateTime(year, month, day, hour, minute, second);
                            }
                        }
                        catch { }
                    }
                    if (bytes.Length >= 5)
                    {
                        try
                        {
                            var gx = (GXDateTime)GXDLMSClient.ChangeType(bytes, DataType.DateTime);
                            if (gx != null && gx.Value.DateTime.Year > 1 && gx.Value.DateTime.Year < 9999)
                                return gx.Value.DateTime;
                        }
                        catch
                        {
                            try
                            {
                                var gxDate = (GXDateTime)GXDLMSClient.ChangeType(bytes, DataType.Date);
                                if (gxDate != null && gxDate.Value.DateTime.Year > 1 && gxDate.Value.DateTime.Year < 9999)
                                    return gxDate.Value.DateTime;
                            }
                            catch { }
                        }
                    }
                }

                if (value is not string && value is System.Collections.IEnumerable enumerable)
                {
                    var subValues = enumerable.Cast<object?>().ToArray();
                    var innerTs = ExtractTimestamp(subValues);
                    if (innerTs.HasValue)
                        return innerTs;
                }
            }

            return null;
        }

        // =========================================================
        // CONNECTION CHECK
        // =========================================================

        private void EnsureConnected()
        {
            if (!_connected)
                throw new InvalidOperationException("Meter is not connected. Call ConnectAsync() first.");
        }

        // =========================================================
        // DISCONNECT
        // =========================================================

        /// <summary>
        /// Async disconnect: sends RLRQ and waits for the RLRE response before closing
        /// the TCP socket. This ensures the meter has fully released the session before
        /// a new connection can be established — critical for back-to-back sync runs.
        /// Called by DisposeAsync (used by ProfileSyncService's 'await using' pattern).
        /// </summary>
        public async System.Threading.Tasks.Task DisconnectAsync()
        {
            if (_client == null)
            {
                Console.WriteLine("[DISCONNECT TRACE] _client is null, nothing to disconnect. Skipping.");
                _isAssociated = false;
                _connected = false;
                return;
            }

            if (!_connected)
            {
                _isAssociated = false;
                return;
            }

            try
            {
                // _isAssociated guard: only send ReleaseRequest if association
                // was successfully established.
                if (_isAssociated)
                {
                    byte[][]? releaseReqs = null;
                    byte[]? discReq = null;

                    if (_client != null && _client.InterfaceType != InterfaceType.WRAPPER)
                    {
                        try { releaseReqs = _client.ReleaseRequest(); } catch (Exception ex) { Console.WriteLine($"[DISCONNECT TRACE] ReleaseRequest ex: {ex.Message}"); }
                        try { discReq = _client.DisconnectRequest(); } catch (Exception ex) { Console.WriteLine($"[DISCONNECT TRACE] DisconnectRequest ex: {ex.Message}"); }
                    }

                    Console.WriteLine($"[DISCONNECT TRACE] ReleaseRequest count: {releaseReqs?.Length ?? 0}, DisconnectRequest frame: {(discReq != null ? BitConverter.ToString(discReq) : "null")}");

                    var requests = (releaseReqs != null && releaseReqs.Length > 0) 
                        ? releaseReqs 
                        : (discReq != null ? new[] { discReq } : Array.Empty<byte[]>());

                    try
                    {
                        if (requests.Length == 0 && _client != null && _client.InterfaceType == InterfaceType.WRAPPER)
                        {
                            // Gurux ReleaseRequest() returns empty for LN WRAPPER + Low Auth.
                            // Manually construct standard DLMS RLRQ PDU for WRAPPER mode:
                            // Header: Ver(00 01), ClientAddress, ServerAddress, Length(00 05)
                            // Payload: 62 03 80 01 00 (RLRQ APDU, reason: normal)
                            byte clientHi = (byte)((_client.ClientAddress >> 8) & 0xFF);
                            byte clientLo = (byte)(_client.ClientAddress & 0xFF);
                            byte serverHi = (byte)((_client.ServerAddress >> 8) & 0xFF);
                            byte serverLo = (byte)(_client.ServerAddress & 0xFF);

                            byte[] customRlrq = new byte[]
                            {
                                0x00, 0x01,
                                clientHi, clientLo,
                                serverHi, serverLo,
                                0x00, 0x05,
                                0x62, 0x03, 0x80, 0x01, 0x00
                            };

                            requests = new[] { customRlrq };
                            Console.WriteLine($"[DISCONNECT TRACE] Constructed custom WRAPPER RLRQ frame: {BitConverter.ToString(customRlrq)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DISCONNECT TRACE] Manual WRAPPER RLRQ construction/send failed: {ex.Message}");
                    }

                    if (requests != null && requests.Length > 0)
                    {
                        foreach (var request in requests)
                        {
                            try
                            {
                                var reply = await SendAndReceiveAsync(request);
                                Console.WriteLine($"[DISCONNECT TRACE] Disconnect response received! Size: {reply?.Data?.Size ?? 0}, Bytes: {(reply?.Data?.Data != null ? BitConverter.ToString(reply.Data.Data) : "null")}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[DISCONNECT TRACE] Disconnect response error (ignored): {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DISCONNECT TRACE] Disconnect error (ignored): {ex.Message}");
            }
            finally
            {
                _isAssociated = false;
                try { _media?.Close(); } catch { }
                _connected = false;

                if (_device != null)
                {
                    try
                    {
                        string key = $"{_device.IP}:{_device.PORT}";
                        _meterLastDisconnect[key] = DateTime.UtcNow;
                        Console.WriteLine($"[COOLDOWN] Recorded disconnect for {key} at {_meterLastDisconnect[key]:HH:mm:ss} UTC.");
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Synchronous disconnect: fires RLRQ best-effort without waiting for RLRE.
        /// Used by the synchronous Dispose() path only. Prefer 'await using' (DisposeAsync)
        /// so that DisconnectAsync runs and the meter's session is properly released.
        /// </summary>
        public void Disconnect()
        {
            if (_client == null)
            {
                _isAssociated = false;
                _connected = false;
                return;
            }

            if (!_connected)
            {
                _isAssociated = false;
                return;
            }

            try
            {
                if (_isAssociated)
                {
                    byte[][]? requests = null;
                    try { requests = _client.ReleaseRequest(); } catch { }
                    Console.WriteLine($"[RLRQ-SYNC] ReleaseRequest() produced {requests?.Length ?? 0} packet(s) (sync path, best-effort).");
                    if (requests != null)
                    {
                        foreach (var request in requests)
                        {
                            try { _media?.Send(request, null); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // Ignore disconnect errors.
            }
            finally
            {
                _isAssociated = false;
                try { _media?.Close(); } catch { }
                _connected = false;

                if (_device != null)
                {
                    try
                    {
                        string key = $"{_device.IP}:{_device.PORT}";
                        _meterLastDisconnect[key] = DateTime.UtcNow;
                        Console.WriteLine($"[COOLDOWN-SYNC] Recorded disconnect for {key} at {_meterLastDisconnect[key]:HH:mm:ss} UTC.");
                    }
                    catch { }
                }
            }
        }

        // =========================================================
        // DISPOSE / ASYNC DISPOSE
        // =========================================================

        public void Dispose()
        {
            try { Disconnect(); } catch { }
            try { _media?.Dispose(); } catch { }
        }

        /// <summary>
        /// Async dispose: awaits DisconnectAsync so the RLRE is received before closing.
        /// Always prefer 'await using var reader = new DlmsMeterReader(device)' over
        /// 'using var reader = ...' to ensure proper session teardown.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try { await DisconnectAsync(); } catch { }
            try { _media?.Dispose(); } catch { }
        }
    }
}
