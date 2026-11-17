using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PvZPlantSwap.Core;

/// <summary>备份 / 互换 / 还原。对应 qt_app/core.py 的 do_backup / do_swap / do_restore。</summary>
public static class Swapper
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>相对 content 的路径（与 os.path.relpath 一致）。</summary>
    public static string Rel(string path, string content) => Path.GetRelativePath(content, path);

    /// <summary>等价于 shutil.copy2：复制内容并保留修改/访问时间。</summary>
    public static void CopyFile(string src, string dst)
    {
        var dir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(src, dst, overwrite: true);
        try
        {
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
            File.SetLastAccessTimeUtc(dst, File.GetLastAccessTimeUtc(src));
        }
        catch
        {
            // 时间戳保留失败不影响内容正确性
        }
    }

    /// <summary>
    /// 备份全部配对文件到 BackupRoot/&lt;yyyyMMdd_HHmmss&gt;/ 并写 manifest.json。
    /// 返回本次备份目录。对应 Python do_backup()。
    /// </summary>
    public static string DoBackup(IReadOnlyList<AssetPair> pairs, string content)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var root = Path.Combine(PlantSwap.BackupRoot, stamp);
        Directory.CreateDirectory(root);

        var manifest = new List<Dictionary<string, string>>(pairs.Count);
        foreach (var p in pairs)
        {
            foreach (var path in new[] { p.A, p.B })
            {
                var rel = Rel(path, content);
                CopyFile(path, Path.Combine(root, rel));
            }
            manifest.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a"] = Rel(p.A, content),
                ["b"] = Rel(p.B, content)
            });
        }

        var json = JsonSerializer.Serialize(manifest, ManifestJson);
        File.WriteAllText(Path.Combine(root, "manifest.json"), json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return root;
    }

    /// <summary>
    /// 先备份再执行互换。mode="swap" 双向互换，其他值（"replace"）用 B 覆盖 A。
    /// 返回本次备份目录。对应 Python do_swap()。
    /// </summary>
    public static string DoSwap(IReadOnlyList<AssetPair> pairs, string content, string mode)
    {
        var root = DoBackup(pairs, content);
        var swap = string.Equals(mode, "swap", StringComparison.Ordinal);

        foreach (var p in pairs)
        {
            if (swap)
            {
                var tmp = p.A + ".swaptmp";
                CopyFile(p.A, tmp);
                CopyFile(p.B, p.A);
                CopyFile(tmp, p.B);
                File.Delete(tmp);
            }
            else
            {
                CopyFile(p.B, p.A);
            }
        }
        return root;
    }

    /// <summary>
    /// 从备份目录还原。返回 manifest 中的配对数。对应 Python do_restore()。
    /// </summary>
    public static int DoRestore(string backupDir, string content)
    {
        var mf = Path.Combine(backupDir, "manifest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(mf, Encoding.UTF8));
        var count = 0;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var relA = item.GetProperty("a").GetString() ?? string.Empty;
            var relB = item.GetProperty("b").GetString() ?? string.Empty;
            foreach (var rel in new[] { relA, relB })
            {
                var src = Path.Combine(backupDir, rel);
                if (File.Exists(src)) CopyFile(src, Path.Combine(content, rel));
            }
            count++;
        }
        return count;
    }
}
