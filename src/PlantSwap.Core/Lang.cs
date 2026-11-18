using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PvZPlantSwap.Core;

/// <summary>
/// 本地语言文件：&lt;游戏目录&gt;\资源修改器\Language\ 下的四个纯文本文件
/// （plants.txt / zombies.txt / others.txt / actions.txt），每行 <c>资源名=中文</c>。
/// 程序启动时会把内置表里的现有名字填进去，用户可以直接改；留空 = 回落内置名。
/// 程序只<strong>追加</strong>文件里还没有的条目，不会改写或删除用户已经填好的内容。
/// </summary>
public static class Lang
{
    /// <summary>语言文件目录名（&lt;游戏目录&gt;\资源修改器\Language）。</summary>
    public const string DirName = "Language";

    public const string PlantsFile = "plants.txt";
    public const string ZombiesFile = "zombies.txt";
    public const string OthersFile = "others.txt";
    public const string ActionsFile = "actions.txt";

    static readonly Dictionary<string, string> _plants = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> _others = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> _zombies = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> _anims = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前读的语言目录（还没读过时为空）。</summary>
    public static string DirPath { get; private set; } = "";

    /// <summary>动作文件路径（随目录变化）。</summary>
    public static string ActionsPath =>
        DirPath.Length == 0 ? "" : Path.Combine(DirPath, ActionsFile);

    /// <summary>已经生效的自定义名字条数。</summary>
    public static int Count => _plants.Count + _others.Count + _zombies.Count + _anims.Count;

    /// <summary>读整个语言目录；目录或文件不存在就当没有自定义名字。重复调用会整份重读。</summary>
    public static void Load(string? dir)
    {
        DirPath = dir ?? "";
        _plants.Clear();
        _others.Clear();
        _zombies.Clear();
        _anims.Clear();
        if (DirPath.Length == 0) return;

        ReadInto(Path.Combine(DirPath, PlantsFile), _plants);
        ReadInto(Path.Combine(DirPath, OthersFile), _others);
        ReadInto(Path.Combine(DirPath, ZombiesFile), _zombies);
        ReadInto(Path.Combine(DirPath, ActionsFile), _anims);
    }

    static void ReadInto(string path, Dictionary<string, string> map)
    {
        if (!File.Exists(path)) return;
        try
        {
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim().Trim('\uFEFF');
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#' || line[0] == '[') continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;
                if (value.Length == 0) map.Remove(key);   // 留空 = 用内置名
                else map[key] = value;
            }
        }
        catch
        {
            map.Clear();
        }
    }

    static string Lookup(Dictionary<string, string> map, string? key) =>
        string.IsNullOrEmpty(key) ? "" : (map.TryGetValue(key, out var v) ? v : "");

    /// <summary>plants.txt 里的名字。</summary>
    public static string Plant(string? key) => Lookup(_plants, key);

    /// <summary>others.txt 里的名字（音效/粒子/贴图等资源组）。</summary>
    public static string Other(string? key) => Lookup(_others, key);

    /// <summary>zombies.txt 里的名字。</summary>
    public static string Zombie(string? key) => Lookup(_zombies, key);

    /// <summary>actions.txt 里的动作名。</summary>
    public static string Animation(string? name) => Lookup(_anims, name);

    /// <summary>资源名：先看 plants.txt，再看 others.txt。</summary>
    public static string ResourceName(string? key)
    {
        var v = Plant(key);
        return v.Length > 0 ? v : Other(key);
    }

    /// <summary>
    /// 把文件里还没有的条目追加进去（只追加，不动已有内容）。
    /// <paramref name="header"/> 只在新建文件时写一次；传 null 表示不加表头。
    /// </summary>
    public static void EnsureEntries(string? path, string? header, IEnumerable<KeyValuePair<string, string>>? entries)
    {
        if (string.IsNullOrEmpty(path) || entries == null) return;
        try
        {
            var have = ReadKeys(path);
            var add = new List<KeyValuePair<string, string>>();
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Key)) continue;
                if (!have.Add(e.Key)) continue;
                add.Add(e);
            }
            if (add.Count == 0) return;

            var sb = new StringBuilder();
            if (!File.Exists(path) && !string.IsNullOrEmpty(header)) sb.Append(header);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            foreach (var e in add) sb.AppendLine(e.Key + "=" + e.Value);
            if (sb.Length > 0) File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // 只读目录等情况：没有语言文件也能正常用内置名字
        }
    }

    /// <summary>文件里已有的 key（大小写不敏感）。</summary>
    static HashSet<string> ReadKeys(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;
        try
        {
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim().Trim('\uFEFF');
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                var eq = line.IndexOf('=');
                if (eq > 0) set.Add(line.Substring(0, eq).Trim());
            }
        }
        catch { }
        return set;
    }
}
