using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PvZPlantSwap.Core;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* 输出重定向时忽略 */ }
        try
        {
            return Run(args);
        }
        catch (ArgumentException ex)
        {
            // 参数/目录问题：给一句人话，不打堆栈
            Console.Error.WriteLine("错误: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("失败: " + ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {

        string? game = null, contentArg = null, restoreDir = null, a = null, b = null;
        var mode = "swap";
        var selftest = false;
        var doSwap = false;
        var doPairs = false;
        var force = false;
        var listPlants = false;
        string? poseFile = null, poseTrack = null;
        var poseTime = -1f;
        var poseFrames = 4;
        var poseLayers = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--selftest": selftest = true; break;
                case "--game": game = Take(args, ref i); break;
                case "--content": contentArg = Take(args, ref i); break;
                case "--mode": mode = Take(args, ref i) ?? "swap"; break;
                case "--restore": restoreDir = Take(args, ref i); break;
                case "--pairs":
                    a = Take(args, ref i); b = Take(args, ref i); doPairs = true; break;
                case "--swap":
                    a = Take(args, ref i); b = Take(args, ref i); doSwap = true; break;
                case "--plants": listPlants = true; break;
                case "--force": force = true; break;
                case "--pose": poseFile = Take(args, ref i); break;
                case "--track": poseTrack = Take(args, ref i); break;
                case "--time":
                    if (float.TryParse(Take(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out var pt)) poseTime = pt;
                    break;
                case "--frames":
                    if (int.TryParse(Take(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pf)) poseFrames = pf;
                    break;
                case "--layers": poseLayers = true; break;
                case "-h":
                case "--help": PrintUsage(); return 0;
                default:
                    Console.Error.WriteLine("未知参数: " + args[i]);
                    PrintUsage();
                    return 2;
            }
        }

        if (selftest) return SelfTest(game);

        if (poseFile is not null) return PoseDump(poseFile, poseTrack, poseTime, poseFrames, poseLayers);

        var content = ResolveContent(contentArg, game);

        if (restoreDir is not null)
        {
            if (!Directory.Exists(restoreDir))
            {
                Console.Error.WriteLine("备份目录不存在: " + restoreDir);
                return 1;
            }
            var n = Swapper.DoRestore(restoreDir, content);
            Console.WriteLine("已还原 " + n + " 对文件 -> " + content);
            return 0;
        }

        if (doSwap || doPairs)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                Console.Error.WriteLine("缺少 A / B 参数");
                return 2;
            }
            var table = PlantSwap.Scan(content);
            var res = PlantSwap.Resolve(table, PlantSwap.Folders, a!, b!, force);
            Console.WriteLine("content: " + content);
            Console.WriteLine("pairs " + a + " <-> " + b + ": " + res.Pairs.Count);
            foreach (var p in res.Pairs) Console.WriteLine("  " + p);
            foreach (var m in res.Missing) Console.WriteLine("  missing: " + m);

            if (doSwap)
            {
                if (res.Pairs.Count == 0)
                {
                    Console.Error.WriteLine("没有可互换的文件，已中止");
                    return 1;
                }
                var root = Swapper.DoSwap(res.Pairs, content, mode);
                Console.WriteLine("mode: " + mode + "  备份: " + root);
            }
            return 0;
        }

        if (listPlants)
        {
            var names = CnNames.LoadCnNames(content);
            var built = PlantSwap.BuildLists(content, names);
            Console.WriteLine("content: " + content);
            Console.WriteLine("plants:  " + built.Plants.Count);
            foreach (var k in built.Plants)
                Console.WriteLine("  " + k + "\t" + CnNames.DisplayName(names, k));
            return 0;
        }

        PrintUsage();
        return 0;
    }

    private static string? Take(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) return null;
        i++;
        return args[i];
    }

    private static string ResolveContent(string? contentArg, string? gameArg)
    {
        if (!string.IsNullOrEmpty(contentArg)) return contentArg!;
        var g = gameArg ?? PlantSwap.GuessGame();
        if (string.IsNullOrEmpty(g))
            throw new ArgumentException("没有指定游戏目录，也没能自动找到 Content。请用 --game <游戏根目录> 或 --content <Content 目录>。");
        return PlantSwap.FindContent(g) ?? Path.Combine(g, "Content");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PlantSwapCore — PvZ 植物资源互换核心 (.NET 8)");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PlantSwapCore --selftest [--game DIR]");
        Console.WriteLine("  PlantSwapCore --plants   [--content DIR | --game DIR]");
        Console.WriteLine("  PlantSwapCore --pairs  <A> <B> [--content DIR | --game DIR]");
        Console.WriteLine("  PlantSwapCore --swap   <A> <B> [--mode swap|replace] [--content DIR | --game DIR]");
        Console.WriteLine("  PlantSwapCore --restore <备份目录> [--content DIR | --game DIR]");
        Console.WriteLine("  PlantSwapCore --pose <file.xnb> [--track NAME] [--time T | --frames N]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  --game DIR     游戏根目录（不填则自动从 exe / 当前目录向上找 Content）");
        Console.WriteLine("  --content DIR  直接指定 Content 目录（优先级高于 --game）");
        Console.WriteLine("  --mode MODE    swap=双向互换（默认），replace=用 B 覆盖 A");
        Console.WriteLine("  --force        强制配对：名称对不上的文件按扩展名顺序硬配（配合 --pairs/--swap 使用）");
    }

    private static int PoseDump(string path, string? layerName, float time, int frames, bool listLayers)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("文件不存在: " + path);
            return 2;
        }

        var data = ReanimData.Load(path);
        var images = data.ImageNames;
        var hit = 0;
        foreach (var id in images) if (ReanimAtlas.TryGet(id, out _)) hit++;

        Console.WriteLine("=== " + Path.GetFileName(path) + " ===");
        Console.WriteLine("  帧率: " + F(data.Fps) + " fps, 轨道: " + data.TrackCount +
                          ", 关键帧: " + data.TotalTransforms + ", 时长: " + F(data.Duration) + " 秒");
        Console.WriteLine("  图集表: " + ReanimAtlas.Count + " 条矩形 / " + ReanimAtlas.Atlases.Count +
                          " 个图集, 本文件图片可解析 " + hit + "/" + images.Count);

        if (listLayers)
        {
            Console.WriteLine("  动画层（轨道名 / 可见帧范围 / 时长）:");
            for (var i = 0; i < data.Tracks.Count; i++)
            {
                var tr = data.Tracks[i];
                var lr = ReanimPoseBuilder.GetLayerRange(data, i);
                Console.WriteLine("    [" + i.ToString().PadLeft(2) + "] " + tr.Name.PadRight(36) +
                                  " frames " + lr.Start.ToString().PadLeft(4) + ".." + lr.End.ToString().PadLeft(4) +
                                  " (" + lr.Count.ToString().PadLeft(4) + " 帧)" +
                                  " " + F(lr.Count / (data.Fps > 0f ? data.Fps : 12f)) + "s");
            }
            return 0;
        }

        var layerIndex = FindLayer(data, layerName);
        if (layerIndex < 0)
        {
            Console.Error.WriteLine("找不到轨道/动画层: " + layerName);
            return 2;
        }

        var layer = data.Tracks[layerIndex];
        var range = ReanimPoseBuilder.GetLayerRange(data, layerIndex);
        Console.WriteLine("  动画层: " + layer.Name + "  帧 " + range.Start + ".." + range.End +
                          "（" + range.Count + " 帧, " + F(range.Count / (data.Fps > 0f ? data.Fps : 12f)) + " 秒）");

        var layerSeconds = range.Count / (data.Fps > 0f ? data.Fps : 12f);
        var times = new List<float>();
        if (time >= 0f) times.Add(time);
        else if (frames <= 1) times.Add(0f);
        else for (var i = 0; i < frames; i++) times.Add(layerSeconds * i / (frames - 1));

        var union = ReanimRect.Empty;
        var nanTotal = 0;

        foreach (var t in times)
        {
            var pose = ReanimPoseBuilder.Build(data, layerIndex, t);
            union = ReanimRect.Union(union, pose.Bounds);

            var nan = 0;
            foreach (var it in pose.Items) if (IsNaN(it)) nan++;
            nanTotal += nan;

            Console.WriteLine("  t=" + t.ToString("0.000", CultureInfo.InvariantCulture) + "s layerFrame=" + pose.LayerFrame +
                              " frame=" + pose.Frame + " items=" + pose.Items.Count + " bounds=" + pose.Bounds +
                              (nan > 0 ? "  NaN=" + nan : string.Empty));

            if (layerName is not null)
            {
                var shown = 0;
                foreach (var it in pose.Items)
                {
                    if (shown >= 10)
                    {
                        Console.WriteLine("    ... 其余 " + (pose.Items.Count - 10) + " 项省略");
                        break;
                    }
                    shown++;
                    var quad = ReanimPoseBuilder.GetQuad(it);
                    Console.WriteLine("    " + it.TrackName + ": " + it);
                    Console.WriteLine("      region=" + (it.HasRegion ? it.Region.ToString() : "<missing>") +
                                      " logical=" + F(it.LocalWidth) + "x" + F(it.LocalHeight) + " cel=" + it.Cel);
                    Console.WriteLine("      quad=" + quad[0] + quad[1] + quad[2] + quad[3]);
                }
                if (pose.Items.Count == 0) Console.WriteLine("    (这一帧没有可绘制项)");
            }
        }

        Console.WriteLine("  采样并集包围盒: " + union + "  NaN 项: " + nanTotal);
        if (union.IsEmpty) Console.WriteLine("  WARN: 包围盒为空（没有任何可绘制项）");
        else if (union.Width <= 0f || union.Height <= 0f) Console.WriteLine("  WARN: 包围盒尺寸为 0");
        return nanTotal == 0 ? 0 : 1;
    }

    private static int FindLayer(ReanimData data, string? name)
    {
        if (name is not null) return ReanimPoseBuilder.IndexOfTrack(data, name);

        // 优先 anim_idle；但它有时只是 1 帧的标记，太短就取 anim_* 里帧数最多的一条
        var idle = ReanimPoseBuilder.IndexOfTrack(data, "anim_idle");
        if (idle >= 0 && ReanimPoseBuilder.GetLayerRange(data, idle).Count >= 5) return idle;

        var best = -1;
        var bestCount = 0;
        for (var i = 0; i < data.Tracks.Count; i++)
        {
            if (!data.Tracks[i].Name.StartsWith("anim_", StringComparison.OrdinalIgnoreCase)) continue;
            var count = ReanimPoseBuilder.GetLayerRange(data, i).Count;
            if (count > bestCount) { bestCount = count; best = i; }
        }

        if (best >= 0) return best;
        if (idle >= 0) return idle;
        return data.Tracks.Count > 0 ? 0 : -1;
    }

    private static bool IsNaN(in ReanimDrawItem it) =>
        float.IsNaN(it.M11) || float.IsNaN(it.M12) || float.IsNaN(it.M21) ||
        float.IsNaN(it.M22) || float.IsNaN(it.M31) || float.IsNaN(it.M32) || float.IsNaN(it.Alpha);

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static int SelfTest(string? gameArg)
    {
        var game = gameArg ?? PlantSwap.GuessGame();
        var content = PlantSwap.FindContent(game) ?? Path.Combine(game, "Content");

        Console.WriteLine("game:    " + game);
        Console.WriteLine("content: " + content);
        if (!Directory.Exists(content))
        {
            Console.WriteLine("FAIL: content 目录不存在");
            return 1;
        }

        var names = CnNames.LoadCnNames(content);
        var built = PlantSwap.BuildLists(content, names);

        Console.WriteLine("cn-names: " + names.Count);
        Console.WriteLine("keys:     " + built.Keys.Count);
        Console.WriteLine("plants:   " + built.Plants.Count);
        foreach (var folder in PlantSwap.Folders)
            Console.WriteLine("  " + folder.PadRight(10) + built.Table[folder].Count + " files");

        var res = PlantSwap.Resolve(built.Table, PlantSwap.Folders, "agave", "sunflower");
        Console.WriteLine("pairs agave<->sunflower: " + res.Pairs.Count);
        foreach (var g in res.Pairs.GroupBy(p => p.Folder))
            Console.WriteLine("  " + g.Key + ": " + g.Count());
        Console.WriteLine("missing agave<->sunflower: " + res.Missing.Count);
        foreach (var m in res.Missing)
            Console.WriteLine("  " + m.Folder + "/" + m.Name);

        Console.WriteLine("display(agave):            " + CnNames.DisplayName(names, "agave"));
        Console.WriteLine("display(sunflower):        " + CnNames.DisplayName(names, "sunflower"));
        Console.WriteLine("display(agavefullattack):  " + CnNames.DisplayName(names, "agavefullattack"));
        Console.WriteLine("display(zorrose):          " + CnNames.DisplayName(names, "zorrose"));

        var ok = RoundTripTest();
        Console.WriteLine("swap-roundtrip(temp): " + (ok ? "OK" : "FAIL"));
        return ok ? 0 : 1;
    }

    /// <summary>在临时假目录里跑 backup/swap/replace/restore 全流程，绝不碰真实游戏目录。</summary>
    private static bool RoundTripTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pgvz_selftest_" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "Content");

        void Write(string rel, string text)
        {
            var p = Path.Combine(content, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, text);
        }

        string Read(string rel) => File.ReadAllText(Path.Combine(content, rel));

        try
        {
            Write("spanim/agave.atlas", "A-atlas");
            Write("spanim/agave.png", "A-png");
            Write("spanim/agave.skel", "A-skel");
            Write("spanim/sunflower.atlas", "B-atlas");
            Write("spanim/sunflower.png", "B-png");
            Write("spanim/sunflower.skel", "B-skel");
            Write("reanim/Agave.xnb", "A-reanim");
            Write("reanim/AgaveFullattack.xnb", "A-fullattack");
            Write("reanim/SunFlower.xnb", "B-reanim");
            Write("sounds/agave_attack.xnb", "A-snd");
            Write("sounds/sunflower_attack.xnb", "B-snd");
            Write("voices/agave-plant1.ogg", "A-voice");
            Write("voices/sunflower-plant1.ogg", "B-voice");

            PlantSwap.BackupRootOverride = Path.Combine(root, "backup");

            var table = PlantSwap.Scan(content);
            var res = PlantSwap.Resolve(table, PlantSwap.Folders, "agave", "sunflower");
            if (res.Pairs.Count != 6)
            {
                Console.WriteLine("  roundtrip: 期望 6 对，实际 " + res.Pairs.Count);
                return false;
            }
            if (res.Missing.Count != 1 || res.Missing[0].Name != "AgaveFullattack.xnb")
            {
                Console.WriteLine("  roundtrip: missing 不符合预期 (" + res.Missing.Count + ")");
                return false;
            }

            var backup = Swapper.DoSwap(res.Pairs, content, "swap");
            if (!File.Exists(Path.Combine(backup, "manifest.json")))
            {
                Console.WriteLine("  roundtrip: 未生成 manifest.json");
                return false;
            }
            if (Read("spanim/agave.atlas") != "B-atlas" || Read("spanim/sunflower.atlas") != "A-atlas")
            {
                Console.WriteLine("  roundtrip: swap 后内容未互换");
                return false;
            }

            var restored = Swapper.DoRestore(backup, content);
            if (restored != 6)
            {
                Console.WriteLine("  roundtrip: restore 数量 " + restored);
                return false;
            }
            if (Read("spanim/agave.atlas") != "A-atlas" || Read("spanim/sunflower.atlas") != "B-atlas")
            {
                Console.WriteLine("  roundtrip: restore 后内容未复原");
                return false;
            }

            Swapper.DoSwap(res.Pairs, content, "replace");
            if (Read("spanim/agave.atlas") != "B-atlas" || Read("spanim/sunflower.atlas") != "B-atlas")
            {
                Console.WriteLine("  roundtrip: replace 行为不正确");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  roundtrip 异常: " + ex.Message);
            return false;
        }
        finally
        {
            PlantSwap.BackupRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
