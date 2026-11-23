using System.Runtime.InteropServices;
using System.Text;
using Fannyfique.Core.Platform;

namespace Fannyfique.App.Services;

/// <summary>Volumes Windows : lecteurs amovibles, nom et numéro de série (CLAUDE.md 4.2).</summary>
public sealed class WindowsVolumeInfoProvider : IVolumeInfoProvider
{
    public IReadOnlyList<VolumeInfo> GetRemovableVolumes() => DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.Removable)
        .Select(ToVolume)
        .OfType<VolumeInfo>()
        .ToList();

    public VolumeInfo? GetVolume(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null; // chemin réseau
        }

        return ToVolume(new DriveInfo(root));
    }

    private static VolumeInfo? ToVolume(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return null;
            }

            return new VolumeInfo(drive.RootDirectory.FullName, drive.VolumeLabel, Serial(drive.RootDirectory.FullName), drive.DriveType == DriveType.Removable);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Serial(string root)
    {
        var name = new StringBuilder(261);
        var fileSystem = new StringBuilder(261);
        return GetVolumeInformation(root, name, name.Capacity, out var serial, out _, out _, fileSystem, fileSystem.Capacity)
            ? $"{serial >> 16:X4}-{serial & 0xFFFF:X4}"
            : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        int fileSystemNameSize);
}
