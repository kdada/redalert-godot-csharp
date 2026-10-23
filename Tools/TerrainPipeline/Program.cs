namespace Ra2TerrainPipeline;

internal static class Program
{
    private const string Usage = """
        RA2 TerrainPipeline — standalone net10 TMP converter
        convert --tmp <path> --pal <path> --out <new-or-emptydir> [--palette-depth 6|8] [--no-extra]
        --self-test
        --help

        Exports one transparent 60x30 diamond RGBA PNG per nonempty TMP table entry,
        original packed indices/Z binaries, separate extra layers, and manifest.json.
        Palette defaults to the RA2 6-bit VGA expansion. Input is RA2 TMP, regardless
        of extension (.tem/.sno/.urb/.des/.lun/.ubn may be TMP or SHP; SHP is rejected).
        """;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] || args is ["-h"])
            {
                Console.WriteLine(Usage);
                return 0;
            }
            if (args is ["--self-test"])
            {
                SelfTests.Run();
                return 0;
            }
            if (args[0] != "convert") throw new ArgumentException("Expected convert, --self-test, or --help.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool extra = true;
            for (int i = 1; i < args.Length; i++)
            {
                string option = args[i];
                if (option == "--no-extra")
                {
                    if (!extra) throw new ArgumentException("Duplicate --no-extra.");
                    extra = false;
                    continue;
                }
                if (option is not ("--tmp" or "--pal" or "--out" or "--palette-depth"))
                    throw new ArgumentException($"Unknown option: {option}");
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for {option}.");
                if (!values.TryAdd(option, args[++i])) throw new ArgumentException($"Duplicate option: {option}");
            }
            string Required(string option) => values.TryGetValue(option, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException($"Required option: {option}");
            int bits = 6;
            if (values.TryGetValue("--palette-depth", out string? depth) && (!int.TryParse(depth, out bits) || bits is not (6 or 8)))
                throw new ArgumentException("--palette-depth must be 6 or 8.");
            var frames = Converter.Convert(Required("--tmp"), Required("--pal"), Required("--out"), bits, extra);
            Console.WriteLine($"Converted {frames.Count} nonempty diamonds and {frames.Count(x => x.Extra?.Exported == true)} extra layers.");
            Console.WriteLine($"Output: {Path.GetFullPath(Required("--out"))}");
            Console.WriteLine($"Manifest: {Path.Combine(Path.GetFullPath(Required("--out")), "manifest.json")}");
            foreach (var frame in frames)
                foreach (string warning in frame.Warnings)
                    Console.Error.WriteLine($"Warning: tile {frame.Index}: {warning}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or OverflowException)
        {
            Console.Error.WriteLine($"TerrainPipeline: {ex.Message}");
            return 1;
        }
    }
}
