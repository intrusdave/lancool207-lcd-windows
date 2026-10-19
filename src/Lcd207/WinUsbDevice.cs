using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Lcd207;

/// <summary>Minimal WinUSB transport: find device by VID/PID, open, bulk read/write.</summary>
public sealed partial class WinUsbDevice : IDisposable
{
    private static readonly Guid GuidDevInterfaceUsbDevice = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private readonly SafeFileHandle _file;
    private nint _winUsb;
    private readonly byte _outPipe;
    private readonly byte _inPipe;
    private uint _currentReadTimeout;

    public string DevicePath { get; }

    private WinUsbDevice(string path, SafeFileHandle file, nint winUsb, byte outPipe, byte inPipe)
    {
        DevicePath = path;
        _file = file;
        _winUsb = winUsb;
        _outPipe = outPipe;
        _inPipe = inPipe;
    }

    public static WinUsbDevice Open(ushort vid, ushort pid, byte outPipe, byte inPipe, uint writeTimeoutMs, uint readTimeoutMs)
    {
        string path = FindDevicePath(vid, pid)
            ?? throw new InvalidOperationException(
                $"USB device {vid:X4}:{pid:X4} not found. Is the panel connected, in LCD mode, and bound to WinUSB?");

        var file = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, 0);
        if (file.IsInvalid)
        {
            int err = Marshal.GetLastPInvokeError();
            file.Dispose();
            throw new Win32Exception(err, $"CreateFile failed for {path} (is L-Connect running and holding the device?)");
        }

        if (!WinUsb_Initialize(file, out nint handle))
        {
            int err = Marshal.GetLastPInvokeError();
            file.Dispose();
            throw new Win32Exception(err, "WinUsb_Initialize failed");
        }

        var dev = new WinUsbDevice(path, file, handle, outPipe, inPipe);
        try
        {
            dev.VerifyPipes();
            dev.SetTimeout(outPipe, writeTimeoutMs);
            dev.SetTimeout(inPipe, readTimeoutMs);
            dev._currentReadTimeout = readTimeoutMs;
        }
        catch
        {
            dev.Dispose();
            throw;
        }
        return dev;
    }

    /// <summary>Writes the whole buffer, looping over short writes.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            uint transferred;
            bool ok;
            unsafe
            {
                fixed (byte* p = data)
                    ok = WinUsb_WritePipe(_winUsb, _outPipe, p, (uint)data.Length, out transferred, 0);
            }
            if (!ok)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "WinUsb_WritePipe failed");
            if (transferred == 0)
                throw new IOException("WinUsb_WritePipe wrote 0 bytes");
            data = data[(int)transferred..];
        }
    }

    /// <summary>Reads one transfer. Returns 0 on timeout.</summary>
    public int Read(Span<byte> buffer, uint timeoutMs)
    {
        if (timeoutMs != _currentReadTimeout)
        {
            SetTimeout(_inPipe, timeoutMs);
            _currentReadTimeout = timeoutMs;
        }

        uint transferred;
        bool ok;
        unsafe
        {
            fixed (byte* p = buffer)
                ok = WinUsb_ReadPipe(_winUsb, _inPipe, p, (uint)buffer.Length, out transferred, 0);
        }
        if (ok)
            return (int)transferred;

        int err = Marshal.GetLastPInvokeError();
        if (err == ERROR_SEM_TIMEOUT)
            return 0;
        throw new Win32Exception(err, "WinUsb_ReadPipe failed");
    }

    /// <summary>Equivalent of libusb clear_halt on both endpoints.</summary>
    public void ResetPipes()
    {
        WinUsb_ResetPipe(_winUsb, _outPipe);
        WinUsb_ResetPipe(_winUsb, _inPipe);
    }

    private void VerifyPipes()
    {
        if (!WinUsb_QueryInterfaceSettings(_winUsb, 0, out USB_INTERFACE_DESCRIPTOR desc))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WinUsb_QueryInterfaceSettings failed");

        bool foundOut = false, foundIn = false;
        for (byte i = 0; i < desc.bNumEndpoints; i++)
        {
            if (!WinUsb_QueryPipe(_winUsb, 0, i, out WINUSB_PIPE_INFORMATION pipe))
                continue;
            foundOut |= pipe.PipeId == _outPipe;
            foundIn |= pipe.PipeId == _inPipe;
        }
        if (!foundOut || !foundIn)
            throw new InvalidOperationException(
                $"Expected endpoints 0x{_outPipe:X2}/0x{_inPipe:X2} not present on interface 0");
    }

    private void SetTimeout(byte pipe, uint timeoutMs)
    {
        if (!WinUsb_SetPipePolicy(_winUsb, pipe, PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref timeoutMs))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WinUsb_SetPipePolicy failed");
    }

    public void Dispose()
    {
        if (_winUsb != 0)
        {
            WinUsb_Free(_winUsb);
            _winUsb = 0;
        }
        _file.Dispose();
    }

    // ---- Device discovery ----

    private static string? FindDevicePath(ushort vid, ushort pid)
    {
        string match = $"vid_{vid:x4}&pid_{pid:x4}";

        // Prefer the device-specific interface GUID that WinUSB registered (MS OS descriptor / Zadig).
        foreach (var guid in DeviceInterfaceGuidsFromRegistry(vid, pid).Append(GuidDevInterfaceUsbDevice))
        {
            var paths = EnumerateInterfacePaths(guid)
                .Where(p => p.Contains(match, StringComparison.OrdinalIgnoreCase))
                .ToList();
            // Composite devices expose per-interface paths; we want interface 0.
            string? path = paths.FirstOrDefault(p => p.Contains("&mi_00", StringComparison.OrdinalIgnoreCase))
                ?? paths.FirstOrDefault(p => !p.Contains("&mi_", StringComparison.OrdinalIgnoreCase));
            if (path != null)
                return path;
        }
        return null;
    }

    private static IEnumerable<Guid> DeviceInterfaceGuidsFromRegistry(ushort vid, ushort pid)
    {
        var result = new List<Guid>();
        using var root = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\VID_{vid:X4}&PID_{pid:X4}");
        if (root == null)
            return result;
        foreach (var instance in root.GetSubKeyNames())
        {
            using var parms = root.OpenSubKey($@"{instance}\Device Parameters");
            var values = new List<string>();
            switch (parms?.GetValue("DeviceInterfaceGUIDs"))
            {
                case string[] arr: values.AddRange(arr); break;
                case string s: values.Add(s); break;
            }
            if (parms?.GetValue("DeviceInterfaceGUID") is string single)
                values.Add(single);
            foreach (var v in values)
                if (Guid.TryParse(v, out var g) && !result.Contains(g))
                    result.Add(g);
        }
        return result;
    }

    private static List<string> EnumerateInterfacePaths(Guid guid)
    {
        var paths = new List<string>();
        nint set = SetupDiGetClassDevsW(ref guid, 0, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID_HANDLE_VALUE)
            return paths;
        try
        {
            var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref ifData); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref ifData, 0, 0, out uint required, 0);
                if (required == 0)
                    continue;
                nint detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
                    Marshal.WriteInt32(detail, nint.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref ifData, detail, required, out _, 0))
                    {
                        string? path = Marshal.PtrToStringUni(detail + 4);
                        if (path != null)
                            paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    // ---- Native interop ----

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint PIPE_TRANSFER_TIMEOUT = 0x03;
    private const int ERROR_SEM_TIMEOUT = 121;
    private static readonly nint INVALID_HANDLE_VALUE = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial nint SetupDiGetClassDevsW(ref Guid classGuid, nint enumerator, nint hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, nint deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_Initialize(SafeFileHandle deviceHandle, out nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_Free(nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_QueryInterfaceSettings(nint interfaceHandle, byte alternateSettingNumber,
        out USB_INTERFACE_DESCRIPTOR usbAltInterfaceDescriptor);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_QueryPipe(nint interfaceHandle, byte alternateInterfaceNumber,
        byte pipeIndex, out WINUSB_PIPE_INFORMATION pipeInformation);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_SetPipePolicy(nint interfaceHandle, byte pipeId, uint policyType,
        uint valueLength, ref uint value);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WinUsb_WritePipe(nint interfaceHandle, byte pipeId, byte* buffer,
        uint bufferLength, out uint lengthTransferred, nint overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WinUsb_ReadPipe(nint interfaceHandle, byte pipeId, byte* buffer,
        uint bufferLength, out uint lengthTransferred, nint overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinUsb_ResetPipe(nint interfaceHandle, byte pipeId);
}
