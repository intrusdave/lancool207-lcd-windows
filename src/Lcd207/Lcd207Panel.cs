using System.Diagnostics;
using System.Text;

namespace Lcd207;

/// <summary>LANCOOL 207 Digital LCD (1CBE:A065). Mirrors lian-li-linux winusb/lcd/base.rs + core.rs.</summary>
public sealed class Lcd207Panel : IDisposable
{
    public const ushort Vid = 0x1CBE;
    public const ushort Pid = 0xA065;
    public const int Width = 720;
    public const int Height = 1472;
    public const int MaxPayload = 512_000;

    private const byte EpOut = 0x01;
    private const byte EpIn = 0x81;
    private const uint WriteTimeoutMs = 2000;
    private const uint ReadTimeoutMs = 200;
    private const uint DrainTimeoutMs = 5;
    private const int ReplyDeadlineMs = 1000;

    private readonly WinUsbDevice _usb;
    private readonly PacketBuilder _builder = new();
    private readonly byte[] _reply = new byte[512];

    public string? Firmware { get; private set; }
    public Action<string> Log { get; set; } = _ => { };

    private Lcd207Panel(WinUsbDevice usb) => _usb = usb;

    public static Lcd207Panel Open()
    {
        var usb = WinUsbDevice.Open(Vid, Pid, EpOut, EpIn, WriteTimeoutMs, ReadTimeoutMs);
        return new Lcd207Panel(usb);
    }

    public string DevicePath => _usb.DevicePath;

    /// <summary>Full init as base.rs do_init. clearLayers pushes a transparent PNG + black JPEG.</summary>
    public void Init(bool clearLayers = true)
    {
        Drain();
        StopPlayback();
        ReadFirmware();
        SendCommand(_builder.SyncClock(DateTime.Now, 2), PacketBuilder.CmdSetClock, "SyncClock");
        SendCommand(_builder.StopClock(), PacketBuilder.CmdStopClock, "StopClock");
        if (clearLayers)
            ClearLayers();
    }

    public void StopPlayback()
    {
        _usb.Write(_builder.StopPlay());
        ReadReply("StopPlay", PacketBuilder.CmdStopPlay);
        Drain();
        Thread.Sleep(150);
    }

    public string? ReadFirmware()
    {
        _usb.Write(_builder.GetVer());
        int n = ReadReply("GetVer", PacketBuilder.CmdGetVer);
        if (n >= 8 && _reply[0] == PacketBuilder.CmdGetVer)
        {
            var span = _reply.AsSpan(8, Math.Min(32, n - 8));
            int end = span.IndexOf((byte)0);
            if (end >= 0)
                span = span[..end];
            Firmware = Encoding.ASCII.GetString(span).Trim();
        }
        return Firmware;
    }

    public void SetBrightness(byte value) => SendCommand(_builder.Brightness(value), PacketBuilder.CmdBrightness, "Brightness");

    public void PushJpeg(byte[] jpeg)
    {
        if (jpeg.Length > MaxPayload)
            throw new ArgumentException($"JPEG is {jpeg.Length} bytes; panel max is {MaxPayload}");

        var packet = Concat(_builder.PushJpg(jpeg.Length), jpeg);
        try
        {
            _usb.Write(packet);
        }
        catch (Exception e)
        {
            Log($"Frame write failed ({e.Message}); resetting pipes and retrying once");
            _usb.ResetPipes();
            Init(clearLayers: false);
            packet = Concat(_builder.PushJpg(jpeg.Length), jpeg);
            _usb.Write(packet);
        }

        int n = ReadReply("frame ack", PacketBuilder.CmdPushJpg);
        if (n > 8 && _reply[8] > 3)
            WaitBuffer(2, TimeSpan.FromSeconds(5));
    }

    public void PushPng(byte[] png)
    {
        _usb.Write(Concat(_builder.PushPng(png.Length), png));
        ReadReply("PushPng", PacketBuilder.CmdPushPng);
    }

    private void ClearLayers()
    {
        PushPng(ImageEncoder.TransparentPng(Width, Height));
        PushJpeg(ImageEncoder.SolidJpeg(Width, Height));
    }

    private void WaitBuffer(int target, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            Thread.Sleep(50);
            _usb.Write(_builder.QueryBlock());
            int n = ReadReply("QueryBlock", PacketBuilder.CmdQueryBlock);
            if (n > 8 && _reply[8] <= target)
                return;
        }
        Log("Timed out waiting for panel buffer to drain");
    }

    private void SendCommand(byte[] header, byte command, string name)
    {
        _usb.Write(header);
        ReadReply(name, command);
        Drain();
    }

    /// <summary>
    /// Reads until a reply echoing <paramref name="command"/> in byte 0 arrives, discarding stale
    /// replies to earlier commands (the panel sometimes answers slower than one read timeout).
    /// </summary>
    private int ReadReply(string what, byte command)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ReplyDeadlineMs)
        {
            int n = _usb.Read(_reply, ReadTimeoutMs);
            if (n == 0)
                continue;
            if (_reply[0] == command)
            {
                Log($"{what}: {n}-byte reply [8]=0x{(n > 8 ? _reply[8] : 0):X2} after {sw.ElapsedMilliseconds} ms");
                return n;
            }
            Log($"{what}: discarded stale reply [0]=0x{_reply[0]:X2}");
        }
        Log($"{what}: no reply");
        return 0;
    }

    private void Drain()
    {
        var scratch = new byte[512];
        for (int i = 0; i < 64 && _usb.Read(scratch, DrainTimeoutMs) > 0; i++) { }
    }

    private static byte[] Concat(byte[] header, byte[] payload)
    {
        var packet = new byte[header.Length + payload.Length];
        header.CopyTo(packet, 0);
        payload.CopyTo(packet, header.Length);
        return packet;
    }

    public void Dispose()
    {
        _builder.Dispose();
        _usb.Dispose();
    }
}
