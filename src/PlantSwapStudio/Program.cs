using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using MonoGame.ImGuiNet;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Input;
using PvZPlantSwap.Core;
using CoreSwap = PvZPlantSwap.Core.PlantSwap;

namespace PlantSwap.App
{
    public partial class AppGame : Game
    {
        GraphicsDeviceManager _gdm;
        ImGuiRenderer _imgui;
        SpriteBatch _batch;

        string _gameDir = "";   // 空 = 由配置 / 自动探测决定（构造函数末尾）
        string _content;
        Dictionary<string, string> _names = new();
        BuildListsResult _lists;
        // 植物 A / B 各自一份筛选结果，搜索互不影响
        string[] _dispA = Array.Empty<string>();
        string[] _dispB = Array.Empty<string>();
        List<string> _keysA = new();
        List<string> _keysB = new();

        SpinePreview _pvA, _pvB, _pvMod;
        Texture2D _sheetA, _sheetB;
        ContentManager _cm;
        readonly Dictionary<string, Texture2D> _paintCache = new();
        Dictionary<string, string> _paintIndex = new();
        string _keyA = "", _keyB = "";
        // 上次退出时的状态（写进 ini，下次打开还原）
        int _savedMode = -1;
        string _savedKeyA = "", _savedKeyB = "";
        int _selA, _selB;
        bool _onlyPlants = true;
        bool _paused;
        bool _portraitMode = true;
        string _searchA = "";     // 植物 A 的搜索词
        string _searchB = "";     // 植物 B 的搜索词（替换/互换模式独立）
        string _watchSearch = ""; // 观赏模式的搜索词
        readonly Dictionary<string, bool> _folders = new()
        {
            ["spanim"] = true, ["reanim"] = true, ["particles"] = true,
            ["sounds"] = true, ["voices"] = true, ["images"] = false
        };
        readonly List<string> _log = new();
        bool _hasOriginal;
        float _fps, _fpsAcc; int _fpsFrames;
        Rectangle _rectA, _rectB;
        bool _dragA, _dragB, _dragM;
        Rectangle _rectMod, _listRect;
        Texture2D _iconTex; IntPtr _iconTexId;
        bool _maximized;
        int _ovDir;
        Point _lastMouse;
        int _mode = -1;              // -1=选择模式 0=替换 1=互换 2=观赏 3=Mod
        int _watchAnim;
        string[] _animNames = Array.Empty<string>();
        string[] _animLabels = Array.Empty<string>();

        string CfgPath => Path.Combine(ToolRoot, "资源修改器.ini");

        void LoadCfg()
        {
            // 只用旧位置的 json 记住游戏目录；其余设置一律走 资源修改器\资源修改器.ini
            try
            {
                var boot = Path.Combine(CoreSwap.DataDir, "studio.json");
                if (!File.Exists(boot)) return;
                var jo = System.Text.Json.JsonDocument.Parse(File.ReadAllText(boot)).RootElement;
                if (jo.TryGetProperty("game", out var g2) && g2.GetString() is string gs && gs.Length > 0)
                    _gameDir = gs;
            }
            catch { }
        }

        void LoadIni()
        {
            try
            {
                if (!File.Exists(CfgPath)) { SaveCfg(); return; }
                string sec = "";
                foreach (var raw in File.ReadAllLines(CfgPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        sec = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = line.Substring(0, eq).Trim();
                    var v = line.Substring(eq + 1).Trim();
                    if (sec == "general")
                    {
                        if (k.Equals("game", StringComparison.OrdinalIgnoreCase)) { if (v.Length > 0) _gameDir = v; }
                        else if (k.Equals("onlyPlants", StringComparison.OrdinalIgnoreCase)) _onlyPlants = v == "1";
                        else if (k.Equals("portrait", StringComparison.OrdinalIgnoreCase)) _portraitMode = v == "1";
                        else if (k.Equals("mode", StringComparison.OrdinalIgnoreCase)) { _savedMode = int.TryParse(v, out var mm) ? mm : -1; }
                        else if (k.Equals("keyA", StringComparison.OrdinalIgnoreCase)) _savedKeyA = v;
                        else if (k.Equals("keyB", StringComparison.OrdinalIgnoreCase)) _savedKeyB = v;
                        else if (k.Length == 5 && k.StartsWith("keyA", StringComparison.OrdinalIgnoreCase) && k[4] >= '0' && k[4] <= '3')
                        { int mi = k[4] - '0'; if (v.Length > 0) _modeKeyA[mi] = v; }
                        else if (k.Length == 5 && k.StartsWith("keyB", StringComparison.OrdinalIgnoreCase) && k[4] >= '0' && k[4] <= '3')
                        { int mi = k[4] - '0'; if (v.Length > 0) _modeKeyB[mi] = v; }
                        else if (k.Length == 9 && k.StartsWith("portrait", StringComparison.OrdinalIgnoreCase) && k[8] >= '0' && k[8] <= '3')
                        { _modePortrait[k[8] - '0'] = v == "1"; }
                        else if (k.Length == 11 && k.StartsWith("onlyPlants", StringComparison.OrdinalIgnoreCase) && k[10] >= '0' && k[10] <= '3')
                        { _modeOnlyPlants[k[10] - '0'] = v == "1"; }
                        else if (k.Equals("keyC", StringComparison.OrdinalIgnoreCase)) _watchKey = v;
                        else if (k.Equals("watchTab", StringComparison.OrdinalIgnoreCase)) _watchTab = v == "1" ? 1 : 0;
                        else if (k.Equals("watchScope", StringComparison.OrdinalIgnoreCase)) _watchScope = int.TryParse(v, out var ws) ? Math.Clamp(ws, 0, 2) : 0;
                        else if (k.Equals("modSel", StringComparison.OrdinalIgnoreCase)) _modSelDir = v;
                    }
                    else if (sec == "folders" && _folders.ContainsKey(k)) _folders[k] = v == "1";
                }
            }
            catch { }
        }

        void SaveCfg()
        {
            try
            {
                Directory.CreateDirectory(ToolRoot);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("; 植物娘大战僵尸 HD · 资源修改器 配置（可手改）");
                sb.AppendLine("[general]");
                sb.AppendLine("game=" + _gameDir);
                sb.AppendLine("onlyPlants=" + (_onlyPlants ? "1" : "0"));
                sb.AppendLine("portrait=" + (_portraitMode ? "1" : "0"));
                SaveModeState(_mode);
                sb.AppendLine("mode=" + _mode);
                for (int i = 0; i < 4; i++)
                {
                    sb.AppendLine("keyA" + i + "=" + (_modeKeyA[i] ?? ""));
                    sb.AppendLine("keyB" + i + "=" + (_modeKeyB[i] ?? ""));
                    sb.AppendLine("portrait" + i + "=" + (_modePortrait[i] ? "1" : "0"));
                    sb.AppendLine("onlyPlants" + i + "=" + (_modeOnlyPlants[i] ? "1" : "0"));
                }
                sb.AppendLine("keyC=" + _watchKey);
                sb.AppendLine("watchTab=" + _watchTab);
                sb.AppendLine("watchScope=" + _watchScope);
                sb.AppendLine("modSel=" + _modSelDir);
                sb.AppendLine("[folders]");
                foreach (var kv in _folders) sb.AppendLine(kv.Key + "=" + (kv.Value ? "1" : "0"));
                File.WriteAllText(CfgPath, sb.ToString());
                try
                {
                    var boot = Path.Combine(CoreSwap.DataDir, "studio.json");
                    File.WriteAllText(boot, "{\"game\":\"" + _gameDir + "\"}");
                }
                catch { }
            }
            catch { }
        }

        /// <summary>旧版本把产物放在「植物互换器」目录里，启动时搬到新目录，老用户不用手动整理。</summary>
        void MigrateOldLayout()
        {
            try
            {
                if (string.IsNullOrEmpty(_gameDir)) return;
                var oldRoot = Path.Combine(_gameDir, "植物互换器");
                if (Directory.Exists(oldRoot) && !Directory.Exists(ToolRoot)) Directory.Move(oldRoot, ToolRoot);
                Directory.CreateDirectory(ToolRoot);
                var oldIni = Path.Combine(ToolRoot, "植物互换器.ini");
                if (File.Exists(oldIni) && !File.Exists(CfgPath)) File.Move(oldIni, CfgPath);
                var oldBak = Path.Combine(_gameDir, "植物互换器备份");
                if (Directory.Exists(oldBak) && !Directory.Exists(OriginalRoot)) Directory.Move(oldBak, OriginalRoot);
                var oldMod = Path.Combine(_gameDir, "植物互换器Mod");
                if (Directory.Exists(oldMod) && !Directory.Exists(ModRoot)) Directory.Move(oldMod, ModRoot);
            }
            catch { }
        }
        protected override void OnExiting(object sender, EventArgs args) { SaveCfg(); base.OnExiting(sender, args); }

        public AppGame()
        {
            _gdm = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1280,
                PreferredBackBufferHeight = 860,
                SynchronizeWithVerticalRetrace = true
            };
            Window.AllowUserResizing = true;
            IsMouseVisible = true;
            Window.Title = TitleText;
            try { SDL_SetWindowTitle(Window.Handle, TitleText); } catch { }
            try { Window.IsBorderless = false; } catch { }
            LoadCfg();
            // 便携模式：exe 放在游戏目录里时，直接以 exe 所在目录为准
            // 顺序有讲究：ini 就放在游戏目录下，所以要先定出游戏目录再读 ini
            if (string.IsNullOrEmpty(_gameDir)) _gameDir = CoreSwap.GuessGame();
            MigrateOldLayout();
            LoadIni();
            if (string.IsNullOrEmpty(_gameDir)) _gameDir = CoreSwap.GuessGame();   // 还是找不到就留空，等用户在顶栏填
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint fuLoad);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        void ApplyWindowIcon()
        {
            try
            {
                string ico = System.IO.Path.Combine(CoreSwap.DataDir, "app.ico");
                if (!System.IO.File.Exists(ico)) ico = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (!System.IO.File.Exists(ico)) return;
                var hwnd = Window.Handle;
                foreach (var size in new[] { 32, 64 })
                {
                    var h = LoadImage(IntPtr.Zero, ico, 1, size, size, 0x10);
                    if (h != IntPtr.Zero) SendMessage(hwnd, 0x80, (IntPtr)(size == 32 ? 0 : 1), h);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowTextLengthW(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_GetVersion(IntPtr ver);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern bool SDL_GetWindowWMInfo(IntPtr window, IntPtr info);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr LoadImageW(IntPtr hinst, string name, uint type, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool SetWindowTextW(IntPtr hWnd, string text);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern IntPtr SDL_CreateRGBSurfaceWithFormatFrom(IntPtr pixels, int width, int height, int depth, int pitch, uint format);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern int SDL_SetWindowIcon(IntPtr window, IntPtr icon);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_FreeSurface(IntPtr surface);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_GetWindowPosition(IntPtr win, out int x, out int y);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_SetWindowPosition(IntPtr win, int x, int y);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_SetWindowTitle(IntPtr win, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string title);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_MinimizeWindow(IntPtr win);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_MaximizeWindow(IntPtr win);
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_RestoreWindow(IntPtr win);

        void DragWindow(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            try
            {
                var p0 = Window.Position;
                Window.Position = new Point(p0.X + dx, p0.Y + dy);
            }
            catch { }
        }

        void ToggleMaximize()
        {
            _maximized = !_maximized;
            try
            {
                if (_maximized) SDL_MaximizeWindow(Window.Handle);
                else SDL_RestoreWindow(Window.Handle);
            }
            catch { }
        }

        void DrawTitleBar(int w)
        {
            const int H = 34;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(w, H), ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.05f, 0.07f, 0.10f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));
            ImGui.Begin("##titlebar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus);

            var dl = ImGui.GetWindowDrawList();
            uint gold = 0xFFF0D98A;
            // 图标
            var ip0 = new System.Numerics.Vector2(10, 6);
            if (_iconTexId != IntPtr.Zero)
                dl.AddImage(_iconTexId, ip0, new System.Numerics.Vector2(ip0.X + 22, ip0.Y + 22));
            else
                dl.AddRectFilled(ip0, new System.Numerics.Vector2(ip0.X + 22, ip0.Y + 22), 0xFFC8781E, 4f);
            // 标题
            dl.AddText(new System.Numerics.Vector2(40, 9), gold, "植物娘大战僵尸 HD · 资源修改器");

            // 拖动区（图标与按钮之间）
            float bw = 38f, bh = 26f;
            float bx = w - bw * 3 - 8;
            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(40 + 300, 4));
            ImGui.InvisibleButton("##drag", new System.Numerics.Vector2(System.Math.Max(10, bx - 8 - (40 + 300)), 26));
            if (ImGui.IsItemActive())
            {
                var d = ImGui.GetIO().MouseDelta;
                DragWindow((int)d.X, (int)d.Y);
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ToggleMaximize();

            // 三个按钮（自绘符号，避免缺字）
            for (int i = 0; i < 3; i++)
            {
                ImGui.SetCursorScreenPos(new System.Numerics.Vector2(bx + i * bw, 4));
                ImGui.InvisibleButton("##cb" + i, new System.Numerics.Vector2(bw - 2, bh));
                bool hov = ImGui.IsItemHovered();
                var a0 = ImGui.GetItemRectMin();
                var b0 = ImGui.GetItemRectMax();
                if (hov)
                    dl.AddRectFilled(a0, b0, i == 2 ? 0x66B03030u : 0x33FFFFFFu, 3f);
                float cx = (a0.X + b0.X) / 2f, cy = (a0.Y + b0.Y) / 2f;
                uint col = hov ? 0xFFFFFFFF : 0xFFB9C4D4;
                if (i == 0)
                    dl.AddLine(new System.Numerics.Vector2(cx - 5, cy), new System.Numerics.Vector2(cx + 5, cy), col, 1.6f);
                else if (i == 1)
                    dl.AddRect(new System.Numerics.Vector2(cx - 5, cy - 4), new System.Numerics.Vector2(cx + 5, cy + 4), col, 0f, ImDrawFlags.None, 1.3f);
                else
                {
                    dl.AddLine(new System.Numerics.Vector2(cx - 5, cy - 5), new System.Numerics.Vector2(cx + 5, cy + 5), col, 1.6f);
                    dl.AddLine(new System.Numerics.Vector2(cx - 5, cy + 5), new System.Numerics.Vector2(cx + 5, cy - 5), col, 1.6f);
                }
                if (ImGui.IsItemClicked())
                {
                    if (i == 0) { try { SDL_MinimizeWindow(Window.Handle); } catch { } }
                    else if (i == 1) ToggleMaximize();
                    else Exit();
                }
            }
            ImGui.End();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        protected override void Initialize()
        {
            base.Initialize();
            try { Window.Title = TitleText; } catch { }
            try { SDL_SetWindowTitle(Window.Handle, TitleText); } catch { }
        }

        protected override void LoadContent()
        {
            // 窗口一建好就设标题，否则会先闪一下默认的 PlantSwapStudio
            try { Window.Title = TitleText; } catch { }
            try { SDL_SetWindowTitle(Window.Handle, TitleText); } catch { }
            _imgui = new ImGuiRenderer(this);
            var io = ImGui.GetIO();
            string fp = @"C:\Windows\Fonts\msyh.ttc";
            if (!File.Exists(fp)) fp = @"C:\Windows\Fonts\simhei.ttf";
            if (File.Exists(fp))
                io.Fonts.AddFontFromFileTTF(fp, 19f, IntPtr.Zero, io.Fonts.GetGlyphRangesChineseFull());
            _imgui.RebuildFontAtlas();
            try
            {
                var ip2 = Path.Combine(CoreSwap.DataDir, "icon.png");
                if (File.Exists(ip2))
                {
                    using var fs = File.OpenRead(ip2);
                    _iconTex = Texture2D.FromStream(GraphicsDevice, fs);
                    _iconTexId = _imgui.BindTexture(_iconTex);
                    try
                    {
                        uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        IntPtr hwnd = IntPtr.Zero;
                        EnumWindows((h, l) =>
                        {
                            GetWindowThreadProcessId(h, out uint pid);
                            if (pid == me && IsWindowVisible(h) && GetWindowTextLengthW(h) > 0) { hwnd = h; return false; }
                            return true;
                        }, IntPtr.Zero);
                        IntPtr big = IntPtr.Zero, small = IntPtr.Zero;
                        if (hwnd != IntPtr.Zero)
                        {
                            string ico2 = Path.Combine(CoreSwap.DataDir, "app.ico");
                            small = LoadImageW(IntPtr.Zero, ico2, 1, 16, 16, 0x10);
                            big = LoadImageW(IntPtr.Zero, ico2, 1, 0, 0, 0x10);
                            if (small != IntPtr.Zero) SendMessageW(hwnd, 0x80, IntPtr.Zero, small);
                            if (big != IntPtr.Zero) SendMessageW(hwnd, 0x80, (IntPtr)1, big);
                        }
                        File.WriteAllText(Path.Combine(CoreSwap.DataDir, "icon-debug.txt"), $"hwnd={hwnd} small={small} big={big}");
                    }
                    catch (Exception ex2) { try { File.WriteAllText(Path.Combine(CoreSwap.DataDir, "icon-debug.txt"), "ex " + ex2.Message); } catch { } }
                    var px = new Color[_iconTex.Width * _iconTex.Height];
                    _iconTex.GetData(px);
                    var gh = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        var surf = SDL_CreateRGBSurfaceWithFormatFrom(gh.AddrOfPinnedObject(), _iconTex.Width, _iconTex.Height, 32, _iconTex.Width * 4, 0x16462004u);
                        int r = 0;
                        if (surf != IntPtr.Zero) { r = SDL_SetWindowIcon(Window.Handle, surf); SDL_FreeSurface(surf); }
                        else { r = -2; } // icon-debug

                    }
                    finally { gh.Free(); }
                }
            }
            catch { }

            ApplyWindowIcon();
            _batch = new SpriteBatch(GraphicsDevice);
            _cm = new ContentManager(Services, _gameDir + "\\Content");
            // 三个预览先建好：没找到游戏目录时 ReloadGame 会提前返回，
            // 但只要对象存在，后面的 Update/Draw 就不会因为空引用炸掉（首次运行的常见情况）
            _pvA = new SpinePreview(GraphicsDevice, "");
            _pvB = new SpinePreview(GraphicsDevice, "");
            _pvMod = new SpinePreview(GraphicsDevice, "");
            ReloadGame();
            // 每次启动都停在四选一；每个模式自己的选择留在各自的槽位里，进去就恢复
            _modeWatchKey[2] = _watchKey;
            for (int i = 0; i < 4; i++)
            {
                _modeSelA[i] = string.IsNullOrEmpty(_modeKeyA[i]) ? -1 : _keysA.IndexOf(_modeKeyA[i]);
                _modeSelB[i] = string.IsNullOrEmpty(_modeKeyB[i]) ? -1 : _keysB.IndexOf(_modeKeyB[i]);
            }
            // 老版本 ini 只有 keyA/keyB：算到 0 号模式（替换）头上
            if (_savedMode > 0 && _savedMode <= 3)
            {
                if (string.IsNullOrEmpty(_modeKeyA[_savedMode]) && !string.IsNullOrEmpty(_savedKeyA)) _modeKeyA[_savedMode] = _savedKeyA;
                if (string.IsNullOrEmpty(_modeKeyB[_savedMode]) && !string.IsNullOrEmpty(_savedKeyB)) _modeKeyB[_savedMode] = _savedKeyB;
            }
            _keyA = ""; _keyB = ""; _selA = _selB = -1;
            _pvA.Clear(); _pvB.Clear(); _sheetA = _sheetB = null;
            ApplyStartupArgs();
        }

        static string FolderTip(string k) => k switch
        {
            "spanim" => "Spine 骨骼动画：立绘、图集、动作（.skel/.atlas/.png）",
            "reanim" => "原版植物的帧动画（.xnb），原版植物只有这个",
            "particles" => "粒子/特效文件",
            "sounds" => "音效（.ogg）",
            "voices" => "植物语音（.ogg）",
            "images" => "界面、画册贴图（.xnb）",
            _ => k
        };

        void Tip(string s) { if (ImGui.IsItemHovered()) ImGui.SetTooltip(s); }

        void Say(string s) { _log.Add(s); if (_log.Count > 300) _log.RemoveAt(0); }

        void ReloadGame()
        {
            _content = CoreSwap.FindContent(_gameDir);
            if (_content == null) { Say("找不到 Content 目录: " + _gameDir); return; }
            _names = CnNames.LoadCnNames(_content);
            _lists = CoreSwap.BuildLists(_content, _names);
            _pvA = new SpinePreview(GraphicsDevice, Path.Combine(_content, "spanim"));
            _pvB = new SpinePreview(GraphicsDevice, Path.Combine(_content, "spanim"));
            _pvMod = new SpinePreview(GraphicsDevice, "");
            InitReanim();
            _paintCache.Clear();
            BuildPaintIndex();
            RefreshOriginalInfo();
            RebuildFilter();
            RebuildWatchList();
            Say($"已载入 {_content}（植物 {_lists.Plants.Count} / 全部 {_lists.Keys.Count} 组，中文名 {_names.Count}）");
            RefreshMods();
            // 不再自动挑第一只：没记录就保持空预览，等用户自己选
            _sheetA = _sheetB = null;
            ApplySavedSelection();
        }

        /// <summary>把上次退出时的选择恢复出来；没有记录就保持空预览。</summary>
        void ApplySavedSelection()
        {
            _keyA = ""; _keyB = "";
            _selA = _selB = -1;
            _pvA.Clear(); _pvB.Clear();
            _sheetA = _sheetB = null;
            // 重载时如果人在观赏模式，预览要显示观赏那只，而不是替换那边的 A
            if (_mode == 2)
            {
                RebuildWatchList();
                int wi = _watchKeys.IndexOf(_watchKey ?? "");
                if (wi >= 0) { _watchSel = wi; LoadPreviewKey(_watchKey); }
                else if (_watchDisp.Length > 0) SelectWatch(0);
            }
        }

        /// <summary>按 key 选中（不在当前筛选列表里也照样加载预览）。</summary>
        void SelectKey(string key, bool isA)
        {
            if (string.IsNullOrEmpty(key)) return;
            int idx = (isA ? _keysA : _keysB).IndexOf(key);
            if (idx >= 0) { SelectIndex(idx, isA); return; }
            if (isA)
            {
                _keyA = key; _pvA.Load(key); SelectReanimFor(key);
                _sheetA = LoadPainting(key); _selA = -1;
                if (_mode == 2) RefreshAnimNames();
            }
            else
            {
                _keyB = key; _pvB.Load(key);
                _sheetB = LoadPainting(key); _selB = -1;
            }
        }

        /// <summary>按规范化名在 spanim 里找真实文件（文件名可能带下划线，如 spikerock_tachie.skel）。</summary>
        string FindSpanimFile(string key, string ext)
        {
            if (string.IsNullOrEmpty(key) || _content == null) return null;
            var dir = Path.Combine(_content, "spanim");
            if (!Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir, "*" + ext))
                if (CoreSwap.Norm(Path.GetFileNameWithoutExtension(f)) == key) return f;
            return null;
        }

        bool HasSpine(string key) => FindSpanimFile(key, ".skel") != null;

        int FirstWithSpine(int from)
        {
            for (int i = Math.Max(0, from); i < _keysA.Count; i++)
                if (HasSpine(_keysA[i])) return i;
            return -1;
        }

        string ToolRoot => Path.Combine(_gameDir, "资源修改器");
        string OriginalRoot => Path.Combine(ToolRoot, "备份");
        string ModRoot => Path.Combine(ToolRoot, "Mod");
        bool HasOriginal => File.Exists(Path.Combine(OriginalRoot, "manifest.json"));

        void RefreshOriginalInfo() => _hasOriginal = HasOriginal;

        void EnsureOriginal(bool force = false)
        {
            if (HasOriginal && !force) return;
            if (Directory.Exists(OriginalRoot)) Directory.Delete(OriginalRoot, true);
            int n = 0;
            foreach (var f in CurrentFolders())
            {
                var dir = Path.Combine(_content, f);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(_content, file);
                    var dst = Path.Combine(OriginalRoot, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(file, dst, true);
                    n++;
                }
            }
            File.WriteAllText(Path.Combine(OriginalRoot, "manifest.json"),
                "{\"files\":" + n + ",\"time\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"}");
            Say($"已保存原始备份：{n} 个文件（只需一次）");
            RefreshOriginalInfo();
        }

        int RestoreOriginal()
        {
            if (!HasOriginal) return -1;
            int n = 0;
            foreach (var file in Directory.GetFiles(OriginalRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(OriginalRoot, file);
                if (rel == "manifest.json") continue;
                var dst = Path.Combine(_content, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(file, dst, true);
                n++;
            }
            return n;
        }

        void ApplyPairs(List<AssetPair> pairs, string mode)
        {
            EnsureOriginal();
            foreach (var pr in pairs)
            {
                if (mode == "swap")
                {
                    var tmp = pr.A + ".swaptmp";
                    File.Copy(pr.A, tmp, true);
                    File.Copy(pr.B, pr.A, true);
                    File.Copy(tmp, pr.B, true);
                    File.Delete(tmp);
                }
                else if (mode == "bOverA") File.Copy(pr.B, pr.A, true);
                else File.Copy(pr.A, pr.B, true);
            }
        }

        /// <summary>A、B 各用各的搜索词和结果，互不影响。</summary>
        void RebuildFilter()
        {
            RebuildFilterSide(true);
            RebuildFilterSide(false);
        }

        void RebuildFilterSide(bool isA)
        {
            if (_lists == null)
            {
                if (isA) { _keysA = new List<string>(); _dispA = Array.Empty<string>(); }
                else { _keysB = new List<string>(); _dispB = Array.Empty<string>(); }
                return;
            }
            var src = (_onlyPlants ? _lists.Plants : _lists.Keys)
                .Where(k => k.Length >= 3 && k.Any(char.IsLetter));
            string q = (isA ? _searchA : _searchB).Trim().ToLowerInvariant();
            var keys = src.Where(k =>
            {
                var d = CnNames.DisplayName(_names, k);
                return q.Length == 0 || d.ToLowerInvariant().Contains(q) || k.Contains(q);
            }).OrderBy(k => CnNames.DisplayName(_names, k)).ToList();
            var disp = keys.Select(k => CnNames.DisplayName(_names, k)).ToArray();
            // 选中索引跟着「当前实际选中的 key」走：筛选把当前项滤掉时置 -1，
            // 这样下拉标题仍然显示已选中的那株，而不是跳到列表里第一项。
            if (isA) { _keysA = keys; _dispA = disp; _selA = string.IsNullOrEmpty(_keyA) ? -1 : keys.IndexOf(_keyA); }
            else { _keysB = keys; _dispB = disp; _selB = string.IsNullOrEmpty(_keyB) ? -1 : keys.IndexOf(_keyB); }
        }

        void SelectIndex(int idx, bool isA)
        {
            var list = isA ? _keysA : _keysB;
            if (idx < 0 || idx >= list.Count) return;
            var key = list[idx];
            if (isA) { _selA = idx; if (_keyA != key) { _keyA = key; _pvA.Load(key); SelectReanimFor(key); _sheetA = LoadPainting(key); if (_mode == 2) RefreshAnimNames(); } }
            else { _selB = idx; if (_keyB != key) { _keyB = key; _pvB.Load(key); _sheetB = LoadPainting(key); } }
        }

        Texture2D LoadSheet(string key)
        {
            try
            {
                var dir = Path.Combine(_content, "spanim");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.png"))
                        if (CoreSwap.Norm(CoreSwap.StripExt(Path.GetFileName(f))) == key)
                        {
                            using var fs = File.OpenRead(f);
                            return Texture2D.FromStream(GraphicsDevice, fs);
                        }
            }
            catch { }
            return LoadPainting(key);
        }

        void BuildPaintIndex()
        {
            _paintIndex.Clear();
            var d = Path.Combine(_content, "images", "600x1000");
            if (!Directory.Exists(d)) return;
            foreach (var f in Directory.GetFiles(d, "*.xnb"))
                _paintIndex[CoreSwap.Norm(CoreSwap.StripExt(Path.GetFileName(f)))] = Path.GetFileNameWithoutExtension(f);
        }

        /// <summary>去掉 orig / painting 前后缀和尾部数字，得到基础 key（jalapenoorig → jalapeno）。</summary>
        static string BaseKey(string key)
        {
            var k = key;
            if (k.StartsWith("orig", StringComparison.Ordinal) && k.Length > 4) k = k.Substring(4);
            if (k.EndsWith("orig", StringComparison.Ordinal) && k.Length > 4) k = k.Substring(0, k.Length - 4);
            if (k.StartsWith("painting", StringComparison.Ordinal) && k.Length > 8) k = k.Substring(8);
            var trimmed = k.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return trimmed.Length >= 3 ? trimmed : k;
        }

        Texture2D LoadPainting(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_paintCache.TryGetValue(key, out var cached)) return cached;
            string stem = null;
            var bases = new List<string> { key };
            var bk = BaseKey(key);
            if (bk != key) bases.Add(bk);
            foreach (var k2 in bases)
            {
                var cands = new List<string> { "painting" + k2 + "0", "painting" + k2 + "1", "painting" + k2, k2 };
                // 卡通帧只在基础 key 上找，避免 spikerock2 命中 cartoon_spikerock21 这种离谱帧
                if (k2 == bk) { cands.Add("cartoon" + k2 + "1"); cands.Add("thumbnailcartoon" + k2); }
                foreach (var cand in cands)
                    if (_paintIndex.TryGetValue(cand, out var v)) { stem = v; break; }
                if (stem != null) break;
            }
            Texture2D tex = null;
            if (stem != null)
            {
                try { tex = _cm.Load<Texture2D>("images/600x1000/" + stem); } catch { tex = null; }
            }
            _paintCache[key] = tex;
            return tex;
        }

        List<string> CurrentFolders() => _folders.Where(kv => kv.Value).Select(kv => kv.Key).ToList();

        void DoResolve(bool apply, string mode, bool force = false)
        {
            if (_lists == null) { Say("还没有载入游戏资源，先把顶栏的游戏目录填对"); return; }
            if (string.IsNullOrEmpty(_keyA) || string.IsNullOrEmpty(_keyB) || _keyA == _keyB) { Say("请选择两个不同的植物"); return; }
            var res = CoreSwap.Resolve(_lists.Table, CurrentFolders(), _keyA, _keyB, force);
            var tag = force ? "（强制配对）" : "";
            Say($"{CnNames.DisplayName(_names, _keyA)} <-> {CnNames.DisplayName(_names, _keyB)}：{res.Count} 对{tag}");
            foreach (var p in res.Pairs) Say($"  [{p.Folder}] {p.NameA} <-> {p.NameB}");
            foreach (var m in res.Missing) Say($"  跳过 {m.Folder}/{m.Name}");
            if (!apply) return;
            if (res.Count == 0) { Say("没有可操作的文件对"); return; }
            try
            {
                string label = mode == "aOverB" ? "A 覆盖 B" : mode == "bOverA" ? "B 覆盖 A" : "互换";
                ApplyPairs(res.Pairs, mode);
                Say($"完成（{label}）");
                _pvA.Load(_keyA); _pvB.Load(_keyB);
                _sheetA = LoadSheet(_keyA); _sheetB = LoadSheet(_keyB);
            }
            catch (Exception ex) { Say("失败：" + ex.Message); }
        }

        void RestoreOriginalAction()
        {
            if (!HasOriginal) { Say("还没有原始备份，请先点「重建原始备份」"); return; }
            try
            {
                int n = RestoreOriginal();
                Say($"已还原到原始状态（{n} 个文件）");
                _pvA.Load(_keyA); _pvB.Load(_keyB);
                _sheetA = LoadSheet(_keyA); _sheetB = LoadSheet(_keyB);
            }
            catch (Exception ex) { Say("还原失败：" + ex.Message); }
        }

        void RebuildOriginalAction()
        {
            try
            {
                EnsureOriginal(force: true);
                Say("原始备份已重建（以当前文件状态为准）");
            }
            catch (Exception ex) { Say("重建失败：" + ex.Message); }
        }

        int _frames; bool _iconDone;
        IntPtr _hwnd; float _imeProbe;
        System.Numerics.Vector2 _imeAnchor;      // 候选窗锚点（输入框左下角）
        bool _imeAnchorSet;
        float _imeLastX = float.NaN, _imeLastY = float.NaN;

        IntPtr FindMyHwnd()
        {
            if (_hwnd != IntPtr.Zero) return _hwnd;
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != me) return true;
                if (GetWindowTextLengthW(h) == 0) return true;   // 跳过隐藏的辅助窗口
                found = h;
                return false;
            }, IntPtr.Zero);
            _hwnd = found;
            return found;
        }

        /// <summary>输入框画完后调用：把候选窗锚到它的左下角，别跟着鼠标跑。</summary>
        void NoteImeAnchor()
        {
            if (!ImGui.IsItemActive()) return;
            var mn = ImGui.GetItemRectMin();
            var mx = ImGui.GetItemRectMax();
            _imeAnchor = new System.Numerics.Vector2(mn.X, mx.Y + 4);
            _imeAnchorSet = true;
        }

        void ProbeIme()
        {
            var io = ImGui.GetIO();
            if (!io.WantTextInput) { _imeProbe = 0f; return; }
            _imeProbe -= (float)gameTimeSeconds;
            if (_imeProbe > 0) return;
            _imeProbe = 60f;
            Program.EnsureTextInput();
        }

        double gameTimeSeconds => _lastGameTime?.TotalGameTime.TotalSeconds ?? 0;
        GameTime _lastGameTime;

        bool TrySetIconLate()
        {
            try
            {
                uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                IntPtr hwnd = IntPtr.Zero;
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    // 不再要求「已可见」：窗口一创建就拿到，赶在第一次绘制前把标题和图标设好
                    if (pid == me) { hwnd = h; return false; }
                    return true;
                }, IntPtr.Zero);
                IntPtr big = IntPtr.Zero, small = IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                {
                    string ico2 = Path.Combine(CoreSwap.DataDir, "app.ico");
                    small = LoadImageW(IntPtr.Zero, ico2, 1, 16, 16, 0x10);
                    big = LoadImageW(IntPtr.Zero, ico2, 1, 0, 0, 0x10);
                    if (small != IntPtr.Zero) SendMessageW(hwnd, 0x80, IntPtr.Zero, small);
                    if (big != IntPtr.Zero) SendMessageW(hwnd, 0x80, (IntPtr)1, big);
                    // MonoGame 的 Window.Title 只在 _title 变化时才推给 SDL，构造函数里设过就再也不推了 → 直接改窗口标题
                    SetWindowTextW(hwnd, TitleText);
                }
                File.WriteAllText(Path.Combine(CoreSwap.DataDir, "icon-debug2.txt"), $"hwnd={hwnd} small={small} big={big}");
                return hwnd != IntPtr.Zero;
            }
            catch { }
            return false;
        }

        const string TitleText = "植物娘大战僵尸 HD · 资源修改器";

        protected override void Update(GameTime gameTime)
        {
            // 标题只在需要时补一次（SDL 建窗前设不进去）
            if (Window.Title != TitleText) { try { Window.Title = TitleText; } catch { } }
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _fpsAcc += dt; _fpsFrames++;
            if (_fpsAcc >= 0.5f)
            {
                _fps = _fpsFrames / _fpsAcc; _fpsAcc = 0; _fpsFrames = 0;
            }

            _frames++;
            _lastGameTime = gameTime;
            if (!_iconDone) { _iconDone = TrySetIconLate(); }
            if (_frames == 2) { Program.EnsureTextInput(); Program.LogIme("startup " + Program.DumpIme(FindMyHwnd()).Replace("\r\n", " | ").Replace("\n", " | ")); }
            ProbeIme();
            if (!_paused) { _pvA?.Update(dt); _pvB?.Update(dt); _pvMod?.Update(dt); TickReanim(dt); }
            HandlePreviewMouse();
            base.Update(gameTime);
        }

        void HandlePreviewMouse()
        {
            var io = ImGui.GetIO();
            var ms = Mouse.GetState();
            bool overA = _rectA.Contains(ms.Position), overB = _rectB.Contains(ms.Position);
            bool overM = _rectMod.Contains(ms.Position);
            if (HandleReanimMouse(ms, io)) return;
            var target = overA ? _pvA : overB ? _pvB : overM ? _pvMod : null;
            if (target == null || io.WantCaptureMouse) { _dragA = _dragB = _dragM = false; return; }

            int wheel = ms.ScrollWheelValue;
            if (!_wheelInit) { _lastWheel = wheel; _wheelInit = true; }
            if (wheel != _lastWheel)
            {
                float d = (wheel - _lastWheel) / 120f;
                target.Zoom = Math.Clamp(target.Zoom * (float)Math.Pow(1.12, d), 0.2f, 8f);
                _lastWheel = wheel;
            }
            if (ms.LeftButton == ButtonState.Pressed && !_wasDown)
            {
                double now = gameTimeSec();
                ref double last = ref (overA ? ref _lastClickTimeA : ref _lastClickTimeB);
                if (now - last < 0.32) { target.Zoom = 1f; target.Pan = Vector2.Zero; }
                last = now;
            }
            _wasDown = ms.LeftButton == ButtonState.Pressed;
            bool down = ms.LeftButton == ButtonState.Pressed || ms.RightButton == ButtonState.Pressed || ms.MiddleButton == ButtonState.Pressed;
            if (down)
            {
                bool dragging = overA ? _dragA : overB ? _dragB : _dragM;
                if (dragging) target.Pan += new Vector2(ms.X - _lastMouse.X, ms.Y - _lastMouse.Y);
                if (overA) _dragA = true; else if (overB) _dragB = true; else _dragM = true;
                _lastMouse = ms.Position;
            }
            else { _dragA = _dragB = _dragM = false; }
        }
        int _lastWheel; bool _wheelInit; bool _wasDown;
        static double gameTimeSec() => Environment.TickCount64 / 1000.0;
        double _lastClickTimeA, _lastClickTimeB;

        protected override void Draw(GameTime gameTime)
        {
            var vp = GraphicsDevice.Viewport;
            int w = vp.Width, h = vp.Height;
            GraphicsDevice.Clear(new Color(13, 17, 24));

            if (_mode < 0)
            {
                _imgui.BeforeLayout(gameTime);
                DrawModePicker(w, h);
                _imgui.AfterLayout();
                base.Draw(gameTime);
                return;
            }

            // 僵尸页展开「轨道开关」时顶部控制条要变高，否则轨道列表被窗口截掉
            int top = _mode == 2 ? (142 + (_rvZombie && _trackPanelOpen ? 190 : 0)) : 132;
            int bottom = _mode == 2 ? 132 : 168;
            int mid = Math.Max(120, h - top - bottom);
            if (_mode == 3)
            {
                int listW = (int)(w * 0.42f);
                int gap = 12;
                int pw = (w - 12 - listW - 12 - gap * 2) / 2;
                _rectA = new Rectangle(12 + listW + gap, top + 30, pw, mid - 30);
                _rectMod = new Rectangle(_rectA.Right + gap, top + 30, pw, mid - 30);
                _listRect = new Rectangle(12, top + 6, listW, mid - 6);
                _rectB = Rectangle.Empty;
            }
            else if (_mode == 2)
            {
                // 观赏模式只用一个预览：植物页和僵尸页都占满整宽
                _rectA = new Rectangle(12, top + 30, w - 24, mid - 30);
                _rectB = Rectangle.Empty;
            }
            else
            {
                int halfW = (w - 30) / 2;
                _rectA = new Rectangle(12, top + 30, halfW, mid - 30);
                _rectB = new Rectangle(12 + halfW + 12, top + 30, halfW, mid - 30);
            }

            bool useSpineA = _portraitMode && _pvA.Ready && !_rvZombie;
            bool useReanimA = !useSpineA && ReanimVisible;
            bool useSpineB = (_mode == 0 || _mode == 1) && _portraitMode && _pvB.Ready;
            if (useSpineA) _pvA.Draw(_rectA);
            if (useReanimA) { try { _rvA.Draw(_rectA); } catch { } }
            if (useSpineB) _pvB.Draw(_rectB);
            if (_mode == 3 && _pvMod.Ready) _pvMod.Draw(_rectMod);
            if ((!useSpineA && !useReanimA) || !useSpineB)
            {
                _batch.Begin(samplerState: SamplerState.LinearClamp);
                if (!useSpineA && !useReanimA) DrawSheet(_sheetA, _rectA);
                if (!useSpineB) DrawSheet(_sheetB, _rectB);
                _batch.End();
            }

            _imgui.BeforeLayout(gameTime);
            BuildUi(w, h, top, bottom);
            UpdateImeRect();
            DrawImeOverlay();
            _imgui.AfterLayout();
            base.Draw(gameTime);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct SdlRect { public int X, Y, W, H; }

        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_SetTextInputRect(ref SdlRect rect);

        /// <summary>把输入法候选窗挪到当前输入框附近，否则它会停在窗口左上角。</summary>
        void UpdateImeRect()
        {
            try
            {
                var io = ImGui.GetIO();
                if (!io.WantTextInput) return;
                var m = _imeAnchorSet ? _imeAnchor : io.MousePos;
                // 位置没变就别反复通知输入法：每帧重设会导致组合串被重置，候选列表跟着闪
                if (!float.IsNaN(_imeLastX) && Math.Abs(_imeLastX - m.X) < 1 && Math.Abs(_imeLastY - m.Y) < 1) return;
                _imeLastX = m.X; _imeLastY = m.Y;
                var r = new SdlRect { X = (int)m.X, Y = (int)m.Y, W = 2, H = 24 };
                SDL_SetTextInputRect(ref r);
                // 再直接用 IMM 把组合窗/候选窗钉在输入框下面（SDL 给的位置经常是 (0,0) 或干脆不给）
                var hwnd = FindMyHwnd();
                if (hwnd != IntPtr.Zero) Program.PositionIme(hwnd, (int)m.X, (int)m.Y + 20);
            }
            catch { }
        }

        /// <summary>
        /// 自己画输入法的拼音串和候选词。
        /// 微软拼音在 SDL 窗口上不出原生候选窗（SDL 的 UI-less 模式把它藏了），
        /// 但组合串和候选词通过 IMM 还能读到，所以这里补一个轻量候选窗。
        /// </summary>
        void DrawImeOverlay()
        {
            var io = ImGui.GetIO();
            if (!io.WantTextInput) { _imeAnchorSet = false; _imeLastX = _imeLastY = float.NaN; return; }
            var st = Program.ReadIme(FindMyHwnd());
            if (!st.Active) return;

            var dl = ImGui.GetForegroundDrawList();
            float lh = ImGui.GetTextLineHeight();
            var p = _imeAnchorSet ? _imeAnchor : new System.Numerics.Vector2(io.MousePos.X, io.MousePos.Y + 26);

            // 拼音串
            string comp = string.IsNullOrEmpty(st.Comp) ? "" : st.Comp;
            if (comp.Length > 0)
            {
                var sz = ImGui.CalcTextSize(comp);
                float w = Math.Max(sz.X + 14f, 60f);
                dl.AddRectFilled(p + new System.Numerics.Vector2(-5, -3), p + new System.Numerics.Vector2(w, lh + 5), 0xF0121212u, 4f);
                dl.AddRect(p + new System.Numerics.Vector2(-5, -3), p + new System.Numerics.Vector2(w, lh + 5), 0xFF6FA8D8u, 4f);
                dl.AddText(p, 0xFFEAF2FFu, comp);
                p.Y += lh + 6;
            }

            // 候选词：只显示当前这一页，序号按页内从 1 开始
            int pageStart = Math.Max(0, st.PageStart);
            int remain = Math.Max(0, st.Cand.Count - pageStart);
            // 微软拼音有时只报 pageSize=1，但它其实给了整页候选，这里兜一把
            int pageCount = st.PageSize > 1 ? st.PageSize : Math.Min(9, remain);
            for (int i = 0; i < pageCount && pageStart + i < st.Cand.Count; i++)
            {
                string item = (i + 1) + "  " + st.Cand[pageStart + i];
                var sz = ImGui.CalcTextSize(item);
                bool sel = (pageStart + i) == st.Sel;
                var a = new System.Numerics.Vector2(p.X - 5, p.Y - 2);
                var b = new System.Numerics.Vector2(p.X + Math.Max(sz.X, 90f) + 8, p.Y + lh + 2);
                dl.AddRectFilled(a, b, sel ? 0xFF2A5C8Fu : 0xF0121212u, 3f);
                if (sel) dl.AddRect(a, b, 0xFF8FC7F0u, 3f);
                dl.AddText(p, 0xFFEAF2FFu, item);
                p.Y += lh + 4;
            }
        }

        void DrawSheet(Texture2D tex, Rectangle r)
        {
            if (tex == null || r.Width <= 0 || r.Height <= 0) return;
            float s = Math.Min((float)r.Width / tex.Width, (float)r.Height / tex.Height);
            s = Math.Min(s, 2f);
            int dw = (int)(tex.Width * s), dh = (int)(tex.Height * s);
            _batch.Draw(tex, new Rectangle(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh), Color.White);
        }

        void BuildUi(int w, int h, int top, int bottom)
        {
            const ImGuiWindowFlags fixedFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoBringToFrontOnFocus;

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(w, top), ImGuiCond.Always);
            ImGui.Begin("##top", fixedFlags);
            ImGui.Text("游戏目录");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(560);
            ImGui.InputText("##game", ref _gameDir, 512);
            NoteImeAnchor();
            ImGui.SameLine();
            if (ImGui.Button("重新载入")) ReloadGame();
            ImGui.SameLine();
            if (ImGui.Button("切换模式")) { SaveModeState(_mode); _mode = -1; }
            Tip("回到启动时的三模式选择");
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(0.55f, 0.85f, 0.6f, 1), $"FPS {_fps:F0}");

            // 观赏模式的选择器放在各自标签页里（植物页、僵尸页各一个），顶栏不再重复
            if (_mode != 2)
            {
                ImGui.Text(_mode == 3 ? "植物" : "植物 A");
                ImGui.SameLine();
                DrawPlantCombo("##plantA", true);
                ImGui.SameLine();
                if (_mode == 0 || _mode == 1)
                {
                    ImGui.Text("植物 B");
                    ImGui.SameLine();
                    DrawPlantCombo("##plantB", false);
                }

            }

            BuildModeBody();
            ImGui.End();

            if (_mode == 3)
            {
                DrawModList(_listRect);
                DrawPanelOverlay("当前", _keyA, _rectA, _pvA, _sheetA != null);
                DrawPanelOverlay("作品", _modPlantKey, _rectMod, _pvMod, false);
            }
            else
            {
                if (_mode == 2 && _rvZombie) DrawPanelOverlay("僵尸", _watchZombieKey, _rectA, _pvA, _sheetA != null);
                else DrawPanelOverlay(_mode == 2 ? "植物" : "植物 A", _keyA, _rectA, _pvA, _sheetA != null);
                if (_mode == 0 || _mode == 1) DrawPanelOverlay("植物 B", _keyB, _rectB, _pvB, _sheetB != null);
            }

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, h - bottom), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(w, bottom), ImGuiCond.Always);
            ImGui.Begin("##log", fixedFlags);
            ImGui.TextDisabled(_mode == 2 ? "日志（滚轮缩放 / 拖动平移 / 下拉框换动作）" : "日志（滚轮缩放预览 / 任意键拖动 / 双击预览复位）");
            ImGui.BeginChild("##logscroll", new System.Numerics.Vector2(0, 0), true);
            foreach (var line in _log) ImGui.TextUnformatted(line);
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4) ImGui.SetScrollHereY(1f);
            ImGui.EndChild();
            ImGui.End();
        }

        // 每个模式各自记住自己的缩放/平移，切模式不互相影响
        readonly float[] _zoomA = { 1f, 1f, 1f, 1f };
        readonly float[] _zoomB = { 1f, 1f, 1f, 1f };
        readonly Vector2[] _panA = new Vector2[4];
        readonly Vector2[] _panB = new Vector2[4];

        readonly float[] _zoomM = { 1f, 1f, 1f, 1f };
        readonly Vector2[] _panM = new Vector2[4];

        void SaveView(int m)
        {
            if (m < 0 || m > 3) return;
            _zoomA[m] = _pvA.Zoom; _panA[m] = _pvA.Pan;
            _zoomB[m] = _pvB.Zoom; _panB[m] = _pvB.Pan;
            _zoomM[m] = _pvMod.Zoom; _panM[m] = _pvMod.Pan;
        }

        void RestoreView(int m)
        {
            if (m < 0 || m > 3) return;
            _pvA.Zoom = _zoomA[m]; _pvA.Pan = _panA[m]; _pvA.ResetFit();
            _pvB.Zoom = _zoomB[m]; _pvB.Pan = _panB[m]; _pvB.ResetFit();
            _pvMod.Zoom = _zoomM[m]; _pvMod.Pan = _panM[m]; _pvMod.ResetFit();
        }

        // 四个模式各自一套选择，切模式时保存/还原，谁也不碰谁
        readonly string[] _modeKeyA = new string[4];
        readonly string[] _modeKeyB = new string[4];
        readonly int[] _modeSelA = { -1, -1, -1, -1 };
        readonly int[] _modeSelB = { -1, -1, -1, -1 };
        readonly string[] _modeWatchKey = new string[4];
        readonly bool[] _modePortrait = { true, true, true, true };
        readonly bool[] _modeOnlyPlants = { true, true, true, true };

        void SaveModeState(int m)
        {
            if (m < 0 || m > 3) return;
            SaveView(m);
            _modeKeyA[m] = _keyA; _modeKeyB[m] = _keyB;
            _modeSelA[m] = _selA; _modeSelB[m] = _selB;
            _modePortrait[m] = _portraitMode; _modeOnlyPlants[m] = _onlyPlants;
            if (m == 2) _modeWatchKey[2] = _watchKey;
        }

        void SwitchMode(int m)
        {
            if (_mode >= 0 && _mode <= 3) SaveModeState(_mode);
            _mode = m;
            if (m >= 0 && m <= 3)
            {
                // 每个模式只用自己那份；没选过就是空的
                _keyA = _modeKeyA[m] ?? ""; _keyB = _modeKeyB[m] ?? "";
                _selA = _modeSelA[m]; _selB = _modeSelB[m];
                _portraitMode = _modePortrait[m]; _onlyPlants = _modeOnlyPlants[m];
                RebuildFilter();
                if (m == 2 && !string.IsNullOrEmpty(_modeWatchKey[2])) _watchKey = _modeWatchKey[2];
            }
            OnModeChanged();
            RestoreView(m);
        }

        void OnModeChanged()
        {
            _log.Clear();
            _rvA?.Clear();
            _sheetA = null; _sheetB = null;
            _pvA.Clear(); _pvB.Clear();
            if (_mode == 2)
            {
                _portraitMode = true;
                _paused = false;
                RebuildWatchList();
                if (!string.IsNullOrEmpty(_watchKey))
                {
                    int wi = _watchKeys.IndexOf(_watchKey);
                    if (wi >= 0) { _watchSel = wi; }
                    LoadWatchIntoPreview(_watchKey);
                }
                else if (_watchDisp.Length > 0) SelectWatch(0);
                RefreshAnimNames();
                Say("观赏模式：不改动任何文件，从「动作」下拉框选动作播放");
            }
            else
            {
                if (!string.IsNullOrEmpty(_keyA)) { _pvA.Load(_keyA); SelectReanimFor(_keyA); _sheetA = LoadPainting(_keyA); }
                if (!string.IsNullOrEmpty(_keyB)) { _pvB.Load(_keyB); _sheetB = LoadPainting(_keyB); }
                _pvA.ResetDefaultAnimation(); _pvB.ResetDefaultAnimation();
                _pvA.ResetView(); _pvB.ResetView();
                RefreshAnimNames();
                if (_mode == 0) Say("替换模式：把一个植物的文件复制给另一个（单向覆盖）");
                else if (_mode == 1) Say("互换模式：两个植物的文件互相对调（两边都变成对方）");
                else if (_mode == 3) { RefreshMods(); Say("Mod 模式：导出植物资源去改贴图，改完（或别人的作品）再导回来"); }
            }
        }

        /// <summary>把观赏模式选中的东西放进预览（植物走 Spine，僵尸走 reanim）。</summary>
        void LoadWatchIntoPreview(string key)
        {
            _keyA = key;
            _keyB = "";
            _selB = -1;
            ShareWatchSelection(key);
        }

        /// <summary>只换 A 的预览内容，不动 _keyA 以外的状态。</summary>
        void LoadPreviewKey(string key)
        {
            _rvA?.Clear();
            _keyA = key;
            if (string.IsNullOrEmpty(key)) { _pvA.Clear(); _sheetA = null; return; }
            _pvA.Load(key);
            SelectReanimFor(key);
            _sheetA = LoadPainting(key);
        }

        void RefreshAnimNames()
        {
            try { _animNames = _pvA.Animations.ToArray(); } catch { _animNames = Array.Empty<string>(); }
            _animLabels = _animNames.Select(AnimCn.Label).ToArray();
            _watchAnim = 0;
            var cur = _pvA.CurrentAnimation;
            if (cur != null) { int i = Array.IndexOf(_animNames, cur); if (i >= 0) _watchAnim = i; }
        }

        /// <summary>植物下拉：当前这只不在筛选列表里（换了文件夹/搜索）也要显示出来，不能空着。</summary>
        void DrawPlantCombo(string id, bool isA)
        {
            int sel = isA ? _selA : _selB;
            var key = isA ? _keyA : _keyB;
            var disp = isA ? _dispA : _dispB;
            string label;
            if (sel >= 0 && sel < disp.Length) label = disp[sel];
            else if (!string.IsNullOrEmpty(key)) label = CnNames.DisplayName(_names, key);
            else label = "（未选择）";
            ImGui.SetNextItemWidth(330);
            // 固定弹窗大小：否则每帧内容变化时弹窗不跟着长，只能看到 6 行
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(360, 360), ImGuiCond.Always);
            if (ImGui.BeginCombo(id, label))
            {
                // 筛选框就在下拉里：打开即聚焦，输入即过滤本列表
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                string q = isA ? _searchA : _searchB;
                ImGui.SetNextItemWidth(-1);
                var filterChanged = ImGui.InputTextWithHint("##filter" + id, "筛选：中文名或英文 key", ref q, 64);
                NoteImeAnchor();
                if (filterChanged)
                {
                    if (isA) _searchA = q; else _searchB = q;
                    RebuildFilterSide(isA);
                    disp = isA ? _dispA : _dispB;
                    sel = isA ? _selA : _selB;
                }

                ImGui.BeginChild("##list" + id, new System.Numerics.Vector2(0, 320), false);
                for (int i = 0; i < disp.Length; i++)
                {
                    bool on = i == sel;
                    if (ImGui.Selectable(disp[i] + "##" + id + i, on)) SelectIndex(i, isA);
                    if (on) ImGui.SetItemDefaultFocus();
                }
                if (disp.Length == 0) ImGui.TextDisabled("（没有匹配项）");
                ImGui.EndChild();
                ImGui.EndCombo();
            }
        }

        void BuildModeBody()
        {
            if (_mode == 2) { BuildWatchBody(); return; }
            if (_mode == 3) { BuildModRow(); return; }
            ImGui.Text("预览");
            ImGui.SameLine();
            int pmode = _portraitMode ? 0 : 1;
            ImGui.SetNextItemWidth(128);
            if (ImGui.Combo("##portrait", ref pmode, new[] { "立绘（实时）", "图集" }, 2)) _portraitMode = pmode == 0;
            Tip("立绘=实时 Spine 动画；图集=静态贴图");
            ImGui.SameLine();
            ImGui.SameLine();
            ImGui.Checkbox("暂停动画", ref _paused);
            ImGui.SameLine();
            if (ImGui.Checkbox("只显示植物", ref _onlyPlants)) RebuildFilter();
            ImGui.SameLine();
            foreach (var k in _folders.Keys.ToList())
            {
                bool v = _folders[k];
                if (ImGui.Checkbox(k, ref v)) _folders[k] = v;
                Tip(FolderTip(k));
                ImGui.SameLine();
            }
            ImGui.NewLine();
            if (_mode == 0) BuildReplaceRow(); else BuildSwapRow();
            ImGui.SameLine();
            if (ImGui.Button("还原原始")) RestoreOriginalAction();
            Tip("把所有文件还原到最初保存的原始备份");
            ImGui.SameLine();
            if (ImGui.Button("重建原始备份")) RebuildOriginalAction();
            Tip("以当前文件状态重新拍一份原始备份（确认现在是原版再点）");
            ImGui.SameLine();
            ImGui.TextDisabled(_hasOriginal ? "原始备份：已保存" : "原始备份：未保存（首次操作时自动保存）");
            ImGui.SameLine();
            if (ImGui.Button("复位 A")) { _pvA.Zoom = 1; _pvA.Pan = Vector2.Zero; }
            Tip("只复位左边预览的缩放/平移");
            ImGui.SameLine();
            if (ImGui.Button("复位 B")) { _pvB.Zoom = 1; _pvB.Pan = Vector2.Zero; }
            Tip("只复位右边预览的缩放/平移");
        }

        void BuildReplaceRow()
        {
            if (ImGui.Button("预览配对")) DoResolve(false, "swap");
            Tip("只列出会处理的文件，不改动任何东西");
            ImGui.SameLine();
            ImGui.Text("方向");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(96);
            ImGui.Combo("##ovdir", ref _ovDir, new[] { "A -> B", "B -> A" }, 2);
            Tip(_ovDir == 0 ? "A 的文件复制给 B：B 变成 A 的样子，A 自己不变" : "B 的文件复制给 A：A 变成 B 的样子，B 自己不变");
            ImGui.SameLine();
            if (ImGui.Button(_ovDir == 0 ? "用 A 替换 B" : "用 B 替换 A")) DoResolve(true, _ovDir == 0 ? "aOverB" : "bOverA");
            Tip("单向覆盖，没有反向撤销；想恢复请点「还原原始」");
            ImGui.SameLine();
            if (ImGui.Button(_ovDir == 0 ? "强制用 A 替换 B" : "强制用 B 替换 A")) DoResolve(true, _ovDir == 0 ? "aOverB" : "bOverA", true);
            Tip("名称/后缀对不上的文件也按扩展名顺序强行配上去：适合两边部件数量不一样的植物。" +
                "只会覆盖 B 自己的文件，不会新增文件。");
        }

        void BuildSwapRow()
        {
            if (ImGui.Button("预览配对")) DoResolve(false, "swap");
            Tip("只列出会处理的文件，不改动任何东西");
            ImGui.SameLine();
            if (ImGui.Button("执行互换")) DoResolve(true, "swap");
            Tip("A 和 B 的文件互相对调：两边都变成对方的样子");
        }

        void BuildWatchBody()
        {
            // 一套选择器走全部：范围决定列表，选到僵尸就自动切到僵尸那套控件
            ImGui.Text("范围");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.Combo("##wscope", ref _watchScope, new[] { "植物", "僵尸", "全部" }, 3))
            {
                RebuildWatchList();
                int wi2 = _watchKeys.IndexOf(_watchKey ?? "");
                if (wi2 >= 0) { _watchSel = wi2; }
                else if (_watchDisp.Length > 0)
                {
                    int pick = 0;
                    if (_watchScope == 1)
                    {
                        int zi = _watchKeys.FindIndex(k => string.Equals(k, "Zombie", StringComparison.OrdinalIgnoreCase));
                        if (zi >= 0) pick = zi;
                    }
                    SelectWatch(pick);
                }
                else { _watchKey = ""; _keyA = ""; _watchZombie = false; _pvA.Clear(); _sheetA = null; }
            }
            Tip("观赏模式自己的列表范围：植物 / 僵尸 / 全部（不影响其它模式）");
            ImGui.SameLine();
            ImGui.Text("选择（" + _watchDisp.Length + "）");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(360, 360), ImGuiCond.Always);
            string wlabel = string.IsNullOrEmpty(_watchKey) ? "（未选择）"
                          : (IsZombieKey(_watchKey) ? ZombieLabel(_watchKey) : CnNames.DisplayName(_names, _watchKey));
            if (ImGui.BeginCombo("##watchsel", wlabel))
            {
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                string wq = _watchSearch;
                ImGui.SetNextItemWidth(-1);
                var wFilterChanged = ImGui.InputTextWithHint("##wfilter", "筛选：中文名或英文 key", ref wq, 64);
                NoteImeAnchor();
                if (wFilterChanged)
                {
                    _watchSearch = wq;
                    RebuildWatchList();
                }
                ImGui.BeginChild("##wlist", new System.Numerics.Vector2(0, 320), false);
                for (int i = 0; i < _watchDisp.Length; i++)
                {
                    bool on = i == _watchSel;
                    if (ImGui.Selectable(_watchDisp[i] + "##w" + i, on)) SelectWatch(i);
                    if (on) ImGui.SetItemDefaultFocus();
                }
                if (_watchDisp.Length == 0) ImGui.TextDisabled("（没有匹配项）");
                ImGui.EndChild();
                ImGui.EndCombo();
            }
            Tip("观赏模式只预览，不改任何文件");

            if (_watchZombie) { BuildZombieControls(); return; }

            // 没有 Spine 的植物走 reanim 行，另起一行，免得挤在一起
            if (_rvA != null && _rvA.Ready && ReanimVisible) { BuildReanimRows(); return; }

            var names = _animLabels.Length > 0 ? _animLabels : new[] { "（这个植物没有动作）" };
            ImGui.SameLine();
            ImGui.Text("动作");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260);
            if (ImGui.Combo("##anim", ref _watchAnim, names, names.Length) && _animNames.Length > 0)
                _pvA.Play(_animNames[_watchAnim], true);
            Tip("这个植物自带的全部 Spine 动作，选一个就循环播放");
            ImGui.SameLine();
            ImGui.Checkbox("暂停动画", ref _paused);
            ImGui.SameLine();
            if (ImGui.Button("复位视图")) { _pvA.Zoom = 1; _pvA.Pan = Vector2.Zero; }
        }

        void DrawModePicker(int w, int h)
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(w, h), ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new System.Numerics.Vector4(0.043f, 0.055f, 0.075f, 1f));
            ImGui.Begin("##picker", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus);
            var dl = ImGui.GetWindowDrawList();
            float total = 250f * 4 + 26f * 3;
            float x0 = (w - total) / 2f;
            float y0 = Math.Max(120f, h * 0.28f);
            dl.AddText(new System.Numerics.Vector2(x0, y0 - 92), 0xFF8AD9F0u, "植物娘大战僵尸 HD · 资源修改器");
            dl.AddText(new System.Numerics.Vector2(x0, y0 - 56), 0xFFC6B09Fu, "选择一个模式开始（随时可以在右上角切换）");
            ModeCard(0, "替换模式", new[] { "把一个植物的文件", "复制给另一个植物", "单向覆盖，不可反悔" }, x0, y0);
            ModeCard(1, "互换模式", new[] { "两个植物的文件", "互相对调", "两边都变成对方的样子" }, x0 + 276f, y0);
            ModeCard(2, "观赏模式", new[] { "不改任何文件", "点一下换个动作", "像在游戏里那样看植物" }, x0 + 552f, y0);
            ModeCard(3, "Mod 模式", new[] { "导出贴图去改", "改完再导回来", "也能导入别人的作品" }, x0 + 828f, y0);
            dl.AddText(new System.Numerics.Vector2(x0, y0 + 196f), 0xFFA8978Au, "第一次改动文件时会自动保存一份原始备份，之后可以一键还原。");
            ImGui.End();
            ImGui.PopStyleColor();
        }

        void ModeCard(int idx, string title, string[] body, float x, float y)
        {
            const float cw = 250f, chh = 170f;
            var p0 = new System.Numerics.Vector2(x, y);
            var p1 = new System.Numerics.Vector2(x + cw, y + chh);
            ImGui.SetCursorScreenPos(p0);
            ImGui.InvisibleButton("##card" + idx, new System.Numerics.Vector2(cw, chh));
            bool hov = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p1, hov ? 0xFF4F3726u : 0xFF2F2118u, 10f);
            dl.AddRect(p0, p1, hov ? 0xFF8AD9F0u : 0xFF5A4233u, 10f, ImDrawFlags.None, hov ? 2.2f : 1.4f);
            dl.AddText(new System.Numerics.Vector2(x + 20, y + 22), 0xFF8AD9F0u, title);
            float ty = y + 66;
            foreach (var l in body)
            {
                dl.AddText(new System.Numerics.Vector2(x + 20, ty), 0xFFD4C4B9u, l);
                ty += 27;
            }
            if (hov) dl.AddText(new System.Numerics.Vector2(x + 20, y + chh - 32), 0xFFB0E9FFu, "点击进入 >");
            if (ImGui.IsItemClicked()) SwitchMode(idx);
        }


        void DrawPanelOverlay(string title, string key, Rectangle r, SpinePreview pv, bool hasSheet)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(r.X, r.Y - 28), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(r.Width, 26), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(6, 3));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.Begin("##t" + title, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoBringToFrontOnFocus);
            string label;
            if (pv == _pvA && _rvZombie)
            {
                var zk = key;
                label = "僵尸：" + (string.IsNullOrEmpty(zk) ? "（未选择）" : ZombieLabel(zk));
            }
            else label = string.IsNullOrEmpty(key) ? title : title + "：" + CnNames.DisplayName(_names, key);
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.84f, 0.42f, 1), label);
            var rvBadge = pv == _pvA ? ReanimBadge() : null;
            if (rvBadge != null) { ImGui.SameLine(); ImGui.TextColored(new System.Numerics.Vector4(0.55f, 0.85f, 0.6f, 1), rvBadge); }
            else if (!pv.Ready && !hasSheet) { ImGui.SameLine(); ImGui.TextDisabled("（无可用预览）"); }
            else if (!pv.Ready) { ImGui.SameLine(); ImGui.TextDisabled("（原版植物 · 显示原画）"); }
            else if (pv.Error != null) { ImGui.SameLine(); ImGui.TextColored(new System.Numerics.Vector4(0.9f, 0.4f, 0.4f, 1), pv.Error); }
            else { ImGui.SameLine(); ImGui.TextDisabled($"zoom {pv.Zoom:F2}x"); }
            ImGui.End();
            ImGui.PopStyleVar(2);
        }
    }

    public static class Program
    {
        // SDL2 在 Windows 上默认把输入法候选窗藏起来（WM_IME_SETCONTEXT 里去掉 ISC_SHOWUICOMPOSITIONWINDOW），
        // 必须提前打开这个 hint，中文输入法才能在搜索框里正常弹出候选窗。
        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern bool SDL_SetHint([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string name,
                                       [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string value);

        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern IntPtr SDL_GetHint([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string name);

        static string HintText(string name)
        {
            // SDL 返回的是它自己持有的字符串，只能读不能释放（用 string 返回值会让编组器去 free → 堆损坏）
            var p = SDL_GetHint(name);
            return p == IntPtr.Zero ? "(null)" : System.Runtime.InteropServices.Marshal.PtrToStringUTF8(p);
        }

        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern void SDL_StartTextInput();

        [System.Runtime.InteropServices.DllImport("SDL2.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern bool SDL_IsTextInputActive();

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern IntPtr ImmGetContext(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct ImePoint { public int X, Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct ImeRect { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct CompositionForm { public int Style; public ImePoint Pos; public ImeRect Area; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct CandidateForm { public int Index; public int Style; public ImePoint Pos; public ImeRect Area; }

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref CompositionForm form);

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern bool ImmSetCandidateWindow(IntPtr hIMC, ref CandidateForm form);

        [System.Runtime.InteropServices.DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, System.Text.StringBuilder lpBuf, int dwBufLen);

        [System.Runtime.InteropServices.DllImport("imm32.dll")]
        static extern uint ImmGetCandidateListCountW(IntPtr hIMC, out uint count);

        [System.Runtime.InteropServices.DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint ImmGetCandidateListW(IntPtr hIMC, uint dwIndex, IntPtr lpCandList, uint dwBufLen);

        internal const int GCS_COMPSTR = 0x0008;
        internal const int GCS_CURSORPOS = 0x0080;

        /// <summary>当前输入法状态：拼音串 + 光标位置 + 候选词列表。原生候选窗不显示时我们自己画。</summary>
        internal sealed class ImeState2
        {
            public string Comp = "";
            public int Cursor = -1;
            public readonly List<string> Cand = new List<string>();
            public int Sel = -1;
            public int PageStart, PageSize;
            public bool Active => Comp.Length > 0 || Cand.Count > 0;
        }

        internal static ImeState2 ReadIme(IntPtr hwnd)
        {
            var st = new ImeState2();
            var imc = ImmGetContext(hwnd);
            if (imc == IntPtr.Zero) return st;
            try
            {
                var sb = new System.Text.StringBuilder(512);
                // 返回的是字节数，字符数要除以 2；不截断会带出缓冲区里的空字符
                var n = ImmGetCompositionStringW(imc, GCS_COMPSTR, sb, 1024);
                if (n > 0) st.Comp = sb.ToString(0, Math.Min(sb.Length, n / 2));
                try { st.Cursor = ImmGetCompositionStringW(imc, GCS_CURSORPOS, null, 0); } catch { }

                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4096);
                try
                {
                    var cnt = ImmGetCandidateListW(imc, 0, buf, 4096);
                    if (cnt > 0)
                    {
                        st.Sel = System.Runtime.InteropServices.Marshal.ReadInt32(buf, 4);
                        st.PageStart = System.Runtime.InteropServices.Marshal.ReadInt32(buf, 8);
                        st.PageSize = System.Runtime.InteropServices.Marshal.ReadInt32(buf, 12);
                        int count = System.Runtime.InteropServices.Marshal.ReadInt32(buf, 0);
                        for (int i = 0; i < count && i < 32; i++)
                        {
                            int off = System.Runtime.InteropServices.Marshal.ReadInt32(buf, 16 + i * 4);
                            if (off <= 0 || off >= 4096) continue;
                            var s = System.Runtime.InteropServices.Marshal.PtrToStringUni(IntPtr.Add(buf, off));
                            st.Cand.Add(s ?? "");
                        }
                    }
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
            }
            catch { }
            finally { ImmReleaseContext(hwnd, imc); }
            return st;
        }

        /// <summary>读一下当前组合字符串，用于确认输入法到底有没有在组词。</summary>
        internal static string CompositionInfo(IntPtr hwnd)
        {
            try
            {
                var imc = ImmGetContext(hwnd);
                if (imc == IntPtr.Zero) return "imc=0";
                try
                {
                    var sb = new System.Text.StringBuilder(256);
                    var n = ImmGetCompositionStringW(imc, GCS_COMPSTR, sb, 512);
                    uint candCount = 0;
                    try { ImmGetCandidateListCountW(imc, out candCount); } catch { }
                    return "compLen=" + n + " comp='" + sb.ToString() + "' candLists=" + candCount;
                }
                finally { ImmReleaseContext(hwnd, imc); }
            }
            catch (Exception e) { return "comp ex " + e.Message; }
        }

        /// <summary>
        /// 手动把输入法的组合窗/候选窗钉到光标附近。
        /// SDL 有时不给 Windows 传位置，候选窗就会出现在窗口原点或干脆看不见。
        /// </summary>
        internal static bool PositionIme(IntPtr hwnd, int x, int y)
        {
            try
            {
                var imc = ImmGetContext(hwnd);
                if (imc == IntPtr.Zero) return false;
                try
                {
                    var cf = new CompositionForm
                    {
                        Style = 0x0002,     // CFS_POINT
                        Pos = new ImePoint { X = x, Y = y },
                        Area = new ImeRect { Left = x - 2, Top = y - 24, Right = x + 300, Bottom = y + 60 }
                    };
                    ImmSetCompositionWindow(imc, ref cf);

                    var cand = new CandidateForm
                    {
                        Index = 0,
                        Style = 0x0040,     // CFS_CANDIDATEPOS
                        Pos = new ImePoint { X = x, Y = y },
                        Area = new ImeRect { Left = x - 2, Top = y - 24, Right = x + 300, Bottom = y + 60 }
                    };
                    ImmSetCandidateWindow(imc, ref cand);
                    return true;
                }
                finally { ImmReleaseContext(hwnd, imc); }
            }
            catch { return false; }
        }

        static string ImeState(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder();
            try { sb.AppendLine("hint=" + HintText("SDL_IME_SHOW_UI")); } catch (Exception e) { sb.AppendLine("hint ex " + e.Message); }
            try { sb.AppendLine("textInputActive=" + SDL_IsTextInputActive()); } catch (Exception e) { sb.AppendLine("active ex " + e.Message); }
            sb.AppendLine("hwnd=" + hwnd);
            try
            {
                var imc = ImmGetContext(hwnd);
                sb.AppendLine("imc=" + imc + " open=" + (imc != IntPtr.Zero && ImmGetOpenStatus(imc)));
                if (imc != IntPtr.Zero) ImmReleaseContext(hwnd, imc);
            }
            catch (Exception e) { sb.AppendLine("imm ex " + e.Message); }
            return sb.ToString();
        }

        [STAThread]
        static void Main()
        {
            PrepEnvironment();                                        // 先把 SDL2.dll 解出来/加载好
            try { SDL_SetHint("SDL_IME_SHOW_UI", "1"); } catch (Exception e) { LogIme("SetHint ex " + e.Message); }
            try
            {
                using var g = new AppGame();
                g.Run();
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plantswap_crash.log"), ex.ToString()); } catch { }
                throw;
            }
        }

        internal static string ImeLogPath => System.IO.Path.Combine(PvZPlantSwap.Core.PlantSwap.DataDir, "ime-debug.txt");

        internal static void LogIme(string line)
        {
            try
            {
                System.IO.Directory.CreateDirectory(PvZPlantSwap.Core.PlantSwap.DataDir);
                System.IO.File.AppendAllText(ImeLogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { }
        }

        internal static string DumpIme(IntPtr hwnd) => ImeState(hwnd);

        internal static void EnsureTextInput()
        {
            try { SDL_StartTextInput(); } catch (Exception e) { LogIme("StartTextInput ex " + e.Message); }
        }

        static void PrepEnvironment()
        {
            string dataDir = PvZPlantSwap.Core.PlantSwap.DataDir;
            string nativeDir = System.IO.Path.Combine(dataDir, "native");
            System.IO.Directory.CreateDirectory(nativeDir);
            var asm = typeof(Program).Assembly;
            foreach (var name in new[] { "SDL2.dll", "soft_oal.dll" })
            {
                string dst = System.IO.Path.Combine(nativeDir, name);
                // 优先用随程序打包的 dll：游戏目录里那份 SDL2 是 2.0.20，
                // 它的 Windows 输入法支持不完整（候选窗不显示），必须用新版本。
                var loaded = false;
                try
                {
                    using var s = asm.GetManifestResourceStream(name);
                    if (s != null)
                    {
                        bool need = !System.IO.File.Exists(dst) || new System.IO.FileInfo(dst).Length != s.Length;
                        if (need)
                        {
                            using var fs = System.IO.File.Create(dst);
                            s.CopyTo(fs);
                        }
                        try { System.Runtime.InteropServices.NativeLibrary.Load(dst); loaded = true; } catch { }
                    }
                }
                catch { }
                if (loaded) continue;
                // 兜底：exe 同目录（游戏目录）自带的 dll
                string beside = System.IO.Path.Combine(AppContext.BaseDirectory, name);
                if (System.IO.File.Exists(beside))
                {
                    try { System.Runtime.InteropServices.NativeLibrary.Load(beside); } catch { }
                }
            }
            try
            {
                using var ip = asm.GetManifestResourceStream("icon.png");
                if (ip != null)
                {
                    using var fp = System.IO.File.Create(System.IO.Path.Combine(dataDir, "icon.png"));
                    ip.CopyTo(fp);
                }
            }
            catch { }
            try
            {
                using var ico = asm.GetManifestResourceStream("app.ico");
                if (ico != null)
                {
                    using var fs2 = System.IO.File.Create(System.IO.Path.Combine(dataDir, "app.ico"));
                    ico.CopyTo(fs2);
                }
            }
            catch { }
            try { System.IO.Directory.SetCurrentDirectory(dataDir); } catch { }
        }
    }
}