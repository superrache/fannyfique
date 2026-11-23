using Fannyfique.Core.Storage;
using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Directory = MetadataExtractor.Directory;

namespace Fannyfique.Core.Metadata;

/// <summary>Lecture des métadonnées avec MetadataExtractor.</summary>
public sealed class MetadataExtractorReader : IRawMetadataReader
{
    public RawMetadata Read(string path)
    {
        IReadOnlyList<Directory> directories;
        using (var stream = SafeFile.OpenRead(path))
        {
            try
            {
                directories = ImageMetadataReader.ReadMetadata(stream);
            }
            catch (ImageProcessingException ex)
            {
                throw new RawFormatException(ex.Message, ex);
            }
        }

        return Extract(directories);
    }

    internal static RawMetadata Extract(IReadOnlyList<Directory> directories)
    {
        var exif = directories.OfType<ExifDirectoryBase>().ToList();

        DateTime? dateTaken = null;
        foreach (var dir in exif)
        {
            if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var date))
            {
                dateTaken = AddSubSeconds(date, dir.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal));
                break;
            }
        }

        var make = FirstString(exif, ExifDirectoryBase.TagMake);
        var model = FirstString(exif, ExifDirectoryBase.TagModel);
        var serial = FirstString(exif, ExifDirectoryBase.TagBodySerialNumber) ?? MakerNoteSerial(directories);

        return new RawMetadata(dateTaken, serial, make, model);
    }

    private static string? FirstString(IEnumerable<Directory> directories, int tag) =>
        directories.Select(d => Clean(d.GetString(tag))).FirstOrDefault(s => s is not null);

    /// <summary>Numéro de série du boîtier dans les MakerNotes (Canon, Nikon, Fujifilm, Olympus, Pentax…).</summary>
    private static string? MakerNoteSerial(IEnumerable<Directory> directories)
    {
        foreach (var dir in directories)
        {
            foreach (var tag in dir.Tags)
            {
                if (tag.Name.Contains("Serial", StringComparison.OrdinalIgnoreCase)
                    && !tag.Name.Contains("Lens", StringComparison.OrdinalIgnoreCase))
                {
                    var value = Clean(dir.GetString(tag.Type)) ?? Clean(tag.Description);
                    if (value is not null)
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    private static string? Clean(string? value)
    {
        value = value?.Trim().Trim('\0').Trim();
        return string.IsNullOrEmpty(value) || value.All(c => c == '0') ? null : value;
    }

    private static DateTime AddSubSeconds(DateTime date, string? subSeconds)
    {
        var digits = new string((subSeconds ?? "").Trim().TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return date;
        }

        var fraction = double.Parse("0." + digits, CultureInfo.InvariantCulture);
        return date.AddMilliseconds(Math.Floor(fraction * 1000));
    }
}
