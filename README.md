# MrLurix App Updater

Windows (WPF) app that keeps your installed programs up to date using winget.

## Features

- Runs with administrator privileges for automatic installs
- Finds and installs pending winget updates
- Optional scheduled download window (e.g. only at night)
- Optional automatic shutdown after updates finish
- Self-update via GitHub releases
- System tray icon, single-instance guard

## Use

1. Download `MrLurixAppUpdater.exe` from the [Releases](https://github.com/mrlurix/MrLurixAppUpdater/releases) page.
2. Run it and allow the administrator prompt.
3. Click **Check for updates** and then **Update all**.

Optional: place `update-config.json` next to the exe with `{"updateUrl": "https://example.com/manifest.json"}` to point self-update at your own server.

## Build

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o publish
```