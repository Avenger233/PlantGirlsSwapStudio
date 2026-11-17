using System;
using System.Collections.Generic;
using System.Globalization;

namespace PvZPlantSwap.Core;

/// <summary>屏幕坐标点。</summary>
public readonly struct ReanimPoint
{
    public ReanimPoint(float x, float y) { X = x; Y = y; }

    public float X { get; }
    public float Y { get; }

    public override string ToString() =>
        "(" + X.ToString("0.##", CultureInfo.InvariantCulture) + "," + Y.ToString("0.##", CultureInfo.InvariantCulture) + ")";
}

/// <summary>轴对齐包围盒（未设置时 IsEmpty 为 true）。</summary>
public readonly struct ReanimRect
{
    public ReanimRect(float minX, float minY, float maxX, float maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public float MinX { get; }
    public float MinY { get; }
    public float MaxX { get; }
    public float MaxY { get; }

    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public static ReanimRect Empty =>
        new(float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);

    public static ReanimRect Union(in ReanimRect a, in ReanimRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return new ReanimRect(
            Math.Min(a.MinX, b.MinX),
            Math.Min(a.MinY, b.MinY),
            Math.Max(a.MaxX, b.MaxX),
            Math.Max(a.MaxY, b.MaxY));
    }

    public override string ToString() =>
        IsEmpty
            ? "<empty>"
            : "x=" + MinX.ToString("0.##", CultureInfo.InvariantCulture) +
              " y=" + MinY.ToString("0.##", CultureInfo.InvariantCulture) +
              " w=" + Width.ToString("0.##", CultureInfo.InvariantCulture) +
              " h=" + Height.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>某条轨道的“可见帧范围”，等价于游戏的 GetFramesForLayer。</summary>
public readonly struct ReanimFrameRange
{
    public ReanimFrameRange(int start, int count)
    {
        Start = start;
        Count = count;
    }

    /// <summary>起始帧（含）。</summary>
    public int Start { get; }

    /// <summary>帧数。</summary>
    public int Count { get; }

    public int End => Start + Count - 1;

    public override string ToString() => Start + ".." + End + " (" + Count + ")";
}

/// <summary>
/// 一条轨道在某一帧的绘制项。
/// 矩阵是 2x3 仿射、行向量约定：x' = x*M11 + y*M21 + M31，y' = x*M12 + y*M22 + M32。
/// 局部坐标 (0,0) 是图片左上角，图片逻辑尺寸为 (LocalWidth, LocalHeight)：
/// 把 (0,0)-(W,H) 这个矩形经矩阵变换后绘制，图集裁剪用 Region.X/Y/SrcWidth/SrcHeight。
/// </summary>
public readonly struct ReanimDrawItem
{
    /// <summary>这一帧用的图片 id（如 IMAGE_REANIM_ZOMBIE_BODY）。</summary>
    public readonly string? ImageName;

    /// <summary>图集矩形；图集表里没有该 id 时 IsEmpty 为 true（此时 LocalWidth/Height 为 0）。</summary>
    public readonly AtlasRegion Region;

    /// <summary>有效 alpha（0..1）。</summary>
    public readonly float Alpha;

    public readonly float M11;
    public readonly float M12;
    public readonly float M21;
    public readonly float M22;
    public readonly float M31;
    public readonly float M32;

    /// <summary>契约外附加信息：所属轨道名。</summary>
    public readonly string TrackName;

    /// <summary>契约外附加信息：图片内的 cel 列号（游戏用 (int)(mFrame+0.5)，本游戏数据只有 0）。</summary>
    public readonly int Cel;

    /// <summary>契约外附加信息：图片逻辑宽（= Region.Width，缺失矩形时为 0）。</summary>
    public readonly float LocalWidth;

    /// <summary>契约外附加信息：图片逻辑高。</summary>
    public readonly float LocalHeight;

    public ReanimDrawItem(string? imageName, in AtlasRegion region, float alpha,
        float m11, float m12, float m21, float m22, float m31, float m32,
        string trackName = "", int cel = 0, float localWidth = 0f, float localHeight = 0f)
    {
        ImageName = imageName;
        Region = region;
        Alpha = alpha;
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        M31 = m31;
        M32 = m32;
        TrackName = trackName;
        Cel = cel;
        LocalWidth = localWidth;
        LocalHeight = localHeight;
    }

    public bool HasRegion => !Region.IsEmpty;

    /// <summary>把图片局部坐标（左上角原点）变换到屏幕。</summary>
    public ReanimPoint TransformPoint(float x, float y) =>
        new(M11 * x + M21 * y + M31, M12 * x + M22 * y + M32);

    public override string ToString() =>
        (ImageName ?? "<none>") + " alpha=" + Alpha.ToString("0.###", CultureInfo.InvariantCulture) +
        " M=[" + M11.ToString("0.###", CultureInfo.InvariantCulture) + " " +
        M12.ToString("0.###", CultureInfo.InvariantCulture) + " " +
        M21.ToString("0.###", CultureInfo.InvariantCulture) + " " +
        M22.ToString("0.###", CultureInfo.InvariantCulture) + " " +
        M31.ToString("0.###", CultureInfo.InvariantCulture) + " " +
        M32.ToString("0.###", CultureInfo.InvariantCulture) + "]";
}

/// <summary>一帧的绘制列表。Items 按轨道顺序，先画的是底层。</summary>
public sealed class ReanimPose
{
    public ReanimPose(string trackName, int frame, int frameStart, int frameCount, IReadOnlyList<ReanimDrawItem> items)
    {
        TrackName = trackName;
        Frame = frame;
        FrameStart = frameStart;
        FrameCount = frameCount;
        Items = items;

        var bounds = ReanimRect.Empty;
        for (var i = 0; i < items.Count; i++)
            bounds = ReanimRect.Union(bounds, ReanimPoseBuilder.GetBounds(items[i]));
        Bounds = bounds;
    }

    /// <summary>动画层（轨道）名。</summary>
    public string TrackName { get; }

    /// <summary>当前帧号（轨道内的绝对下标）。</summary>
    public int Frame { get; }

    /// <summary>该动画层的起始帧。</summary>
    public int FrameStart { get; }

    /// <summary>该动画层的帧数。</summary>
    public int FrameCount { get; }

    /// <summary>相对动画层起点的帧号（= Frame - FrameStart）。</summary>
    public int LayerFrame => Frame - FrameStart;

    public IReadOnlyList<ReanimDrawItem> Items { get; }

    /// <summary>全部绘制项的并集包围盒（屏幕像素）。</summary>
    public ReanimRect Bounds { get; }

    public bool IsEmpty => Items.Count == 0;

    public override string ToString() =>
        TrackName + " frame=" + Frame + " items=" + Items.Count + " bounds=" + Bounds;
}

/// <summary>
/// 把 reanim 数据采样成可绘制的矩阵列表。
///
/// 真实数学（全部来自反编译 Lawn.dll：Reanimation.DrawTrack / Reanimation.MatrixFromTransform /
/// Graphics.DrawImageTransformed / PrimitiveBatch.Draw / Reanimation.GetFramesForLayer）：
///
/// 1) MatrixFromTransform 给出的 2x2：
///      M11 =  cos(-skewX * DEG_TO_RAD) * scaleX
///      M12 = -sin(-skewX * DEG_TO_RAD) * scaleX
///      M21 =  sin(-skewY * DEG_TO_RAD) * scaleY
///      M22 =  cos(-skewY * DEG_TO_RAD) * scaleY
///    DEG_TO_RAD = PI/180，所以 skew 单位是角度；skewX==skewY 时是纯旋转，
///    正角度在 y 向下的屏幕坐标里表现为顺时针。
///
/// 2) DrawTrack 把矩阵组成 T(半宽,半高) * Transform * Overlay，随后 PrimitiveBatch.Draw 又减去
///    center=(半宽,半高)（M41 -= M11*cx + M21*cy），两项正好抵消。所以最终作用于
///    “左上角为原点的图片矩形 (0,0)-(W,H)” 的矩阵是：
///      M11..M22 同上，M31 = transX - 0.5，M32 = transY - 0.5
///    （-0.5 是游戏绘制路径里的半像素偏移，见 DrawTrack 的 - 0.5f）。
///    也就是说 transX/transY 是图片左上角的位置，图片绕左上角缩放/旋转；锚点不是中心。
///
/// 3) 一个 reanim 文件里的多条“anim_*”轨道是动画层标记：它们的 mFrame 只在各自那段帧里 >= 0，
///    游戏用 GetFramesForLayer 求出 [start, start+count-1] 再在这段里采样。
///    所以 Build(data, 轨道, t) 会先用该轨道的可见范围当动画区间，再把**所有轨道**在该帧的
///    绘制项按文件顺序返回（先画的是底层）。
/// </summary>
public static class ReanimPoseBuilder
{
    /// <summary>游戏绘制路径里的半像素偏移（Reanimation.DrawTrack 的 - 0.5f）。</summary>
    public const float HalfPixelOffset = -0.5f;

    private const double DegToRad = Math.PI / 180.0;

    /// <summary>取某条轨道的可见帧范围（等价于游戏 GetFramesForLayer）。</summary>
    public static ReanimFrameRange GetLayerRange(ReanimData data, int trackIndex)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (trackIndex < 0 || trackIndex >= data.Tracks.Count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex), trackIndex, "轨道下标越界");

        EnsureDefaults(data);

        var track = data.Tracks[trackIndex];
        var n = track.FrameCount;
        if (n == 0) return new ReanimFrameRange(0, 0);

        var start = -1;
        for (var i = 0; i < n; i++)
            if (track.Transforms[i].Frame >= 0f) { start = i; break; }

        // 游戏在找不到时保留 (0, 1)
        if (start < 0) return new ReanimFrameRange(0, 1);

        var last = start;
        for (var i = n - 1; i >= start; i--)
            if (track.Transforms[i].Frame >= 0f) { last = i; break; }

        return new ReanimFrameRange(start, last - start + 1);
    }

    /// <summary>是否在帧之间插值。游戏里 Reanimation.mInterpolate 恒为 true，所以默认开启。</summary>
    public static bool Interpolate { get; set; } = true;

    /// <summary>按动画层（轨道）下标取某一时刻的整只姿态。</summary>
    public static ReanimPose Build(ReanimData data, int trackIndex, float timeSeconds)
    {
        var range = GetLayerRange(data, trackIndex);
        return BuildRange(data, data.Tracks[trackIndex].Name, range, timeSeconds);
    }

    /// <summary>按动画层名取某一时刻的整只姿态（名字不区分大小写）。</summary>
    public static ReanimPose Build(ReanimData data, string trackName, float timeSeconds)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (trackName is null) throw new ArgumentNullException(nameof(trackName));

        var index = IndexOfTrack(data, trackName);
        if (index < 0) throw new ArgumentException("找不到轨道: " + trackName, nameof(trackName));
        return Build(data, index, timeSeconds);
    }

    /// <summary>不指定动画层，按整个文件的帧范围采样（调试用）。</summary>
    public static ReanimPose BuildAll(ReanimData data, float timeSeconds)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        EnsureDefaults(data);
        return BuildRange(data, string.Empty, new ReanimFrameRange(0, data.MaxFrames), timeSeconds);
    }

    /// <summary>只取单条轨道（调试/单件预览用）。</summary>
    public static ReanimPose BuildTrack(ReanimData data, int trackIndex, float timeSeconds)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (trackIndex < 0 || trackIndex >= data.Tracks.Count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex), trackIndex, "轨道下标越界");

        EnsureDefaults(data);

        var track = data.Tracks[trackIndex];
        var frame = FrameFromTime(data, track.FrameCount, timeSeconds);
        var items = new List<ReanimDrawItem>(1);
        if (track.FrameCount > 0 && TryMakeItem(track, track.Transforms[frame], out var item))
            items.Add(item);

        return new ReanimPose(track.Name, frame, 0, track.FrameCount, items);
    }

    private static ReanimPose BuildRange(ReanimData data, string name, in ReanimFrameRange range, float timeSeconds)
    {
        EnsureDefaults(data);

        var fps = data.Fps > 0f ? data.Fps : 12f;
        var raw = timeSeconds * fps;
        if (float.IsNaN(raw) || raw < 0f) raw = 0f;
        var maxLayer = range.Count - 1;
        if (maxLayer < 0) maxLayer = 0;
        if (raw > maxLayer) raw = maxLayer;

        var layerFrame = (int)Math.Floor(raw);
        var fraction = raw - layerFrame;
        var frame = range.Start + layerFrame;

        var items = new List<ReanimDrawItem>(data.Tracks.Count);
        foreach (var track in data.Tracks)
        {
            if (track.FrameCount == 0) continue;
            var i0 = frame < track.FrameCount ? frame : track.FrameCount - 1;
            if (i0 < 0) i0 = 0;
            var i1 = i0 + 1 < track.FrameCount ? i0 + 1 : i0;

            ReanimDrawItem item;
            if (Interpolate && fraction > 1e-6f && i1 != i0)
            {
                if (TryMakeItem(track, track.Transforms[i0], track.Transforms[i1], fraction, out item))
                    items.Add(item);
            }
            else if (TryMakeItem(track, track.Transforms[i0], out item))
            {
                items.Add(item);
            }
        }

        return new ReanimPose(name, frame, range.Start, range.Count, items);
    }

    /// <summary>轨道名 → 下标（不区分大小写），找不到返回 -1。</summary>
    public static int IndexOfTrack(ReanimData data, string trackName)
    {
        if (data is null) return -1;
        for (var i = 0; i < data.Tracks.Count; i++)
            if (string.Equals(data.Tracks[i].Name, trackName, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>某动画层在某一时刻的帧号 = floor(time*fps)，clamp 到 [0, FrameCount-1]。</summary>
    public static int FrameAt(ReanimData data, ReanimTrack track, float timeSeconds) =>
        FrameFromTime(data, track.FrameCount, timeSeconds);

    private static int FrameFromTime(ReanimData data, int frameCount, float timeSeconds)
    {
        if (frameCount <= 0) return 0;
        var fps = data.Fps > 0f ? data.Fps : 12f;
        var frame = (int)Math.Floor(timeSeconds * fps);
        if (frame < 0) frame = 0;
        if (frame > frameCount - 1) frame = frameCount - 1;
        return frame;
    }

    /// <summary>
    /// 在两帧之间插值再变成绘制项（对应游戏 mInterpolate）。
    /// 图片 / 帧号取前帧；前帧有图而后帧 mFrame &lt; 0 时按游戏规则整条丢弃。
    /// </summary>
    public static bool TryMakeItem(ReanimTrack track, ReanimTransform a, ReanimTransform b, float fraction, out ReanimDrawItem item)
    {
        item = default;
        if (track is null || a is null) return false;
        if (b is null || fraction <= 0f) return TryMakeItem(track, a, out item);

        if (a.Frame >= 0f && b.Frame < 0f) return false;

        var m = a.Clone();
        m.TransX = Lerp(a.TransX, b.TransX, fraction);
        m.TransY = Lerp(a.TransY, b.TransY, fraction);
        m.SkewX = Lerp(a.SkewX, b.SkewX, fraction);
        m.SkewY = Lerp(a.SkewY, b.SkewY, fraction);
        m.ScaleX = Lerp(a.ScaleX, b.ScaleX, fraction);
        m.ScaleY = Lerp(a.ScaleY, b.ScaleY, fraction);
        m.Alpha = Lerp(a.Alpha, b.Alpha, fraction);

        return TryMakeItem(track, m, out item);
    }

    static float Lerp(float x, float y, float t) => x + (y - x) * t;

    /// <summary>把一帧的 ReanimatorTransform 变成绘制项；不绘制时返回 false。</summary>
    public static bool TryMakeItem(ReanimTrack track, ReanimTransform transform, out ReanimDrawItem item)
    {
        item = default;
        if (track is null || transform is null) return false;

        // 游戏：mFrame < 0 表示这一帧不显示
        if (transform.Frame < 0f) return false;

        // 没有图片（纯文字/空白轨道）就没有可画的东西
        if (string.IsNullOrEmpty(transform.ImageName)) return false;

        var alpha = transform.Alpha;
        if (alpha <= 0f) return false;
        // 游戏先把 alpha 取整到 0..255，<=0 直接跳过
        if ((int)Math.Round(alpha * 255f) <= 0) return false;

        ReanimAtlas.TryGet(transform.ImageName, out var region);

        var cosX = (float)Math.Cos(-transform.SkewX * DegToRad);
        var sinX = (float)Math.Sin(-transform.SkewX * DegToRad);
        var cosY = (float)Math.Cos(-transform.SkewY * DegToRad);
        var sinY = (float)Math.Sin(-transform.SkewY * DegToRad);

        var m11 = cosX * transform.ScaleX;
        var m12 = -sinX * transform.ScaleX;
        var m21 = sinY * transform.ScaleY;
        var m22 = cosY * transform.ScaleY;

        var cel = (int)Math.Floor(transform.Frame + 0.5f);

        // 游戏的 pivot 修正用的是图集里的**源矩形**尺寸（PrimitiveBatch.Draw 里
        // M41 -= M11*center.X + M21*center.Y，center = srcRect 的半个宽高），
        // 而绘制矩形是**逻辑尺寸**（Image.GetCelWidth）。两者不相等（图集按 0.8 打包），
        // 差值会以 M*(Δ/2) 的形式把每个部件整体推出去，部件越大偏得越多 —— 这就是错位的来源。
        var dx = 0f;
        var dy = 0f;
        if (!region.IsEmpty)
        {
            dx = (region.Width - region.SrcWidth) * 0.5f;
            dy = (region.Height - region.SrcHeight) * 0.5f;
        }

        item = new ReanimDrawItem(
            transform.ImageName,
            region,
            alpha,
            m11, m12, m21, m22,
            m11 * dx + m21 * dy + transform.TransX + HalfPixelOffset,
            m12 * dx + m22 * dy + transform.TransY + HalfPixelOffset,
            track.Name,
            cel,
            region.IsEmpty ? 0f : region.Width,
            region.IsEmpty ? 0f : region.Height);
        return true;
    }

    /// <summary>图片四角在屏幕上的位置：左上、右上、左下、右下。</summary>
    public static ReanimPoint[] GetQuad(in ReanimDrawItem item) => new[]
    {
        item.TransformPoint(0f, 0f),
        item.TransformPoint(item.LocalWidth, 0f),
        item.TransformPoint(0f, item.LocalHeight),
        item.TransformPoint(item.LocalWidth, item.LocalHeight),
    };

    /// <summary>图片的轴对齐包围盒；没有矩形时为退化点。</summary>
    public static ReanimRect GetBounds(in ReanimDrawItem item)
    {
        var w = item.LocalWidth;
        var h = item.LocalHeight;
        var x0 = item.M31;
        var y0 = item.M32;
        var x1 = item.M11 * w + item.M31;
        var y1 = item.M12 * w + item.M32;
        var x2 = item.M21 * h + item.M31;
        var y2 = item.M22 * h + item.M32;
        var x3 = item.M11 * w + item.M21 * h + item.M31;
        var y3 = item.M12 * w + item.M22 * h + item.M32;

        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new ReanimRect(minX, minY, maxX, maxY);
    }

    private static void EnsureDefaults(ReanimData data)
    {
        if (!data.DefaultsResolved) data.ResolveDefaults();
        if (!data.ImageNamesResolved) data.ResolveImageNames();
    }
}
