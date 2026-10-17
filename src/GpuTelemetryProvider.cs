using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal sealed class GpuTelemetryReading
{
    public readonly int UtilizationPercent;
    public readonly double BoardPowerWatts;
    public readonly int TemperatureCelsius;
    public readonly bool HasUtilization;
    public readonly bool HasBoardPower;
    public readonly bool HasTemperature;
    public readonly string Diagnostic;

    public GpuTelemetryReading(int utilization, double power, int temperature,
        bool hasUtilization, bool hasPower, bool hasTemperature, string diagnostic = null)
    {
        UtilizationPercent = utilization;
        BoardPowerWatts = power;
        TemperatureCelsius = temperature;
        HasUtilization = hasUtilization;
        HasBoardPower = hasPower;
        HasTemperature = hasTemperature;
        Diagnostic = diagnostic;
    }
}

internal static class GpuTelemetryProvider
{
    private static bool nvmlInitialized;
    private static IntPtr amdContext;
    private static readonly AdlMemoryAlloc amdAllocator = delegate(int size) { return Marshal.AllocHGlobal(size); };

    internal static void Reset()
    {
        if (nvmlInitialized)
        {
            nvmlInitialized = false;
            try { NvmlShutdown(); } catch { }
        }
        if (amdContext != IntPtr.Zero)
        {
            IntPtr old = amdContext;
            amdContext = IntPtr.Zero;
            try { AdlMainDestroy(old); } catch { }
        }
    }
    public static bool TryRead(string gpuName, out GpuTelemetryReading reading, out string error)
    {
        return TryReadCore(gpuName, null, true, out reading, out error);
    }

    internal static bool TryRead(VramAdapterInfo adapter, bool uniqueName,
        out GpuTelemetryReading reading, out string error)
    {
        return TryReadCore(adapter.Name, adapter, uniqueName, out reading, out error);
    }

    private static bool TryReadCore(string gpuName, VramAdapterInfo adapter, bool uniqueName,
        out GpuTelemetryReading reading, out string error)
    {
        reading = null;
        error = null;
#if TEST_TELEMETRY_SUCCESS
        reading = new GpuTelemetryReading(42, 186.4, 67, true, true, true);
        return true;
#endif
        try
        {
            if (gpuName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                gpuName.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string identityDiagnostic = "No Windows GPU identity supplied.";
                GpuPciAddress address = adapter == null ? null :
                    GpuPciProbe.Read(adapter.LuidLow, adapter.LuidHigh, out identityDiagnostic);
                return TryReadNvidia(gpuName, address, uniqueName, identityDiagnostic, out reading, out error);
            }

            if (gpuName.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                gpuName.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!uniqueName) { error = "AMD GPU name is ambiguous."; return false; }
                return TryReadAmd(gpuName, out reading, out error);
            }

            error = "Unsupported GPU vendor.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadNvidia(string wantedName, GpuPciAddress address, bool uniqueName,
        string identityDiagnostic, out GpuTelemetryReading reading, out string error)
    {
        reading = null;
        error = null;
        // Keep the helper process, but restore the pre-optimization NVML lifecycle.
        // Partial sensor failures must not leave a stale session alive indefinitely.
        int result = NvmlInit();
        if (result != 0)
        {
            error = "NVML init failed: " + result;
            return false;
        }
        nvmlInitialized = true;
        try
        {
            uint count;
            result = NvmlDeviceGetCount(out count);
            if (result != 0 || count == 0)
            {
                error = "NVML found no GPU: " + result;
                return false;
            }

            List<NvidiaCandidate> candidates = new List<NvidiaCandidate>();
            StringBuilder devices = new StringBuilder();
            for (uint index = 0; index < count; index++)
            {
                IntPtr device;
                int handleCode = NvmlDeviceGetHandleByIndex(index, out device);
                if (handleCode != 0)
                {
                    devices.AppendFormat("NVML[{0}] handle error={1}; ", index, handleCode);
                    continue;
                }
                StringBuilder name = new StringBuilder(128);
                int nameCode = NvmlDeviceGetName(device, name, (uint)name.Capacity);
                GpuPciAddress pci = null;
                string pciText = "unavailable";
                try
                {
                    NvmlPciInfo info;
                    int pciCode = NvmlDeviceGetPciInfo(device, out info);
                    if (pciCode == 0)
                    {
                        pciText = string.IsNullOrEmpty(info.BusId) ? info.BusIdLegacy : info.BusId;
                        pci = GpuPciAddress.ParseNvml(pciText);
                        if (pci != null && (pci.Bus != info.Bus || pci.Device != info.Device)) pci = null;
                    }
                    else pciText = "error=" + pciCode;
                }
                catch (EntryPointNotFoundException) { pciText = "API missing"; }
                candidates.Add(new NvidiaCandidate(device, nameCode == 0 ? name.ToString() : "", pci));
                devices.AppendFormat("NVML[{0}] name={1}, nameCode={2}, PCI={3}; ", index, name, nameCode, pciText);
            }
            string matchReason;
            int chosen = SelectNvidiaCandidate(wantedName, address, uniqueName, candidates,
                candidates.Count == count, out matchReason);
            string selectionDiagnostic = identityDiagnostic + "; " + matchReason + "; " + devices;
            if (chosen < 0)
            {
                error = "NVML GPU match failed: " + selectionDiagnostic;
                return false;
            }
            IntPtr selected = candidates[chosen].Handle;

            reading = ReadNvidiaSensors(
                delegate(out uint value)
                {
                    NvmlUtilization utilization;
                    int code = NvmlDeviceGetUtilizationRates(selected, out utilization);
                    value = utilization.Gpu;
                    return code;
                },
                delegate(out uint value) { return NvmlDeviceGetPowerUsage(selected, out value); },
                delegate(out uint value) { return NvmlDeviceGetTemperature(selected, 0, out value); },
                delegate(out uint value) { return ReadNvidiaPowerField(selected, out value); },
                delegate(out uint value) { return ReadNvidiaTemperatureV(selected, out value); });
            reading = new GpuTelemetryReading(reading.UtilizationPercent, reading.BoardPowerWatts,
                reading.TemperatureCelsius, reading.HasUtilization, reading.HasBoardPower,
                reading.HasTemperature, selectionDiagnostic + "\r\n" + reading.Diagnostic);
            error = reading.Diagnostic;
            return reading.HasUtilization || reading.HasBoardPower || reading.HasTemperature;
        }
        finally
        {
            nvmlInitialized = false;
            try { NvmlShutdown(); } catch { }
        }
    }

    internal sealed class NvidiaCandidate
    {
        internal readonly IntPtr Handle;
        internal readonly string Name;
        internal readonly GpuPciAddress Address;
        internal NvidiaCandidate(IntPtr handle, string name, GpuPciAddress address)
        {
            Handle = handle;
            Name = name;
            Address = address;
        }
    }

    internal static int SelectNvidiaCandidate(string name, GpuPciAddress target, bool uniqueDxgiName,
        IList<NvidiaCandidate> candidates, bool completeEnumeration, out string reason)
    {
        reason = "Incomplete NVML enumeration; cannot verify GPU identity.";
        if (!completeEnumeration) return -1;
        if (target != null)
        {
            int pciMatch = -1, matches = 0, known = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Address != null) known++;
                if (target.Matches(candidates[i].Address)) { pciMatch = i; matches++; }
            }
            if (matches == 1 && known == candidates.Count)
            {
                reason = "Matched Windows LUID to NVML by PCI address " + target;
                return pciMatch;
            }
            if (known > 0)
            {
                reason = "PCI identity missing/ambiguous (matches=" + matches + ", known=" + known + ").";
                return -1; // Never ignore a known mismatch and silently pick another board.
            }
        }
        // NVML enumerates physical NVIDIA devices, unlike duplicate logical DXGI entries.
        // When it exposes exactly one device, a matching model name is unambiguous.
        int nameMatch = -1, nameMatches = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (NormalizeGpuName(candidates[i].Name) == NormalizeGpuName(name))
            { nameMatch = i; nameMatches++; }
        }
        if (nameMatches == 1 && (uniqueDxgiName || candidates.Count == 1))
        {
            reason = candidates.Count == 1 ? "Single physical NVML GPU with matching name" : "Unique GPU name fallback";
            return nameMatch;
        }
        reason = "Cannot safely identify GPU by PCI or name (name matches=" + nameMatches + ").";
        return -1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct NvmlPciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string BusIdLegacy;
        public uint Domain, Bus, Device, DeviceId, SubsystemId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId;
    }

    internal delegate int UIntSensor(out uint value);

    // Sensor calls are independent: an unavailable temperature API must not erase power/utilization.
    internal static GpuTelemetryReading ReadNvidiaSensors(UIntSensor utilizationSensor,
        UIntSensor powerSensor, UIntSensor temperatureSensor, UIntSensor powerFallback,
        UIntSensor temperatureFallback)
    {
        uint utilization, power, temperature;
        int utilCode = ReadSensor(utilizationSensor, out utilization);
        int powerCode = ReadSensor(powerSensor, out power);
        int temperatureCode = ReadSensor(temperatureSensor, out temperature);
        int powerFallbackCode = -2, temperatureFallbackCode = -2;
        bool hasUtil = utilCode == 0 && utilization <= 100;
        bool hasPower = powerCode == 0 && power != uint.MaxValue;
        bool hasTemperature = temperatureCode == 0 && temperature <= 150;
        if (!hasPower)
        {
            powerFallbackCode = ReadSensor(powerFallback, out power);
            hasPower = powerFallbackCode == 0 && power != uint.MaxValue;
        }
        if (!hasTemperature)
        {
            temperatureFallbackCode = ReadSensor(temperatureFallback, out temperature);
            hasTemperature = temperatureFallbackCode == 0 && temperature <= 150;
        }
        string diagnostic = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "NVML codes: utilization={0}; power={1}; temperature={2}; power-field={3}; temperature-v1={4}. " +
            "Available: utilization={5}; power={6}; temperature={7}. " +
            "Codes: 0=success, 1=uninitialized, 2=invalid argument, 3=not supported, 4=no permission, " +
            "15=GPU lost, -1=API missing, -2=not needed, -3=invalid value. NVML session refreshed each read.",
            utilCode, powerCode, temperatureCode, powerFallbackCode, temperatureFallbackCode,
            hasUtil, hasPower, hasTemperature);
        return new GpuTelemetryReading(hasUtil ? (int)utilization : 0,
            hasPower ? power / 1000d : 0, hasTemperature ? (int)temperature : 0,
            hasUtil, hasPower, hasTemperature, diagnostic);
    }

    private static int ReadSensor(UIntSensor sensor, out uint value)
    {
        value = 0;
        try { return sensor(out value); }
        catch (EntryPointNotFoundException) { return -1; }
    }

    private static int ReadNvidiaTemperatureV(IntPtr device, out uint value)
    {
        NvmlTemperature temperature = new NvmlTemperature();
        temperature.Version = (uint)Marshal.SizeOf(typeof(NvmlTemperature)) | (1U << 24);
        temperature.SensorType = 0;
        int result = NvmlDeviceGetTemperatureV(device, ref temperature);
        value = temperature.Temperature >= 0 ? (uint)temperature.Temperature : uint.MaxValue;
        return result;
    }

    private static int ReadNvidiaPowerField(IntPtr device, out uint value)
    {
        value = 0;
        // Only actual power readings: never substitute a power limit for consumption.
        NvmlFieldValue[] fields = new NvmlFieldValue[2];
        fields[0].FieldId = 185; // NVML_FI_DEV_POWER_AVERAGE, milliwatts, GPU scope 0
        fields[1].FieldId = 186; // NVML_FI_DEV_POWER_INSTANT
        int result = NvmlDeviceGetFieldValues(device, fields.Length, fields);
        if (result != 0) return result;
        foreach (NvmlFieldValue field in fields)
            if (TryGetPowerField(field, out value)) return 0;
        return fields[1].ReturnCode == 0 ? -3 : fields[1].ReturnCode;
    }

    internal static bool TryGetPowerField(NvmlFieldValue field, out uint value)
    {
        value = 0;
        if (field.ReturnCode != 0 || field.ScopeId != 0 ||
            (field.FieldId != 185 && field.FieldId != 186)) return false;
        double number;
        switch (field.ValueType)
        {
            case 0: number = field.Value.Double; break;
            case 1: case 2: number = field.Value.UInt32; break; // Windows unsigned long is 32-bit
            case 3: number = field.Value.UInt64; break;
            case 4: number = field.Value.Int64; break;
            case 5: number = field.Value.Int32; break;
            default: return false;
        }
        if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number >= uint.MaxValue)
            return false;
        value = (uint)Math.Round(number);
        return true;
    }

    // Layouts follow NVIDIA's nvml.h (Windows x64 ABI).
    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlTemperature
    {
        public uint Version, SensorType;
        public int Temperature;
    }
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    internal struct NvmlValue
    {
        [FieldOffset(0)] public double Double;
        [FieldOffset(0)] public uint UInt32;
        [FieldOffset(0)] public ulong UInt64;
        [FieldOffset(0)] public long Int64;
        [FieldOffset(0)] public int Int32;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlFieldValue
    {
        public uint FieldId, ScopeId;
        public long Timestamp, Latency;
        public int ValueType, ReturnCode;
        public NvmlValue Value;
    }

    private static bool TryReadAmd(string wantedName, out GpuTelemetryReading reading, out string error)
    {
        reading = null;
        error = null;
        int result = amdContext != IntPtr.Zero ? 0 : AdlMainCreate(amdAllocator, 1, out amdContext);
        IntPtr context = amdContext;
        if (result != 0)
        {
            error = "ADL init failed: " + result;
            return false;
        }
        try
        {
            int count;
            result = AdlAdapterCount(context, out count);
            if (result != 0 || count <= 0)
            {
                error = "ADL found no GPU: " + result;
                return false;
            }

            int[] targetIndices = GetAmdTargetIndices(context, count, wantedName);
            int bestUtil = -1;
            int bestPowerRaw = -1;
            int bestTemperature = -1;
            foreach (int index in targetIndices)
            {
                AdlOd6CurrentStatus status;
                AdlOdnPerformanceStatus odnStatus;
                int powerRaw;
                int pmUtil;
                int pmPower;
                int pmTemperature;
                bool pmHasUtil;
                bool pmHasPower;
                bool pmHasTemperature;
                TryReadAmdPmLogSmoothed(context, index, out pmUtil, out pmPower,
                    out pmTemperature, out pmHasUtil, out pmHasPower, out pmHasTemperature);
                bool hasUtil = pmHasUtil;
                int candidateUtil = pmHasUtil ? pmUtil : -1;
                if (!hasUtil)
                {
                    try
                    {
                        hasUtil = AdlCurrentStatus(context, index, out status) == 0 &&
                            status.ActivityPercent >= 0 && status.ActivityPercent <= 100;
                        if (hasUtil) candidateUtil = status.ActivityPercent;
                    }
                    catch (EntryPointNotFoundException) { }
                }
                if (!hasUtil)
                {
                    try
                    {
                        hasUtil = AdlOdnPerformanceStatusGet(context, index, out odnStatus) == 0 &&
                            odnStatus.GpuActivityPercent >= 0 && odnStatus.GpuActivityPercent <= 100;
                        if (hasUtil) candidateUtil = odnStatus.GpuActivityPercent;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        hasUtil = false;
                    }
                }

                if (pmHasUtil)
                {
                    hasUtil = true;
                    candidateUtil = pmUtil;
                }

                // ASIC power (Overdrive6 type 0) is not total board power.
                bool hasPower = pmHasPower;
                powerRaw = pmHasPower ? pmPower * 256 : -1;
                if (candidateUtil > bestUtil ||
                    (candidateUtil == bestUtil && hasPower && powerRaw > bestPowerRaw) ||
                    (candidateUtil == bestUtil && bestTemperature < 0 && pmHasTemperature))
                {
                    bestUtil = candidateUtil;
                    bestPowerRaw = hasPower ? powerRaw : -1;
                    bestTemperature = pmHasTemperature ? pmTemperature : -1;
                }
            }

            bool utilizationAvailable = bestUtil >= 0;
            bool powerAvailable = bestPowerRaw >= 0;
            bool temperatureAvailable = bestTemperature >= 0;
            reading = new GpuTelemetryReading(
                utilizationAvailable ? bestUtil : 0,
                powerAvailable ? bestPowerRaw / 256d : 0,
                temperatureAvailable ? bestTemperature : 0,
                utilizationAvailable, powerAvailable, temperatureAvailable);
            if (!utilizationAvailable && !powerAvailable && !temperatureAvailable)
            {
                error = "ADL utilization, power, and temperature are unsupported.";
                return false;
            }
            return true;
        }
        finally
        {
            // The context and rooted allocator are reused by the isolated worker.
            GC.KeepAlive(amdAllocator);
        }
    }

    private static int[] GetAmdTargetIndices(IntPtr context, int count, string wantedName)
    {
        // ADL can list multiple display entries for one board. Deduplicate by PCI address;
        // never choose a different board merely because it currently has more activity.
        Dictionary<string, int> physicalMatches = new Dictionary<string, int>();
        int size = Marshal.SizeOf(typeof(AdlAdapterInfo));
        IntPtr buffer = Marshal.AllocHGlobal(checked(size * count));
        try
        {
            for (int offset = 0; offset < count; offset++)
            {
                AdlAdapterInfo empty = new AdlAdapterInfo();
                empty.Size = size;
                Marshal.StructureToPtr(empty, IntPtr.Add(buffer, offset * size), false);
            }
            if (AdlAdapterInfoGet(context, buffer, size * count) != 0) return new int[0];
            string wanted = NormalizeGpuName(wantedName);
            for (int offset = 0; offset < count; offset++)
            {
                AdlAdapterInfo info = (AdlAdapterInfo)Marshal.PtrToStructure(
                    IntPtr.Add(buffer, offset * size), typeof(AdlAdapterInfo));
                if (info.Exist == 0 || NormalizeGpuName(info.AdapterName) != wanted) continue;
                string pci = info.BusNumber + ":" + info.DeviceNumber + ":" + info.FunctionNumber;
                int active = 0;
                try { AdlAdapterActiveGet(context, info.AdapterIndex, out active); }
                catch (EntryPointNotFoundException) { }
                if (!physicalMatches.ContainsKey(pci) || active != 0)
                    physicalMatches[pci] = info.AdapterIndex;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        if (physicalMatches.Count != 1) return new int[0];
        foreach (int index in physicalMatches.Values) return new int[] { index };
        return new int[0];
    }

    internal static string NormalizeGpuName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        StringBuilder normalized = new StringBuilder(name.Length);
        foreach (char character in name.ToUpperInvariant())
            if (char.IsLetterOrDigit(character)) normalized.Append(character);
        return normalized.ToString().Replace("AMD", "").Replace("GRAPHICS", "");
    }

    private static void TryReadAmdPmLogSmoothed(IntPtr context, int adapterIndex,
        out int utilization, out int boardPower, out int temperature,
        out bool hasUtilization, out bool hasBoardPower, out bool hasTemperature)
    {
        if (TryReadAmdPmLogShared(context, adapterIndex, out utilization, out boardPower,
            out temperature, out hasUtilization, out hasBoardPower, out hasTemperature)) return;

        List<int> utilizations = new List<int>();
        List<int> powers = new List<int>();
        List<int> temperatures = new List<int>();
        for (int sample = 0; sample < 5; sample++)
        {
            int sampleUtilization;
            int samplePower;
            int sampleTemperature;
            bool sampleHasUtilization;
            bool sampleHasPower;
            bool sampleHasTemperature;
            TryReadAmdPmLogOnce(context, adapterIndex, out sampleUtilization, out samplePower,
                out sampleTemperature, out sampleHasUtilization, out sampleHasPower,
                out sampleHasTemperature);
            if (sampleHasUtilization) utilizations.Add(sampleUtilization);
            if (sampleHasPower) powers.Add(samplePower);
            if (sampleHasTemperature) temperatures.Add(sampleTemperature);
            if (sample < 4) Thread.Sleep(80);
        }
        FinishSamples(utilizations, powers, temperatures, out utilization, out boardPower,
            out temperature, out hasUtilization, out hasBoardPower, out hasTemperature);
    }

    private static bool TryReadAmdPmLogShared(IntPtr context, int adapterIndex,
        out int utilization, out int boardPower, out int temperature,
        out bool hasUtilization, out bool hasBoardPower, out bool hasTemperature)
    {
        utilization = 0;
        boardPower = 0;
        temperature = 0;
        hasUtilization = false;
        hasBoardPower = false;
        hasTemperature = false;
        uint device = 0;
        IntPtr sharedMemory = IntPtr.Zero;
        bool started = false;
        try
        {
            int supported;
            if (AdlPmLogSharedSupport(context, adapterIndex, out supported, 0) != 0 || supported == 0)
                return false;
            int[] sensors = new int[] { 19, 73, 8 };
            if (AdlPmLogSharedStart(context, adapterIndex, 80, sensors.Length, sensors,
                out device, out sharedMemory, 0) != 0) return false;
            started = true;
            Thread.Sleep(100);

            List<int> utilizations = new List<int>();
            List<int> powers = new List<int>();
            List<int> temperatures = new List<int>();
            for (int sample = 0; sample < 5; sample++)
            {
                AdlPmLogDataOutput output = CreatePmLogOutput();
                if (AdlPmLogSharedRead(context, adapterIndex, sensors.Length, sensors,
                    ref sharedMemory, ref output) == 0 && output.Sensors != null)
                {
                    AdlSingleSensorData activity = GetPmLogSensor(output, 19, 0);
                    AdlSingleSensorData power = GetPmLogSensor(output, 73, 1);
                    AdlSingleSensorData gpuTemperature = GetPmLogSensor(output, 8, 2);
                    if (activity.Supported != 0 && activity.Value >= 0 && activity.Value <= 100)
                        utilizations.Add(activity.Value);
                    if (power.Supported != 0 && power.Value > 0) powers.Add(power.Value);
                    if (gpuTemperature.Supported != 0 && gpuTemperature.Value >= 0 &&
                        gpuTemperature.Value <= 150) temperatures.Add(gpuTemperature.Value);
                }
                if (sample < 4) Thread.Sleep(80);
            }
            FinishSamples(utilizations, powers, temperatures, out utilization, out boardPower,
                out temperature, out hasUtilization, out hasBoardPower, out hasTemperature);
            return hasUtilization || hasBoardPower || hasTemperature;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (started)
            {
                try { AdlPmLogSharedStop(context, adapterIndex, ref device); }
                catch { }
            }
        }
    }

    private static AdlPmLogDataOutput CreatePmLogOutput()
    {
        AdlPmLogDataOutput output = new AdlPmLogDataOutput();
        output.Size = Marshal.SizeOf(typeof(AdlPmLogDataOutput));
        output.Sensors = new AdlSingleSensorData[256];
        return output;
    }

    private static AdlSingleSensorData GetPmLogSensor(AdlPmLogDataOutput output,
        int sensorIndex, int listIndex)
    {
        AdlSingleSensorData sensor = output.Sensors[sensorIndex];
        if (sensor.Supported == 0 && listIndex < output.Sensors.Length)
            sensor = output.Sensors[listIndex];
        return sensor;
    }

    private static void FinishSamples(List<int> utilizations, List<int> powers,
        List<int> temperatures, out int utilization, out int boardPower, out int temperature,
        out bool hasUtilization, out bool hasBoardPower, out bool hasTemperature)
    {
        utilizations.Sort();
        powers.Sort();
        temperatures.Sort();
        hasUtilization = utilizations.Count > 0;
        hasBoardPower = powers.Count > 0;
        hasTemperature = temperatures.Count > 0;
        utilization = hasUtilization ? utilizations[utilizations.Count / 2] : 0;
        boardPower = hasBoardPower ? powers[powers.Count / 2] : 0;
        temperature = hasTemperature ? temperatures[temperatures.Count / 2] : 0;
    }

    private static void TryReadAmdPmLogOnce(IntPtr context, int adapterIndex, out int utilization,
        out int boardPower, out int temperature, out bool hasUtilization,
        out bool hasBoardPower, out bool hasTemperature)
    {
        utilization = 0;
        boardPower = 0;
        temperature = 0;
        hasUtilization = false;
        hasBoardPower = false;
        hasTemperature = false;
        try
        {
            AdlPmLogDataOutput output = CreatePmLogOutput();
            if (AdlQueryPmLogData(context, adapterIndex, ref output) != 0 || output.Sensors == null)
                return;

            const int GpuActivitySensor = 19;
            const int BoardPowerSensor = 73;
            const int TemperatureSensor = 8;
            AdlSingleSensorData activity = output.Sensors[GpuActivitySensor];
            AdlSingleSensorData power = output.Sensors[BoardPowerSensor];
            AdlSingleSensorData gpuTemperature = output.Sensors[TemperatureSensor];
            hasUtilization = activity.Supported != 0 && activity.Value >= 0 && activity.Value <= 100;
            hasBoardPower = power.Supported != 0 && power.Value > 0;
            hasTemperature = gpuTemperature.Supported != 0 && gpuTemperature.Value >= 0 &&
                gpuTemperature.Value <= 150;
            if (hasUtilization) utilization = activity.Value;
            if (hasBoardPower) boardPower = power.Value;
            if (hasTemperature) temperature = gpuTemperature.Value;
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlOd6CurrentStatus
    {
        public int EngineClock;
        public int MemoryClock;
        public int ActivityPercent;
        public int CurrentPerformanceLevel;
        public int CurrentBusSpeed;
        public int CurrentBusLanes;
        public int MaximumBusLanes;
        public int ExtValue;
        public int ExtMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlOdnPerformanceStatus
    {
        public int CoreClock;
        public int MemoryClock;
        public int DcefClock;
        public int GfxClock;
        public int UvdClock;
        public int VceClock;
        public int GpuActivityPercent;
        public int CurrentCorePerformanceLevel;
        public int CurrentMemoryPerformanceLevel;
        public int CurrentDcefPerformanceLevel;
        public int CurrentGfxPerformanceLevel;
        public int UvdPerformanceLevel;
        public int VcePerformanceLevel;
        public int CurrentBusSpeed;
        public int CurrentBusLanes;
        public int MaximumBusLanes;
        public int Vddc;
        public int Vddci;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlSingleSensorData
    {
        public int Supported;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlPmLogDataOutput
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public AdlSingleSensorData[] Sensors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdlAdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Udid;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PnpString;
        public int OsDisplayIndex;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AdlMemoryAlloc(int size);

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlInit();
    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlShutdown();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetCount(out uint count);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int NvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPciInfo_v3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetPciInfo(IntPtr device, out NvmlPciInfo info);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetTemperature(IntPtr device, uint sensorType,
        out uint temperature);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperatureV", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetTemperatureV(IntPtr device, ref NvmlTemperature temperature);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFieldValues", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetFieldValues(IntPtr device, int count,
        [In, Out] NvmlFieldValue[] values);

    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Main_Control_Create", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlMainCreate(AdlMemoryAlloc callback, int enumConnectedAdapters, out IntPtr context);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Main_Control_Destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlMainDestroy(IntPtr context);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Adapter_NumberOfAdapters_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlAdapterCount(IntPtr context, out int count);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Adapter_AdapterInfo_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlAdapterInfoGet(IntPtr context, IntPtr info, int inputSize);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Adapter_Active_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlAdapterActiveGet(IntPtr context, int adapterIndex, out int active);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Overdrive6_CurrentStatus_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlCurrentStatus(IntPtr context, int adapterIndex, out AdlOd6CurrentStatus status);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Overdrive6_CurrentPower_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlCurrentPower(IntPtr context, int adapterIndex, int powerType, out int currentValue);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_OverdriveN_PerformanceStatus_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlOdnPerformanceStatusGet(IntPtr context, int adapterIndex, out AdlOdnPerformanceStatus status);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_New_QueryPMLogData_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlQueryPmLogData(IntPtr context, int adapterIndex, ref AdlPmLogDataOutput output);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Overdrive8_PMLog_ShareMemory_Support", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlPmLogSharedSupport(IntPtr context, int adapterIndex, out int supported, int option);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Overdrive8_PMLog_ShareMemory_Start", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlPmLogSharedStart(IntPtr context, int adapterIndex, int sampleRate,
        int sensorCount, [In] int[] sensorList, out uint device, out IntPtr sharedMemory, int option);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Overdrive8_PMLog_ShareMemory_Read", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlPmLogSharedRead(IntPtr context, int adapterIndex, int sensorCount,
        [In] int[] sensorList, ref IntPtr sharedMemory, ref AdlPmLogDataOutput output);
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Overdrive8_PMLog_ShareMemory_Stop", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AdlPmLogSharedStop(IntPtr context, int adapterIndex, ref uint device);
}
