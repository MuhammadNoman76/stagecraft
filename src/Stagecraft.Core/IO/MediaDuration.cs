using System.Text;

namespace Stagecraft.Core.IO;

/// <summary>
/// Reads the length of MP4-family files (MP4, M4V, MOV, M4A: the movie header) and WAV files from their headers,
/// without playing them. Other formats return null (the editor measures them when it plays them).
/// </summary>
public static class MediaDuration
{
    public static double? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".wav" => Wav(stream),
                ".mp4" or ".m4v" or ".mov" or ".m4a" or ".3gp" => Mp4(stream),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static uint U32(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        if (b.Length < 4) throw new EndOfStreamException();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static ulong U64(BinaryReader r) => (ulong)U32(r) << 32 | U32(r);

    /// <summary>Finds moov/mvhd: duration / timescale.</summary>
    private static double? Mp4(Stream s)
    {
        using var r = new BinaryReader(s, Encoding.ASCII, leaveOpen: true);
        return Boxes(r, 0, s.Length, depth: 0);
    }

    private static double? Boxes(BinaryReader r, long start, long end, int depth)
    {
        var pos = start;
        while (pos + 8 <= end && depth < 4)
        {
            r.BaseStream.Position = pos;
            long size = U32(r);
            var type = Encoding.ASCII.GetString(r.ReadBytes(4));
            var header = 8L;
            if (size == 1)
            {
                size = (long)U64(r);
                header = 16;
            }
            else if (size == 0) size = end - pos;
            if (size < header || pos + size > end) return null;
            if (type == "moov") return Boxes(r, pos + header, pos + size, depth + 1);
            if (type == "mvhd")
            {
                var version = r.ReadByte();
                r.ReadBytes(3); // flags
                if (version == 1)
                {
                    U64(r); // created
                    U64(r); // modified
                    var scale = U32(r);
                    var duration = U64(r);
                    return scale == 0 ? null : duration / (double)scale;
                }
                else
                {
                    U32(r);
                    U32(r);
                    var scale = U32(r);
                    var duration = U32(r);
                    return scale == 0 || duration == uint.MaxValue ? null : duration / (double)scale;
                }
            }
            pos += size;
        }
        return null;
    }

    /// <summary>RIFF/WAVE: the data chunk's size over the byte rate.</summary>
    private static double? Wav(Stream s)
    {
        using var r = new BinaryReader(s, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") return null;
        r.ReadUInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") return null;
        uint byteRate = 0;
        while (s.Position + 8 <= s.Length)
        {
            var id = Encoding.ASCII.GetString(r.ReadBytes(4));
            var size = r.ReadUInt32();
            var next = s.Position + size + (size % 2);
            if (id == "fmt " && size >= 12)
            {
                r.ReadUInt16(); // format
                r.ReadUInt16(); // channels
                r.ReadUInt32(); // sample rate
                byteRate = r.ReadUInt32();
            }
            else if (id == "data")
            {
                return byteRate == 0 ? null : size / (double)byteRate;
            }
            s.Position = next;
        }
        return null;
    }
}
