# 架构说明

## 分层

```
PlantSwap.Core   纯逻辑，不依赖图形库（可以单独当命令行用）
      ↑
PlantSwapStudio  MonoGame + ImGui 界面、实时预览
```

Core 的目标是「任何想复用这套逻辑的人都能直接拿到」：扫描、配对、备份、还原、reanim 解码全在这里，
UI 只负责把它接起来。

## Core 主要模块

| 文件 | 职责 |
| --- | --- |
| PlantSwap.cs | 扫描 Content 六个子目录、文件名解析成 key、族内配对、互换/覆盖/还原 |
| Models.cs | AssetTable / AssetPair / ResolveResult 等数据结构 |
| Swapper.cs | 备份 manifest、按对执行复制、还原 |
| CnNames.cs | 从 LawnStrings_zh_cn.txt 读中文名，带别名表与「中文名（key）」格式化 |
| Reanim.cs | reanim .xnb 解码 → 轨道/帧数据 |
| ReanimAtlas.cs | 图片 id → 图集矩形（内嵌 TSV） |
| ReanimPose.cs | 由帧数据插值出某一时刻的绘制项（矩阵、颜色、贴图区域） |
| Program.cs | 命令行入口 |

## App 主要模块

| 文件 | 职责 |
| --- | --- |
| Program.cs | 主循环、窗口、顶栏、模式选择页、替换/互换面板、日志 |
| SpinePreview.cs | Spine 立绘渲染 + 自适应缩放 + 缩放/平移 |
| ReanimPreview.cs | reanim 帧动画渲染（自建顶点缓冲，支持斜切） |
| ReanimIntegration.cs | 把 reanim 预览接进页面：僵尸选择、动作、配件、轨道开关 |
| ModUi.cs | Mod 模式的导出/导入与作品列表 |
| AnimCn.cs | Spine 动作名中文表 |

## 四个模式的状态隔离

每个模式各自保存一套状态，切换时存旧的、取新的，互不污染：

```csharp
readonly string[] _modeKeyA = new string[4];   // 每个模式选的植物 A
readonly string[] _modeKeyB = new string[4];   // 每个模式选的植物 B
readonly bool[]   _modePortrait = ...;         // 每个模式的「立绘 / 图集」
readonly bool[]   _modeOnlyPlants = ...;       // 每个模式的「只显示植物」
readonly float[]  _zoomA/_zoomB/_zoomM = ...;  // 每个模式的缩放/平移
```

观赏模式还额外有自己的列表范围（植物 / 僵尸 / 全部）和选中的那一条。
这些状态会写进 `资源修改器.ini`，下次启动恢复。

## 一帧的流程

```
Update:  预览动画推进 → 处理鼠标（缩放/拖动/双击复位）
Draw:    计算各面板矩形 → 先画 3D 预览 → 再画 ImGui 界面
```

顺序很重要：预览是直接往 backbuffer 画的，ImGui 后画才能盖在上面。

## 几个关键取舍

**为什么用 ImGui 而不是原生控件**
预览需要和控件同屏，且控件要能随时按窗口尺寸重排。ImGui 的即时模式配合 MonoGame 最省事。

**为什么预览自己建顶点缓冲**
Spine 的 SkeletonRenderer 走 SpriteBatch，无法表达斜切（skew）；
而游戏里的 reanim 大量使用 skew，用 SpriteBatch 会画歪。所以 ReanimPreview 用
`VertexPositionColorTexture` 手工拼四边形，把完整 2×3 矩阵塞进顶点位置。

**为什么自适应缩放要离屏测量**
`Skeleton.GetBounds()` 对这些娘化模型不准（带不可见的占位/特效，包围盒会宽出好几倍）。
改成离屏渲染一帧再扫像素求真实内容范围，比例才准。详见 [SPINE-RENDERING.md](SPINE-RENDERING.md)。
