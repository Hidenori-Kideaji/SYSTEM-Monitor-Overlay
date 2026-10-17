using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed class VramAdapterInfo
{
    public readonly string Name;
    public readonly long DedicatedBytes;
    public readonly uint LuidLow;
    public readonly int LuidHigh;

    public VramAdapterInfo(string name, long dedicatedBytes, uint luidLow, int luidHigh)
    {
        Name = name;
        DedicatedBytes = dedicatedBytes;
        LuidLow = luidLow;
        LuidHigh = luidHigh;
    }
}

internal sealed class GpuPciAddress
{
    internal readonly uint Bus, Device, Function;
    internal GpuPciAddress(uint bus, uint device, uint function)
    {
        Bus = bus;
        Device = device;
        Function = function;
    }
    internal bool Matches(GpuPciAddress other)
    {
        return other != null && Bus == other.Bus && Device == other.Device && Function == other.Function;
    }
    public override string ToString()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:X2}:{1:X2}.{2:X}", Bus, Device, Function);
    }
    internal static GpuPciAddress ParseNvml(string busId)
    {
        if (string.IsNullOrEmpty(busId)) return null;
        string[] segments = busId.Split(':');
        if (segments.Length != 3) return null;
        string[] deviceParts = segments[2].Split('.');
        if (deviceParts.Length != 2) return null;
        uint domain, bus, device, function;
        System.Globalization.NumberStyles hex = System.Globalization.NumberStyles.AllowHexSpecifier;
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!uint.TryParse(segments[0], hex, culture, out domain) ||
            !uint.TryParse(segments[1], hex, culture, out bus) ||
            !uint.TryParse(deviceParts[0], hex, culture, out device) ||
            !uint.TryParse(deviceParts[1], hex, culture, out function) ||
            bus > 255 || device > 31 || function > 7) return null;
        return new GpuPciAddress(bus, device, function);
    }
}

internal static class GpuPciProbe
{
    // This runs only in the timed, isolated telemetry process.
    internal static GpuPciAddress Read(uint low, int high, out string diagnostic)
    {
        diagnostic = null;
        OpenAdapter open = new OpenAdapter();
        open.Low = low;
        open.High = high;
        bool opened = false;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            int result = D3DKMTOpenAdapterFromLuid(ref open);
            if (result != 0)
            {
                diagnostic = "D3DKMTOpenAdapterFromLuid=0x" + result.ToString("X8");
                return null;
            }
            opened = true;
            AdapterAddress address = new AdapterAddress();
            address.Bus = address.Device = address.Function = uint.MaxValue;
            buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AdapterAddress)));
            Marshal.StructureToPtr(address, buffer, false);
            QueryAdapter query = new QueryAdapter();
            query.Handle = open.Handle;
            query.Type = 6; // KMTQAITYPE_ADAPTERADDRESS
            query.Data = buffer;
            query.Size = (uint)Marshal.SizeOf(typeof(AdapterAddress));
            result = D3DKMTQueryAdapterInfo(ref query);
            if (result != 0)
            {
                diagnostic = "D3DKMTQueryAdapterInfo=0x" + result.ToString("X8");
                return null;
            }
            address = (AdapterAddress)Marshal.PtrToStructure(buffer, typeof(AdapterAddress));
            if (address.Bus > 255 || address.Device > 31 || address.Function > 7)
            {
                diagnostic = "Windows returned an invalid PCI address.";
                return null;
            }
            GpuPciAddress identity = new GpuPciAddress(address.Bus, address.Device, address.Function);
            diagnostic = "Windows PCI=" + identity;
            return identity;
        }
        catch (EntryPointNotFoundException) { diagnostic = "Windows PCI query API missing."; return null; }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (opened) D3DKMTCloseAdapter(ref open.Handle);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OpenAdapter { public uint Low; public int High; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryAdapter { public uint Handle; public int Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterAddress { public uint Bus, Device, Function; }
    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapter query);
    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref uint handle);
}

internal static class VramCapacityProbe
{
    private const uint SoftwareAdapterFlag = 2;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    public static bool TryGetTotalDedicatedBytes(out long totalBytes, out string error)
    {
        totalBytes = 0;
        VramAdapterInfo[] adapters;
        if (!TryGetAdapters(out adapters, out error)) return false;

        long total = 0;
        foreach (VramAdapterInfo adapter in adapters)
            total = checked(total + adapter.DedicatedBytes);

        if (total <= 0)
        {
            error = "DXGI reported no dedicated video memory.";
            return false;
        }

        totalBytes = total;
        return true;
    }

    public static bool TryGetAdapters(out VramAdapterInfo[] adapters, out string error)
    {
        adapters = new VramAdapterInfo[0];
        error = null;
        IDXGIFactory1 factory = null;
        try
        {
            Guid factoryId = typeof(IDXGIFactory1).GUID;
            int result = CreateDXGIFactory1(ref factoryId, out factory);
            if (result < 0)
            {
                error = string.Format("CreateDXGIFactory1 failed (0x{0:X8}).", result);
                return false;
            }

            List<VramAdapterInfo> found = new List<VramAdapterInfo>();
            HashSet<string> seenLuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1 adapter;
                result = factory.EnumAdapters1(index, out adapter);
                if (result == DxgiErrorNotFound) break;
                if (result < 0)
                {
                    error = string.Format("EnumAdapters1 failed (0x{0:X8}).", result);
                    return false;
                }

                try
                {
                    AdapterDescription description;
                    result = adapter.GetDesc1(out description);
                    if (result < 0)
                    {
                        error = string.Format("GetDesc1 failed (0x{0:X8}).", result);
                        return false;
                    }

                    ulong dedicated = description.DedicatedVideoMemory.ToUInt64();
                    if ((description.Flags & SoftwareAdapterFlag) == 0 && dedicated > 0)
                    {
                        string luidKey = description.AdapterLuid.HighPart.ToString("X8") + ":" +
                            description.AdapterLuid.LowPart.ToString("X8");
                        if (seenLuids.Add(luidKey))
                        {
                            found.Add(new VramAdapterInfo(
                                (description.Description ?? "Unknown GPU").Trim(),
                                checked((long)dedicated),
                                description.AdapterLuid.LowPart,
                                description.AdapterLuid.HighPart));
                        }
                    }
                }
                finally
                {
                    if (adapter != null) Marshal.ReleaseComObject(adapter);
                }
            }

            if (found.Count == 0)
            {
                error = "DXGI reported no dedicated video memory.";
                return false;
            }

            adapters = found.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid interfaceId, out IntPtr parent);
        [PreserveSig] int EnumAdapters(uint index, out IntPtr adapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr window, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr window);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr description, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        [return: MarshalAs(UnmanagedType.Bool)] bool IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid interfaceId, out IntPtr parent);
        [PreserveSig] int EnumOutputs(uint index, out IntPtr output);
        [PreserveSig] int GetDesc(out IntPtr description);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long version);
        [PreserveSig] int GetDesc1(out AdapterDescription description);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);
}
