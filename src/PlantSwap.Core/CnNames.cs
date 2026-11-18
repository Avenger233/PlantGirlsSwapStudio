using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PvZPlantSwap.Core;

/// <summary>中文名解析与显示名生成，对应 qt_app/core.py 的 load_cn_names / display_name。</summary>
public static class CnNames
{
    /// <summary>后缀中文名映射。</summary>
    public static readonly Dictionary<string, string> SuffixCn = new(StringComparer.Ordinal)
    {
        ["fullattack"] = "连击",
        ["particle"] = "粒子",
        ["tachie"] = "立绘",
        ["garden"] = "花园",
        ["orig"] = "原作",
        ["body"] = "身体",
        ["lower"] = "下半",
        ["upper"] = "上半"
    };

    /// <summary>手工别名（解析结果里没有、但需要展示的 key）。</summary>
    public static readonly Dictionary<string, string> Alias = new(StringComparer.Ordinal)
    {
        ["zorrose"] = "萝丝",
        ["hypnocattail"] = "魅惑香蒲",
        ["hypnocattail2"] = "魅惑香蒲2",
        ["caltrop"] = "地刺",
        ["cornpult"] = "玉米投手",
        ["pumpkinbody"] = "南瓜头"
    };

    private static readonly Regex HeaderRx = new(@"^\[([A-Za-z0-9_]+)\]$", RegexOptions.Compiled);
    private static readonly Regex PunctRx = new(@"[。，！？,.]", RegexOptions.Compiled);

    /// <summary>
    /// 解析 Content/LawnStrings_zh_cn.txt 的 [KEY] + 下一行中文名 格式。
    /// 文件不存在时直接返回空表（此时只加下面的 Alias 手工别名）。
    /// 用户自己想补/改名字请用 &lt;游戏目录&gt;\资源修改器\中文名.txt（见 <see cref="Lang"/>）。
    /// </summary>
    public static Dictionary<string, string> LoadCnNames(string content)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(content, "LawnStrings_zh_cn.txt");
        if (!File.Exists(path)) return names;

        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        string? cur = null;

        foreach (var raw in File.ReadLines(path, enc))
        {
            var line = raw.Trim().Trim('\r');
            if (line.Length == 0) continue;

            var m = HeaderRx.Match(line);
            if (m.Success)
            {
                cur = PlantSwap.Norm(m.Groups[1].Value);
                continue;
            }
            if (cur is null) continue;
            if (line.StartsWith("{", StringComparison.Ordinal))
            {
                cur = null;
                continue;
            }

            if (line.IndexOf(' ') < 0 && line.Length <= 14 && !PunctRx.IsMatch(line))
            {
                if (!names.ContainsKey(cur)) names[cur] = line;
                cur = null;
            }
            else
            {
                cur = null;
            }
        }

        foreach (var kv in Alias)
            if (!names.ContainsKey(kv.Key)) names[kv.Key] = kv.Value;

        return names;
    }

    /// <summary>
    /// 生成显示名：命中全名 -> “中文名（key）”；命中前缀 -> “中文名·后缀（key）”；否则原样返回 key。
    /// 对应 Python display_name()。
    /// </summary>
    public static string DisplayName(IReadOnlyDictionary<string, string> names, string key)
    {
        // 用户自己在「资源修改器\中文名.txt」里填的名字优先
        var custom = Lang.ResourceName(key);
        if (custom.Length > 0) return custom + "（" + key + "）";

        if (names.TryGetValue(key, out var cn) && !string.IsNullOrEmpty(cn))
            return cn + "（" + key + "）";

        string? baseKey = null;
        for (var n = key.Length - 1; n > 2; n--)
        {
            var prefix = key.Substring(0, n);
            if (names.TryGetValue(prefix, out var pv) && !string.IsNullOrEmpty(pv))
            {
                baseKey = prefix;
                break;
            }
        }

        if (baseKey is not null)
        {
            var rest = key.Substring(baseKey.Length);
            var suffix = SuffixCn.TryGetValue(rest, out var s) ? s : rest;
            return names[baseKey] + "·" + suffix + "（" + key + "）";
        }

        return key;
    }
}
