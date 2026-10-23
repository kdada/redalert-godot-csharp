using System.Security.Cryptography;
using System.Text.Json;

namespace Ra2AssetPipeline;

public sealed record ExportOptions(string OutputDirectory, string Layout = "atlas", int Columns = 0,
    string ShadowLayout = "unspecified", byte ShadowAlpha = 128, byte TransparentIndex = 0);
public sealed record IntRect(int X, int Y, int Width, int Height);
public sealed record FloatPoint(float X, float Y);
public sealed record ImageSet(string Color, string Indices, string TeamMask, int Width, int Height);
public sealed record ExportFrame(int Index, IntRect SourceRect, FloatPoint CenterOffset, byte Compression,
    uint DataOffset, int EncodedLength, bool HasData, string ReservedHeaderBase64,
    string Role, int? PairedFrame, int ImageSet, IntRect ImageRect, string IndicesSha256);
public sealed record PaletteMetadata(string SourceFile, string Sha256, int ComponentBits,
    string Expansion, string RawRgbBase64, IReadOnlyList<PaletteColor> Colors);
public sealed record TeamMaskMetadata(int FirstIndex, int LastIndex, string Encoding);
public sealed record ExportManifest(int SchemaVersion, string Format, string SourceFile, string SourceSha256,
    int CanvasWidth, int CanvasHeight, string Layout, string ShadowLayout, byte ShadowAlpha,
    byte TransparentIndex, string Coordinates, TeamMaskMetadata TeamMask, PaletteMetadata Palette,
    IReadOnlyList<ImageSet> Images, IReadOnlyList<ExportFrame> Frames);

public static class Exporter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static ExportManifest Export(ShpSprite sprite, Palette palette, ExportOptions options,
        string shpFile, byte[] shpBytes, string palFile)
    {
        ShpTs.Require(options.Layout is "atlas" or "frames", "Layout must be atlas or frames.");
        ShpTs.Require(options.ShadowLayout is "unspecified" or "second-half", "Shadow layout must be unspecified or second-half.");
        ShpTs.Require(options.Columns >= 0 && options.Columns <= sprite.Frames.Count, "Columns must be between 1 and the frame count, or omitted.");
        ShpTs.Require(options.Layout == "atlas" || options.Columns == 0, "--columns is only valid for atlas layout.");
        if (options.ShadowLayout == "second-half")
            ShpTs.Require(sprite.Frames.Count % 2 == 0, "second-half shadow layout requires an even frame count.");
        string directory = Path.GetFullPath(options.OutputDirectory);
        ShpTs.Require(!File.Exists(directory), "Output path is an existing file.");
        ShpTs.Require(!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any(),
            "Output directory must be new or empty; existing exports are never overwritten.");

        var images = new List<ImageSet>();
        var frames = new List<ExportFrame>();
        int columns = options.Columns == 0 ? (int)Math.Ceiling(Math.Sqrt(sprite.Frames.Count)) : options.Columns;
        int cellWidth = sprite.Frames.Max(f => Math.Max(1, f.Width));
        int cellHeight = sprite.Frames.Max(f => Math.Max(1, f.Height));
        if (options.Layout == "atlas")
        {
            int width = checked(columns * cellWidth);
            int height = checked(((sprite.Frames.Count + columns - 1) / columns) * cellHeight);
            CheckImage(width, height);
        }
        else
            foreach (var frame in sprite.Frames) CheckImage(Math.Max(1, frame.Width), Math.Max(1, frame.Height));
        Directory.CreateDirectory(directory);

        if (options.Layout == "atlas")
        {
            int width = columns * cellWidth;
            int height = ((sprite.Frames.Count + columns - 1) / columns) * cellHeight;
            var samples = NewSamples(width, height, options.TransparentIndex);
            foreach (var frame in sprite.Frames)
            {
                int x = frame.Index % columns * cellWidth, y = frame.Index / columns * cellHeight;
                Blit(frame, palette, options, sprite.Frames.Count, width, x, y, samples);
                frames.Add(DescribeFrame(sprite, frame, options, 0, x, y));
            }
            images.Add(WriteImages(directory, "atlas", width, height, samples));
        }
        else
        {
            foreach (var frame in sprite.Frames)
            {
                int width = Math.Max(1, frame.Width), height = Math.Max(1, frame.Height);
                var samples = NewSamples(width, height, options.TransparentIndex);
                Blit(frame, palette, options, sprite.Frames.Count, width, 0, 0, samples);
                frames.Add(DescribeFrame(sprite, frame, options, frame.Index, 0, 0));
                images.Add(WriteImages(directory, $"frame-{frame.Index:D5}", width, height, samples));
            }
        }
        var manifest = new ExportManifest(1, "shp-ts-ra2", Path.GetFileName(shpFile), Hash(shpBytes),
            sprite.CanvasWidth, sprite.CanvasHeight, options.Layout, options.ShadowLayout,
            options.ShadowAlpha, options.TransparentIndex,
            "top-left, pixels; sourceRect is relative to canvas; centerOffset is crop center minus canvas center; imageRect excludes cell padding",
            new TeamMaskMetadata(16, 31, "grayscale8: 0=not-remappable, 1..16=original-index-minus-15; read raw samples without sRGB conversion"),
            new PaletteMetadata(Path.GetFileName(palFile), Hash(palette.RawBytes), palette.ComponentBits,
                palette.ComponentBits == 6 ? "channel << 2 (0..252)" : "identity (0..255)",
                Convert.ToBase64String(palette.RawBytes), palette.Colors), images, frames);
        using var output = new FileStream(Path.Combine(directory, "manifest.json"), FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(output, manifest, JsonOptions);
        return manifest;
    }

    private static ExportFrame DescribeFrame(ShpSprite sprite, ShpFrame frame, ExportOptions options, int imageSet, int x, int y)
    {
        bool declared = options.ShadowLayout == "second-half";
        int half = sprite.Frames.Count / 2;
        string role = declared ? (frame.Index >= half ? "shadow" : "color") : "unspecified";
        int? paired = declared ? (frame.Index >= half ? frame.Index - half : frame.Index + half) : null;
        return new ExportFrame(frame.Index, new IntRect(frame.X, frame.Y, frame.Width, frame.Height),
            new FloatPoint(frame.X + frame.Width / 2f - sprite.CanvasWidth / 2f,
                frame.Y + frame.Height / 2f - sprite.CanvasHeight / 2f),
            frame.Compression, frame.DataOffset, frame.EncodedLength, frame.HasData,
            Convert.ToBase64String(frame.ReservedHeaderBytes), role, paired, imageSet,
            new IntRect(x, y, frame.Width, frame.Height), Hash(frame.Indices));
    }

    private static void CheckImage(int width, int height) =>
        ShpTs.Require(width > 0 && height > 0 && (long)width * height <= PngWriter.MaxImagePixels,
            "Output image pixel budget exceeded; try --layout frames or fewer --columns.");

    private sealed record Samples(byte[] Color, byte[] Indices, byte[] Mask);
    private static Samples NewSamples(int width, int height, byte transparentIndex)
    {
        int pixels = checked(width * height);
        var indices = new byte[pixels];
        if (transparentIndex != 0) Array.Fill(indices, transparentIndex);
        return new Samples(new byte[pixels * 4], indices, new byte[pixels]);
    }

    private static void Blit(ShpFrame frame, Palette palette, ExportOptions options,
        int frameCount, int imageWidth, int originX, int originY, Samples samples)
    {
        // A zero data pointer represents an empty frame regardless of crop dimensions.
        if (!frame.HasData) return;
        bool shadow = options.ShadowLayout == "second-half" && frame.Index >= frameCount / 2;
        for (int y = 0; y < frame.Height; y++)
        for (int x = 0; x < frame.Width; x++)
        {
            byte index = frame.Indices[y * frame.Width + x];
            int destination = (originY + y) * imageWidth + originX + x;
            samples.Indices[destination] = index;
            samples.Mask[destination] = index is >= 16 and <= 31 ? (byte)(index - 15) : (byte)0;
            int p = destination * 4;
            var color = palette.Colors[index];
            samples.Color[p] = shadow ? (byte)0 : color.R;
            samples.Color[p + 1] = shadow ? (byte)0 : color.G;
            samples.Color[p + 2] = shadow ? (byte)0 : color.B;
            samples.Color[p + 3] = index == options.TransparentIndex ? (byte)0 : shadow ? options.ShadowAlpha : (byte)255;
        }
    }

    private static ImageSet WriteImages(string directory, string stem, int width, int height, Samples samples)
    {
        var files = new ImageSet($"{stem}.png", $"{stem}.indices.png", $"{stem}.team-mask.png", width, height);
        PngWriter.Write(Path.Combine(directory, files.Color), width, height, samples.Color, 4);
        PngWriter.Write(Path.Combine(directory, files.Indices), width, height, samples.Indices, 1);
        PngWriter.Write(Path.Combine(directory, files.TeamMask), width, height, samples.Mask, 1);
        return files;
    }

    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
