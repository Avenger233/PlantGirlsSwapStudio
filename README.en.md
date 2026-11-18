# 植物娘大战僵尸 HD · Asset Swapper

> A resource **replace / swap / watch / mod** tool with live preview.
> Pure file-level operations — no exe patching, no process injection, one-click restore.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![MonoGame](https://img.shields.io/badge/MonoGame-3.8.1-E73C00)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![License](https://img.shields.io/badge/license-MIT-green)

[中文说明](README.md) · [Releases](../../releases)

---

## Disclaimer

- This repository contains **no game assets**, game binaries or decompiled code. You need your own copy of the game.
- The tool only reads and writes standalone asset files under the game's `Content` directory. It does not patch executables or save files.
- Not affiliated with PopCap Games / EA or the fangame's authors.
- A full snapshot of the original files is taken before the first modification, so everything can be restored.

## What it solves

This fangame keeps its assets unpacked under `Content`, but swapping a single plant's look means hunting down dozens of files across six directories and hoping the names line up. This tool turns that into two dropdowns.

It can also **render the game's own animations**:

- Spine rigs (`spanim/*.skel`) in real time, crisp at any zoom
- Classic frame animations (`reanim/*.xnb`), including accessories (cone / bucket / flag / screen door / ducky tube)
- So you can see the result before touching anything

## Modes

| Mode | Writes files | What it does |
| --- | --- | --- |
| **Replace** | one-way copy | Copy plant A's assets onto plant B |
| **Swap** | two-way | Exchange A's and B's assets |
| **Watch** | no | Read-only preview: Spine rigs, reanim plants and zombies |
| **Mod** | yes | Export a plant's assets, edit the textures, import them back |

Each mode remembers its own selection and preview state.

## Download

| File | Size | .NET 8 required |
| --- | --- | --- |
| `PlantGirlsSwapStudio_full.exe` | ~83 MB | **No** — runtime is bundled |
| `PlantGirlsSwapStudio_lite.exe` | ~9 MB | **Yes** — [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

Both are functionally identical. Drop the downloaded file into the game root (next to `Lawn.exe`) and run it.

The `lite` build shows a "must install .NET Desktop Runtime 8.0" dialog with a download link if the runtime is missing.

## Requirements

- Windows 10/11 x64
- Your own copy of the game
- Building from source additionally needs the Spine 4.2 runtime compiled locally (see [docs/BUILD.md](docs/BUILD.md))

## Generated files

Everything the tool creates lives in `<game>\资源修改器\`; deleting that folder does not affect the game.

```
资源修改器/
├── dll/                  extracted SDL2.dll / soft_oal.dll
├── Language/             editable name tables (plants / zombies / others / actions.txt)
├── 备份/                 full snapshot taken before the first modification
├── Mod/                  exported works
├── 资源修改器.ini         settings (hand-editable)
├── app.ico / icon.png    window icon
└── crash-*.txt           only if the program crashes
```

The four files under `Language/` are generated on first launch with the game's built-in Chinese names already filled in; empty values fall back to the built-in name.

## Build from source

```powershell
git clone https://github.com/Avenger233/PlantGirlsSwapStudio
cd PlantGirlsSwapStudio
# compile spine-runtimes 4.2 first, see docs/BUILD.md
$env:SPINE_MONOGAME_DIR = "<path to netstandard2.0 output>"
dotnet build PlantGirlsSwapStudio.sln -c Release
```

Release builds: see [docs/BUILD.md](docs/BUILD.md) §4.

## Documentation

All documentation is in Chinese:

- [Build & release](docs/BUILD.md)
- [Architecture](docs/ARCHITECTURE.md)
- [reanim (.xnb) format](docs/REANIM-FORMAT.md)
- [Spine rendering notes](docs/SPINE-RENDERING.md)
- [FAQ](docs/FAQ.md)

## Known limitations

- Windows only
- Some plants' reanim files contain placeholder tracks only (Caltrop, Chomper, ZombiesWon, menu_crow); their preview is empty
- Spine runtimes older than 4.2 cannot read these assets

## License

[MIT](LICENSE) © 2026 Avenger233

Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

[![](https://img.shields.io/badge/powered_by-dsh-4D6BFE?style=flat-square&logo=deepseek&logoColor=white)](https://github.com/deepseek-ai/deepseek-harness)
