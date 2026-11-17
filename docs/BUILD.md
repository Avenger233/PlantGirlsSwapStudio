# 构建与发布

## 环境

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 0. 原生依赖

`src/PlantSwapStudio/native/` 下两个 DLL 会以 EmbeddedResource 形式打进单文件：

- `SDL2.dll` —— **2.30.11**（不是 MonoGame 官方包里的 2.0.20）
  游戏目录/官方包里的 2.0.20 在 Windows 上对中文输入法支持不完整，必须用新版；
  `PrepEnvironment()` 会优先加载资源里这份，其次才回退到 exe 同目录的 dll。
- `soft_oal.dll` —— OpenAL Soft（音效，本工具其实没用到，保留以便 MonoGame 初始化）

所有运行期文件都生成在 **`<游戏目录>/资源修改器/`**（exe 同级）：

```
资源修改器/
├── native/SDL2.dll, soft_oal.dll   ← 运行时解压
├── app.ico / icon.png              ← 窗口图标
├── 备份/                            ← 资源原始快照
├── Mod/                            ← 导出的作品
└── 资源修改器.ini                   ← 界面配置
```

只有在 exe 目录**不可写**（例如装在 Program Files 且无权限）时才退回 `%APPDATA%\PvZPlantSwap`。
游戏目录里原本的 `SDL2.dll` 不会被覆盖；首次启动会把 `%APPDATA%` 里的旧配置搬过来。

调试开关：设环境变量 `PLANTSWAP_DEBUG=1` 才会写 `ime-debug.txt`，普通用户目录里不会出现调试文件。
配置只有 `资源修改器.ini` 一个来源（早期版本还写过只存游戏目录的 `studio.json`，已废弃并在启动时清理）。

## 1. 准备 Spine 运行时

游戏里的骨骼是 **Spine 4.2** 导出的（资源版本 4.2.43），必须用 4.2 的 C# 运行时。
版本不对会在 `SkeletonBinary.ReadSkeletonData` 里直接抛异常（旧版常表现为 IndexOutOfRange）。

Spine 运行时**不随本仓库分发**（许可证不允许），需要自己编译：

```powershell
git clone --branch 4.2 https://github.com/EsotericSoftware/spine-runtimes.git
cd spine-runtimes/spine-monogame/spine-monogame
dotnet build -c Release
```

产物在：

```
spine-runtimes/spine-monogame/spine-monogame/bin/Release/netstandard2.0/spine-monogame.dll
```

> 该工程只含 spine-monogame 和 spine-csharp 两个目录也能编译，仓库体积大可以只拷这两个目录。

## 2. 让 MSBuild 找到它

三选一，优先级从高到低：

```powershell
# a) 命令行直接指定
dotnet build PlantGirlsSwapStudio.sln -c Release -p:SpineMonogamePath="D:\path\to\spine-monogame.dll"

# b) 环境变量（推荐，一次配好）
setx SPINE_MONOGAME_DIR "D:\path\to\netstandard2.0"
# 新开一个终端生效

# c) 放进仓库里的 third_party/，会自动探测
#    third_party/spine-runtimes/spine-monogame/spine-monogame/bin/Release/netstandard2.0/spine-monogame.dll
```

找不到时构建会明确报错并提示上面三种方式。

> third_party/ 已在 .gitignore 里如果不需要提交可以忽略；若你想让 CI 也能构建，
> 可以在 CI 脚本里 clone 并编译 Spine（注意遵循其许可证，不要把 DLL 提交上来）。

## 3. 构建

```powershell
dotnet build PlantGirlsSwapStudio.sln -c Release
```

产物：

| 项目 | 输出 |
| --- | --- |
| src/PlantSwap.Core | `PlantSwapCore.dll` + 可执行的命令行入口 |
| src/PlantSwapStudio | `资源修改器.dll`（MonoGame 桌面程序） |
| tools/DumpAnims | 导出 Spine 动作名的小工具 |
| tools/ReanimDump | 导出 / 校验 reanim 的小工具 |

## 4. 单文件发布

```powershell
dotnet publish src/PlantSwapStudio/PlantSwapStudio.csproj `
  -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

得到 `publish/资源修改器.exe`（约 86 MB，自带运行时，免安装）。

**为什么还要 `IncludeNativeLibrariesForSelfExtract`**：MonoGame 依赖 SDL2 和 OpenAL，
单文件发布时这两个原生 DLL 必须一起打进去，否则双击闪退。
`native/SDL2.dll` 和 `native/soft_oal.dll` 以 EmbeddedResource 形式打包，运行时解压到临时目录。

> **注意**：重复往同一个 `publish/` 目录发布会得到 9.7 MB 的**框架依赖**版（缺运行时，双击闪退）。
> 每次发布前先 `rm -rf publish`；正确产物约 86 MB。

## 5. 部署

```powershell
# 先退出正在运行的实例，否则覆盖会失败（Windows 文件占用）
Get-Process 资源修改器 -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item publish\资源修改器.exe "<游戏目录>\资源修改器.exe" -Force
```

也可以直接把 exe 放在任意目录运行 —— 程序会先看 exe 所在目录有没有 `Content`，有就当游戏目录用（便携模式）。

## 常见构建问题

| 现象 | 原因 |
| --- | --- |
| `找不到类型或命名空间名 Spine` | 没配 SPINE_MONOGAME_DIR，见第 2 步 |
| `找不到类型或命名空间名 ImGuiRenderer` | MonoGame.ImGuiNet 是裸 DLL 包，需要 `dotnet restore` 先把它下载到本地 NuGet 缓存 |
| 双击闪退、无窗口 | 单文件发布漏了 `-p:IncludeNativeLibrariesForSelfExtract=true` |
| 图标没生效 | `<ApplicationIcon>` 留空会静默不嵌入；本工程已指定 app.ico |
