# Every Store

An app store for the Every apps (EveryCopy, Every Scanner, EverySearch, EveryDownload, EveryDns, Every Share, Every Proxy). Pick an app, press **Install**, and the store downloads the latest release from GitHub and installs it.

Built with WinUI 3 (Windows App SDK) on .NET 8, in the same style as the other Every apps. There is no server: GitHub Releases and Issues are the backend.

## Features

- **Install, update, open, uninstall.** One button downloads the latest GitHub release, checks its SHA-256 against GitHub's checksum, and runs the installer silently. Installed apps are found through Windows' own uninstall list, so apps installed outside the store show up too.
- **Download only** saves the installer to your Downloads folder without installing it.
- **Library** lists installed apps and offers **Update all**.
- **App page** shows the latest version, last released date, size, downloads, rating and developer, with tabs for Overview (README), What's new (release notes), Versions (every release) and Bugs & feedback.
- **Bug reports and feedback** go to the app's GitHub issues, as `[Bug]` and `[Feedback]` issues. Feedback includes a 1-5 star rating, and the store averages those into the app's rating.
- Works offline from saved data, light and dark themes.

## Bug reports and feedback

Without a token, **Report a bug** and **Send feedback** open GitHub's new-issue page with the report filled in, and you press Submit there. With a fine-grained GitHub token (Settings, Issues read and write) the store posts the report itself. The token is stored encrypted for your Windows account (DPAPI). A token also lifts GitHub's anonymous limit of 60 requests an hour to 5,000.

## Adding an app

Edit `catalog.json` (next to the exe), or add `%AppData%\EveryStore\catalog.json` to list an app without rebuilding. Fields:

| Field | Meaning |
|---|---|
| `id`, `name`, `tagline`, `category` | What the card shows |
| `repo` | `owner/name` on GitHub |
| `uninstallId` | The Inno Setup `AppId` GUID, without braces. This is how the store detects the install |
| `exe` | The app's exe name, used by **Open** |
| `assetMatch` | Text in the installer's file name (default `Setup`) |
| `iconPath` | Icon path inside the repository |
| `kind` | `installer` (default) or `extension` (link only) |

An app needs a GitHub release with a `...Setup...exe` attached, built with Inno Setup and installable without admin rights (`PrivilegesRequired=lowest`). Every Proxy is a browser extension with a manual setup, so it only links to its repository.

## Build

```
dotnet build EveryStore.csproj -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File build-installer.ps1   # EveryStore-Setup-<version>.exe (needs Inno Setup 6)
dotnet test tests\EveryStore.Tests
```

To publish the store itself, attach `installer\Output\EveryStore-Setup-1.0.1.exe` to a GitHub release of this repository.
