using System.Globalization;
using System.Text;

namespace Fannyfique.Core.Tests.Support;

/// <summary>
/// Écrit un faux RAW : un TIFF minimal avec les balises EXIF utiles à l'identité. MetadataExtractor
/// le lit comme les RAW basés sur TIFF (CR2, NEF, ARW, DNG…).
/// </summary>
public static class TestRaw
{
    public static void Write(
        string path,
        DateTime? date = null,
        string? serial = "012345678901",
        string? make = "Canon",
        string? model = "Canon EOS R6",
        string? subSeconds = null,
        int padding = 1000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var exif = new List<Entry>();
        if (date is { } d)
        {
            exif.Add(Ascii(0x9003, d.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        if (subSeconds is not null)
        {
            exif.Add(Ascii(0x9291, subSeconds));
        }

        if (serial is not null)
        {
            exif.Add(Ascii(0xA431, serial));
        }

        var ifd0 = new List<Entry>();
        if (make is not null)
        {
            ifd0.Add(Ascii(0x010F, make));
        }

        if (model is not null)
        {
            ifd0.Add(Ascii(0x0110, model));
        }

        // Deux passes : la position de l'IFD EXIF dépend de la taille de l'IFD0.
        ifd0.Add(Long(0x8769, 0));
        var ifd0Length = Ifd(ifd0, 8).Length;
        ifd0[^1] = Long(0x8769, (uint)(8 + ifd0Length));

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("II"u8);
        writer.Write((ushort)42);
        writer.Write(8u);
        writer.Write(Ifd(ifd0, 8));
        writer.Write(Ifd(exif, (uint)(8 + ifd0Length)));
        writer.Write(new byte[padding]);
    }

    /// <summary>Fichier au format non reconnu (identité faible).</summary>
    public static void WriteGarbage(string path, int size = 500)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x42, size).ToArray());
    }

    private static byte[] Ifd(List<Entry> entries, uint offset)
    {
        var sorted = entries.OrderBy(e => e.Tag).ToList();
        var dataStart = offset + 2 + (12 * (uint)sorted.Count) + 4;
        using var ifd = new MemoryStream();
        using var data = new MemoryStream();
        using var w = new BinaryWriter(ifd);
        w.Write((ushort)sorted.Count);
        foreach (var e in sorted)
        {
            w.Write(e.Tag);
            w.Write(e.Type);
            w.Write(e.Count);
            if (e.Data.Length <= 4)
            {
                w.Write(e.Data);
                w.Write(new byte[4 - e.Data.Length]);
            }
            else
            {
                w.Write((uint)(dataStart + data.Length));
                data.Write(e.Data);
                if (data.Length % 2 == 1)
                {
                    data.WriteByte(0);
                }
            }
        }

        w.Write(0u);
        w.Write(data.ToArray());
        return ifd.ToArray();
    }

    private static Entry Ascii(ushort tag, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        return new Entry(tag, 2, (uint)bytes.Length, bytes);
    }

    private static Entry Long(ushort tag, uint value) => new(tag, 4, 1, BitConverter.GetBytes(value));

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Data);
}
