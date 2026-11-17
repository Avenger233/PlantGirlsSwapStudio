using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PvZPlantSwap.Core;

/// <summary>
/// 资源互换核心逻辑，行为与 qt_app/core.py 保持一致。
/// 纯逻辑，无 UI 依赖，可被 WPF/WinForms/控制台直接调用。
/// </summary>
public static class PlantSwap
{
    /// <summary>
    /// 默认游戏根目录。留空：由命令行 --game / --content、配置文件或 <see cref="GuessGame"/>
    /// 的自动探测决定，避免把某个人的安装路径写死进代码。
    /// </summary>
    public const string DefaultGame = "";

    /// <summary>
    /// 猜游戏目录：依次看 exe 所在目录、当前工作目录及其上级（最多三层），谁能找到 Content 就用谁。
    /// 找不到返回空字符串。
    /// </summary>
    public static string GuessGame()
    {
        // 不做启发式搜索：工具就是放在游戏目录里跑的，exe 在哪就以哪为游戏目录。
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!string.IsNullOrEmpty(exeDir) && FindContent(exeDir) is not null) return exeDir;
        var cwd = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar);
        if (!string.IsNullOrEmpty(cwd) && FindContent(cwd) is not null) return cwd;
        return "";
    }

    /// <summary>参与扫描的 Content 子目录（顺序即 resolve 的遍历顺序）。</summary>
    public static readonly string[] Folders = { "spanim", "reanim", "particles", "sounds", "voices", "images" };

    /// <summary>strip_ext 识别的小写扩展名。</summary>
    public static readonly string[] Exts = { ".xnb", ".atlas", ".skel", ".json", ".png", ".ogg", ".old", ".meta" };

    /// <summary>images 贴图前缀（长的在前，否则 ThumbnailCartoon 会被 cartoon 抢先匹配）。去掉后归到植物自己的 key。</summary>
    public static readonly string[] ImagePrefixes = { "ThumbnailCartoon", "cartoon", "painting" };

    /// <summary>看着像植物、实际不是的 key（漫画图 / 护盾特效），一律不进植物列表。</summary>
    public static readonly string[] NotAPlant = { "pumpkin", "pumpkinlower", "pumpkinupper" };

    /// <summary>非植物关键字（含这些子串的 reanim key 不当作植物）。</summary>
    public static readonly string[] NonPlant =
    {
        "zombie", "coin", "sun", "portal", "rain", "load", "selector", "rake",
        "pool", "roof", "zen", "tree", "text", "hammer", "slot", "credits",
        "mower", "lawn", "stinky", "food", "final", "start", "sod", "diamond",
        "gold", "silver", "note", "mail", "almanac", "store", "award", "ui"
    };

    /// <summary>
    /// 数据目录：优先 exe 同级的「资源修改器」目录（跟备份、Mod、ini 放一起），
    /// 该位置不可写时退回 %APPDATA%\PvZPlantSwap。
    /// </summary>
    public static string DataDir { get; }

    /// <summary>备份根目录，默认 %APPDATA%\PvZPlantSwap\backup；设为非空可覆盖（测试用）。</summary>
    public static string? BackupRootOverride { get; set; }

    /// <summary>当前生效的备份根目录。</summary>
    public static string BackupRoot => BackupRootOverride ?? Path.Combine(DataDir, "backup");

    /// <summary>预览目录（供上层 UI 使用）。</summary>
    public static string PreviewDir => Path.Combine(DataDir, "preview");

    static PlantSwap()
    {
        // 优先生成在 exe 同级的工具目录里，跟备份/Mod/ini 放一起
        var beside = Path.Combine(AppContext.BaseDirectory, "资源修改器");
        if (CanWrite(beside)) { DataDir = beside; return; }

        var appdata = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appdata)) appdata = AppContext.BaseDirectory;
        DataDir = Path.Combine(appdata!, "PvZPlantSwap");
        try { Directory.CreateDirectory(DataDir); } catch { /* 只读环境下忽略 */ }
    }

    static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>小写并只保留字母/数字。对应 Python norm()。</summary>
    public static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>该文件是不是骨骼源（.skel，或旧版导出的 .json）。spanim 里只有骨骼源才算植物。</summary>
    public static bool HasSpineSource(string path) =>
        path.EndsWith(".skel", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>去掉已知扩展名（大小写不敏感）。对应 Python strip_ext()。</summary>
    public static string StripExt(string name)
    {
        if (string.IsNullOrEmpty(name)) return name ?? string.Empty;
        foreach (var ext in Exts)
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - ext.Length);
        return name;
    }

    /// <summary>
    /// 解析文件名 -> (key, rem)。对应 Python parse(folder, name)。
    /// sounds 用第一个 '_' 分隔，voices 用第一个 '-' 分隔；
    /// images 先剥掉 ImagePrefixes 里的画册前缀，再按“首个数字之前”切分；其余整个 stem 作 key。
    /// </summary>
    public static (string Key, string Rem) Parse(string folder, string name)
    {
        var stem = StripExt(name);
        switch (folder)
        {
            case "sounds":
            {
                var i = stem.IndexOf('_');
                if (i < 0) return (Norm(stem), string.Empty);
                return (Norm(stem.Substring(0, i)), Norm(stem.Substring(i + 1)));
            }
            case "voices":
            {
                var i = stem.IndexOf('-');
                if (i < 0) return (Norm(stem), string.Empty);
                return (Norm(stem.Substring(0, i)), Norm(stem.Substring(i + 1)));
            }
            case "images":
            {
                // 画册贴图带 painting / cartoon / ThumbnailCartoon 前缀，先剥掉，让它们归到植物自己的 key
                var body = stem;
                foreach (var prefix in ImagePrefixes)
                    if (body.Length > prefix.Length && body.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        body = body.Substring(prefix.Length);
                        break;
                    }
                var i = 0;
                while (i < body.Length && !char.IsDigit(body[i])) i++;
                var head = i > 0 ? body.Substring(0, i) : body;
                return (Norm(head), Norm(body.Substring(i)));
            }
            default:
                return (Norm(stem), string.Empty);
        }
    }

    /// <summary>扫描 Content 下的 6 个子目录。对应 Python scan()。</summary>
    public static AssetTable Scan(string content)
    {
        var table = new AssetTable();
        foreach (var folder in Folders)
        {
            var dir = Path.Combine(content, folder);
            if (!Directory.Exists(dir)) continue;

            var files = Directory.EnumerateFileSystemEntries(dir)
                .Where(File.Exists)
                .ToList();

            // images 的贴图全在 images/600x1000 里，顶层只有子目录，必须再扫一层
            if (folder == "images")
            {
                var sub = Path.Combine(dir, "600x1000");
                if (Directory.Exists(sub))
                    files.AddRange(Directory.EnumerateFileSystemEntries(sub).Where(File.Exists));
            }

            var items = new List<AssetEntry>(files.Count);
            foreach (var path in files.OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                var (key, rem) = Parse(folder, Path.GetFileName(path));
                items.Add(new AssetEntry
                {
                    Folder = folder,
                    Key = key,
                    Rem = rem,
                    Path = path
                });
            }
            table.Set(folder, items);
        }
        return table;
    }

    /// <summary>
    /// 取某植物在某子目录下的全部文件。对应 Python family()。
    /// sounds/voices 只认 key 精确相等且保留解析出的 rem；
    /// 其他目录精确相等时 rem 强制为 ""，前缀匹配时 rem 为剩余部分。
    /// </summary>
    public static List<FamilyEntry> Family(AssetTable table, string folder, string plant)
    {
        var result = new List<FamilyEntry>();
        var items = table[folder];
        var isSoundLike = folder == "sounds" || folder == "voices";

        foreach (var e in items)
        {
            if (isSoundLike)
            {
                if (e.Key == plant)
                    result.Add(new FamilyEntry { Rem = e.Rem, Path = e.Path });
                continue;
            }

            if (e.Key == plant)
                result.Add(new FamilyEntry { Rem = string.Empty, Path = e.Path });
            else if (e.Key.StartsWith(plant, StringComparison.Ordinal))
                result.Add(new FamilyEntry { Rem = e.Key.Substring(plant.Length), Path = e.Path });
        }
        return result;
    }

    /// <summary>
    /// 按 rem 一一配对（先到先得，每个 B 只用一次）。对应 Python pair_family()。
    /// </summary>
    public static (List<AssetPair> Pairs, List<string> Missing) PairFamily(
        IReadOnlyList<FamilyEntry> fa, IReadOnlyList<FamilyEntry> fb)
    {
        var pairs = new List<AssetPair>();
        var missing = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var a in fa)
        {
            string? match = null;
            foreach (var b in fb)
            {
                if (string.Equals(b.Rem, a.Rem, StringComparison.Ordinal) && !used.Contains(b.Path))
                {
                    match = b.Path;
                    break;
                }
            }
            if (match is null)
            {
                missing.Add(a.Path);
                continue;
            }
            used.Add(match);
            pairs.Add(new AssetPair { A = a.Path, B = match, Folder = "" });
        }
        return (pairs, missing);
    }

    /// <summary>
    /// 求 A、B 两个植物在各子目录下的配对。对应 Python resolve()。
    /// spanim 额外要求 (rem, 扩展名) 都相同。
    /// </summary>
    /// <param name="table">已扫描的资源表。</param>
    /// <param name="folders">参与处理的子目录。</param>
    /// <param name="a">植物 A 的 key。</param>
    /// <param name="b">植物 B 的 key。</param>
    /// <param name="force">
    /// true = 强制配对：名称/后缀对不上的文件，在同一扩展名内按文件名顺序强行配成一对。
    /// 不会新增或删除 B 的文件，只是把原本「跳过」的那些也配上。
    /// </param>
    public static ResolveResult Resolve(AssetTable table, IReadOnlyList<string> folders, string a, string b, bool force = false)
    {
        var ka = Norm(a);
        var kb = Norm(b);
        var pairs = new List<AssetPair>();
        var missing = new List<MissingEntry>();

        foreach (var folder in folders)
        {
            var fa = Family(table, folder, ka);
            var fb = Family(table, folder, kb);

            if (fa.Count == 0 || fb.Count == 0)
            {
                if (fa.Count > 0 || fb.Count > 0)
                    missing.Add(new MissingEntry { Folder = folder, Name = fa.Count > 0 ? a : b });
                continue;
            }

            int before = pairs.Count;
            var folderMissing = new List<MissingEntry>();

            if (folder == "spanim")
            {
                var idx = new Dictionary<(string Rem, string Ext), string>();
                foreach (var e in fb)
                    idx[(e.Rem, Path.GetExtension(e.Path).ToLowerInvariant())] = e.Path;

                foreach (var e in fa)
                {
                    var key = (e.Rem, Path.GetExtension(e.Path).ToLowerInvariant());
                    if (idx.TryGetValue(key, out var pb))
                        pairs.Add(new AssetPair { A = e.Path, B = pb, Folder = folder });
                    else
                        folderMissing.Add(new MissingEntry { Folder = folder, Name = Path.GetFileName(e.Path) });
                }
            }
            else
            {
                var (fp, fm) = PairFamily(fa, fb);
                foreach (var p in fp)
                    pairs.Add(new AssetPair { A = p.A, B = p.B, Folder = folder });
                foreach (var m in fm)
                    folderMissing.Add(new MissingEntry { Folder = folder, Name = Path.GetFileName(m) });
            }

            if (force)
            {
                var usedA = new HashSet<string>(StringComparer.Ordinal);
                var usedB = new HashSet<string>(StringComparer.Ordinal);
                for (var i = before; i < pairs.Count; i++) { usedA.Add(pairs[i].A); usedB.Add(pairs[i].B); }

                var leftA = fa.Where(e => !usedA.Contains(e.Path)).Select(e => e.Path)
                              .OrderBy(p => p, StringComparer.Ordinal).ToList();
                var leftB = fb.Where(e => !usedB.Contains(e.Path)).Select(e => e.Path)
                              .OrderBy(p => p, StringComparer.Ordinal).ToList();

                var exts = leftA.Select(Path.GetExtension)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var ext in exts)
                {
                    var la = leftA.Where(p => string.Equals(Path.GetExtension(p), ext, StringComparison.OrdinalIgnoreCase)).ToList();
                    var lb = leftB.Where(p => string.Equals(Path.GetExtension(p), ext, StringComparison.OrdinalIgnoreCase)).ToList();

                    // 不再按文件名顺序硬配：先按「名字相似度」撮合（同尾号优先，其次最长公共前缀），
                    // 这样 painting_marigold_0 会优先配 painting_zombie_0，而不是随便抓一个文件。
                    foreach (var pa in la)
                    {
                        string? bestPath = null;
                        int bestScore = int.MinValue;
                        foreach (var pb in lb)
                        {
                            if (usedB.Contains(pb)) continue;
                            int sc = PairScore(pa, pb);
                            if (sc > bestScore) { bestScore = sc; bestPath = pb; }
                        }
                        // 名字压根不像（比如 painting_marigold_0 vs zombienote1）就别硬配，留成「跳过」更诚实
                        if (bestPath is null || bestScore < 4) break;
                        pairs.Add(new AssetPair { A = pa, B = bestPath, Folder = folder });
                        usedA.Add(pa);
                        usedB.Add(bestPath);
                    }
                }

                folderMissing.Clear();
                foreach (var p in leftA)
                    if (!usedA.Contains(p))
                        folderMissing.Add(new MissingEntry { Folder = folder, Name = Path.GetFileName(p) });
            }

            missing.AddRange(folderMissing);
        }

        return new ResolveResult(pairs, missing);
    }

    /// <summary>强制配对时的相似度：尾号相同最优先，其次看文件名公共前缀长度。</summary>
    static int PairScore(string a, string b)
    {
        var na = Path.GetFileNameWithoutExtension(a);
        var nb = Path.GetFileNameWithoutExtension(b);
        int n = Math.Min(na.Length, nb.Length), i = 0;
        while (i < n && char.ToLowerInvariant(na[i]) == char.ToLowerInvariant(nb[i])) i++;
        int score = i;

        int ta = TrailingNumber(na), tb = TrailingNumber(nb);
        if (ta >= 0 && tb >= 0)
            score += ta == tb ? 1000 : -Math.Min(50, Math.Abs(ta - tb));
        return score;
    }

    static int TrailingNumber(string stem)
    {
        int i = stem.Length;
        while (i > 0 && char.IsDigit(stem[i - 1])) i--;
        if (i == stem.Length) return -1;
        return int.TryParse(stem.AsSpan(i), out var v) ? v : -1;
    }

    /// <summary>定位 Content 目录（优先 &lt;game&gt;/Content，其次 &lt;game&gt; 本身）。对应 Python find_content()。</summary>
    public static string? FindContent(string gameDir)
    {
        foreach (var cand in new[] { Path.Combine(gameDir, "Content"), gameDir })
            if (Directory.Exists(Path.Combine(cand, "spanim")))
                return cand;
        return null;
    }

    /// <summary>
    /// 扫描并生成 key / 植物列表。对应 Python build_lists()。
    /// 植物 = spanim 中带骨骼源（.skel / .json）的 key + reanim 中有中文名且不含非植物关键字的 key。
    /// </summary>
    static bool IsNumericKey(string k)
    {
        if (string.IsNullOrEmpty(k)) return true;
        foreach (var c in k) if (!char.IsDigit(c)) return false;
        return true;
    }

    public static BuildListsResult BuildLists(string content, IReadOnlyDictionary<string, string> names)
    {
        var table = Scan(content);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var spanim = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in Folders)
            foreach (var e in table[folder])
            {
                // spanim 里只有骨骼源才算植物：chomper_2.png 这类是图集分页，不能单独当成一个植物
                if (folder == "spanim")
                {
                    if (!HasSpineSource(e.Path)) continue;
                    spanim.Add(e.Key);
                }
                keys.Add(e.Key);
            }

        var reanim = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in table["reanim"]) reanim.Add(e.Key);

        // spanim 里“以别的骨骼 key 为前缀”的是附属资源（立绘 / 部件 / 弹道），不是独立植物：
        // squash2 → 窝瓜·2、fumeshroomtachie → 大喷菇·立绘、endoflamespike → 火红莲·spike
        // 它们仍留在所属植物的 Family 里，互换/替换时跟着母体一起走。
        var subAssets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in spanim)
            foreach (var o in spanim)
                if (o.Length < k.Length && k.StartsWith(o, StringComparison.Ordinal))
                {
                    subAssets.Add(k);
                    break;
                }

        var plants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in spanim)
            if (!subAssets.Contains(k)) plants.Add(k);
        foreach (var k in reanim)
        {
            if (spanim.Contains(k)) continue;
            if (NonPlant.Any(bad => k.Contains(bad, StringComparison.Ordinal))) continue;
            if (names.TryGetValue(k, out var cn) && !string.IsNullOrEmpty(cn))
                plants.Add(k);
        }

        // 漫画图 / 护盾特效这类同名的 key 直接剔掉
        foreach (var k in NotAPlant) plants.Remove(k);
        // 纯数字 key（images 里 190306 这种编号图集页）不是植物
        plants.RemoveWhere(IsNumericKey);

        return new BuildListsResult(
            table,
            keys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            plants.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }
}
