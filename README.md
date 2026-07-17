<div align="center">

# 🔮 ObsidianDisk

**A modern visual disk space manager for Windows**

Find out what's eating your disk, explore it in a real-time interactive treemap,
hunt down huge files and duplicates, and free up space — all in a single app.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-7C5CFF?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Windows-3B82F6?logo=windows&logoColor=white)](#)
[![MIT License](https://img.shields.io/badge/License-MIT-22C55E)](LICENSE)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-14B8A6?logo=windows11&logoColor=white)](#)

<img src="docs/screenshot-overview.png" alt="ObsidianDisk — Overview" width="850"/>

</div>

---

## ✨ Features

- **🏠 Overview** — dashboard with disk usage, semantic space blocks (Programs, Games, Windows, Users, Temp Files…) and per-category breakdown
- **🗺️ Space Map** — SpaceSniffer-style *squarified* treemap that builds **live while scanning**; double-click to drill down, breadcrumb navigation, hover tooltips, filters by category and by old files
- **📄 Large Files** — the biggest files on your disk, filterable by minimum size, with in-app deletion
- **👯 Duplicates** — 3-stage detection (size → partial hash → full SHA-256); keeps the most recent copy and shows how much space you can reclaim
- **🧹 Cleanup** — one click to clear user Temp, Windows Temp, Windows Update cache, thumbnail cache, error reports and the Recycle Bin
- **📈 History** — every scan becomes a persistent record: evolution chart, statistics, growth trend, disk-full projection and CSV export
- **🗑️ Safe deletion** — to the Recycle Bin (undoable) or permanent, always with a clear confirmation
- **🌙 Modern UI** — full dark theme, borderless window with custom title bar, shipped as a single dependency-free `.exe`

## 📥 Download

Grab the latest `.exe` from the [**Releases**](../../releases) page — no installation required (not even .NET): it's a single self-contained executable.

> 💡 Tip: `ObsidianDisk.exe "C:\some\folder"` opens the app already scanning that path.

## 🔧 Build from source

Requirements: [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
git clone https://github.com/oPaozinh0/ObsidianDisk.git
cd ObsidianDisk
dotnet publish -c Release
# output: bin\Release\net8.0-windows\win-x64\publish\ObsidianDisk.exe
```

## 🏗️ Architecture

| Component | Role |
|---|---|
| `Services/DiskScanner` | Parallel scan with atomic size propagation (enables live rendering) |
| `Controls/TreemapControl` | *Squarified* layout + direct rendering via `OnRender` |
| `Services/DuplicateFinder` | 3-stage duplicate detection with parallel hashing |
| `Services/TempCleaner` | Measures and cleans known Windows temp locations |
| `Services/SemanticGrouper` | Classifies disk folders into semantic blocks |
| `Views/*` | Dashboard pages (WPF, custom dark theme) |

## 🤝 Contributing

Issues and pull requests are welcome! Found a bug or have an idea? [Open an issue](../../issues).

## ☕ Support the project

If ObsidianDisk helped you reclaim a few gigabytes, consider buying me a coffee:

<a href="https://www.buymeacoffee.com/oPaozinh0" target="_blank">
  <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="50"/>
</a>

## 📄 License

Distributed under the [MIT](LICENSE) license.
