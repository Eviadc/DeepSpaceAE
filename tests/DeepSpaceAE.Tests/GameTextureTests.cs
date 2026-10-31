using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using Xunit;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.Tests;

public sealed class GameTextureTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "assets", "game", "Textures", "Items");

    [Fact]
    public void CoreTextureCoversAllNineCellsAndWhitelistContainsAllActiveAndInactiveImages()
    {
        Assert.Equal(87, Directory.GetFiles(Root, "*.png").Length);
        foreach (var name in new[] { "core", "terminal", "item-storage-bus", "pipeline" })
            Assert.True(File.Exists(Path.Combine(Root, name + "-off.png")));
        for (int mask = 0; mask < 16; mask++) Assert.True(File.Exists(Path.Combine(Root, $"pipeline-{mask}-off.png")));
        var (width, height, pixels) = Decode("core.png");
        Assert.Equal(48, width);
        Assert.Equal(48, height);
        foreach (int y in new[] { 8, 24, 40 })
            foreach (int x in new[] { 8, 24, 40 }) Assert.Equal(255, pixels[(y * width + x) * 4 + 3]);
    }

    [Fact]
    public void IoBusProfilesKeepTheExistingFourFaceHitGeometry()
    {
        foreach(var kind in new[]{"import","export"})
            for(int side=0;side<4;side++) foreach(var suffix in new[]{"","-off"})
            {
                var original=Decode($"bus-face-{side}{suffix}.png"); var profile=Decode($"{kind}-face-{side}{suffix}.png");
                Assert.Equal(original.Width,profile.Width); Assert.Equal(original.Height,profile.Height);
                for(int i=3;i<original.Pixels.Length;i+=4) Assert.Equal(original.Pixels[i],profile.Pixels[i]);
            }
    }
    [Fact]
    public void DriveAndDisksHaveTheirActualCellDimensions()
    {
        foreach(var name in new[]{"drive.png","drive-off.png"})
        { var (width,height,_)=Decode(name); Assert.Equal(32,width); Assert.Equal(32,height); }
        foreach(var name in new[]{"disk-small.png","disk-large.png"})
        { var (width,height,_)=Decode(name); Assert.Equal(16,width); Assert.Equal(32,height); }
    }

    [Fact]
    public void ControllerAnimationKeepsTheCanvasAndOccupancyWhileChangingItsLights()
    {
        var (width, height, original) = Decode("core.png");
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 12; i++)
        {
            var frame = Decode($"core-{i}.png");
            Assert.Equal(width, frame.Width); Assert.Equal(height, frame.Height);
            for (int pixel = 0; pixel < width * height; pixel++)
                Assert.Equal(original[pixel * 4 + 3], frame.Pixels[pixel * 4 + 3]);
            Assert.Equal(original[..4], frame.Pixels[..4]); // chassis corner remains fixed
            Assert.True(distinct.Add(Convert.ToBase64String(frame.Pixels)), "Repeated light frame.");
            if (i == 0) Assert.Equal(original, frame.Pixels);
        }
    }

    [Fact]
    public void AllCableTexturesExposeExactlyTheirNamedPorts()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            var (width, height, pixels) = Decode($"pipeline-{mask}.png");
            Assert.Equal(16, width);
            Assert.Equal(16, height);
            for (int side = 0; side < 4; side++)
            {
                // AE glass has transparent holes. Inspect the whole boundary, rather than
                // requiring one opaque center pixel that is not part of the source texture.
                bool visible = false;
                for (int cell = 0; cell < 16; cell++)
                {
                    var (x, y) = side switch { 0 => (cell, 0), 1 => (15, cell), 2 => (cell, 15), _ => (0, cell) };
                    visible |= pixels[(y * width + x) * 4 + 3] > 0;
                }
                Assert.Equal((mask & (1 << side)) != 0, visible);
            }
            bool hasCenter = false;
            for (int y = 6; y < 10; y++) for (int x = 6; x < 10; x++) hasCenter |= pixels[(y * width + x) * 4 + 3] > 0;
            Assert.True(hasCenter);
        }
    }

    [Fact]
    public void BusFaceHitRegionsCoverTheirActualModelPixelsAndLeaveCenterFree()
    {
        for(int side=0;side<4;side++)
        {
            var (width,height,pixels)=Decode($"bus-face-{side}.png");
            var inactive=Decode($"bus-face-{side}-off.png");
            Assert.Equal(16,width); Assert.Equal(16,height); int visible=0;
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                int index=(y*width+x)*4+3;
                Assert.Equal(pixels[index],inactive.Pixels[index]);
                if(pixels[index]==0) continue;
                visible++; Assert.Equal(side,CablePorts.FaceAt(1<<side,x+0.5f,y+0.5f));
            }
            Assert.True(visible>0); Assert.Equal(-1,CablePorts.FaceAt(15,8,8));
            Assert.Equal(-1,CablePorts.FaceAt(0,8,0.5f));
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
