namespace CS2SaveFixer.Tool;

public static class SaveFormatUtils
{
    public const int CosvHeaderSize = 8;
    public static readonly byte[] CosvMagicBytes = [(byte)'C', (byte)'O', (byte)'S', (byte)'V'];

    public static bool HasValidCosvHeader(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < CosvHeaderSize)
        {
            return false;
        }

        return buffer.StartsWith(CosvMagicBytes);
    }

    public static bool ApplyCosvHeader(Span<byte> buffer)
    {
        if (buffer.Length < 4)
        {
            return false;
        }

        if (!buffer.StartsWith(CosvMagicBytes))
        {
            CosvMagicBytes.CopyTo(buffer);
            return true;
        }

        return false;
    }
}
