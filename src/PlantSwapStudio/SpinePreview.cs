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
            _plainMeasure = false;   // 用户手动选的动作：按这个动作的真实范围重新测量
            _measured = false;
            _fit = 0;
        }

        /// <summary>回到载入时的默认动作（idle / enter / 第一个），用于离开观赏模式。</summary>
        /// <summary>
        /// 默认动作：优先 idle 一类；没有的话在剩下的里挑时长最长的，
        /// 跳过 special / fx / effect 这些特效动作（萝丝就只有 special，直接被选中就成了一坨光）。
        /// </summary>
        Spine.Animation PickDefaultAnimation()
        {
            if (_data == null) return null;
            foreach (var n in new[] { "idle", "anim_idle", "stand", "standby", "wait", "enter", "default", "show" })
            {
                var a = _data.FindAnimation(n);
                if (a != null) return a;
            }
            Spine.Animation best = null;
            foreach (var a in _data.Animations)
            {
                var nm = (a.Name ?? "").ToLowerInvariant();
                if (nm.Contains("special") || nm.Contains("fx") || nm.Contains("effect")
                    || nm.Contains("hit") || nm.Contains("die") || nm.Contains("attack")) continue;
                if (best == null || a.Duration > best.Duration) best = a;
            }
            return best;   // 全是特效动作时返回 null：改用 setup pose（萝丝就只有 special，播起来是一坨光）
        }

        public void ResetDefaultAnimation()
        {
            if (_data == null || _state == null) return;
            var anim = PickDefaultAnimation();
            if (anim == null && _data.Animations.Count > 0) { _plainMeasure = true; _fit = 0; return; }
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
                var anim = PickDefaultAnimation();
                if (anim == null && _data.Animations.Count > 0)
                {
                    // 只有特效类动作（萝丝就是 special，一刀砍出去会飘出画面）：
                    // 默认用 setup pose 定格显示，用户想看动作可以在「动作」里手动选。
                    _plainMeasure = true;
                }
                if (anim != null) { _state.SetAnimation(0, anim.Name, true); _animDuration = Math.Max(anim.Duration, 0.05f); }

                _fit = 0f; _fitW = _fitH = 0;
                Zoom = 1f; Pan = Vector2.Zero; Time = 0; Paused = false;
                PlantKey = key; Error = null;
            }
            catch (Exception ex)
            {
                Clear(); PlantKey = key; Error = ex.Message;
            }
        }

        float _measUw = 1f, _measUh = 1f, _measCx, _measCy;
        bool _measured;
        int _measTries;
        float _measScale;

        /// <summary>为 true 时尺寸测量忽略动画（只有特效动作的 skel 用，避免特效把包围盒撑爆）。</summary>
        bool _plainMeasure;

        void MeasureFallback()
        {
            try
            {
                _skel.SetToSetupPose();
                // 测量循环里把 ScaleX/Y 改小了，GetBounds 会跟着缩，必须先还原成 1
                _skel.ScaleX = _skel.ScaleY = 1f;
                _skel.X = 0f; _skel.Y = 0f;
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
            // 像素测量没拿到东西（有 skel 带超大隐藏顶点），退回包围盒，总比 m=1x1 放大到出框好
            if (_measUw < 4f || _measUh < 4f || float.IsNaN(_measUw) || float.IsNaN(_measUh))
            {
                MeasureFallback();
            }
            // 量出来的尺寸兜个底：太小说明测量失败（会被放到出框），太大说明混进了特效
            _uw = Math.Clamp(_measUw, 8f, 4000f);
            _uh = Math.Clamp(_measUh, 8f, 4000f);
            _cx = _measCx; _cy = _measCy;
            // 按实际画出来的像素定比例：每只都填到面板高度的 92%，两边看起来一样大
            _fit = Math.Min(w * 0.92f / _uw, h * 0.92f / _uh);
            if (_fit <= 0 || float.IsNaN(_fit) || float.IsInfinity(_fit)) _fit = 1f;
            // 注意：不要在这里再做「渲染一次再按实际像素重算」的二次校正，
            // 初始 fit 偏大/偏小时会互相放大（萝丝就从 0.34 被推到 2.1），老老实实按测量值来。
        }

        /// <summary>
        /// 用「面板尺寸的真实渲染」再校一次：先按算出来的 fit 画一遍，找实际像素范围，
        /// 再据此修正中心点和比例。像萝丝这种包围盒里一大块是看不见的光效，只靠包围盒会缩成一坨。
        /// </summary>
        void RefineFit(int w, int h)
        {
            if (_skel == null || w <= 0 || h <= 0) return;
            RenderTarget2D rt = null;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    float sc = _fit;
                    rt?.Dispose();
                    rt = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
                    _skel.SetToSetupPose();
                    var anim = _plainMeasure ? null : _state?.GetCurrent(0)?.Animation;
                    if (anim != null)
                    {
                        var probe = new AnimationState(new AnimationStateData(_data));
                        probe.SetAnimation(0, anim.Name, true);
                        probe.Update(_animDuration * 0.4f);
                        probe.Apply(_skel);
                    }
                    _skel.ScaleX = _skel.ScaleY = sc;
                    _skel.X = w / 2f - _cx * sc;
                    _skel.Y = h / 2f + _cy * sc;
                    _skel.UpdateWorldTransform(Skeleton.Physics.None);

                    var gd = _device;
                    var oldVp = gd.Viewport; var oldSc = gd.ScissorRectangle;
                    gd.SetRenderTarget(rt);
                    gd.Clear(Color.Transparent);
                    gd.Viewport = new Viewport(0, 0, w, h);
                    gd.ScissorRectangle = new Rectangle(0, 0, w, h);
                    _effect.Projection = Matrix.CreateOrthographicOffCenter(0, w, h, 0, -1, 1);
                    _renderer.Begin(); _renderer.Draw(_skel); _renderer.End();
                    gd.SetRenderTarget(null);
                    gd.Viewport = oldVp; gd.ScissorRectangle = oldSc;

                    var px = new Color[w * h];
                    rt.GetData(px);
                    var col = new int[w]; var row = new int[h];
                    int total = 0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            if (px[y * w + x].A >= 24) { col[x]++; row[y]++; total++; }
                    if (total < 40) return;

                    int thr = Math.Max(3, total / 400);
                    int x0 = 0, x1 = w - 1, y0 = 0, y1 = h - 1;
                    while (x0 < x1 && col[x0] < thr) x0++;
                    while (x1 > x0 && col[x1] < thr) x1--;
                    while (y0 < y1 && row[y0] < thr) y0++;
                    while (y1 > y0 && row[y1] < thr) y1--;
                    if (x1 - x0 < 4 || y1 - y0 < 4) return;

                    float bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                    float bcx = (x0 + x1) / 2f - w / 2f;    // 相对面板中心的偏移（像素）
                    float bcy = (y0 + y1) / 2f - h / 2f;

                    // 把内容平移到面板中心
                    _cx += bcx / sc;
                    _cy -= bcy / sc;
                    // 再按内容占面板的比例修正缩放
                    float fill = Math.Max(bw / (w * 0.92f), bh / (h * 0.92f));
                    if (fill > 0.02f && Math.Abs(fill - 1f) > 0.05f)
                    {
                        _fit /= fill;
                        if (_fit <= 0 || float.IsNaN(_fit) || float.IsInfinity(_fit)) _fit = 1f;
                    }
                }
            }
            catch { }
            finally { rt?.Dispose(); }
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
                const int SAMPLES = 8;      // 一帧测不准：有的动画开头只有特效/身体还在画面外
                float scale = 0.5f;
                var anim = _plainMeasure ? null : _state?.GetCurrent(0)?.Animation;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    _measTries = attempt + 1; _measScale = scale;
                    var samples = new System.Collections.Generic.List<(float uw, float uh, float cx, float cy)>();
                    bool clippedAny = false;

                    for (int s = 0; s < SAMPLES && !clippedAny; s++)
                    {
                        _skel.SetToSetupPose();
                        if (anim != null)
                        {
                            var probe = new AnimationState(new AnimationStateData(_data));
                            probe.SetAnimation(0, anim.Name, true);
                            probe.Update(_animDuration * (s + 0.5f) / SAMPLES);
                            probe.Apply(_skel);
                        }
                        _skel.ScaleX = _skel.ScaleY = scale;
                        _skel.X = SZ / 2f; _skel.Y = SZ / 2f;
                        _skel.UpdateWorldTransform(Skeleton.Physics.None);

                        rt?.Dispose();
                        rt = new RenderTarget2D(_device, SZ, SZ, false, SurfaceFormat.Color, DepthFormat.None);
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
                        // 按「列/行里不透明像素的密度」定边界：有的娘化 skel 带大片半透明光效，
                        // 光效会顶到画布边缘，用最外像素当边界就会永远判成「被裁掉」。
                        var colCnt = new int[SZ];
                        var rowCnt = new int[SZ];
                        int total = 0;
                        for (int y = 0; y < SZ; y++)
                            for (int x = 0; x < SZ; x++)
                            {
                                if (px[y * SZ + x].A < 24) continue;
                                colCnt[x]++; rowCnt[y]++; total++;
                            }
                        if (total == 0) continue;                 // 这一帧什么都没有，跳过
                        int thr = Math.Max(4, total / 400);       // 约 0.25% 密度才算「实体」
                        int x0 = 0, x1 = SZ - 1, y0 = 0, y1 = SZ - 1;
                        while (x0 < x1 && colCnt[x0] < thr) x0++;
                        while (x1 > x0 && colCnt[x1] < thr) x1--;
                        while (y0 < y1 && rowCnt[y0] < thr) y0++;
                        while (y1 > y0 && rowCnt[y1] < thr) y1--;
                        if (x1 <= x0 || y1 <= y0) continue;
                        if (x0 <= 1 || y0 <= 1 || x1 >= SZ - 2 || y1 >= SZ - 2) { clippedAny = true; break; }

                        float ux0 = (x0 - SZ / 2f) / scale, ux1 = (x1 - SZ / 2f) / scale;
                        float uyTop = -(y0 - SZ / 2f) / scale, uyBot = -(y1 - SZ / 2f) / scale;
                        samples.Add((ux1 - ux0, uyTop - uyBot, (ux0 + ux1) / 2f, (uyBot + uyTop) / 2f));
                    }

                    if (clippedAny)
                    {
                        // 有的 skel 里塞了超大坐标的隐藏顶点，怎么缩都会顶到边缘 → 直接放弃像素测量
                        if (attempt < 3) { scale *= 0.5f; continue; }
                        break;
                    }
                    if (samples.Count == 0) break;
                    // 取「中位数那一帧」：既躲开只有特效的帧，也躲开特效糊满屏的帧
                    samples.Sort((p, q) => (p.uw * p.uh).CompareTo(q.uw * q.uh));
                    var pick = samples[samples.Count / 2];
                    _measUw = Math.Max(pick.uw, 1f);
                    _measUh = Math.Max(pick.uh, 1f);
                    _measCx = pick.cx;
                    _measCy = pick.cy;
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