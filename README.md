# Fannyfique

Fannyfique is a Windows desktop app that finds duplicate RAW photo files across SD cards, PCs, external drives and NAS shares. Its main job is to answer one question: **can I safely wipe this SD card?**

- Scans any number of folders recursively for RAW files (`.CR2`, `.CR3`, `.NEF`, `.ARW`, `.RAF`, `.DNG`…).
- Identifies each photo by file name, capture date and camera body serial number, read from EXIF and MakerNotes metadata.
- Groups copies by shooting session (one folder = one batch), shows how much of each batch is copied elsewhere, and estimates how much disk space you would free by deleting the duplicates.
- Can match renamed files, such as the `recup_dir.N` folders left by PhotoRec after a card recovery, using the capture date and camera serial instead of the file name.
- Saves results to an index, so later scans only read new or changed files.
- **Read-only:** Fannyfique never copies, moves, modifies or deletes your photos. It only points you to the folders, and you clean up by hand.

The interface is in French.

## Tech stack

- C# / .NET 8: WPF app using the MVVM pattern (CommunityToolkit.Mvvm)
- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) for reading metadata
- Serilog for logging
- JSON persistence in `%LOCALAPPDATA%\Fannyfique`
- xUnit tests
- Inno Setup installer

```
src/Fannyfique.Core/        net8.0           scanning, metadata, index, duplicate analysis (no Windows dependency)
src/Fannyfique.App/         net8.0-windows   WPF views, view models, Windows integrations
tests/Fannyfique.Core.Tests/ net8.0          xUnit tests (run on Linux too)
tests/TestData/generate.sh                   demo folder tree generator
installer/fannyfique.iss                     Inno Setup script
```

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows to run the app. The whole solution, including the WPF project, also builds on Linux and macOS with `EnableWindowsTargeting=true`.
- Optional:
  - [Inno Setup 6](https://jrsoftware.org/isinfo.php), to build the installer
  - ImageMagick and exiftool, to generate the demo data

The `Dockerfile` / `docker-compose.yml` set up a Linux development container with all of the above, except Inno Setup.

## Build and run

```bash
dotnet build Fannyfique.sln
```

On Linux or macOS, also set `EnableWindowsTargeting` (it is already set in the dev container):

```bash
dotnet build Fannyfique.sln -p:EnableWindowsTargeting=true
```

To run the app on Windows:

```bash
dotnet run --project src/Fannyfique.App
```

## Test

```bash
dotnet test tests/Fannyfique.Core.Tests
```

The unit tests build their own minimal RAW files and need no external tools. The WPF app is tested by hand on Windows.

To try the app on realistic data, generate a demo folder tree that simulates an SD card, a PC, a NAS, an external drive and a recovered card:

```bash
tests/TestData/generate.sh                 # writes to tests/TestData/demo (git-ignored)
tests/TestData/generate.sh /some/other/dir
```

Then add the subfolders of `demo` as sources in the app. Check *“Fichiers renommés : ignorer les noms”* on the `recup` source.

## Package and deploy (Windows)

1. Publish a self-contained build, with the .NET runtime included:

   ```powershell
   dotnet publish src/Fannyfique.App -c Release -r win-x64 --self-contained -o publish
   ```

2. Build the installer with Inno Setup:

   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\fannyfique.iss
   ```

   This produces `installer/Output/Fannyfique-<version>-setup.exe`. The installer:
   - installs the app per user, without admin rights;
   - adds a Start menu shortcut.

   It is not code-signed, so Windows SmartScreen may warn on first launch. The version is set in `installer/fannyfique.iss` (`AppVersion`).

## Data and logs

Everything the app writes goes to `%LOCALAPPDATA%\Fannyfique`:

| File            | Content                                         |
| --------------- | ----------------------------------------------- |
| `fannyfique.json` | Sources and preferences                       |
| `index.json`    | Scan cache (metadata of every RAW file found)   |
| `logs\`         | Rolling log files (kept 7 days)                 |

To reset the app, delete this folder. Your photos are never touched.

## App icon

`src/Fannyfique.App/Assets/app.ico` is generated from `icon.svg` and `icon-small.svg`:

```bash
cd "$(mktemp -d)" && npm i @resvg/resvg-js@2.6.2 png-to-ico@2.1.8
cp /path/to/src/Fannyfique.App/Assets/generate-icon.mjs .
node generate-icon.mjs /path/to/src/Fannyfique.App/Assets/icon.svg .
cp app.ico /path/to/src/Fannyfique.App/Assets/
```
