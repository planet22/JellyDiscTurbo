# Disc Stub Hider (JellyDiscTurbo)

A Jellyfin plugin that automatically hides `.disc` stub placeholders once a real video file is available for that title, and restores them again if the video is removed.

## The problem

Jellyfin uses `.disc` stub files to represent physical DVD/Blu-ray entries in your library when you don't have a digital copy on hand. If you later add the actual video (a ripped file, a remux, or a `.strm` pointing at one), Jellyfin can end up showing both the disc placeholder and the playable video side by side. Disc Stub Hider removes that duplication automatically.

## What it does

For any library folder that contains both a `.disc` stub and a video file, the plugin renames the stub to `<name>.disc.bak` so Jellyfin stops treating it as a disc placeholder and the real video takes over. If the video is later deleted, the `.disc.bak` file is renamed back to `.disc`, restoring the placeholder.

Recognized video extensions: `.strm`, `.mkv`, `.mp4`, `.avi`, `.mov`, `.wmv`.

Detection runs three independent ways, each toggleable in the plugin settings:

- **Library events** — reacts to Jellyfin's `ItemAdded` / `ItemRemoved` / `ItemUpdated` events as your library is scanned.
- **Folder watching** — uses a `FileSystemWatcher` on affected folders to react immediately to files being added, removed, or renamed on disk (e.g. while a season pack is being extracted), independent of a library scan.
- **Scheduled task** — a "Scan and Hide Disc Stubs" task (found under Dashboard → Scheduled Tasks) that sweeps your whole library. Runs weekly on Sundays at 2 AM by default, and can also be triggered manually at any time.

Filesystem changes are debounced (750ms) and processed per-directory so bursts of events during an extraction or import don't cause repeated or overlapping work.

## Installation

### Via plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**.
2. Add a new repository with this manifest URL:
   ```
   https://github.com/planet22/JellyDiscTurbo/raw/main/manifest.json
   ```
3. Go to **Dashboard → Plugins → Catalog**, find **DiscStubHider** under the General category, and click install.
4. Restart Jellyfin when prompted.

### Manual install

1. Download the latest release `.zip` from the [Releases](https://github.com/planet22/JellyDiscTurbo/releases) page.
2. Extract it into a new `DiscStubHider` folder under your Jellyfin data directory's `plugins` folder, e.g.:
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\DiscStubHider\`
   - Linux: `/var/lib/jellyfin/plugins/DiscStubHider/`
   - Docker: the `plugins` volume you have mapped into the container
3. Restart Jellyfin.

## Configuration

After installing, go to **Dashboard → Plugins → Disc Stub Hider** to configure:

| Setting | Description |
|---|---|
| **Respond to library events** | Evaluate a folder whenever Jellyfin adds, removes, or updates an item in it during scans. |
| **Watch folders (FileSystemWatcher)** | Watch folders directly on disk for file changes, independent of library scans. |
| **Handling mode** | `Rename to .disc.bak` (default) to enable automatic hide/restore behavior, or `Ignore / do nothing` to disable all processing without uninstalling the plugin. |

Both triggers can be enabled together, used individually, or turned off in favor of relying solely on the scheduled task.

## Requirements

- Jellyfin server 10.11 / ABI `12.0.0.0` or compatible.
- .NET 10 runtime (bundled with compatible Jellyfin server builds).

## Building from source

```
dotnet build Jellyfin.Plugin.DiscStubHider/Jellyfin.Plugin.DiscStubHider.csproj -c Release
```

The built `Jellyfin.Plugin.DiscStubHider.dll` can be copied into your Jellyfin `plugins/DiscStubHider/` folder as described above.
