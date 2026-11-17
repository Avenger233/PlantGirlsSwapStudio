using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PvZPlantSwap.Core;

/// <summary>reanim 数据格式异常。</summary>
public sealed class ReanimFormatException : Exception
{
    public ReanimFormatException(string message) : base(message) { }
}

/// <summary>关键帧的存储方式，对应游戏 ReanimReader.ReanimOptimisationType。</summary>
public enum ReanimTransformMode : byte
{
    /// <summary>完整数据（3 个字符串 + 8 个 float）。</summary>
    New = 0,

    /// <summary>与上一个关键帧完全相同（文件里没有数据）。</summary>
    CopyPrevious = 1,

    /// <summary>占位帧，全部数值为 -99999 哨兵（文件里没有数据）。</summary>
    Placeholder = 2,
}

/// <summary>单个关键帧（对应 ReanimatorTransform）。数值为 -99999 表示“未指定”，由 <see cref="ReanimData.ResolveDefaults"/> 继承前一帧。</summary>
public sealed class ReanimTransform
{
    /// <summary>未指定字段的哨兵值（ReanimatorXnaHelpers.DEFAULT_FIELD_PLACEHOLDER）。</summary>
    public const float Placeholder = -99999f;

    private const double DegToRad = Math.PI / 180.0;

    public ReanimTransformMode Mode { get; set; }

    public string FontName { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public float Alpha { get; set; }
    public float Frame { get; set; }
    public float ScaleX { get; set; }
    public float ScaleY { get; set; }
    public float SkewX { get; set; }
    public float SkewY { get; set; }
    public float TransX { get; set; }
    public float TransY { get; set; }

    public bool HasImage => !string.IsNullOrEmpty(ImageName);
    public bool IsPlaceholder => Mode == ReanimTransformMode.Placeholder;

    // 游戏在 ReanimationLoadDefinition 里预先算好这几个值（角度取负）。
    public float SkewXCos => (float)Math.Cos(-SkewX * DegToRad);
    public float SkewXSin => (float)Math.Sin(-SkewX * DegToRad);
    public float SkewYCos => (float)Math.Cos(-SkewY * DegToRad);
    public float SkewYSin => (float)Math.Sin(-SkewY * DegToRad);

    public ReanimTransform Clone() => (ReanimTransform)MemberwiseClone();

    public override string ToString() =>
        "Image: " + (ImageName.Length == 0 ? "<none>" : ImageName) +
        " frame=" + Frame.ToString("0.###") +
        " trans=(" + TransX.ToString("0.###") + "," + TransY.ToString("0.###") + ")";
}

/// <summary>一条轨道（对应 ReanimatorTrack）。</summary>
public sealed class ReanimTrack
{
    public ReanimTrack(string name) => Name = name;

    public string Name { get; }
    public List<ReanimTransform> Transforms { get; } = new();

    public int FrameCount => Transforms.Count;

    /// <summary>attach 轨道（名字以 attacher__ 开头）。</summary>
    public bool IsAttacher => Name.StartsWith("attacher__", StringComparison.Ordinal);

    public override string ToString() => Name;
}

/// <summary>
/// 解析后的 reanim 帧动画（对应 ReanimatorDefinition）。
/// 字段布局（实测 199/199 个文件精确解析到文件末尾）：
///   XNB 头 → 对象区固定 5 字节前缀 → byte scaleMode → float fps → int32 trackCount
///   → 每轨道：int32 名称字符数 + UTF-16LE 名称 + int32 关键帧数
///   → 每关键帧：byte mode；mode==0 时再读 3 个字符串 + 8 个 float。
/// 文件内没有图片矩形表（解析精确结束，尾部 0 字节）。
/// </summary>
public sealed class ReanimData
{
    /// <summary>对象区前固定前缀长度（实测恒为 00 00 00 00 01）。</summary>
    public const int ObjectPreludeLength = 5;

    private List<string>? _imageNames;

    /// <summary>来源文件路径（FromXnb 直接调用时为空）。</summary>
    public string SourcePath { get; internal set; } = string.Empty;

    /// <summary>XNB 里记录的 ContentTypeReader 名，正常为 "Sexy.TodLib.ReanimReader, LAWN"。</summary>
    public string ReaderName { get; internal set; } = string.Empty;

    /// <summary>XNB 共享资源数（本游戏全部为 0）。</summary>
    public int SharedResourceCount { get; internal set; }

    /// <summary>缩放模式：0=NoScale, 1=InvertAndScale, 255=ScaleFromPC（对应 ReanimReader.ReanimScaleType）。</summary>
    public int ScaleMode { get; internal set; }

    /// <summary>帧率。</summary>
    public float Fps { get; internal set; }

    /// <summary>全部轨道（顺序与文件一致）。</summary>
    public List<ReanimTrack> Tracks { get; } = new();

    /// <summary>解析消耗的字节数。</summary>
    public int ParsedBytes { get; internal set; }

    /// <summary>文件总字节数。</summary>
    public int FileBytes { get; internal set; }

    /// <summary>文件尾部未被解析的字节数；0 表示没有额外数据块（例如图片矩形表）。</summary>
    public int TrailingBytes => FileBytes - ParsedBytes;

    public int TrackCount => Tracks.Count;

    public int TotalTransforms
    {
        get
        {
            var n = 0;
            foreach (var t in Tracks) n += t.FrameCount;
            return n;
        }
    }

    /// <summary>单条轨道的最长关键帧数。</summary>
    public int MaxFrames
    {
        get
        {
            var n = 0;
            foreach (var t in Tracks) if (t.FrameCount > n) n = t.FrameCount;
            return n;
        }
    }

    /// <summary>整段动画时长（秒）= MaxFrames / Fps。</summary>
    public float Duration => Fps > 0f ? MaxFrames / Fps : 0f;

    /// <summary>文件里出现过的全部图片 id（去重，按首次出现顺序）。</summary>
    public IReadOnlyList<string> ImageNames
    {
        get
        {
            if (_imageNames is null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var list = new List<string>();
                foreach (var track in Tracks)
                    foreach (var t in track.Transforms)
                        if (t.HasImage && seen.Add(t.ImageName))
                            list.Add(t.ImageName);
                _imageNames = list;
            }
            return _imageNames;
        }
    }

    /// <summary>按名字找轨道（不区分大小写，与游戏 Common.Lower 后比较一致）。</summary>
    public ReanimTrack? FindTrack(string name)
    {
        foreach (var t in Tracks)
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    /// <summary>是否已经执行过 ResolveDefaults（-99999 哨兵是否已补全）。</summary>
    public bool DefaultsResolved { get; private set; }

    /// <summary>是否已经把空图片名按轨道继承为上一帧的图片名。</summary>
    public bool ImageNamesResolved { get; private set; }

    /// <summary>
    /// 把空图片名沿轨道继承为上一帧的图片名。
    /// reanim 文件只在图片变化时写一次图片 id，其余帧为空，游戏在 ReanimationLoadDefinition 里做同样的继承。
    /// </summary>
    public void ResolveImageNames()
    {
        foreach (var track in Tracks)
        {
            var current = string.Empty;
            foreach (var t in track.Transforms)
            {
                if (!string.IsNullOrEmpty(t.ImageName)) current = t.ImageName;
                else t.ImageName = current;
            }
        }

        ImageNamesResolved = true;
    }

    /// <summary>
    /// 把 -99999 哨兵替换为“沿轨道继承的前一帧值”，与游戏 ReanimationLoadDefinition 一致。
    /// 初值：trans=(0,0) skew=(0,0) scale=(1,1) frame=0 alpha=1。
    /// </summary>
    public void ResolveDefaults()
    {
        foreach (var track in Tracks)
        {
            float transX = 0f, transY = 0f, skewX = 0f, skewY = 0f;
            float scaleX = 1f, scaleY = 1f, frame = 0f, alpha = 1f;

            foreach (var t in track.Transforms)
            {
                t.TransX = Fill(t.TransX, ref transX);
                t.TransY = Fill(t.TransY, ref transY);
                t.SkewX = Fill(t.SkewX, ref skewX);
                t.SkewY = Fill(t.SkewY, ref skewY);
                t.ScaleX = Fill(t.ScaleX, ref scaleX);
                t.ScaleY = Fill(t.ScaleY, ref scaleY);
                t.Frame = Fill(t.Frame, ref frame);
                t.Alpha = Fill(t.Alpha, ref alpha);
            }
        }

        DefaultsResolved = true;
    }

    private static float Fill(float value, ref float previous)
    {
        if (value == ReanimTransform.Placeholder) return previous;
        previous = value;
        return value;
    }

    /// <summary>
    /// 取某轨道在指定帧时刻的插值关键帧（线性插值，skew 走最短角）。
    /// 仅作预览用的便利方法，与游戏 BlendTransform 近似。
    /// </summary>
    public ReanimTransform? Sample(int trackIndex, float frame)
    {
        if (trackIndex < 0 || trackIndex >= Tracks.Count) return null;
        var transforms = Tracks[trackIndex].Transforms;
        if (transforms.Count == 0) return null;
        if (frame <= 0f) return transforms[0].Clone();
        var i0 = (int)Math.Floor(frame);
        if (i0 >= transforms.Count - 1) return transforms[transforms.Count - 1].Clone();

        var a = transforms[i0];
        var b = transforms[i0 + 1];
        var f = frame - i0;
        var r = a.Clone();
        r.Mode = ReanimTransformMode.New;
        r.TransX = Lerp(a.TransX, b.TransX, f);
        r.TransY = Lerp(a.TransY, b.TransY, f);
        r.ScaleX = Lerp(a.ScaleX, b.ScaleX, f);
        r.ScaleY = Lerp(a.ScaleY, b.ScaleY, f);
        r.Alpha = Lerp(a.Alpha, b.Alpha, f);
        r.Frame = a.Frame;
        r.SkewX = LerpAngle(a.SkewX, b.SkewX, f);
        r.SkewY = LerpAngle(a.SkewY, b.SkewY, f);
        return r;
    }

    private static float Lerp(float a, float b, float f) => a + (b - a) * f;

    private static float LerpAngle(float a, float b, float f)
    {
        var d = b - a;
        while (d > 180f) d -= 360f;
        while (d < -180f) d += 360f;
        return a + d * f;
    }

    /// <summary>解析 reanim .xnb 字节。数值保持文件原样（含 -99999 哨兵）。</summary>
    public static ReanimData FromXnb(byte[] data) => FromXnb(data, null);

    /// <summary>解析 reanim .xnb 字节。数值保持文件原样（含 -99999 哨兵）。</summary>
    public static ReanimData FromXnb(byte[] data, string? sourcePath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        var header = ParseHeader(data);

        ReanimData? fallback = null;
        foreach (var prelude in PreludeCandidates)
        {
            try
            {
                var parsed = ParseObject(data, header, prelude, sourcePath);
                if (parsed.TrailingBytes == 0) return parsed;
                fallback ??= parsed;
            }
            catch (ReanimFormatException)
            {
                // 换一个前缀长度再试
            }
        }

        return fallback ?? throw new ReanimFormatException(
            "无法解析 reanim XNB（reader=" + header.ReaderName + "）: " + (sourcePath ?? "<memory>"));
    }

    /// <summary>读文件并解析；默认把 -99999 哨兵按游戏规则补全为实际值。</summary>
    public static ReanimData Load(string path, bool resolveDefaults = true)
    {
        var data = FromXnb(File.ReadAllBytes(path), path);
        if (resolveDefaults) data.ResolveDefaults();
        return data;
    }

    /// <summary>解析目录下全部 *.xnb（按文件名排序）。</summary>
    public static List<ReanimData> LoadAll(string directory, bool resolveDefaults = true)
    {
        var result = new List<ReanimData>();
        if (!Directory.Exists(directory)) return result;
        var files = Directory.GetFiles(directory, "*.xnb");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files)
        {
            try
            {
                result.Add(Load(f, resolveDefaults));
            }
            catch (ReanimFormatException)
            {
                // 跳过坏文件，调用方可用 Load 单独定位
            }
        }
        return result;
    }

    private static readonly int[] PreludeCandidates = { ObjectPreludeLength, 4, 6, 3, 7, 2, 8, 1, 0 };

    private sealed class XnbHeader
    {
        public string ReaderName = string.Empty;
        public int SharedResourceCount;
        public int ObjectStart;
    }

    private static XnbHeader ParseHeader(byte[] data)
    {
        if (data.Length < 12 || data[0] != (byte)'X' || data[1] != (byte)'N' || data[2] != (byte)'B')
            throw new ReanimFormatException("不是 XNB 文件（magic 不匹配）");

        var flags = data[5];
        if ((flags & 0x80) != 0)
            throw new ReanimFormatException("XNB 为压缩格式（flags=0x80），本解析器只支持未压缩");

        var pos = 10;
        var readerCount = Read7Bit(data, ref pos);
        if (readerCount < 1 || readerCount > 64)
            throw new ReanimFormatException("XNB 类型读取器数量异常: " + readerCount);

        var header = new XnbHeader();
        for (var i = 0; i < readerCount; i++)
        {
            var len = Read7Bit(data, ref pos);
            if (len < 0 || pos + len > data.Length)
                throw new ReanimFormatException("XNB 读取器名越界");
            header.ReaderName = Encoding.UTF8.GetString(data, pos, len);
            pos += len;
        }

        header.SharedResourceCount = Read7Bit(data, ref pos);
        header.ObjectStart = pos;
        return header;
    }

    private static int Read7Bit(byte[] data, ref int pos)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            if (pos >= data.Length) throw new ReanimFormatException("XNB 头越界");
            var b = data[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 28) throw new ReanimFormatException("XNB 7bit 长度非法");
        }
        return result;
    }

    private static ReanimData ParseObject(byte[] data, XnbHeader header, int prelude, string? sourcePath)
    {
        var rd = new XnbReader(data, header.ObjectStart + prelude);

        var result = new ReanimData
        {
            SourcePath = sourcePath ?? string.Empty,
            ReaderName = header.ReaderName,
            SharedResourceCount = header.SharedResourceCount,
            FileBytes = data.Length,
        };

        result.ScaleMode = rd.Byte();
        result.Fps = rd.Single();
        var trackCount = rd.Int32();

        if (!(result.Fps > 0.1f && result.Fps < 1000f))
            throw new ReanimFormatException("fps 异常: " + result.Fps);
        if (trackCount < 0 || trackCount > 4096)
            throw new ReanimFormatException("轨道数异常: " + trackCount);

        ReanimTransform? previous = null;

        for (var ti = 0; ti < trackCount; ti++)
        {
            var name = rd.String();
            var count = rd.Int32();
            if (count < 0 || count > 1_000_000)
                throw new ReanimFormatException("轨道 " + name + " 的关键帧数异常: " + count);

            var track = new ReanimTrack(name);
            if (count > 0) track.Transforms.Capacity = count;

            for (var k = 0; k < count; k++)
            {
                var rawMode = rd.Byte();
                if (rawMode > 2)
                    throw new ReanimFormatException("轨道 " + name + " 第 " + k + " 帧的存储方式未知: " + rawMode);

                var mode = (ReanimTransformMode)rawMode;
                ReanimTransform t;

                switch (mode)
                {
                    case ReanimTransformMode.Placeholder:
                        t = NewPlaceholder(mode);
                        break;

                    case ReanimTransformMode.CopyPrevious:
                        // 游戏里直接复用上一个关键帧对象；无前项时（文件异常）退化为占位帧
                        t = previous?.Clone() ?? NewPlaceholder(mode);
                        t.Mode = mode;
                        break;

                    default:
                        t = new ReanimTransform { Mode = ReanimTransformMode.New };
                        t.FontName = rd.String();
                        t.ImageName = rd.String();
                        t.Text = rd.String();
                        t.Alpha = rd.Single();
                        t.Frame = rd.Single();
                        t.ScaleX = rd.Single();
                        t.ScaleY = rd.Single();
                        t.SkewX = rd.Single();
                        t.SkewY = rd.Single();
                        t.TransX = rd.Single();
                        t.TransY = rd.Single();
                        break;
                }

                previous = t;
                track.Transforms.Add(t);
            }

            result.Tracks.Add(track);
        }

        result.ParsedBytes = rd.Position;
        return result;
    }

    private static ReanimTransform NewPlaceholder(ReanimTransformMode mode) => new()
    {
        Mode = mode,
        Alpha = ReanimTransform.Placeholder,
        Frame = ReanimTransform.Placeholder,
        ScaleX = ReanimTransform.Placeholder,
        ScaleY = ReanimTransform.Placeholder,
        SkewX = ReanimTransform.Placeholder,
        SkewY = ReanimTransform.Placeholder,
        TransX = ReanimTransform.Placeholder,
        TransY = ReanimTransform.Placeholder,
    };

    /// <summary>顺序读取 XNB 对象的自定义字符串（int32 字符数 + UTF-16LE）。</summary>
    private sealed class XnbReader
    {
        private readonly byte[] _data;
        private int _pos;

        public XnbReader(byte[] data, int start) => (_data, _pos) = (data, start);

        public int Position => _pos;

        public byte Byte()
        {
            if (_pos + 1 > _data.Length) throw new ReanimFormatException("读取越界(byte) @" + _pos);
            return _data[_pos++];
        }

        public int Int32()
        {
            if (_pos + 4 > _data.Length) throw new ReanimFormatException("读取越界(int32) @" + _pos);
            var v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_pos, 4));
            _pos += 4;
            return v;
        }

        public float Single()
        {
            if (_pos + 4 > _data.Length) throw new ReanimFormatException("读取越界(float) @" + _pos);
            var v = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(_pos, 4));
            _pos += 4;
            return v;
        }

        public string String()
        {
            var n = Int32();
            if (n < 0) throw new ReanimFormatException("字符串长度非法: " + n);
            if ((long)_pos + (long)n * 2 > _data.Length) throw new ReanimFormatException("字符串越界 @" + _pos);
            if (n == 0) return string.Empty;
            var s = Encoding.Unicode.GetString(_data, _pos, n * 2);
            _pos += n * 2;
            return s;
        }
    }
}
