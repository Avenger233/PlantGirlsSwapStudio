# PlantSwapCore — 对外 API（供 UI 调用）

命名空间：`PvZPlantSwap.Core`（程序集 `PlantSwapCore`，目标 `net8.0`，`Nullable=enable`）

行为与 `pgvz_assets/qt_app/core.py` 一一对应，已用真实游戏 Content 做逐项比对（plants 76、keys 501、
中文名 648、agave<->sunflower 7 对，全部一致）。

UI 侧引用方式：

```xml
<ProjectReference Include="..\Core\PlantSwapCore.csproj" />
```

```csharp
using PvZPlantSwap.Core;
```

## 1. 静态类 PlantSwap（扫描 / 解析 / 配对）

```csharp
public const string DefaultGame;                      // 空字符串：游戏目录由调用方指定或 GuessGame() 探测
public static string GuessGame();                     // 从 exe / 当前目录向上找 Content，找不到返回 ""
public static readonly string[] Folders;              // spanim, reanim, particles, sounds, voices, images
public static readonly string[] Exts;                 // .xnb .atlas .skel .json .png .ogg .old .meta
public static readonly string[] NonPlant;             // 非植物关键字

public static string DataDir { get; }                 // %APPDATA%\PvZPlantSwap
public static string? BackupRootOverride { get; set; }// 非空时覆盖备份根目录（测试用）
public static string BackupRoot { get; }              // 默认 DataDir\backup
public static string PreviewDir { get; }              // DataDir\preview

public static string Norm(string s);
public static string StripExt(string name);
public static (string Key, string Rem) Parse(string folder, string name);
public static AssetTable Scan(string content);
public static List<FamilyEntry> Family(AssetTable table, string folder, string plant);
public static (List<AssetPair> Pairs, List<string> Missing) PairFamily(
    IReadOnlyList<FamilyEntry> fa, IReadOnlyList<FamilyEntry> fb);
public static ResolveResult Resolve(AssetTable table, IReadOnlyList<string> folders, string a, string b);
public static string? FindContent(string gameDir);
public static BuildListsResult BuildLists(string content, IReadOnlyDictionary<string, string> names);
```

## 2. 静态类 Swapper（备份 / 互换 / 还原）

```csharp
public static string Rel(string path, string content);
public static void CopyFile(string src, string dst);                                   // 等价 shutil.copy2
public static string DoBackup(IReadOnlyList<AssetPair> pairs, string content);          // 返回备份目录
public static string DoSwap(IReadOnlyList<AssetPair> pairs, string content, string mode); // mode: "swap" | "replace"
public static int DoRestore(string backupDir, string content);                          // 返回还原的配对数
```

备份目录结构：`<BackupRoot>/<yyyyMMdd_HHmmss>/<相对 content 的路径>` + `manifest.json`（`[{"a":..,"b":..}]`）。

## 3. 静态类 CnNames（中文名）

```csharp
public static readonly Dictionary<string, string> SuffixCn;  // fullattack=连击, particle=粒子, tachie=立绘, ...
public static readonly Dictionary<string, string> Alias;     // zorrose=萝丝, hypnocattail=魅惑香蒲, ...
public static Dictionary<string, string> LoadCnNames(string content);
public static string DisplayName(IReadOnlyDictionary<string, string> names, string key);
```

## 4. 数据模型

```csharp
AssetEntry      { string Folder, Key, Rem, Path; string Name; }
AssetTable      { IReadOnlyList<AssetEntry> this[string folder]; IReadOnlyCollection<string> FolderNames;
                  void Set(string, List<AssetEntry>); bool Contains(string); int TotalCount; }
FamilyEntry     { string Rem, Path; string Name; }
AssetPair       { string A, B, Folder; string NameA, NameB; }
MissingEntry    { string Folder, Name; }
ResolveResult   { List<AssetPair> Pairs; List<MissingEntry> Missing; int Count; }
BuildListsResult{ AssetTable Table; List<string> Keys; List<string> Plants; }
```

## 5. 命令行（自测 / 手工验证）

```
PlantSwapCore --selftest [--game DIR]
PlantSwapCore --plants   [--content DIR | --game DIR]
PlantSwapCore --pairs  <A> <B> [--content DIR | --game DIR]
PlantSwapCore --swap   <A> <B> [--mode swap|replace] [--content DIR | --game DIR]
PlantSwapCore --restore <备份目录> [--content DIR | --game DIR]
```

`--selftest` 在 `%TEMP%` 下建临时假 Content 跑 backup/swap/replace/restore 全流程，不会碰真实游戏目录。

## 6. reanim 帧动画解析（Reanim.cs）

```csharp
public sealed class ReanimData
{
    public const int ObjectPreludeLength = 5;
    public string SourcePath { get; }
    public string ReaderName { get; }          // "Sexy.TodLib.ReanimReader, LAWN"
    public int SharedResourceCount { get; }
    public int ScaleMode { get; }              // 0=NoScale, 1=InvertAndScale, 255=ScaleFromPC
    public float Fps { get; }
    public List<ReanimTrack> Tracks { get; }
    public int ParsedBytes { get; }
    public int FileBytes { get; }
    public int TrailingBytes { get; }          // 0 = 文件里没有矩形表等额外数据块
    public int TrackCount { get; }
    public int TotalTransforms { get; }
    public int MaxFrames { get; }
    public float Duration { get; }
    public IReadOnlyList<string> ImageNames { get; }
    public ReanimTrack? FindTrack(string name);
    public void ResolveDefaults();
    public ReanimTransform? Sample(int trackIndex, float frame);
    public static ReanimData FromXnb(byte[] data);
    public static ReanimData FromXnb(byte[] data, string? sourcePath);
    public static ReanimData Load(string path, bool resolveDefaults = true);
    public static List<ReanimData> LoadAll(string directory, bool resolveDefaults = true);
}

public sealed class ReanimTrack
{
    public string Name { get; }
    public List<ReanimTransform> Transforms { get; }
    public int FrameCount { get; }
    public bool IsAttacher { get; }            // 名字以 attacher__ 开头
}

public sealed class ReanimTransform
{
    public ReanimTransformMode Mode { get; set; }   // New / CopyPrevious / Placeholder
    public string FontName, ImageName, Text;
    public float Alpha, Frame, ScaleX, ScaleY, SkewX, SkewY, TransX, TransY;
    public const float Placeholder = -99999f;       // 未指定哨兵
    public bool HasImage { get; }
    public float SkewXCos/SkewXSin/SkewYCos/SkewYSin { get; }
    public ReanimTransform Clone();
}

public sealed class ReanimFormatException : Exception;
public enum ReanimTransformMode : byte { New = 0, CopyPrevious = 1, Placeholder = 2 }
```

### 实测字段布局（199/199 个文件精确解析到末尾）

| # | 字段 | 说明 |
|---|---|---|
| 1 | 3B "XNB" + 1B 平台 'm' + 1B 版本 5 + 1B flags 0x00 + 4B 文件长度 | XNB 头，未压缩 |
| 2 | 7bit 读取器数(=1) + 7bit 长度 + UTF8 | "Sexy.TodLib.ReanimReader, LAWN" |
| 3 | 7bit 共享资源数 = 0 | |
| 4 | **5 字节固定前缀** `00 00 00 00 01` | 199 个文件取值恒定（XNB 对象头 + 版本/标志） |
| 5 | 1B scaleMode | 0 / 1 / 255 |
| 6 | 4B float fps | 12 / 15 / … / 60 |
| 7 | 4B int32 轨道数 | 1 .. 109 |
| 8 | 每轨道：4B 名称字符数 + UTF-16LE 名称 + 4B 关键帧数 | anim_* / zombie_* / 图层_40 |
| 9 | 每关键帧：1B mode | 0=New / 1=CopyPrevious / 2=Placeholder |
| 10 | mode==0 时：3×(4B+UTF16 字符串) + 8×4B float | font, image, text, alpha, frame, scaleX, scaleY, skewX, skewY, transX, transY |

- 字符串是 **int32 字符数 + UTF-16LE**（不是 XNB 标准的 7bit+UTF8），所以中文轨道名可正常解析。
- `-99999` 是“未指定”哨兵；`ResolveDefaults()` 按轨道继承前一帧（初值 trans=(0,0)、skew=(0,0)、scale=(1,1)、frame=0、alpha=1），与游戏 ReanimationLoadDefinition 一致。
- **文件里没有图片矩形表**：199 个文件全部精确解析到最后一字节（尾部 0 字节），矩形/位图来自运行期资源（AtlasResources / images），不在 reanim .xnb 内。

### 命令行（ReanimDump）

```
ReanimDump                                   解析 Agave.xnb 与 Zombie.xnb
ReanimDump --verify                          解析 Content/reanim 下全部 199 个并汇总
ReanimDump --frames <file> [--track NAME]    打印某轨道前 8 帧（原始 + 补全后）
```

## 7. reanim 姿态采样与绘制矩阵（ReanimPose.cs）

```csharp
public static class ReanimPoseBuilder
{
    public const float HalfPixelOffset = -0.5f;

    // 动画层（= 文件里的一条轨道；anim_* 是动画层标记）
    public static ReanimFrameRange GetLayerRange(ReanimData data, int trackIndex);
    public static int IndexOfTrack(ReanimData data, string trackName);

    // 一帧的整只姿态：Items 是所有轨道在该帧的绘制项，按文件顺序（先画的是底层）
    public static ReanimPose Build(ReanimData data, int trackIndex, float timeSeconds);
    public static ReanimPose Build(ReanimData data, string trackName, float timeSeconds);
    public static ReanimPose BuildAll(ReanimData data, float timeSeconds);                    // 整文件帧范围（调试）
    public static ReanimPose BuildTrack(ReanimData data, int trackIndex, float timeSeconds);  // 单条轨道（调试）

    public static bool TryMakeItem(ReanimTrack track, ReanimTransform transform, out ReanimDrawItem item);
    public static ReanimPoint[] GetQuad(in ReanimDrawItem item);   // 左上/右上/左下/右下
    public static ReanimRect GetBounds(in ReanimDrawItem item);
    public static int FrameAt(ReanimData data, ReanimTrack track, float timeSeconds);
}

public sealed class ReanimPose
{
    public string TrackName { get; }      // 动画层名
    public int Frame { get; }             // 轨道内绝对帧号
    public int FrameStart { get; }        // 该层起始帧
    public int FrameCount { get; }        // 该层帧数
    public int LayerFrame { get; }        // Frame - FrameStart
    public IReadOnlyList<ReanimDrawItem> Items { get; }
    public ReanimRect Bounds { get; }     // 所有项的并集包围盒
    public bool IsEmpty { get; }
}

public readonly struct ReanimDrawItem
{
    public readonly string? ImageName;
    public readonly AtlasRegion Region;   // 可能 IsEmpty
    public readonly float Alpha;          // 0..1
    public readonly float M11, M12, M21, M22, M31, M32;
    // 契约外附加信息
    public readonly string TrackName;
    public readonly int Cel;
    public readonly float LocalWidth, LocalHeight;
    public bool HasRegion { get; }
    public ReanimPoint TransformPoint(float x, float y);
}

public readonly struct ReanimPoint { public float X, Y; }
public readonly struct ReanimRect
{
    public float MinX, MinY, MaxX, MaxY, Width, Height; public bool IsEmpty;
    public static ReanimRect Empty { get; }
    public static ReanimRect Union(in ReanimRect a, in ReanimRect b);
}
public readonly struct ReanimFrameRange { public int Start, Count, End; }
```

### 真实数学（全部来自反编译 Lawn.dll）

1. **2x2 矩阵**（`Reanimation.MatrixFromTransform`）：
   ```
   M11 =  cos(-skewX * DEG_TO_RAD) * scaleX
   M12 = -sin(-skewX * DEG_TO_RAD) * scaleX
   M21 =  sin(-skewY * DEG_TO_RAD) * scaleY
   M22 =  cos(-skewY * DEG_TO_RAD) * scaleY
   DEG_TO_RAD = PI / 180   →  skew 单位是“角度”，不是弧度
   ```
   skewX == skewY 时是纯旋转；正角度在 y 向下的屏幕坐标里表现为**顺时针**。
   skew 允许超过 360°（实测有 591.6°），直接算三角函数即可，不要取模。

2. **平移与锚点**：`DrawTrack` 把矩阵组成 `T(半宽,半高) * Transform * Overlay`，随后
   `PrimitiveBatch.Draw` 又减去 `center=(半宽,半高)`（`M41 -= M11*cx + M21*cy`），两项正好抵消。
   所以最终作用于“左上角为原点的图片矩形 (0,0)-(W,H)”的矩阵是：
   ```
   M31 = transX - 0.5,  M32 = transY - 0.5
   ```
   **锚点是图片左上角，不是中心**：transX/transY 就是左上角位置，图片绕左上角缩放/旋转。
   -0.5 是游戏绘制路径里的半像素偏移（`Reanimation.DrawTrack` 的 `- 0.5f`），
   即 `ReanimPoseBuilder.HalfPixelOffset`。

3. **alpha**：`num2 = ClampInt((int)(mAlpha * mTrackColor.mAlpha + 0.5f), 0, 255)`；
   默认 trackColor/colorOverride 都是白色，所以有效 alpha 就是 `mAlpha`（0..1）；
   `num2 <= 0` 时不画。alpha 不跨轨道相乘、不继承。

4. **父子轨道 / attacher**：经典 reanim **没有** per-track 父子继承，每条轨道都直接乘
   `mOverlayMatrix`（整只 reanim 的放置矩阵）。`attacher__xxx` 轨道是另一种机制：
   它的 mText 编码“挂到另一只 reanim 的某条轨道”，游戏递归加载子 reanim，用
   `GetAttachmentOverlayMatrix` 把父轨道矩阵换算成子 reanim 的 overlay。
   本构建器只输出**单个文件局部空间**的矩阵；要放置到场景，用 世界矩阵 × 本矩阵。
   需要挂接时用 `ReanimTrack.IsAttacher` 识别后递归 Build 子 reanim。

5. **帧选择**：离散采样，`frame = floor(time * fps)`，clamp 到 `[0, FrameCount-1]`。
   游戏自己用 `(int)(animFrame + 0.5f)`（四舍五入），这里按接口约定用 floor，差半帧。

6. **动画层**：文件里每条 `anim_*` 轨道的 mFrame 只在它那一段帧里 >= 0，
   `Reanimation.GetFramesForLayer` 就是取这段 [start, last]。`Build(data, 层, t)` 先算这段，
   再在该段内采样，把所有轨道那一帧的项按顺序返回。
   `GetLayerRange` 就是这个算法；`--pose --layers` 可列出全部层。

### 绘制方法（App 侧）

1. `var pose = ReanimPoseBuilder.Build(data, "anim_walk", seconds);`
2. 对每个 `item`：用 `item.Region` 的 X/Y/SrcWidth/SrcHeight 裁剪图集；
   把 (0,0)-(LocalWidth, LocalHeight) 这个矩形用 `M11..M32` 变换后绘制
   （LocalWidth = Region.Width = SrcWidth × InvScale，等价于“绘制尺寸乘 InvScale”）。
3. 颜色不透明度用 `item.Alpha`；`item.HasRegion == false` 表示图集里没有这张图（跳过或占位）。

### 命令行（PlantSwapCore --pose）

```
PlantSwapCore --pose <file.xnb> --layers                      列出全部动画层与帧范围
PlantSwapCore --pose <file.xnb> [--track LAYER] [--frames N]   采样 N 帧打印矩阵与包围盒
PlantSwapCore --pose <file.xnb> --track LAYER --time T         单帧明细（矩阵 / quad / region）
```
