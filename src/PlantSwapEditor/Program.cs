using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.ImGuiNet;

// ImGui 用的是 System.Numerics，MonoGame 也有同名的 Vector2/Vector4，这里统一取 ImGui 那份
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace PlantSwap.Editor;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    static readonly ushort[] GlyphRanges =
    {
        0x0020, 0x00FF, 0x2000, 0x206F, 0x2190, 0x21FF, 0x2460, 0x24FF,
        0x25A0, 0x25FF, 0x2600, 0x26FF, 0x2700, 0x27BF, 0x3000, 0x30FF,
        0x31F0, 0x31FF, 0xFF00, 0xFFEF, 0xFFFD, 0xFFFD, 0x4E00, 0x9FAF, 0x0000
    };
    static GCHandle _glyphHandle;
    internal static IntPtr GlyphRangePtr
    {
        get
        {
            if (!_glyphHandle.IsAllocated)
                _glyphHandle = GCHandle.Alloc(GlyphRanges, GCHandleType.Pinned);
            return _glyphHandle.AddrOfPinnedObject();
        }
    }

    [STAThread]
    static void Main()
    {
        PrepEnvironment();
        try
        {
            using var g = new EditorGame();
            g.Run();
        }
        catch (Exception ex)
        {
            try { MessageBoxW(IntPtr.Zero, "编辑器出错了：" + Environment.NewLine + ex.Message, "资源编辑器", 0x10); } catch { }
        }
    }

    /// <summary>把内嵌的 SDL2 / OpenAL 解压到 exe 同级的「资源编辑器\dll」，再加载。</summary>
    static void PrepEnvironment()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "资源编辑器", "dll");
            Directory.CreateDirectory(dir);
            var asm = typeof(Program).Assembly;
            foreach (var name in new[] { "SDL2.dll", "soft_oal.dll" })
            {
                var dst = Path.Combine(dir, name);
                try
                {
                    using (var s = asm.GetManifestResourceStream(name))
                    {
                        if (s != null && (!File.Exists(dst) || new FileInfo(dst).Length != s.Length))
                            using (var fs = File.Create(dst)) s.CopyTo(fs);
                    }
                    if (File.Exists(dst)) NativeLibrary.Load(dst);
                }
                catch { }
            }
        }
        catch { }
    }
}

internal sealed class EditorGame : Game
{
    readonly GraphicsDeviceManager _gdm;
    ImGuiRenderer _imgui;

    string _dir = "";
    readonly List<string> _files = new();
    int _fileSel = -1;
    TextureDoc _doc;
    IntPtr _texId;
    float _zoom = 1f;
    Vector2 _pan;
    bool _fitPending;
    AtlasRegionInfo _sel;
    AtlasRegionInfo _hover;
    string _status = "选一个文件夹开始";

    // 文件浏览
    bool _browse;
    string _browseDir = "";
    string _browseFilter = ".png";
    Action<string> _onPick;
    readonly List<(string name, bool dir)> _browseItems = new();
    string _browseErr = "";

    double _lastClick;
    string _exportDir = "";

    public EditorGame()
    {
        _gdm = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1360,
            PreferredBackBufferHeight = 900,
            SynchronizeWithVerticalRetrace = true
        };
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Window.Title = "资源编辑器";
    }

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_SetWindowTitle(IntPtr window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    int _frames;

    protected override void LoadContent()
    {
        _imgui = new ImGuiRenderer(this);
        var io = ImGui.GetIO();
        try { unsafe { ((ImGuiIO*)io.NativePtr)->IniFilename = null; } } catch { }
        var fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var fp = Path.Combine(fontDir, "msyh.ttc");
        if (!File.Exists(fp)) fp = Path.Combine(fontDir, "simhei.ttf");
        if (File.Exists(fp)) io.Fonts.AddFontFromFileTTF(fp, 19f, IntPtr.Zero, Program.GlyphRangePtr);
        _imgui.RebuildFontAtlas();
        AutoOpen();
    }

    /// <summary>启动时优先打开游戏里的 Content/spanim，其次打开 资源修改器/Mod。</summary>
    void AutoOpen()
    {
        var exe = AppContext.BaseDirectory;
        var spanim = Path.Combine(exe, "Content", "spanim");
        if (Directory.Exists(spanim)) { SetDir(spanim); return; }
        var mod = Path.Combine(exe, "资源修改器", "Mod");
        if (Directory.Exists(mod)) { SetDir(mod); return; }
        _status = "点「打开文件夹」选择 spanim 目录或某个作品目录";
    }

    void SetDir(string dir)
    {
        try
        {
            _dir = dir;
            _files.Clear();
            if (Directory.Exists(dir)) CollectPngs(dir, 0);
            _files.Sort(StringComparer.OrdinalIgnoreCase);
            _fileSel = _files.Count > 0 ? 0 : -1;
            _status = _files.Count > 0 ? ("已找到 " + _files.Count + " 张图集") : "这个文件夹里没有 png";
            if (_fileSel >= 0) LoadDoc(_files[_fileSel]);
        }
        catch (Exception ex) { _status = "打开失败：" + ex.Message; }
    }

    /// <summary>作品目录是 作品名/spanim/xxx.png，所以要往下翻几层。</summary>
    void CollectPngs(string dir, int depth)
    {
        if (depth > 3) return;
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                if (f.EndsWith(".orig.png", StringComparison.OrdinalIgnoreCase)) continue;
                _files.Add(f);
            }
            foreach (var d in Directory.GetDirectories(dir))
            {
                var n = Path.GetFileName(d);
                if (n.StartsWith("备份", StringComparison.Ordinal)) continue;
                if (n == "导出部件" || n == "dll" || n == "Language" || n == "native") continue;
                CollectPngs(d, depth + 1);
            }
        }
        catch { }
    }

    string RelName(string full)
    {
        try
        {
            var rel = Path.GetRelativePath(_dir, full);
            return Path.ChangeExtension(rel, null)?.Replace('\\', '/') ?? Path.GetFileNameWithoutExtension(full);
        }
        catch { return Path.GetFileNameWithoutExtension(full); }
    }

    void LoadDoc(string pngPath)
    {
        try
        {
            CloseDoc();
            var atlas = Path.ChangeExtension(pngPath, ".atlas");
            _doc = TextureDoc.Load(GraphicsDevice, pngPath, atlas);
            _texId = _imgui.BindTexture(_doc.Tex);
            _zoom = 1f;
            _pan = Vector2.Zero;
            _fitPending = true;
            _sel = null;
            _hover = null;
            _exportDir = Path.Combine(Path.GetDirectoryName(pngPath) ?? "", "导出部件");
            _status = _doc.Name + "：" + _doc.Width + "×" + _doc.Height + "，" +
                      (_doc.Regions.Count > 0 ? _doc.Regions.Count + " 个部件" : "没有 .atlas（只能整图查看）");
        }
        catch (Exception ex) { _status = "载入失败：" + ex.Message; }
    }

    void CloseDoc()
    {
        if (_texId != IntPtr.Zero)
        {
            try { _imgui.UnbindTexture(_texId); } catch { }
            _texId = IntPtr.Zero;
        }
        _doc?.Dispose();
        _doc = null;
    }

    protected override void Update(GameTime gameTime)
    {
        _frames++;
        // 窗口标题是 SDL 建的，MonoGame 那个托管 Title 属性推不到原生窗口上，这里补一次
        if (_frames == 60)
        {
            try { SDL_SetWindowTitle(Window.Handle, "资源编辑器"); } catch { }
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(24, 24, 26));
        _imgui.BeforeLayout(gameTime);
        DrawUi();
        _imgui.AfterLayout();
        base.Draw(gameTime);
    }

    void DrawUi()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(io.DisplaySize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.Begin("##root", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawTopBar();

        var avail = ImGui.GetContentRegionAvail();
        var h = Math.Max(120f, avail.Y - 34f);

        ImGui.BeginChild("##files", new Vector2(230, h), true);
        DrawFileList();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##canvas", new Vector2(avail.X - 230 - 300 - 16, h), true);
        DrawCanvas();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##side", new Vector2(0, h), true);
        DrawSidePanel();
        ImGui.EndChild();

        ImGui.TextDisabled(_status);

        ImGui.End();
        ImGui.PopStyleVar(2);

        if (_browse) DrawBrowser();
    }

    bool InGameContent =>
        _dir.Replace('/', '\\').IndexOf("\\Content\\", StringComparison.OrdinalIgnoreCase) >= 0;

    void DrawTopBar()
    {
        if (ImGui.Button("打开文件夹…"))
            OpenBrowser(string.IsNullOrEmpty(_dir) ? AppContext.BaseDirectory : _dir, null, SetDir);
        ImGui.SameLine();
        if (ImGui.Button("刷新")) SetDir(_dir);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 150);
        ImGui.InputText("##dir", ref _dir, 512, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("打开所在目录")) Reveal(_dir);
        if (InGameContent)
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.35f, 1f),
                "注意：当前打开的是游戏原始资源目录，保存会直接改游戏文件。建议先「导出为作品」再编辑副本。");
        }
        ImGui.Separator();
    }

    void DrawFileList()
    {
        ImGui.TextDisabled("图集（png）");
        ImGui.Separator();
        for (var i = 0; i < _files.Count; i++)
        {
            var name = RelName(_files[i]);
            var hasAtlas = File.Exists(Path.ChangeExtension(_files[i], ".atlas"));
            if (ImGui.Selectable(name + (hasAtlas ? "" : "  （无 atlas）"), i == _fileSel))
            {
                _fileSel = i;
                LoadDoc(_files[i]);
            }
        }
    }

    void DrawCanvas()
    {
        var canvasMin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        if (avail.X < 10 || avail.Y < 10) return;
        ImGui.InvisibleButton("##cv", avail);
        var hovered = ImGui.IsItemHovered();
        var canvasMax = canvasMin + avail;
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasMin, canvasMax, true);
        dl.AddRectFilled(canvasMin, canvasMax, 0xFF1A1A1C);

        if (_doc == null || _texId == IntPtr.Zero)
        {
            dl.PopClipRect();
            ImGui.TextDisabled("左边选一张图集");
            return;
        }

        if (_fitPending)
        {
            _fitPending = false;
            var s = Math.Min(avail.X / _doc.Width, avail.Y / _doc.Height) * 0.95f;
            _zoom = Math.Clamp(s, 0.05f, 8f);
            _pan = (avail - new Vector2(_doc.Width, _doc.Height) * _zoom) * 0.5f;
        }

        var m = ImGui.GetIO().MousePos;
        var wheel = ImGui.GetIO().MouseWheel;
        if (hovered && wheel != 0f)
        {
            var before = (m - canvasMin - _pan) / _zoom;
            _zoom = Math.Clamp(_zoom * (float)Math.Pow(1.15, wheel), 0.05f, 16f);
            _pan = m - canvasMin - before * _zoom;
        }
        if (hovered && (ImGui.IsMouseDragging(ImGuiMouseButton.Right) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle)))
            _pan += ImGui.GetIO().MouseDelta;

        var imgMin = canvasMin + _pan;
        var imgMax = imgMin + new Vector2(_doc.Width, _doc.Height) * _zoom;
        dl.AddImage(_texId, imgMin, imgMax, new Vector2(0, 0), new Vector2(1, 1));

        var texel = (m - imgMin) / _zoom;
        _hover = null;
        if (hovered && texel.X >= 0 && texel.Y >= 0 && texel.X < _doc.Width && texel.Y < _doc.Height)
        {
            for (var i = _doc.Regions.Count - 1; i >= 0; i--)
            {
                var r = _doc.Regions[i];
                if (texel.X >= r.X && texel.X < r.Right && texel.Y >= r.Y && texel.Y < r.Bottom) { _hover = r; break; }
            }
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _sel = _hover;
            var now = gameTimeSec();
            if (now - _lastClick < 0.35) { _fitPending = true; }
            _lastClick = now;
        }

        foreach (var r in _doc.Regions)
        {
            var p0 = imgMin + new Vector2(r.X, r.Y) * _zoom;
            var p1 = imgMin + new Vector2(r.Right, r.Bottom) * _zoom;
            if (p1.X < canvasMin.X || p1.Y < canvasMin.Y || p0.X > canvasMax.X || p0.Y > canvasMax.Y) continue;
            var isSel = ReferenceEquals(r, _sel);
            var isHov = ReferenceEquals(r, _hover);
            var col = isSel ? 0xFF00FFFFu : isHov ? 0xFFFFFF00u : 0x5078FF50u;
            dl.AddRect(p0, p1, col, 0f, ImDrawFlags.None, isSel ? 2f : 1f);
        }
        if (_hover != null)
        {
            var p = imgMin + new Vector2(_hover.X, _hover.Y) * _zoom;
            dl.AddText(p + new Vector2(3, -18), 0xFFFFFF00u, _hover.Name);
        }

        dl.PopClipRect();
    }

    void DrawSidePanel()
    {
        if (_doc == null) { ImGui.TextDisabled("没有打开图集"); return; }

        ImGui.TextDisabled("当前：" + _doc.Name);
        ImGui.SameLine();
        ImGui.TextDisabled(_doc.Width + "×" + _doc.Height);
        ImGui.TextDisabled("缩放 " + (_zoom * 100f).ToString("0") + "%   （滚轮缩放 / 右键拖动 / 双击复位）");
        ImGui.Separator();

        if (_sel == null)
        {
            ImGui.TextDisabled("点画布上的框，或点下面的部件名");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.84f, 0.42f, 1f), _sel.Name);
            ImGui.TextDisabled("矩形 " + _sel.X + "," + _sel.Y + "  " + _sel.W + "×" + _sel.H);
            if (_sel.OrigW > 0 && (_sel.OrigW != _sel.W || _sel.OrigH != _sel.H))
                ImGui.TextDisabled("原始 " + _sel.OrigW + "×" + _sel.OrigH + "  （atlas 里裁过边）");
            if (_sel.Rotated)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.4f, 1f),
                    "这个部件在 atlas 里旋转了 " + _sel.Degrees + "° 存储，暂时不能换图/导出（会错位）");
            }
            else
            {
                if (ImGui.Button("换成别的图…")) OpenBrowser(_dir, ".png", ReplaceWith);
                ImGui.SameLine();
                if (ImGui.Button("导出部件")) ExportRegion();
                ImGui.SameLine();
                if (ImGui.Button("清空")) { _doc.ClearRegion(_sel); _status = "已清空：" + _sel.Name; }
            }
        }

        ImGui.Separator();
        if (ImGui.Button("撤销")) { if (_doc.Undo()) _status = "已撤销一步"; else _status = "没有可撤销的操作"; }
        ImGui.SameLine();
        if (ImGui.Button("保存 png"))
        {
            try { var orig = _doc.Save(); _status = "已保存 " + Path.GetFileName(_doc.PngPath) + "（原图备份 " + Path.GetFileName(orig) + "）"; }
            catch (Exception ex) { _status = "保存失败：" + ex.Message; }
        }
        ImGui.SameLine();
        if (ImGui.Button("还原原图")) RestoreOriginal();
        ImGui.TextDisabled("撤销深度 " + _doc.UndoDepth + "/5");

        ImGui.Separator();
        ImGui.TextDisabled("部件（" + _doc.Regions.Count + "）");
        if (ImGui.BeginChild("##regions", new Vector2(0, 0), false))
        {
            for (var i = 0; i < _doc.Regions.Count; i++)
            {
                var r = _doc.Regions[i];
                if (ImGui.Selectable(r.Name + "##r" + i, ReferenceEquals(r, _sel)))
                {
                    _sel = r;
                    CenterOn(r);
                }
            }
        }
        ImGui.EndChild();
    }

    void CenterOn(AtlasRegionInfo r)
    {
        var avail = ImGui.GetContentRegionAvail();
        _fitPending = false;
        _pan = new Vector2(-r.X + 40, -r.Y + 40) * _zoom;
    }

    void ReplaceWith(string pngPath)
    {
        if (_doc == null || _sel == null) return;
        try
        {
            using var fs = File.OpenRead(pngPath);
            using var src = Texture2D.FromStream(GraphicsDevice, fs);
            var buf = new Color[src.Width * src.Height];
            src.GetData(buf);
            _doc.PasteRegion(_sel, buf, src.Width, src.Height);
            _status = "已把 " + Path.GetFileName(pngPath) + " 贴进 " + _sel.Name;
        }
        catch (Exception ex) { _status = "贴图失败：" + ex.Message; }
    }

    void ExportRegion()
    {
        if (_doc == null || _sel == null) return;
        try
        {
            Directory.CreateDirectory(_exportDir);
            var buf = _doc.CopyRegion(_sel);
            using var t = new Texture2D(GraphicsDevice, _sel.W, _sel.H);
            t.SetData(buf);
            var path = Path.Combine(_exportDir, _doc.Name + "_" + _sel.Name + ".png");
            using (var fs = File.Create(path)) t.SaveAsPng(fs, _sel.W, _sel.H);
            _status = "已导出 " + path;
        }
        catch (Exception ex) { _status = "导出失败：" + ex.Message; }
    }

    void RestoreOriginal()
    {
        if (_doc == null) return;
        var orig = Path.Combine(Path.GetDirectoryName(_doc.PngPath) ?? "",
                                Path.GetFileNameWithoutExtension(_doc.PngPath) + ".orig.png");
        if (!File.Exists(orig)) { _status = "没有原图备份（还没保存过）"; return; }
        try
        {
            File.Copy(orig, _doc.PngPath, true);
            LoadDoc(_doc.PngPath);
            _status = "已还原成 " + Path.GetFileName(orig);
        }
        catch (Exception ex) { _status = "还原失败：" + ex.Message; }
    }

    // ---------- 文件浏览 ----------

    void OpenBrowser(string startDir, string filter, Action<string> onPick)
    {
        _browseDir = Directory.Exists(startDir) ? startDir : AppContext.BaseDirectory;
        _browseFilter = filter;
        _onPick = onPick;
        _browse = true;
        RefreshBrowse();
    }

    void RefreshBrowse()
    {
        _browseItems.Clear();
        _browseErr = "";
        try
        {
            foreach (var d in Directory.GetDirectories(_browseDir)) _browseItems.Add((Path.GetFileName(d), true));
            foreach (var f in Directory.GetFiles(_browseDir))
            {
                if (_browseFilter != null && !f.EndsWith(_browseFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (_browseFilter == null && !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    && !f.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)) continue;
                _browseItems.Add((Path.GetFileName(f), false));
            }
            _browseItems.Sort((a, b) => a.dir != b.dir ? (a.dir ? -1 : 1) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { _browseErr = ex.Message; }
    }

    void DrawBrowser()
    {
        ImGui.SetNextWindowSize(new Vector2(620, 520), ImGuiCond.Appearing);
        if (!ImGui.Begin("选择" + (_browseFilter == null ? "文件夹" : "图片"), ref _browse))
        {
            if (!_browse) _onPick = null;
            ImGui.End();
            return;
        }
        if (ImGui.Button("上一级"))
        {
            var p = Path.GetDirectoryName(_browseDir.TrimEnd(Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(p)) { _browseDir = p; RefreshBrowse(); }
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##bd", ref _browseDir, 512, ImGuiInputTextFlags.ReadOnly);

        if (_browseErr.Length > 0) ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f), _browseErr);

        ImGui.BeginChild("##bl", new Vector2(0, 400), true);
        foreach (var it in _browseItems)
        {
            var label = (it.dir ? "[ ] " : "    ") + it.name;
            if (ImGui.Selectable(label, false, ImGuiSelectableFlags.AllowDoubleClick))
            {
                if (it.dir) { _browseDir = Path.Combine(_browseDir, it.name); RefreshBrowse(); }
                else
                {
                    var full = Path.Combine(_browseDir, it.name);
                    var cb = _onPick;
                    _browse = false;
                    _onPick = null;
                    ImGui.EndChild();
                    ImGui.End();
                    cb?.Invoke(full);
                    return;
                }
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("用这个文件夹")) { var d = _browseDir; var cb = _onPick; _browse = false; _onPick = null; ImGui.End(); cb?.Invoke(d); return; }
        ImGui.SameLine();
        if (ImGui.Button("取消")) { _browse = false; _onPick = null; }
        ImGui.End();
    }

    static void Reveal(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    static double gameTimeSec() => Environment.TickCount64 / 1000.0;
}
