using System;
using System.Collections.Generic;
using System.IO;

namespace PvZPlantSwap.Core;

/// <summary>单个资源文件的解析结果（对应 Python 的 (key, rem, path) 三元组）。</summary>
public sealed class AssetEntry
{
    /// <summary>所在子目录名：spanim / reanim / particles / sounds / voices / images。</summary>
    public required string Folder { get; init; }

    /// <summary>植物 key（已 norm）。</summary>
    public required string Key { get; init; }

    /// <summary>去掉 key 后的剩余名（已 norm），无剩余时为 ""。</summary>
    public required string Rem { get; init; }

    /// <summary>文件绝对路径。</summary>
    public required string Path { get; init; }

    /// <summary>文件名（含扩展名）。</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    public override string ToString() => Name;
}

/// <summary>扫描结果表：子目录名 -> 该目录下全部资源（按文件名 Ordinal 排序）。</summary>
public sealed class AssetTable
{
    private readonly Dictionary<string, List<AssetEntry>> _map = new(StringComparer.Ordinal);

    /// <summary>取某子目录的资源列表；目录不存在时返回空列表。</summary>
    public IReadOnlyList<AssetEntry> this[string folder] =>
        _map.TryGetValue(folder, out var list) ? list : Array.Empty<AssetEntry>();

    /// <summary>已扫描到的子目录名。</summary>
    public IReadOnlyCollection<string> FolderNames => _map.Keys;

    public void Set(string folder, List<AssetEntry> items) => _map[folder] = items;

    public bool Contains(string folder) => _map.ContainsKey(folder);

    /// <summary>所有子目录的文件总数。</summary>
    public int TotalCount
    {
        get
        {
            var n = 0;
            foreach (var list in _map.Values) n += list.Count;
            return n;
        }
    }
}

/// <summary>family() 的返回项：(rem, path)。</summary>
public sealed class FamilyEntry
{
    public required string Rem { get; init; }
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public override string ToString() => Name;
}

/// <summary>一对待处理的文件（A 与 B），folder 为所属子目录。</summary>
public sealed class AssetPair
{
    public required string A { get; init; }
    public required string B { get; init; }
    public string Folder { get; init; } = "";

    public string NameA => System.IO.Path.GetFileName(A);
    public string NameB => System.IO.Path.GetFileName(B);

    public override string ToString() => $"{Folder}: {NameA} <-> {NameB}";
}

/// <summary>缺失项：某子目录下 A 有而 B 没有（或反之）的文件。</summary>
public sealed class MissingEntry
{
    public required string Folder { get; init; }
    public required string Name { get; init; }
    public override string ToString() => $"{Folder}/{Name}";
}

/// <summary>resolve() 的结果。</summary>
public sealed class ResolveResult
{
    public ResolveResult(List<AssetPair> pairs, List<MissingEntry> missing)
    {
        Pairs = pairs;
        Missing = missing;
    }

    /// <summary>可互换的文件对。</summary>
    public List<AssetPair> Pairs { get; }

    /// <summary>无法配对的文件（仅用于提示，不参与互换）。</summary>
    public List<MissingEntry> Missing { get; }

    public int Count => Pairs.Count;
}

/// <summary>build_lists() 的结果。</summary>
public sealed class BuildListsResult
{
    public BuildListsResult(AssetTable table, List<string> keys, List<string> plants)
    {
        Table = table;
        Keys = keys;
        Plants = plants;
    }

    /// <summary>扫描表。</summary>
    public AssetTable Table { get; }

    /// <summary>全部 key（Ordinal 升序，去重）。</summary>
    public List<string> Keys { get; }

    /// <summary>植物 key 列表（Ordinal 升序）。</summary>
    public List<string> Plants { get; }
}
