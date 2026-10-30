using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class GameTextureTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "assets", "game", "Textures", "Items");

    [Fact]
    public void CoreTextureCoversAllNineCellsAndWhitelistContainsTwentyImages()
    {
        Assert.Equal(20, Directory.GetFiles(Root, "*.png").Length);
        var (width, height, pixels) = Decode("core.png");
        Assert.Equal(48, width);
        Assert.Equal(48, height);
        foreach (int y in new[] { 8, 24, 40 })
            foreach (int x in new[] { 8, 24, 40 }) Assert.Equal(255, pixels[(y * width + x) * 4 + 3]);
    }

    [Fact]
    public void AllCableTexturesExposeExactlyTheirNamedPorts()
    {
        var edges = new[] { (7, 0), (15, 7), (7, 15), (0, 7) };
        for (int mask = 0; mask < 16; mask++)
        {
            var (width, height, pixels) = Decode($"pipeline-{mask}.png");
            Assert.Equal(16, width);
            Assert.Equal(16, height);
            for (int side = 0; side < 4; side++)
            {
                var (x, y) = edges[side];
                Assert.Equal((mask & (1 << side)) != 0, pixels[(y * width + x) * 4 + 3] == 255);
            }
            Assert.Equal(255, pixels[(7 * width + 7) * 4 + 3]);
        }
    }

    private static (int Width, int Height, byte[] Pixels) Decode(string name)
    {
        var data = File.ReadAllBytes(Path.Combine(Root, name));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, data[..8]);
        int width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        Assert.Equal(8, data[24]);
        Assert.Equal(6, data[25]);
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < data.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
            if (data[offset + 4] == 'I' && data[offset + 5] == 'D') compressed.Write(data, offset + 8, length);
            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var rows = raw.ToArray();
        Assert.Equal(height * (width * 4 + 1), rows.Length);
        var pixels = new byte[height * width * 4];
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, rows[y * (width * 4 + 1)]);
            Array.Copy(rows, y * (width * 4 + 1) + 1, pixels, y * width * 4, width * 4);
        }
        return (width, height, pixels);
    }
}
