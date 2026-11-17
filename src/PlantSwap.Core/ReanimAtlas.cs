using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PvZPlantSwap.Core;

/// <summary>图集里某张图片的矩形。</summary>
public readonly struct AtlasRegion
{
    public AtlasRegion(string atlas, int x, int y, int srcWidth, int srcHeight, int width, int height, float invScale)
    {
        Atlas = atlas;
        X = x;
        Y = y;
        SrcWidth = srcWidth;
        SrcHeight = srcHeight;
        Width = width;
        Height = height;
        InvScale = invScale;
    }

    /// <summary>图集名，对应 Content/images/600x1000/&lt;Atlas&gt;.xnb。</summary>
    public string Atlas { get; }

    /// <summary>图集内裁剪左边界。</summary>
    public int X { get; }

    /// <summary>图集内裁剪上边界。</summary>
    public int Y { get; }

    /// <summary>图集里实际存储的像素宽（已按 InvScale 缩小）。裁剪用这个。</summary>
    public int SrcWidth { get; }

    /// <summary>图集里实际存储的像素高。裁剪用这个。</summary>
    public int SrcHeight { get; }

    /// <summary>逻辑宽（游戏按这个尺寸绘制）。</summary>
    public int Width { get; }

    /// <summary>逻辑高。</summary>
    public int Height { get; }

    /// <summary>绘制时的放大系数 = 1/图集缩放（通常是 1.25）。</summary>
    public float InvScale { get; }

    public bool IsEmpty => SrcWidth <= 0 || SrcHeight <= 0;

    public override string ToString() =>
        Atlas + ":" + X + "," + Y + " " + SrcWidth + "x" + SrcHeight + " -> " + Width + "x" + Height;
}

/// <summary>
/// reanim 图片 id（如 IMAGE_REANIM_ZOMBIE_HEAD）→ 图集名 + 矩形。
/// 数据来自反编译 Lawn.dll 里 AtlasResources_600x1000 的 90 个 Unpack*AtlasImages 方法，
/// 内嵌为 ReanimAtlasData.tsv：id \t atlas \t x \t y \t srcW \t srcH \t w \t h \t invScale
/// </summary>
public static class ReanimAtlas
{
    const string ResourceSuffix = "ReanimAtlasData.tsv";

    static readonly Dictionary<string, AtlasRegion> _regions = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> _atlases = new();

    static ReanimAtlas()
    {
        try
        {
            var asm = typeof(ReanimAtlas).Assembly;
            string? name = null;
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase)) { name = n; break; }
            if (name == null) return;

            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) return;
            using var reader = new StreamReader(stream);

            var atlasSet = new HashSet<string>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var f = line.Split('\t');
                if (f.Length < 9) continue;
                var id = f[0];
                var atlas = f[1];
                if (id.Length == 0 || atlas.Length == 0) continue;
                if (!TryInt(f[2], out var x)) continue;
                if (!TryInt(f[3], out var y)) continue;
                if (!TryInt(f[4], out var sw)) continue;
                if (!TryInt(f[5], out var sh)) continue;
                if (!TryInt(f[6], out var w)) continue;
                if (!TryInt(f[7], out var h)) continue;
                if (!float.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var inv) || inv <= 0) inv = 1f;
                _regions[id] = new AtlasRegion(atlas, x, y, sw, sh, w, h, inv);
                if (atlasSet.Add(atlas)) _atlases.Add(atlas);
            }
            _atlases.Sort(StringComparer.Ordinal);
        }
        catch
        {
            // 数据缺失时保持空表，调用方按“无矩形”处理
        }
    }

    static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>表里有多少张图片。</summary>
    public static int Count => _regions.Count;

    /// <summary>全部图集名（升序）。</summary>
    public static IReadOnlyList<string> Atlases => _atlases;

    /// <summary>查某张图片的矩形；没有时返回 false。</summary>
    public static bool TryGet(string? imageId, out AtlasRegion region)
    {
        if (string.IsNullOrEmpty(imageId)) { region = default; return false; }
        return _regions.TryGetValue(imageId, out region);
    }

    /// <summary>图集名 → Content 相对路径（给 ContentManager 用）。</summary>
    public static string ContentPath(string atlas) => "images/600x1000/" + atlas;

    /// <summary>表里全部图片 id。</summary>
    public static IEnumerable<string> ImageIds => _regions.Keys;
}
