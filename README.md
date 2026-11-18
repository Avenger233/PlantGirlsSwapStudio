# 植物娘大战僵尸 HD · 资源修改器

> 一个带**实时预览**的资源替换 / 互换 / 观赏 / Mod 工具。
> 纯文件级操作，不改 exe、不注入进程，随时可一键还原。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![MonoGame](https://img.shields.io/badge/MonoGame-3.8.1-E73C00)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![License](https://img.shields.io/badge/license-MIT-green)

[English](README.en.md)

<!-- 建议放一张界面截图：screenshots/main.png，然后取消下一行注释 -->
<!-- ![界面预览](screenshots/main.png) -->

---

## 免责声明

- 本仓库**不包含**任何游戏资源、游戏程序集或反编译代码。使用前请自备《植物娘大战僵尸 HD》游戏本体。
- 本工具只读写游戏 Content 目录下的**独立资源文件**，不修改可执行文件、不修改存档。
- 与 PopCap Games / EA 以及本 Mod 的作者无隶属关系。请自行确认你的使用场景合规。
- 首次改动文件前会自动做全量备份，出问题可以一键还原。

## 它解决什么问题

这类 Mod 游戏把资源摊开放在 Content 目录里，但想换一只植物的外观时，你得手工找到散落在 6 个目录里的几十个文件、还要担心名字对不上。本工具把这件事变成两次下拉选择。

同时它还能**直接渲染游戏里的原始动画**：

- Spine 立绘（spanim/*.skel）实时渲染，任意缩放不糊
- 原版帧动画（reanim/*.xnb）逐帧还原，连配件（路障 / 铁桶 / 旗子 / 纱门 / 鸭子圈）都能单独挂上
- 所以你在动手改之前就能看到「换成这样会是什么效果」

## 四种模式

| 模式 | 改文件 | 作用 |
| --- | --- | --- |
| **替换** | 单向覆盖 | 把 A 的资源复制给 B（B 变成 A 的样子） |
| **互换** | 双向对调 | A、B 的资源互相对调（两边都变成对方） |
| **观赏** | 否 | 只看不动：Spine 立绘动作、原版植物和僵尸的帧动画 |
| **Mod** | 是 | 导出植物资源去改贴图 / 收别人的作品再导回来 |

四个模式的预览、选择、缩放比例各自独立，互不影响；每个模式的选项都会记住。

### 替换 / 互换

- 顶栏选植物 A、植物 B；每个下拉里自带筛选框（只过滤自己那份列表），可只看植物
- 勾选参与操作的资源目录：spanim / reanim / particles / sounds / voices / images
- 「预览配对」先列出会动哪些文件，确认无误再执行
- 「强制替换」：名称对不上的文件按扩展名顺序硬配，用于两边部件数量不一样的植物（只覆盖 B 自己的文件，不新增文件）
- 双向对调时，双方没有对方文件的会跳过并在日志里列出

### 观赏

- **范围**下拉切换 植物 / 僵尸 / 全部
- 植物走 Spine 立绘，选动作循环播放（动作名带中文，如「待机 (idle)」）
- 僵尸走 reanim 帧动画，可选 44 种僵尸、单独开某条动作轨道、挂单套配件
- 完全只读，一个字节都不改

### Mod

- 「导出为作品」把植物 A 在勾选目录里的文件按 Content 相对路径导出到 资源修改器/Mod/，并写一份 mod.json
- 用任意图像编辑器改里面的 png
- 「导入选中作品」按原路径写回游戏（导入前自动备份）

## 系统要求

- Windows 10/11 x64
- 一份《植物娘大战僵尸 HD》

Release 里有两个版本，功能完全一样，按需选一个：

| 文件 | 大小 | 要不要装 .NET 8 |
| --- | --- | --- |
| `PlantGirlsSwapStudio_full.exe` | 约 83 MB | **不用**，运行时已经打包进去 |
| `PlantGirlsSwapStudio_lite.exe` | 约 9 MB | **要**，需要 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

`lite` 版没装运行时会弹「必须安装 .NET Desktop Runtime 8.0」并给下载链接，不会静默闪退。

## 快速开始

### 用打包好的版本

从 [Releases](../../releases) 下载 `PlantGirlsSwapStudio_full.exe`（或 `_lite.exe`），**改名为 `资源修改器.exe`**
后放到游戏目录（和 Lawn.exe 同级）双击运行。首次启动会自动识别游戏目录；识别不到就在顶栏手填。

也支持便携放置：exe 放在哪个目录，就把那个目录当作游戏目录。

### 自己编译

见 [docs/BUILD.md](docs/BUILD.md)。简单说：

```powershell
git clone <this-repo>
cd PlantGirlsSwapStudio
# 先把 Spine 4.2 运行时编译出来，见 docs/BUILD.md
$env:SPINE_MONOGAME_DIR = "<spine-monogame 编译输出目录>"
dotnet build PlantGirlsSwapStudio.sln -c Release
```

单文件发布（两个版本）：

```powershell
# 完整版：自带运行时，约 83 MB
dotnet publish src/PlantSwapStudio/PlantSwapStudio.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish_full

# 精简版：需要 .NET 8 Desktop Runtime，约 9 MB
dotnet publish src/PlantSwapStudio/PlantSwapStudio.csproj -c Release -r win-x64 `
  --no-self-contained -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish_lite
```

细节（包括每次发布前要 `rm -rf` 输出目录）见 [docs/BUILD.md](docs/BUILD.md)。

## 生成的目录

所有产物集中在 `<游戏目录>\资源修改器\`，删掉这个目录不影响游戏：

```
资源修改器/
├── dll/                 运行时解压出来的 SDL2.dll / soft_oal.dll
├── Language/            中文名表，可手改（plants / zombies / others / actions.txt）
├── 备份/                首次改动前的全量快照 + manifest.json
├── Mod/                 导出的作品
├── 资源修改器.ini        配置（可手改，记录了每个模式的选择）
├── app.ico / icon.png   窗口图标
└── crash-*.txt          只在崩溃时出现
```

`Language/` 下的四个文件在启动时自动生成，里面**已经填好游戏自带的中文名**，
想改名直接改、想补名字留空的行填上即可，详见 [docs/FAQ.md](docs/FAQ.md)。

「还原原始」把文件恢复到最初快照；「重建原始备份」以当前状态重新拍一份（确认现在是原版再点）。

## 命令行

Core 也可以单独当命令行用，适合批量操作：

```bash
dotnet run --project src/PlantSwap.Core -- --plants --game "D:/Games/PlantGirlsVsZombies"
dotnet run --project src/PlantSwap.Core -- swap agave blover --game "..."          # 只预览
dotnet run --project src/PlantSwap.Core -- swap agave blover --apply --game "..."  # 执行
```

完整参数见 [src/PlantSwap.Core/API.md](src/PlantSwap.Core/API.md)。

## 仓库结构

```
src/PlantSwap.Core/     资源扫描、配对、备份/还原、reanim 解析（无 UI 依赖）
src/PlantSwapStudio/    MonoGame + ImGui 界面，Spine / reanim 实时预览
tools/DumpAnims/        导出全部 Spine 动作名（维护中文动作表用）
tools/ReanimDump/       导出 / 校验 reanim 数据
docs/                   构建、架构、格式说明
```

## 文档

- [构建与发布](docs/BUILD.md)
- [架构说明](docs/ARCHITECTURE.md)
- [reanim (.xnb) 格式](docs/REANIM-FORMAT.md)
- [Spine 立绘渲染要点](docs/SPINE-RENDERING.md)
- [常见问题](docs/FAQ.md)
- [发布到 GitHub](docs/PUBLISHING.md)

## 已知限制

- 只有 Windows 版
- 部分植物的 reanim 只含占位轨道（如 Caltrop、Chomper、ZombiesWon、menu_crow），预览会是空的
- 观赏模式的僵尸列表是「僵尸种类」数，不是 reanim 文件数（同种的多个动作文件会归并成一条）
- 4.2 以下的 Spine 运行时读不了这批资源，会抛异常

## 贡献

见 [CONTRIBUTING.md](CONTRIBUTING.md)。中文动作表、植物别名、僵尸名表都欢迎补。

## License

[MIT](LICENSE) © 2026 Avenger233

第三方组件许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

---

[![](https://img.shields.io/badge/powered_by-dsh-4D6BFE?style=flat-square&logo=deepseek&logoColor=white)](https://github.com/deepseek-ai/deepseek-harness)
