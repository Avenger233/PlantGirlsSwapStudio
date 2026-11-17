# Spine 立绘渲染要点

这批娘化立绘是 **Spine 4.2.43** 导出的，用 spine-monogame 直接渲染能正常出图，
但要做到「和游戏里一模一样、每只都填满预览框」，有几个坑必须处理。

## 1. 自适应缩放：不能信 GetBounds()

最初用 `Skeleton.GetBounds()` 遍历若干帧取并集来算缩放，结果大喷菇被缩得很小还偏左：

```
GetBounds 报告   765 × 397
实际画出来的像素  266 × 396
```

原因是这些 skel 里带了**看不见的占位/特效**（或远离身体、alpha 为 0 的附件），包围盒因此被撑大，
按包围盒缩放就会「看着只有一半大」。

**现在的做法**：离屏渲染一帧，扫像素求真实不透明范围。

```csharp
// 1) 缩到 0.5 倍画进 512×512 的 RenderTarget
// 2) GetData 扫 alpha > 20 的像素，得到 x0/y0/x1/y1
// 3) 如果贴到边缘说明画出去被裁了，缩放减半重试（最多 4 次）
// 4) 像素范围 ÷ 缩放 = 模型单位下的真实尺寸与中心
```

另外**别把缩放算成「宽高取小」**：不同植物的模型单位比例差很多（765×397 和 1928×1204 都有），
取小会让扁的那只显得特别小。统一按高度归一，只对异常宽的形态做宽度兜底。

## 2. 垂直居中的符号

内容中心在骨架原点**上方** `cy` 个单位，而屏幕 Y 轴向下，所以是加号：

```csharp
_skel.Y = viewport.Height / 2f + Pan.Y + _cy * sc;   // 注意是 +
```

写成减号会把整只顶到面板外面去。

## 3. MonoGame 共享 Effect

spine-monogame 的 `SkeletonRenderer` 复用同一个 `BasicEffect`，
它的 World/View/Projection 会被其他绘制代码改掉。每帧绘制前务必重设：

```csharp
_effect.World      = Matrix.Identity;
_effect.View       = Matrix.CreateLookAt(new Vector3(0, 0, 1), Vector3.Zero, Vector3.Up);
_effect.Projection = Matrix.CreateOrthographicOffCenter(0, w, h, 0, -1, 1);
```

漏了会出现「画面忽大忽小」。

## 4. 斜切（skew）必须自己算矩阵

游戏里 reanim 的变换矩阵是这么来的（skew 单位是**度**，且不会对 360 取模，出现 >360 的值是正常的）：

```csharp
float rad = -skewX * MathF.PI / 180f;
M11 = MathF.Cos(rad) * scaleX;
M12 = -MathF.Sin(rad) * scaleX;

rad = -skewY * MathF.PI / 180f;
M21 = MathF.Sin(rad) * scaleY;
M22 = MathF.Cos(rad) * scaleY;
```

Spine 的 SkeletonRenderer 走 SpriteBatch（只能表达旋转/缩放/平移），
表达不了两个方向不同的 skew，所以 reanim 预览是**自己拼四边形**：
顶点位置直接乘上面这个 2×3 矩阵。见 [ReanimPreview.cs](../src/PlantSwapStudio/ReanimPreview.cs)。

## 5. 锚点是左上角，且要补 pivot 差值

reanim 的锚点不在中心，在**左上角**：

```csharp
M31 = transX - 0.5f;
M32 = transY - 0.5f;
```

但贴图是按「中心」对齐绘制的，两边用的尺寸还不是同一个：

- 绘制时按**逻辑尺寸** `celW/celH` 居中（`celW/2`）
- 图集裁剪用的是**存储尺寸** `srcW/srcH`（图集里为了省空间按 `invScale` 缩过）

两者差 `(celW - srcW) / 2`，不补的话部件会**按自身大小成比例地往外漂**——
小部件看着还行，大部件能偏出半个屏幕。

```csharp
float dx = (Width - SrcWidth) / 2f;
float dy = (Height - SrcHeight) / 2f;
M31 += M11 * dx + M21 * dy;
M32 += M12 * dx + M22 * dy;
```

## 6. 图集矩形表

reanim 帧里写的是「图片名」，要找它在图集上的位置。
`images/600x1000` 下的 xnb 就是图集页，一张图集里几十上百个小图：

- 共 90 张图集页、5130 个图片 id
- 每个 id 记录：图集名、x、y、srcW、srcH、逻辑宽高、invScale
- 存储像素 = `round(宽 * invScale)`（实测 0.8），画的时候再放大回逻辑尺寸

## 7. 别忘了骨骼文件名可能带下划线

工具内部一律用**去掉下划线**的规范化 key（`spikerock_tachie` → `spikerocktachie`），
但磁盘上的文件名可能带下划线。凡是按 key 找文件，必须反查：

```csharp
foreach (var f in Directory.GetFiles(dir, "*.skel"))
    if (Norm(Path.GetFileNameWithoutExtension(f)) == key) return f;
```

不能直接拼 `key + ".skel"`，否则这些植物会被误判成「没有 Spine 资源」而退回 reanim。
