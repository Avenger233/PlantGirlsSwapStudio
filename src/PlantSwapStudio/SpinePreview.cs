using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Spine;

namespace PlantSwap.App
{
    /// <summary>单个植物的实时 Spine 预览：支持缩放/平移/暂停，任意分辨率都清晰。</summary>
    public sealed class SpinePreview : IDisposable
    {
        readonly GraphicsDevice _device;
        string _contentDir;
        readonly SkeletonRenderer _renderer;
        readonly BasicEffect _effect;

        Atlas _atlas;
        SkeletonData _data;
        Skeleton _skel;
        AnimationState _state;
        float _animDuration = 1f;

        public string PlantKey { get; private set; } = "";
        public string ContentDir { get => _contentDir; set => _contentDir = value; }
        public string Error { get; private set; }
        public float Zoom { get; set; } = 1f;
        public Vector2 Pan { get; set; }
        public bool Paused { get; set; }
        public bool Ready => _skel != null;
        public float Time { get; private set; }

        public IReadOnlyList<string> Animations
        {
            get
            {
                var list = new List<string>();
                if (_data != null)
                    for (int i = 0; i < _data.Animations.Count; i++) list.Add(_data.Animations.Items[i].Name);
                return list;
            }
        }

        public string CurrentAnimation => _state?.GetCurrent(0)?.Animation?.Name;

        public void Play(string name, bool loop = true)
        {
            if (_data == null || _state == null || string.IsNullOrEmpty(name)) return;
            var an = _data.FindAnimation(name);
            if (an == null) return;
            _state.SetAnimation(0, name, loop);
            _animDuration = Math.Max(an.Duration, 0.05f);
            _userPlayed = true;
            _fit = 0;
        }

        /// <summary>回到载入时的默认动作（idle / enter / 第一个），用于离开观赏模式。</summary>
        public void ResetDefaultAnimation()
        {
            if (_data == null || _state == null) return;
            var anim = _data.FindAnimation("idle") ?? _data.FindAnimation("enter")
                       ?? (_data.Animations.Count > 0 ? _data.Animations.Items[0] : null);
            if (anim == null) return;
            _state.SetAnimation(0, anim.Name, true);
            _animDuration = Math.Max(anim.Duration, 0.05f);
            _fit = 0;
        }

        float _fit, _cx, _cy, _uw = 1f, _uh = 1f;
        int _fitW, _fitH;

        /// <summary>本面板自适应算出来的缩放（内容单位 → 像素）。</summary>
        public float FitScale => _fit > 0f ? _fit : 1f;

        /// <summary>外部统一比例：&gt;0 时用它代替自己的自适应缩放（A/B 两边画一样大）。</summary>
        public float SharedScale;

        public string FitInfo => $"m={_measUw:F0}x{_measUh:F0} c=({_measCx:F0},{_measCy:F0}) fit={_fit:F4} tries={_measTries} scale={_measScale:F3}";

        public SpinePreview(GraphicsDevice device, string contentDir)
        {
            _device = device;
            _contentDir = contentDir;
            _renderer = new SkeletonRenderer(device) { PremultipliedAlpha = true };
            _effect = (BasicEffect)_renderer.Effect;
            _effect.TextureEnabled = true;
            _effect.VertexColorEnabled = true;
            _effect.World = Matrix.Identity;
            _effect.View = Matrix.CreateLookAt(new Vector3(0, 0, 1), Vector3.Zero, Vector3.Up);
        }

        public void Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            try
            {
                var atlasPath = Path.Combine(_contentDir, key + ".atlas");
                var skelPath = Path.Combine(_contentDir, key + ".skel");
                if (!File.Exists(skelPath) || !File.Exists(atlasPath))
                {
                    // 文件名可能带下划线（spikerock_tachie），按规范化名再找一遍
                    skelPath = null; atlasPath = null;
                    if (!string.IsNullOrEmpty(_contentDir) && Directory.Exists(_contentDir))
                        foreach (var f in Directory.GetFiles(_contentDir, "*.skel"))
                            if (Norm(Path.GetFileNameWithoutExtension(f)) == key)
                            {
                                skelPath = f;
                                atlasPath = Path.ChangeExtension(f, ".atlas");
                                break;
                            }
                    if (skelPath == null || !File.Exists(atlasPath))
                    {
                        Clear(); Error = "没有 Spine 资源"; PlantKey = key; return;
                    }
                }
                var loader = new FileTextureLoader(_device);
                _atlas = new Atlas(atlasPath, loader);
                var bin = new SkeletonBinary(_atlas);
                using (var fs = File.OpenRead(skelPath)) _data = bin.ReadSkeletonData(fs);
                _skel = new Skeleton(_data);
                _state = new AnimationState(new AnimationStateData(_data));
                var anim = _data.FindAnimation("idle") ?? _data.FindAnimation("enter")
                           ?? (_data.Animations.Count > 0 ? _data.Animations.Items[0] : null);
                if (anim != null) { _state.SetAnimation(0, anim.Name, true); _animDuration = Math.Max(anim.Duration, 0.05f); }

                _fit = 0f; _fitW = _fitH = 0;
                _userPlayed = false;
                Zoom = 1f; Pan = Vector2.Zero; Time = 0; Paused = false;
                PlantKey = key; Error = null;
            }
            catch (Exception ex)
            {
                Clear(); PlantKey = key; Error = ex.Message;
            }
        }

        bool _userPlayed;   // 用户手动播过动作：不要再被 setup pose 覆盖

        /// <summary>兜底测量：用 setup pose 的骨架包围盒（记得先把缩放还原成 1，否则会量小）。</summary>
        void MeasureBoundsFallback()
        {
            // 测不准的素材（萝丝那种：只有一个带位移的特效动作）→ 先停在 setup pose 让构图好看；
            // 但用户点过「播放」（_userPlayed）之后就不能再清，否则每帧把动作又抹掉。
            if (!_userPlayed)
            {
                try { _state?.ClearTracks(); _skel?.SetToSetupPose(); } catch { }
            }
            try
            {
                _skel.SetToSetupPose();
                _skel.ScaleX = _skel.ScaleY = 1f;
                _skel.X = 0f; _skel.Y = 0f;
                _skel.UpdateWorldTransform(Skeleton.Physics.None);
                float bx, by, bw, bh; float[] vb = null;
                _skel.GetBounds(out bx, out by, out bw, out bh, ref vb);
                _measUw = Math.Max(Math.Clamp(bw, 8f, 4000f), 8f);
                _measUh = Math.Max(Math.Clamp(bh, 8f, 4000f), 8f);
                _measCx = bx + bw / 2f;
                _measCy = by + bh / 2f;
            }
            catch { _measUw = _measUh = 1f; _measCx = _measCy = 0f; }
        }

        float _measUw = 1f, _measUh = 1f, _measCx, _measCy;
        bool _measured;
        int _measTries;
        float _measScale;

        void MeasureFallback()
        {
            try
            {
                _skel.SetToSetupPose();
                _skel.UpdateWorldTransform(Skeleton.Physics.None);
                float bx, by, bw, bh; float[] vb = null;
                _skel.GetBounds(out bx, out by, out bw, out bh, ref vb);
                _measUw = Math.Max(bw, 1f);
                _measUh = Math.Max(bh, 1f);
                _measCx = bx + bw / 2f;
                _measCy = by + bh / 2f;
            }
            catch { _measUw = _measUh = 1f; _measCx = _measCy = 0f; }
        }

        void ComputeFit(int w, int h)
        {
            if (_skel == null) return;
            if (w <= 0 || h <= 0) { _fit = 0.4f; _cx = _cy = 0; return; }
            if (!_measured) MeasureContent();
            // 像素测量失败（有的 skel 带超大隐藏顶点，怎么缩都顶到边缘）→ 退回骨架包围盒
            if (_measUw < 4f || _measUh < 4f) MeasureBoundsFallback();
            _uw = Math.Max(_measUw, 1f);
            _uh = Math.Max(_measUh, 1f);
            _cx = _measCx; _cy = _measCy;
            // 按实际画出来的像素定比例：每只都填到面板高度的 92%，两边看起来一样大
            _fit = Math.Min(w * 0.92f / _uw, h * 0.92f / _uh);
            if (_fit <= 0 || float.IsNaN(_fit) || float.IsInfinity(_fit)) _fit = 1f;
        }

        /// <summary>
        /// 离屏画一帧、扫像素求真实内容范围。
        /// 不能用 Skeleton.GetBounds：这些娘化 skel 里带看不见的占位/特效，包围盒会被拉偏
        /// （大喷菇量出来 765×397，实际画出来的女孩只有一半宽），按它缩放会偏小又偏左。
        /// </summary>
        void MeasureContent()
        {
            _measured = true;
            _measUw = 1f; _measUh = 1f; _measCx = _measCy = 0f;
            if (_skel == null) return;
            RenderTarget2D rt = null;
            try
            {
                const int SZ = 512;
                float scale = 0.5f;
                var anim = _state?.GetCurrent(0)?.Animation;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    _measTries = attempt + 1; _measScale = scale;
                    rt?.Dispose();
                    rt = new RenderTarget2D(_device, SZ, SZ, false, SurfaceFormat.Color, DepthFormat.None);
                    _skel.SetToSetupPose();
                    if (anim != null)
                    {
                        var probe = new AnimationState(new AnimationStateData(_data));
                        probe.SetAnimation(0, anim.Name, true);
                        probe.Update(_animDuration * 0.35f);
                        probe.Apply(_skel);
                    }
                    _skel.ScaleX = _skel.ScaleY = scale;
                    _skel.X = SZ / 2f; _skel.Y = SZ / 2f;
                    _skel.UpdateWorldTransform(Skeleton.Physics.None);

                    var gd = _device;
                    var oldVp = gd.Viewport;
                    var oldScissor = gd.ScissorRectangle;
                    gd.SetRenderTarget(rt);
                    gd.Clear(Color.Transparent);
                    gd.Viewport = new Viewport(0, 0, SZ, SZ);
                    gd.ScissorRectangle = new Rectangle(0, 0, SZ, SZ);
                    _effect.Projection = Matrix.CreateOrthographicOffCenter(0, SZ, SZ, 0, -1, 1);
                    _renderer.Begin();
                    _renderer.Draw(_skel);
                    _renderer.End();
                    gd.SetRenderTarget(null);
                    gd.Viewport = oldVp;
                    gd.ScissorRectangle = oldScissor;

                    var px = new Color[SZ * SZ];
                    rt.GetData(px);
                    int x0 = SZ, y0 = SZ, x1 = -1, y1 = -1;
                    for (int y = 0; y < SZ; y++)
                        for (int x = 0; x < SZ; x++)
                        {
                            if (px[y * SZ + x].A < 20) continue;
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                    if (x1 < 0) return;
                    bool clipped = x0 <= 1 || y0 <= 1 || x1 >= SZ - 2 || y1 >= SZ - 2;
                    if (clipped && attempt < 3) { scale *= 0.5f; continue; }
                    float pw = x1 - x0 + 1, ph = y1 - y0 + 1;
                    float pcx = (x0 + x1) / 2f, pcy = (y0 + y1) / 2f;
                    _measUw = pw / scale;
                    _measUh = ph / scale;
                    _measCx = (pcx - SZ / 2f) / scale;
                    _measCy = -(pcy - SZ / 2f) / scale;
                    return;
                }
            }
            catch { }
            finally { rt?.Dispose(); }
            // 离屏测量失败就退回包围盒（不如像素准，但至少不会把整只放大到出框）
            MeasureFallback();
        }

        /// <summary>只让自适应缩放失效，下次绘制重算（保留用户缩放/平移）。</summary>
        public void ResetFit() { _fit = 0; _fitW = _fitH = 0; }

        /// <summary>把缩放/平移/自适应全部清零，下次绘制重新算。</summary>
        public void ResetView() { ResetFit(); Zoom = 1f; Pan = Vector2.Zero; }

        /// <summary>按面板尺寸算好自适应（外部要统一比例时先调它再取 FitScale）。</summary>
        public void EnsureFit(int w, int h)
        {
            if (_skel == null || w <= 0 || h <= 0) return;
            if (_fit <= 0f || _fitW != w || _fitH != h) { ComputeFit(w, h); _fitW = w; _fitH = h; }
        }

        public void Update(float dt)
        {
            if (_skel == null) return;
            if (!Paused)
            {
                Time += dt;
                _state.Update(dt);
                _state.Apply(_skel);
            }
        }

        /// <summary>在指定视口内绘制（视口坐标：左上原点）。</summary>
        public void Draw(Rectangle viewport)
        {
            if (_skel == null) return;
            EnsureFit(viewport.Width, viewport.Height);

            var gd = _device;
            var oldViewport = gd.Viewport;
            var oldScissor = gd.ScissorRectangle;
            gd.Viewport = new Viewport(viewport);
            gd.ScissorRectangle = viewport;
            _effect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, -1, 1);

            float sc = (SharedScale > 0f ? SharedScale : _fit) * Zoom;
            _skel.ScaleX = sc;
            _skel.ScaleY = sc;
            _skel.X = viewport.Width / 2f + Pan.X - _cx * sc;
            // 内容中心在骨架原点上方 _cy 个单位，而屏幕 Y 向下，所以这里是加号
            _skel.Y = viewport.Height / 2f + Pan.Y + _cy * sc;
            _skel.UpdateWorldTransform(Skeleton.Physics.None);

            _renderer.Begin();
            _renderer.Draw(_skel);
            _renderer.End();

            gd.Viewport = oldViewport;
            gd.ScissorRectangle = oldScissor;
        }

        public void Clear()
        {
            _skel = null; _state = null; _data = null; _atlas = null; _fit = 0;
            _measured = false;
        }

        static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public void Dispose() { Clear(); }
    }

    public sealed class FileTextureLoader : Spine.TextureLoader
    {
        readonly GraphicsDevice _gd;
        public FileTextureLoader(GraphicsDevice gd) { _gd = gd; }
        public void Load(AtlasPage page, string path)
        {
            if (!File.Exists(path))
            {
                var alt = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + ".png");
                if (File.Exists(alt)) path = alt;
            }
            using var fs = File.OpenRead(path);
            page.rendererObject = Texture2D.FromStream(_gd, fs);
        }
        public void Unload(object texture) => (texture as Texture2D)?.Dispose();
    }
}