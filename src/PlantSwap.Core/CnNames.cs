using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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
    /// 文件不存在时直接返回空表（与 Python 一致，此时不加别名）。
    /// 之后合并别名与 %APPDATA%\PvZPlantSwap\names_override.json。
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

        var ov = Path.Combine(PlantSwap.DataDir, "names_override.json");
        if (File.Exists(ov))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ov, Encoding.UTF8));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String)
                            names[PlantSwap.Norm(p.Name)] = p.Value.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // 覆盖文件损坏时忽略，与 Python 行为一致
            }
        }

        return names;
    }

    /// <summary>
    /// 生成显示名：命中全名 -> “中文名（key）”；命中前缀 -> “中文名·后缀（key）”；否则原样返回 key。
    /// 对应 Python display_name()。
    /// </summary>
    public static string DisplayName(IReadOnlyDictionary<string, string> names, string key)
    {
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
