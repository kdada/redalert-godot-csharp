namespace Ra2AssetPipeline;

// The two linked BCL helpers call this guard. No SHP reader or AssetPipeline
// project reference is pulled into the independent terrain executable.
internal static class ShpTs
{
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
