using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PlantSwap.Editor;

/// <summary>Spine .atlas 里的一个部件（区域）。</summary>
public sealed class AtlasRegionInfo
{
    public string Name = "";
    public int X, Y, W, H;          // 在 png 里的像素矩形
    public int Degrees;             // 0 / 90 / 180 / 270（4.x 直接写角度，3.x 是 true/false）
    public int OrigW, OrigH;        // 未裁边前的原始尺寸
    public int OffsetX, OffsetY;
    public int Index = -1;

    public int Right => X + W;
    public int Bottom => Y + H;
    public bool Rotated => Degrees % 360 != 0;

    public override string ToString() => Name + "  " + W + "×" + H + " @ " + X + "," + Y;
}

/// <summary>
/// 解析 Spine .atlas 文本。这个游戏的资源是 4.x 格式，页面属性和部件属性都可能顶格写：
/// <code>
/// garlic.png
/// size:2048,664
/// scale:0.5
/// arm_L
/// bounds:1981,499,65,111
/// body
/// bounds:1757,325,138,224
/// rotate:270
/// dang
/// bounds:445,619,35,43
/// offsets:1,0,36,43
/// </code>
/// 3.x 的老格式（xy / size / orig / offset，带缩进）也一起兼容。
/// </summary>
public static class AtlasText
{
    static readonly HashSet<string> PageKeys = new(StringComparer.Ordinal)
    {
        "size", "format", "filter", "repeat", "pma", "scale"
    };

    public static List<AtlasRegionInfo> Parse(string path)
    {
        var list = new List<AtlasRegionInfo>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;

        AtlasRegionInfo cur = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var key = line.Substring(0, colon).Trim();
                var val = line.Substring(colon + 1).Trim();

                if (PageKeys.Contains(key)) { cur = null; continue; }
                if (cur == null) continue;

                switch (key)
                {
                    case "bounds":                       // 4.x：x,y,w,h
                        ReadInts(val, out cur.X, out cur.Y, out cur.W, out cur.H);
                        break;
                    case "offsets":                      // 4.x：offsetX,offsetY,origW,origH
                        ReadInts(val, out cur.OffsetX, out cur.OffsetY, out cur.OrigW, out cur.OrigH);
                        break;
                    case "rotate":
                        cur.Degrees = ParseDegrees(val);
                        break;
                    case "xy":                           // 3.x
                        ReadInts(val, out cur.X, out cur.Y);
                        break;
                    case "size":                         // 3.x 部件尺寸（页面属性已在上面跳过）
                        ReadInts(val, out cur.W, out cur.H);
                        break;
                    case "orig":
                        ReadInts(val, out cur.OrigW, out cur.OrigH);
                        break;
                    case "offset":
                        ReadInts(val, out cur.OffsetX, out cur.OffsetY);
                        break;
                    case "index":
                        int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out cur.Index);
                        break;
                }
                continue;
            }

            // 没有冒号：页面文件名或部件名
            if (line.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                cur = null;
                continue;
            }
            cur = new AtlasRegionInfo { Name = line };
            list.Add(cur);
        }

        list.RemoveAll(r => r.W <= 0 || r.H <= 0);
        return list;
    }

    static int ParseDegrees(string val)
    {
        if (val.StartsWith("true", StringComparison.OrdinalIgnoreCase)) return 90;
        if (val.StartsWith("false", StringComparison.OrdinalIgnoreCase)) return 0;
        return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    static void ReadInts(string s, out int a, out int b, out int c, out int d)
    {
        a = b = c = d = 0;
        var parts = s.Split(',');
        if (parts.Length > 0) int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a);
        if (parts.Length > 1) int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
        if (parts.Length > 2) int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out c);
        if (parts.Length > 3) int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out d);
    }

    static void ReadInts(string s, out int a, out int b)
    {
        ReadInts(s, out a, out b, out _, out _);
    }
}
