using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PlantSwap.Editor;

/// <summary>
/// 一张立绘图集 + 它的 atlas 部件表。所有编辑都在 CPU 的 Pixels 上做，
/// 改完 Upload() 同步到纹理；保存时写回 png（第一次保存前留一份 .orig.png）。
/// </summary>
public sealed class TextureDoc : IDisposable
{
    public string PngPath = "";
    public string AtlasPath = "";
    public int Width, Height;
    public Color[] Pixels;
    public List<AtlasRegionInfo> Regions = new();
    public Texture2D Tex;

    readonly List<Color[]> _undo = new();
    const int MaxUndo = 5;

    public static TextureDoc Load(GraphicsDevice gd, string pngPath, string atlasPath)
    {
        var doc = new TextureDoc { PngPath = pngPath, AtlasPath = atlasPath };
        using (var fs = File.OpenRead(pngPath)) doc.Tex = Texture2D.FromStream(gd, fs);
        doc.Width = doc.Tex.Width;
        doc.Height = doc.Tex.Height;
        doc.Pixels = new Color[doc.Width * doc.Height];
        doc.Tex.GetData(doc.Pixels);
        if (!string.IsNullOrEmpty(atlasPath) && File.Exists(atlasPath))
            doc.Regions = AtlasText.Parse(atlasPath);
        return doc;
    }

    public string Name => Path.GetFileNameWithoutExtension(PngPath);
    public bool CanUndo => _undo.Count > 0;
    public int UndoDepth => _undo.Count;

    public void Upload() => Tex.SetData(Pixels);

    public void PushUndo()
    {
        _undo.Add((Color[])Pixels.Clone());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        Pixels = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        Upload();
        return true;
    }

    public Color Get(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? Color.Transparent : Pixels[y * Width + x];

    public void Set(int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        Pixels[y * Width + x] = c;
    }

    /// <summary>把某个部件清成透明。</summary>
    public void ClearRegion(AtlasRegionInfo r)
    {
        PushUndo();
        for (var y = r.Y; y < r.Bottom; y++)
            for (var x = r.X; x < r.Right; x++)
                Set(x, y, Color.Transparent);
        Upload();
    }

    /// <summary>把某张图（sw×sh）缩放贴进部件矩形。</summary>
    public void PasteRegion(AtlasRegionInfo r, Color[] src, int sw, int sh)
    {
        if (src == null || sw <= 0 || sh <= 0) return;
        PushUndo();
        for (var y = 0; y < r.H; y++)
        {
            var sy = Math.Min(sh - 1, y * sh / Math.Max(r.H, 1));
            for (var x = 0; x < r.W; x++)
            {
                var sx = Math.Min(sw - 1, x * sw / Math.Max(r.W, 1));
                Set(r.X + x, r.Y + y, src[sy * sw + sx]);
            }
        }
        Upload();
    }

    /// <summary>裁出某个部件的像素（导出用）。</summary>
    public Color[] CopyRegion(AtlasRegionInfo r)
    {
        var buf = new Color[Math.Max(r.W, 1) * Math.Max(r.H, 1)];
        for (var y = 0; y < r.H; y++)
            for (var x = 0; x < r.W; x++)
                buf[y * r.W + x] = Get(r.X + x, r.Y + y);
        return buf;
    }

    /// <summary>写回 png；第一次保存前把原图留成 xxx.orig.png。</summary>
    public string Save()
    {
        var orig = Path.Combine(Path.GetDirectoryName(PngPath) ?? "",
                                Path.GetFileNameWithoutExtension(PngPath) + ".orig.png");
        if (!File.Exists(orig)) File.Copy(PngPath, orig);
        using (var fs = File.Create(PngPath)) Tex.SaveAsPng(fs, Width, Height);
        return orig;
    }

    public void Dispose()
    {
        Tex?.Dispose();
        Tex = null;
    }
}
