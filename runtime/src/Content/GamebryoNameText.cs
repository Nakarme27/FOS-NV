using System.Text;

namespace OpenNV.Runtime.Content;

/// <summary>
/// Decoder for the EDID and FULL subrecords of a plugin record. Both carry names: EDID is
/// the author's identifier, FULL the text the player sees. The master files are not tidy
/// about them — a record may keep padding after the terminator, and a FULL may hold
/// cp1252 accents — and the retail engine accepts both. Reading therefore stops at the
/// first terminator and falls back to Latin-1 instead of rejecting the record, which
/// otherwise aborts the whole startup on a file the game itself loads. An empty field or
/// embedded control bytes still mean the data is unusable, so those keep throwing.
/// </summary>
internal static class GamebryoNameText
{
    internal static string Decode(FalloutPluginRecord record, string signature, ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        var text = end < 0 ? bytes : bytes[..end];
        if (text.IsEmpty)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} {signature} is empty.");
        if (text.IndexOfAnyInRange((byte)0x01, (byte)0x1F) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} {signature} carries control bytes.");
        return text.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0
            ? Encoding.Latin1.GetString(text)
            : Encoding.ASCII.GetString(text);
    }
}
