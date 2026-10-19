using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;

namespace Lcd207.Tests;

public class PacketBuilderTests
{
    private static byte[] Decrypt(byte[] frame)
    {
        using var des = DES.Create();
        des.Key = PacketBuilder.DesKey;
        return des.DecryptCbc(frame.AsSpan(0, PacketBuilder.CipherSize), PacketBuilder.DesKey, PaddingMode.PKCS7);
    }

    [Fact]
    public void FrameHasExpectedLayout()
    {
        using var b = new PacketBuilder();
        var frame = b.PushJpg(0x0001_2345);

        Assert.Equal(512, frame.Length);
        Assert.All(frame[504..510], x => Assert.Equal(0, x));
        Assert.Equal(0xA1, frame[510]);
        Assert.Equal(0x1A, frame[511]);

        var plain = Decrypt(frame);
        Assert.Equal(500, plain.Length);
        Assert.Equal(0x65, plain[0]);
        Assert.Equal(0x00, plain[1]);
        Assert.Equal(0x1A, plain[2]);
        Assert.Equal(0x6D, plain[3]);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x23, 0x45 }, plain[8..12]);
        Assert.All(plain[12..], x => Assert.Equal(0, x));
    }

    [Fact]
    public void PaddingIsFourBytesOfFour()
    {
        using var b = new PacketBuilder();
        var frame = b.StopPlay();
        using var des = DES.Create();
        des.Key = PacketBuilder.DesKey;
        var raw = des.DecryptCbc(frame.AsSpan(0, 504), PacketBuilder.DesKey, PaddingMode.None);
        Assert.Equal(new byte[] { 4, 4, 4, 4 }, raw[500..504]);
    }

    [Fact]
    public void TimestampsStrictlyIncrease()
    {
        using var b = new PacketBuilder();
        uint prev = 0;
        for (int i = 0; i < 20; i++)
        {
            uint ts = BinaryPrimitives.ReadUInt32LittleEndian(Decrypt(b.GetVer()).AsSpan(4, 4));
            if (i > 0)
                Assert.True(ts > prev);
            prev = ts;
        }
    }
}
