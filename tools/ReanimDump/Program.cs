using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PvZPlantSwap.Core;

internal static class Program
{
    // 不写死任何人的安装路径：目录必须由命令行 --dir 指定，或按下面的参数猜
    private const string DefaultReanimDir = "";

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* 重定向时忽略 */ }

        var dir = DefaultReanimDir;
        var verify = false;
        var framesFile = (string?)null;
        var trackName = (string?)null;
        var files = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--verify": verify = true; break;
                case "--dir":
                    if (i + 1 < args.Length) dir = args[++i];
                    break;
                case "--frames":
                    if (i + 1 < args.Length) framesFile = args[++i];
                    break;
                case "--track":
                    if (i + 1 < args.Length) trackName = args[++i];
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    files.Add(args[i]);
                    break;
            }
        }

        if (string.IsNullOrEmpty(dir) && files.Count == 0)
        {
            Console.Error.WriteLine("请用 --dir <Content/reanim 目录> 指定 reanim 目录，或直接把 .xnb 路径作为参数。");
            PrintUsage();
            return 2;
        }

        if (verify) return Verify(dir);
        if (framesFile is not null) return DumpFrames(framesFile, trackName);

        if (files.Count == 0)
        {
            files.Add(Path.Combine(dir, "Agave.xnb"));
            files.Add(Path.Combine(dir, "Zombie.xnb"));
        }

        var failed = 0;
        foreach (var f in files)
        {
            if (!File.Exists(f))
            {
                Console.WriteLine("=== " + f + " ===");
                Console.WriteLine("  文件不存在");
                failed++;
                continue;
            }
            try
            {
                DumpFile(f);
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== " + f + " ===");
                Console.WriteLine("  解析失败: " + ex.GetType().Name + ": " + ex.Message);
                failed++;
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ReanimDump — reanim .xnb 解析校验工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  ReanimDump                          解析默认目录下的 Agave.xnb 与 Zombie.xnb");
        Console.WriteLine("  ReanimDump <file.xnb> [...]         解析指定文件");
        Console.WriteLine("  ReanimDump --verify [--dir D]       解析目录下全部 *.xnb 并汇总");
        Console.WriteLine("  ReanimDump --frames <file> [--track NAME]  打印某轨道前 8 帧（原始 + 补全后）");
    }

    private static void DumpFile(string path)
    {
        var data = ReanimData.Load(path);
        var name = Path.GetFileName(path);

        Console.WriteLine("=== " + name + " ===");
        Console.WriteLine("  文件大小: " + data.FileBytes + " 字节，已解析: " + data.ParsedBytes +
                          "，尾部剩余: " + data.TrailingBytes + "（0 表示没有图片矩形表等额外数据块）");
        Console.WriteLine("  类型读取器: " + data.ReaderName);
        Console.WriteLine("  缩放模式: " + data.ScaleMode + " (" + ScaleName(data.ScaleMode) + ")");
        Console.WriteLine("  帧率: " + data.Fps + " fps");
        Console.WriteLine("  轨道数: " + data.TrackCount + "，关键帧总数: " + data.TotalTransforms +
                          "，最长轨道: " + data.MaxFrames + " 帧，时长: " + data.Duration.ToString("0.###", CultureInfo.InvariantCulture) + " 秒");
        Console.WriteLine("  轨道:");

        for (var i = 0; i < data.Tracks.Count; i++)
        {
            var t = data.Tracks[i];
            Console.WriteLine("    [" + i.ToString().PadLeft(2) + "] " + t.Name.PadRight(34) + t.FrameCount.ToString().PadLeft(6) + " 帧" +
                              (t.IsAttacher ? "  (attacher)" : string.Empty));
        }

        var images = data.ImageNames;
        Console.WriteLine("  图片 id: " + images.Count + " 个不同名字");
        foreach (var img in images.Take(3)) Console.WriteLine("    - " + img);

        var first = data.Tracks.Count > 0 && data.Tracks[0].FrameCount > 0 ? data.Tracks[0].Transforms[0] : null;
        if (first is not null)
            Console.WriteLine("  首帧(已补全): mode=" + first.Mode + " image=" + (first.HasImage ? first.ImageName : "<none>") +
                              " alpha=" + F(first.Alpha) + " frame=" + F(first.Frame) +
                              " scale=(" + F(first.ScaleX) + "," + F(first.ScaleY) + ")" +
                              " trans=(" + F(first.TransX) + "," + F(first.TransY) + ")");
        Console.WriteLine();
    }

    private static int DumpFrames(string path, string? trackName)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("文件不存在: " + path);
            return 2;
        }

        var data = ReanimData.FromXnb(File.ReadAllBytes(path), path);
        var track = trackName is null ? data.Tracks.FirstOrDefault() : data.FindTrack(trackName);
        if (track is null)
        {
            Console.Error.WriteLine("找不到轨道: " + trackName);
            return 1;
        }

        Console.WriteLine("=== " + Path.GetFileName(path) + " / " + track.Name + "（共 " + track.FrameCount + " 帧）===");
        Console.WriteLine("原始值（-99999 = 未指定）:");
        PrintFrames(track);
        data.ResolveDefaults();
        Console.WriteLine("ResolveDefaults 之后:");
        PrintFrames(track);
        return 0;
    }

    private static void PrintFrames(ReanimTrack track)
    {
        var n = Math.Min(8, track.FrameCount);
        for (var i = 0; i < n; i++)
        {
            var t = track.Transforms[i];
            Console.WriteLine("  [" + i + "] mode=" + t.Mode +
                              " image=" + (t.HasImage ? t.ImageName : "<none>") +
                              " alpha=" + F(t.Alpha) + " frame=" + F(t.Frame) +
                              " scale=(" + F(t.ScaleX) + "," + F(t.ScaleY) + ")" +
                              " skew=(" + F(t.SkewX) + "," + F(t.SkewY) + ")" +
                              " trans=(" + F(t.TransX) + "," + F(t.TransY) + ")");
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string ScaleName(int mode) => mode switch
    {
        0 => "NoScale",
        1 => "InvertAndScale",
        255 => "ScaleFromPC",
        _ => "unknown",
    };

    private static int Verify(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine("目录不存在: " + dir);
            return 2;
        }

        var files = Directory.GetFiles(dir, "*.xnb");
        Array.Sort(files, StringComparer.Ordinal);

        var ok = 0;
        var bad = 0;
        var tracks = 0;
        var transforms = 0;
        var fpsSet = new SortedDictionary<float, int>();
        var scaleSet = new SortedDictionary<int, int>();
        var withTrailing = 0;

        Console.WriteLine("目录: " + dir);
        Console.WriteLine("文件数: " + files.Length);
        Console.WriteLine();

        foreach (var f in files)
        {
            try
            {
                var data = ReanimData.Load(f, resolveDefaults: false);
                ok++;
                tracks += data.TrackCount;
                transforms += data.TotalTransforms;
                fpsSet[data.Fps] = fpsSet.TryGetValue(data.Fps, out var c) ? c + 1 : 1;
                scaleSet[data.ScaleMode] = scaleSet.TryGetValue(data.ScaleMode, out var c2) ? c2 + 1 : 1;
                if (data.TrailingBytes != 0) withTrailing++;

                Console.WriteLine("OK   " + Path.GetFileName(f).PadRight(34) +
                                  " fps=" + data.Fps.ToString(CultureInfo.InvariantCulture).PadRight(5) +
                                  " scale=" + data.ScaleMode.ToString().PadLeft(3) +
                                  " tracks=" + data.TrackCount.ToString().PadLeft(3) +
                                  " frames=" + data.TotalTransforms.ToString().PadLeft(7) +
                                  " bytes=" + data.FileBytes.ToString().PadLeft(7) +
                                  " trailing=" + data.TrailingBytes);
            }
            catch (Exception ex)
            {
                bad++;
                Console.WriteLine("FAIL " + Path.GetFileName(f).PadRight(34) + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 汇总 ===");
        Console.WriteLine("解析成功: " + ok + " / " + files.Length + "，失败: " + bad);
        Console.WriteLine("轨道总数: " + tracks + "，关键帧总数: " + transforms);
        Console.WriteLine("带尾部多余数据的文件: " + withTrailing + "（0 = 所有文件都精确解析到末尾，说明没有图片矩形表）");
        Console.WriteLine("fps 分布: " + string.Join(", ", fpsSet.Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine("scale 分布: " + string.Join(", ", scaleSet.Select(kv => kv.Key + "(" + ScaleName(kv.Key) + ")=" + kv.Value)));

        return bad == 0 ? 0 : 1;
    }
}
