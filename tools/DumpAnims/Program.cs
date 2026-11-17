using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine;

class NullLoader : TextureLoader
{
    public void Load(AtlasPage page, string path) { }
    public void Unload(object texture) { }
}

static class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: DumpAnims <Content/spanim 目录>");
            Console.WriteLine("作用: 导出全部 Spine 骨骼的动作名，用来核对 AnimCn 里的中文动作表。");
            return;
        }
        var dir = args[0];
        var all = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var skel in Directory.GetFiles(dir, "*.skel").OrderBy(x => x))
        {
            string key = Path.GetFileNameWithoutExtension(skel);
            string atlasPath = Path.ChangeExtension(skel, ".atlas");
            try
            {
                var atlas = new Atlas(atlasPath, new NullLoader());
                var bin = new SkeletonBinary(atlas);
                SkeletonData data;
                using (var fs = File.OpenRead(skel)) data = bin.ReadSkeletonData(fs);
                var names = new List<string>();
                for (int i = 0; i < data.Animations.Count; i++)
                {
                    var n = data.Animations.Items[i].Name;
                    names.Add(n);
                    counts[n] = counts.TryGetValue(n, out int c) ? c + 1 : 1;
                }
                all[key] = names;
                Console.WriteLine(key + " : " + string.Join(", ", names));
            }
            catch (Exception ex)
            {
                Console.WriteLine(key + " : ERROR " + ex.GetType().Name + " " + ex.Message);
            }
        }
        Console.WriteLine();
        Console.WriteLine("=== animation name frequency ===");
        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
            Console.WriteLine(kv.Value + "\t" + kv.Key);
    }
}
