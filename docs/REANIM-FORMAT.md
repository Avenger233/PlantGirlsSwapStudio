# reanim (.xnb) 格式

游戏目录 `Content/reanim/*.xnb` 是**没有压缩的 XNB**，里面装的是 PvZ 系的
`Sexy.TodLib.ReanimReader` 对象。本文记录实测出来的二进制布局，供预览/导出使用。

> 说明：这是对文件格式的描述（互操作所需），代码在
> [src/PlantSwap.Core/Reanim.cs](../src/PlantSwap.Core/Reanim.cs)。

## 外层：XNB 包头

```
4 bytes   'X' 'N' 'B'        魔数
1 byte    0x05               XNB 版本
1 byte    0x00               flags：0 = 未压缩（这批文件全是 0）
4 bytes   int32              文件字节数
...                          XNB 目录 / 对象表
```

对象类型是 `Sexy.TodLib.ReanimReader, LAWN`，其负载开头固定 5 字节：

```
00 00 00 00 01
```

## 对象体

```
byte      scaleMode
float32   fps
int32     trackCount
```

然后是 `trackCount` 条轨道，每条：

```
int32     nameLength（字符数，不含结尾 0）
wchar[]   name（UTF-16LE，nameLength 个字符）
int32     frameCount
```

## 每一帧

先读 1 字节 `mode`：

| mode | 含义 | 后续字节 |
| --- | --- | --- |
| 0 | New（完整帧） | 3 个字符串 + 8 个 float |
| 1 | CopyPrevious（沿用上一帧的图像/文字，只改变换） | 8 个 float |
| 2 | Placeholder（透明占位） | 仅 8 个 float |

mode 0 的 3 个字符串依次是 **font / image / text**（各为 int32 长度 + UTF-16LE）。
图像名就是下一步查图集矩形用的 key。

8 个 float 的顺序：

```
0  alpha
1  frame          （整数，指向图集序列里的第几帧）
2  scaleX
3  scaleY
4  skewX          （单位是**度**，不是弧度）
5  skewY
6  transX
7  transY
```

> 这批文件末尾**没有**矩形表（全部 199 个文件都是 0 字节），
> 所以图集区域必须查 [ReanimAtlasData.tsv](../src/PlantSwap.Core/ReanimAtlasData.tsv)。

## "动画层" 是怎么形成的

reanim 本身只有一堆**平铺的轨道**（如 `zombie_head`、`zombie_torso`、`anim_walk`），
没有显式的动画分组。约定是这样的：

- 每条轨道的每一帧都有 `mFrame`，只有「这条轨道参与当前动作」的帧段里 `mFrame >= 0`
- 名字以 `anim_` 开头的轨道是**动作控制轨**，它的帧号决定了这次播的是哪一段
- 于是「动作 X」= 取所有轨道中 `mFrame >= 0` 且落在 X 那一段的帧

代码里对应 `ReanimData.GetFramesForLayer(track)`：按上述规则把连续的 `mFrame >= 0`
段切出来，每段就是一个动作层。

## 时间 → 帧号

```csharp
double raw      = time * fps;
int    frame    = (int)Math.Floor(raw);
int    layer    = Array.IndexOf(layerTracks, 当前动作);   // 由动作名选的层
double fraction = raw - frame;                            // 用于帧间插值
```

游戏里 `Reanimation.mInterpolate` 是 `true`，所以相邻两帧之间要**线性插值**
（位移、缩放、斜切、alpha 都插），否则会看到明显的 12fps 卡顿。

## 副本帧与占位帧的处理

- `CopyPrevious`：图像名/文字沿用上一帧，只替换变换
- `Placeholder`：整帧不可见，直接丢弃

插值时注意：如果 `frame` 有图而 `frame+1` 是占位帧，应当**丢弃该项**而不是插到半透明，
否则会看到残影。

## 配件是独立文件

路障、铁桶、旗子、纱门、鸭子圈这些**不在** `Zombie.xnb` 里，而是各自一个 reanim：
例如旗子在 `Zombie_flagpole.xnb`（2 条轨道、13 帧），它自己会挥动。

预览里的挂法是：找主体里对应的骨骼轨道（旗杆 → `zombie_flaghand`），
取该轨道的世界矩阵作为父矩阵，再把配件按自己的时间轴采样后乘上去。
