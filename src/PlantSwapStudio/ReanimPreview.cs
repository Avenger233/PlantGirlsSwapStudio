using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PvZPlantSwap.Core;

namespace PlantSwap.App
{
    /// <summary>
    /// reanim 帧动画预览（原版植物 / 僵尸）。
    /// 姿态由 Core 的 <see cref="ReanimPoseBuilder"/> 算（Build 返回整只姿态），
    /// 这里只负责：找文件、按图集惰性加载贴图、按 Items 顺序画四边形、缩放/平移/暂停。
    /// 贴图缺 / 矩形缺 / 轨道空都只降级，不抛异常。
    /// </summary>
    public sealed class ReanimPreview : IDisposable
    {
        readonly GraphicsDevice _gd;
        readonly ContentManager _cm;
        readonly string _content;

        ReanimData _data;
        readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture2D> _texMissing = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        sealed class Layer
        {
            public int TrackIndex;
            public string Name;
            public ReanimFrameRange Range;
        }
        readonly List<Layer> _layers = new List<Layer>();
        int _layer = -1;

        ReanimData _attachData;          // 挂在某条轨道上的配件 reanim（僵尸旗子等）
        string _attachAnchor;            // 挂在哪条轨道的矩阵上

        ReanimPose _pose;
        float _time;
        ReanimRect _bounds = ReanimRect.Empty;
        float _fit = 1f; int _fitW, _fitH;

        VertexBuffer _vb;
        IndexBuffer _ib;
        BasicEffect _fx;
        readonly VertexPositionColorTexture[] _quad = new VertexPositionColorTexture[4];

        public ReanimPreview(GraphicsDevice device, ContentManager cm, string contentDir)
        {
            _gd = device;
            _cm = cm;
            _content = contentDir;
            _fx = new BasicEffect(device)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
            };
            _fx.View = Matrix.Identity;
        }

        public string ContentDir => _content;
        public string Key { get; private set; } = "";
        public string Error { get; private set; }
        public bool Ready => _data != null && _layers.Count > 0;
        public bool Paused { get; set; }
        public float Zoom { get; set; } = 1f;
        public Vector2 Pan { get; set; }
        public float Speed { get; set; } = 1f;
        /// <summary>true = 连路障/铁桶/旗子/纱门等配件轨道一起画。</summary>
        public bool ShowAccessories { get; set; }
        /// <summary>额外隐藏的轨道名（大小写不敏感）。</summary>
        public readonly HashSet<string> HiddenTracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int FrameCount { get; private set; }
        public float Fps { get; private set; } = 12f;
        public int CurrentFrame { get; private set; }
        public int MissingRegions { get; private set; }
        public int MissingTextures { get; private set; }
        public int VisibleTracks { get; private set; }
        public IReadOnlyList<string> Animations
        {
            get
            {
                var list = new List<string>();
                foreach (var l in _layers) list.Add(l.Name);
                return list;
            }
        }
        public string CurrentAnimation => _layer >= 0 && _layer < _layers.Count ? _layers[_layer].Name : "";

        /// <summary>文件里全部轨道名（含部件控制轨），给「轨道开关」用。</summary>
        public IReadOnlyList<string> TrackNames
        {
            get
            {
                var list = new List<string>();
                if (_data != null) foreach (var t in _data.Tracks) list.Add(t.Name);
                return list;
            }
        }
        public int LayerStart => _layer >= 0 && _layer < _layers.Count ? _layers[_layer].Range.Start : 0;
        public int LayerEnd => _layer >= 0 && _layer < _layers.Count ? _layers[_layer].Range.End : 0;
        public int LayerFrame => _pose != null ? _pose.LayerFrame : 0;
        public string Stats => Ready
            ? (LayerFrame + "/" + (_layer >= 0 && _layer < _layers.Count ? _layers[_layer].Range.Count : 0) + " 帧 · " + Fps.ToString("0") + "fps · " + VisibleTracks + " 部件")
            : "—";

        static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public static string FindReanimFile(string contentDir, string key)
        {
            if (string.IsNullOrEmpty(contentDir) || string.IsNullOrEmpty(key)) return null;
            var dir = Path.Combine(contentDir, "reanim");
            if (!Directory.Exists(dir)) return null;
            var want = Norm(key);
            foreach (var f in Directory.GetFiles(dir, "*.xnb"))
                if (Norm(Path.GetFileNameWithoutExtension(f)) == want) return f;
            return null;
        }

        public static List<string> ListReanims(string contentDir, string mustContain = null)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(contentDir)) return list;
            var dir = Path.Combine(contentDir, "reanim");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.xnb"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (mustContain == null || name.IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Add(name);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// 挂一个配件 reanim（比如 Zombie_flagpole 挂到 zombie_flaghand 上）。
        /// key 为 null 时解除。
        /// </summary>
        public void SetAttachment(string key, string anchorTrack)
        {
            _attachData = null;
            _attachAnchor = null;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(anchorTrack)) return;
            try
            {
                var path = FindReanimFile(_content, key);
                if (path == null) return;
                _attachData = ReanimData.Load(path);
                _attachAnchor = anchorTrack;
            }
            catch { _attachData = null; _attachAnchor = null; }
        }

        public void Clear()
        {
            _data = null; _layers.Clear(); _layer = -1; _pose = null;
            _attachData = null; _attachAnchor = null;
            _bounds = ReanimRect.Empty; _fit = 0; _fitW = _fitH = 0; Error = null;
            MissingRegions = MissingTextures = 0; VisibleTracks = 0;
            FrameCount = 0; CurrentFrame = 0;
        }

        public void Load(string key)
        {
            if (string.IsNullOrEmpty(key)) { Clear(); Key = key; return; }
            if (string.Equals(key, Key, StringComparison.Ordinal) && Ready) return;
            Clear();
            Key = key;
            try
            {
                var path = FindReanimFile(_content, key);
                if (path == null) { Error = "没有 reanim 资源"; return; }
                _data = ReanimData.Load(path);
                Fps = _data.Fps > 0.1f ? _data.Fps : 12f;
                FrameCount = _data.MaxFrames;
                if (FrameCount <= 0) { Error = "轨道是空的"; _data = null; return; }

                // 动画层 = 名字以 anim_ 开头、且可见范围短于整个文件的轨道
                for (int i = 0; i < _data.Tracks.Count; i++)
                {
                    var tr = _data.Tracks[i];
                    if (!tr.Name.StartsWith("anim_", StringComparison.OrdinalIgnoreCase)) continue;
                    var range = ReanimPoseBuilder.GetLayerRange(_data, i);
                    if (range.Count <= 0 || range.Count >= FrameCount) continue;
                    _layers.Add(new Layer { TrackIndex = i, Name = tr.Name.Substring(5), Range = range });
                }
                _layers.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start));
                if (_layers.Count == 0)
                {
                    // 兜底：整段当一层
                    _layers.Add(new Layer
                    {
                        TrackIndex = 0,
                        Name = _data.Tracks.Count > 0 ? _data.Tracks[0].Name : "all",
                        Range = new ReanimFrameRange(0, FrameCount),
                    });
                }
                // 默认优先 idle / idle2，没有才用第一层
                int def = _layers.FindIndex(l => string.Equals(l.Name, "idle", StringComparison.OrdinalIgnoreCase));
                if (def < 0) def = _layers.FindIndex(l => string.Equals(l.Name, "idle2", StringComparison.OrdinalIgnoreCase));
                Play(def < 0 ? 0 : def);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _data = null;
                _layers.Clear();
            }
        }

        public void Play(int index)
        {
            if (_layers.Count == 0) return;
            _layer = Math.Max(0, Math.Min(index, _layers.Count - 1));
            _time = 0;
            RebuildPose();
            ComputeBounds();
        }

        public void Play(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < _layers.Count; i++)
                if (string.Equals(_layers[i].Name, name, StringComparison.OrdinalIgnoreCase)) { Play(i); return; }
        }

        void RebuildPose()
        {
            if (_data == null || _layer < 0) { _pose = null; return; }
            try
            {
                _pose = ReanimPoseBuilder.Build(_data, _layers[_layer].TrackIndex, _time);
                CurrentFrame = _pose != null ? _pose.Frame : 0;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _pose = null;
            }
        }

        public void Update(float dt)
        {
            if (_data == null || _layer < 0 || Paused) return;
            _time += dt * Speed;
            // Build 会把时间夹到层的帧范围里，循环要自己取模
            var r = _layers[_layer].Range;
            float dur = r.Count / Math.Max(Fps, 0.001f);
            if (dur > 0.001f)
            {
                _time %= dur;
                if (_time < 0f) _time += dur;
            }
            RebuildPose();
        }

        /// <summary>这一帧要画的项（过滤掉隐藏轨道 / 无矩形 / 全透明）。</summary>
        List<ReanimDrawItem> Collect(bool countMissing)
        {
            var list = new List<ReanimDrawItem>(64);
            if (_pose == null) return list;
            var items = _pose.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Alpha <= 0.002f) continue;
                if (!it.HasRegion) { if (countMissing) MissingRegions++; continue; }
                if (it.LocalWidth <= 0f || it.LocalHeight <= 0f) continue;
                if (IsAccessoryTrack(it.TrackName) && !ShowAccessories) continue;
                if (HiddenTracks.Contains(it.TrackName ?? "")) continue;
                list.Add(it);
            }
            return list;
        }

        static bool IsAccessory(string name)
        {
            var n = name.ToLowerInvariant();
            return n.Contains("cone") || n.Contains("bucket") || n.Contains("flag")
                || n.Contains("screendoor") || n.Contains("whitewater") || n.Contains("duckytube")
                || n.Contains("mustache") || n.Contains("ladder") || n.Contains("newspaper");
        }

        static bool IsAccessoryTrack(string trackName) =>
            !string.IsNullOrEmpty(trackName) && IsAccessory(trackName);

        void ComputeBounds()
        {
            _bounds = ReanimRect.Empty;
            if (_data == null || _layer < 0) return;
            var range = _layers[_layer].Range;
            var saved = _time;
            for (int f = 0; f < range.Count; f += 2)
            {
                _time = f / Fps;
                RebuildPose();
                if (_pose == null) continue;
                var items = Collect(false);
                foreach (var it in items)
                    _bounds = ReanimRect.Union(_bounds, ItemBounds(it));
            }
            _time = saved;
            RebuildPose();
            if (_bounds.IsEmpty) _bounds = new ReanimRect(0, 0, 100, 100);
            _fit = 0;
        }

        static ReanimRect ItemBounds(in ReanimDrawItem it)
        {
            float w = it.LocalWidth, h = it.LocalHeight;
            var r = ReanimRect.Empty;
            for (int c = 0; c < 4; c++)
            {
                float lx = (c == 1 || c == 3) ? w : 0f;
                float ly = (c >= 2) ? h : 0f;
                float x = lx * it.M11 + ly * it.M21 + it.M31;
                float y = lx * it.M12 + ly * it.M22 + it.M32;
                r = ReanimRect.Union(r, new ReanimRect(x, y, x, y));
            }
            return r;
        }

        Texture2D GetAtlasTexture(string atlas)
        {
            if (string.IsNullOrEmpty(atlas)) return null;
            if (_tex.TryGetValue(atlas, out var tex)) return tex;
            if (_texMissing.ContainsKey(atlas)) return null;
            try { tex = _cm.Load<Texture2D>("images/600x1000/" + atlas); }
            catch { tex = null; }
            if (tex == null) { _texMissing[atlas] = null; MissingTextures++; return null; }
            _tex[atlas] = tex;
            return tex;
        }

        public void Draw(Rectangle viewport)
        {
            if (_data == null || _pose == null || viewport.Width <= 0 || viewport.Height <= 0) return;
            var gd = _gd;
            var oldVp = gd.Viewport;
            var oldScissor = gd.ScissorRectangle;
            gd.Viewport = new Viewport(viewport);
            gd.ScissorRectangle = viewport;

            float uw = Math.Max(_bounds.Width, 1f), uh = Math.Max(_bounds.Height, 1f);
            if (Math.Abs(_fit) < 1e-6f || _fitW != viewport.Width || _fitH != viewport.Height)
            {
                _fit = Math.Min(viewport.Width * 0.92f / uw, viewport.Height * 0.92f / uh);
                if (_fit <= 0 || float.IsNaN(_fit) || float.IsInfinity(_fit)) _fit = 1f;
                _fitW = viewport.Width; _fitH = viewport.Height;
            }
            float s = _fit * Zoom;
            float cx = (_bounds.MinX + _bounds.MaxX) * 0.5f, cy = (_bounds.MinY + _bounds.MaxY) * 0.5f;
            var world = Matrix.CreateScale(s, s, 1f) *
                        Matrix.CreateTranslation(viewport.Width * 0.5f + Pan.X - cx * s,
                                                 viewport.Height * 0.5f + Pan.Y - cy * s, 0f);

            _fx.World = world;
            _fx.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, -1f, 1f);

            MissingRegions = 0;
            var items = Collect(true);
            VisibleTracks = items.Count;

            if (_vb == null)
            {
                _vb = new VertexBuffer(gd, typeof(VertexPositionColorTexture), 4, BufferUsage.WriteOnly);
                _ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
                _ib.SetData(new short[] { 0, 1, 2, 2, 1, 3 });
            }

            var oldRaster = gd.RasterizerState;
            gd.RasterizerState = RasterizerState.CullNone;
            var oldDepth = gd.DepthStencilState;
            gd.DepthStencilState = DepthStencilState.None;
            var oldBlend = gd.BlendState;
            gd.BlendState = BlendState.AlphaBlend;

            void DrawOne(in ReanimDrawItem it, in Matrix parent)
            {
                var tex = GetAtlasTexture(it.Region.Atlas);
                if (tex == null) return;
                float w = it.LocalWidth, h = it.LocalHeight;
                float u0 = (float)it.Region.X / tex.Width, v0 = (float)it.Region.Y / tex.Height;
                float u1 = (float)(it.Region.X + it.Region.SrcWidth) / tex.Width;
                float v1 = (float)(it.Region.Y + it.Region.SrcHeight) / tex.Height;
                var col = new Color(1f, 1f, 1f, it.Alpha);
                _quad[0] = new VertexPositionColorTexture(new Vector3(0, 0, 0), col, new Vector2(u0, v0));
                _quad[1] = new VertexPositionColorTexture(new Vector3(w, 0, 0), col, new Vector2(u1, v0));
                _quad[2] = new VertexPositionColorTexture(new Vector3(0, h, 0), col, new Vector2(u0, v1));
                _quad[3] = new VertexPositionColorTexture(new Vector3(w, h, 0), col, new Vector2(u1, v1));

                // 每个 item 的 2x3 仿射（行向量）拼成 XNA 的 4x4：M41/M42 = M31/M32
                var m = Matrix.Identity;
                m.M11 = it.M11; m.M12 = it.M12;
                m.M21 = it.M21; m.M22 = it.M22;
                m.M41 = it.M31; m.M42 = it.M32;
                _fx.World = m * parent * world;

                _vb.SetData(_quad);
                gd.SetVertexBuffer(_vb);
                gd.Indices = _ib;
                _fx.Texture = tex;
                foreach (var pass in _fx.CurrentTechnique.Passes) pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
            }

            foreach (var it in items) DrawOne(it, Matrix.Identity);

            // 配件 reanim：整体乘上锚点轨道的矩阵（僵尸旗子挂在 zombie_flaghand 上）
            if (_attachData != null && !string.IsNullOrEmpty(_attachAnchor) && _pose != null)
            {
                var anchor = Matrix.Identity;
                var found = false;
                foreach (var it in _pose.Items)
                {
                    if (!string.Equals(it.TrackName, _attachAnchor, StringComparison.OrdinalIgnoreCase)) continue;
                    anchor = new Matrix(it.M11, it.M12, 0f, 0f,
                                        it.M21, it.M22, 0f, 0f,
                                        0f, 0f, 1f, 0f,
                                        it.M31, it.M32, 0f, 1f);
                    found = true;
                    break;
                }
                if (found)
                {
                    var afps = _attachData.Fps > 0.1f ? _attachData.Fps : 12f;
                    var adur = _attachData.MaxFrames / afps;
                    var at = adur > 0.001f ? _time % adur : _time;
                    var ap = ReanimPoseBuilder.BuildAll(_attachData, at);
                    foreach (var it in ap.Items)
                    {
                        if (it.Alpha <= 0.002f || !it.HasRegion) continue;
                        DrawOne(it, anchor);
                    }
                }
            }

            gd.RasterizerState = oldRaster;
            gd.DepthStencilState = oldDepth;
            gd.BlendState = oldBlend;
            gd.Viewport = oldVp;
            gd.ScissorRectangle = oldScissor;
        }

        public void Dispose()
        {
            _tex.Clear();
            _texMissing.Clear();
            _vb?.Dispose();
            _ib?.Dispose();
            _fx?.Dispose();
            _data = null;
        }
    }
}
