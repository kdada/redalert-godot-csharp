namespace Ra2AssetPipeline;

public sealed record PaletteColor(byte R, byte G, byte B);

public sealed record Palette(int ComponentBits, byte[] RawBytes, IReadOnlyList<PaletteColor> Colors)
{
    public static Palette Read(ReadOnlySpan<byte> data, int bits = 6)
    {
        ShpTs.Require(data.Length == 768, "PAL must contain exactly 768 RGB bytes (256 colors).");
        ShpTs.Require(bits is 6 or 8, "PAL component depth must be 6 or 8.");
        var colors = new PaletteColor[256];
        for (int i = 0; i < colors.Length; i++)
        {
            int p = i * 3;
            if (bits == 6)
                ShpTs.Require(data[p] <= 63 && data[p + 1] <= 63 && data[p + 2] <= 63,
                    $"PAL color {i}: component outside 6-bit range; use --palette-depth 8 for RGB8 data.");
            // Westwood VGA expansion: 6-bit channels are shifted left by two (63 -> 252).
            // Preserve the original bytes in metadata, so no palette information is lost.
            int shift = bits == 6 ? 2 : 0;
            colors[i] = new PaletteColor((byte)(data[p] << shift), (byte)(data[p + 1] << shift), (byte)(data[p + 2] << shift));
        }
        return new Palette(bits, data.ToArray(), colors);
    }
}
