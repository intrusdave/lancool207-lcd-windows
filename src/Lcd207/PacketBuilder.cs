using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Lcd207;

/// <summary>
/// Port of lian-li-linux crypto.rs build_winusb: 500-byte plaintext, DES-CBC/PKCS7
/// (key = IV = "slv3tuzx") giving 504 bytes, in a 512-byte frame with trailer A1 1A.
/// </summary>
public sealed class PacketBuilder : IDisposable
{
    public const int HeaderSize = 512;
    internal const int PlainSize = 500;
    internal const int CipherSize = 504;
    internal static readonly byte[] DesKey = "slv3tuzx"u8.ToArray();

    public const byte CmdGetVer = 0x0A;
    public const byte CmdBrightness = 0x0E;
    public const byte CmdSetClock = 0x33;
    public const byte CmdStopClock = 0x34;
    public const byte CmdPushJpg = 0x65;
    public const byte CmdPushPng = 0x66;
    public const byte CmdQueryBlock = 0x7A;
    public const byte CmdStopPlay = 0x7B;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DES _des;
    private uint _lastTimestamp;

    public PacketBuilder()
    {
        _des = DES.Create();
        _des.Key = DesKey;
    }

    public byte[] Build(byte command, ReadOnlySpan<byte> parameters = default)
    {
        Span<byte> plain = stackalloc byte[PlainSize];
        plain.Clear();
        plain[0] = command;
        plain[2] = 0x1A;
        plain[3] = 0x6D;

        uint raw = (uint)_clock.ElapsedMilliseconds;
        uint ts = raw <= _lastTimestamp ? _lastTimestamp + 1 : raw;
        _lastTimestamp = ts;
        BinaryPrimitives.WriteUInt32LittleEndian(plain[4..8], ts);

        int copyLen = Math.Min(parameters.Length, PlainSize - 8);
        parameters[..copyLen].CopyTo(plain[8..]);

        var frame = new byte[HeaderSize];
        int written = _des.EncryptCbc(plain, DesKey, frame.AsSpan(0, CipherSize), PaddingMode.PKCS7);
        Debug.Assert(written == CipherSize);
        frame[510] = 0xA1;
        frame[511] = 0x1A;
        return frame;
    }

    public byte[] StopPlay() => Build(CmdStopPlay);
    public byte[] GetVer() => Build(CmdGetVer);
    public byte[] QueryBlock() => Build(CmdQueryBlock);
    public byte[] StopClock() => Build(CmdStopClock, [0]);
    public byte[] Brightness(byte value) => Build(CmdBrightness, [Math.Min(value, (byte)100)]);
    public byte[] PushJpg(int length) => Build(CmdPushJpg, BigEndian(length));
    public byte[] PushPng(int length) => Build(CmdPushPng, BigEndian(length));

    public byte[] SyncClock(DateTime now, byte mode) => Build(CmdSetClock,
    [
        (byte)(now.Year >> 8), (byte)now.Year, (byte)now.Month, (byte)now.Day,
        (byte)now.Hour, (byte)now.Minute, (byte)now.Second, mode,
    ]);

    private static byte[] BigEndian(int value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)value);
        return b;
    }

    public void Dispose() => _des.Dispose();
}
