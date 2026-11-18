using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PvZPlantSwap.Core;

namespace PlantSwap.App
{
    /// <summary>
    /// reanim 预览在 AppGame 里的接线：初始化、选植物、每帧 tick、鼠标缩放/平移、
    /// 观赏模式的「植物 / 僵尸」页签 UI。只碰 App/，不改 Core/。
    /// </summary>
    public partial class AppGame
    {
        ReanimPreview _rvA;
        /// <summary>观赏模式页签：0=植物（默认），1=僵尸。</summary>
        int _watchTab;   // 旧版页签状态，只为兼容老 ini 保留
        bool _rvZombie => _mode == 2 && _watchZombie;
        List<string> _zombieKeys = new List<string>();
        int _zombieSel;                 // 0 = 没选
        int _rvAnimSel;
        int _accSel;                    // 0=不显示配件 1=全部 2..=指定配件组
        bool _trackPanelOpen;           // 「轨道开关」是否展开（顶部控制条要跟着变高）

        // Hide = 选中这套配件时要一起藏掉的原生部件（配件会替换它们）
        static readonly (string Key, string Label, string[] Hide)[] AccessoryGroups =
        {
            ("cone", "路障", null),
            ("bucket", "铁桶", null),
            ("flag", "旗子", new[] { "anim_innerarm3" }),
            ("screendoor", "纱门", new[] { "anim_innerarm1", "anim_innerarm3" }),
            ("duckytube", "鸭子圈", null),
            ("whitewater", "水花", null),
            ("mustache", "胡子", null),
            ("ladder", "梯子", null),
            ("newspaper", "读报", null),
        };

        /// <summary>按 _accSel 决定哪些配件轨道可见（其余塞进 HiddenTracks）。</summary>
        void ApplyAccessoryFilter()
        {
            if (_rvA == null) return;
            _rvA.ShowAccessories = true;   // 过滤统一交给 HiddenTracks，避免两套规则打架
            var names = _rvA.TrackNames;

            string key = null;
            string[] hide = null;
            if (_accSel >= 2)
            {
                var g = AccessoryGroups[Math.Min(_accSel - 2, AccessoryGroups.Length - 1)];
                key = g.Key;
                hide = g.Hide;
            }

            for (int i = 0; i < names.Count; i++)
            {
                var n = names[i];
                bool isAcc = false;
                foreach (var g in AccessoryGroups)
                    if (n.IndexOf(g.Key, StringComparison.OrdinalIgnoreCase) >= 0) { isAcc = true; break; }

                bool show;
                if (!isAcc) show = true;
                else if (_accSel <= 0) show = false;
                else if (_accSel == 1) show = true;
                else show = n.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;

                if (show && hide != null)
                    foreach (var h in hide)
                        if (string.Equals(n, h, StringComparison.OrdinalIgnoreCase)) { show = false; break; }

                if (show) _rvA.HiddenTracks.Remove(n);
                else _rvA.HiddenTracks.Add(n);
            }

            // 旗子本体是独立 reanim，挂到举旗那条轨道的矩阵上
            if (string.Equals(key, "flag", StringComparison.OrdinalIgnoreCase))
                _rvA.SetAttachment("Zombie_flagpole", "zombie_flaghand");
            else
                _rvA.SetAttachment(null, null);
        }
        string[] _rvAnimLabels = Array.Empty<string>();


        /// <summary>当前 A 面板是不是在放 reanim（僵尸页，或植物页里没有 Spine 的植物）。</summary>
        bool ReanimVisible => _rvA != null && _rvA.Ready && (_rvZombie || (_portraitMode && !_pvA.Ready));

        void InitReanim()
        {
            try
            {
                if (_rvA == null) _rvA = new ReanimPreview(GraphicsDevice, _cm, _content);
                _zombieKeys = ReanimPreview.ListReanims(_content, "Zombie");
                if (_zombieSel > _zombieKeys.Count) _zombieSel = 0;
            }
            catch (Exception ex) { Say("reanim 初始化失败：" + ex.Message); }
        }

        /// <summary>选植物时同步加载它的 reanim（没有就清空，退回原画）。不动页签状态。</summary>
        void SelectReanimFor(string key)
        {
            try { _rvA?.Load(key); } catch (Exception ex) { Say("reanim 载入失败：" + ex.Message); }
            RefreshReanimAnimNames();
        }

        void RefreshReanimAnimNames()
        {
            try
            {
                if (_rvA == null || !_rvA.Ready) { _rvAnimLabels = Array.Empty<string>(); return; }
                var names = _rvA.Animations;
                EnsureLangAnims(names);
                var labels = new List<string>();
                foreach (var n in names) labels.Add(AnimCn.Label(n));
                _rvAnimLabels = labels.ToArray();
                _rvAnimSel = 0;
                for (int i = 0; i < names.Count; i++)
                    if (string.Equals(names[i], _rvA.CurrentAnimation, StringComparison.OrdinalIgnoreCase)) { _rvAnimSel = i; break; }
            }
            catch { _rvAnimLabels = Array.Empty<string>(); }
        }

        void TickReanim(float dt)
        {
            if (_rvA == null) return;
            try
            {
                _rvA.Paused = _paused;
                _rvA.Update(dt);
                }
            catch { }
        }

        /// <summary>鼠标在 A 面板上且是 reanim 预览时，接管滚轮缩放 / 拖动平移 / 双击复位。</summary>
        bool HandleReanimMouse(MouseState ms, ImGuiIOPtr io)
        {
            if (_rvA == null || !ReanimVisible) return false;
            if (!_rectA.Contains(ms.Position) || io.WantCaptureMouse) return false;

            int wheel = ms.ScrollWheelValue;
            if (!_wheelInit) { _lastWheel = wheel; _wheelInit = true; }
            if (wheel != _lastWheel)
            {
                float d = (wheel - _lastWheel) / 120f;
                _rvA.Zoom = Math.Clamp(_rvA.Zoom * (float)Math.Pow(1.12, d), 0.2f, 8f);
                _lastWheel = wheel;
            }
            if (ms.LeftButton == ButtonState.Pressed && !_wasDown)
            {
                double now = gameTimeSec();
                if (now - _lastClickTimeA < 0.32) { _rvA.Zoom = 1f; _rvA.Pan = Vector2.Zero; }
                _lastClickTimeA = now;
            }
            _wasDown = ms.LeftButton == ButtonState.Pressed;
            bool down = ms.LeftButton == ButtonState.Pressed || ms.RightButton == ButtonState.Pressed || ms.MiddleButton == ButtonState.Pressed;
            if (down)
            {
                if (_dragA) _rvA.Pan += new Vector2(ms.X - _lastMouse.X, ms.Y - _lastMouse.Y);
                _dragA = true;
                _lastMouse = ms.Position;
            }
            else _dragA = false;
            return true;
        }

        /// <summary>僵尸 reanim 的中文名（图鉴名）。</summary>
        static readonly Dictionary<string, string> ZombieCn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Zombie"] = "普通僵尸",
            ["Zombie_flagpole"] = "旗帜僵尸",
            ["Zombie_bungi"] = "铁桶僵尸",
            ["Zombie_polevaulter"] = "撑杆跳僵尸",
            ["Zombie_paper"] = "读报僵尸",
            ["Zombie_football"] = "橄榄球僵尸",
            ["Zombie_disco"] = "舞王僵尸",
            ["Zombie_backup"] = "伴舞僵尸",
            ["Zombie_dolphinrider"] = "海豚骑士僵尸",
            ["Zombie_snorkle"] = "潜水僵尸",
            ["Zombie_pogo"] = "跳跳僵尸",
            ["Zombie_ladder"] = "梯子僵尸",
            ["Zombie_balloon"] = "气球僵尸",
            ["Zombie_digger"] = "矿工僵尸",
            ["Zombie_catapult"] = "投石车僵尸",
            ["Zombie_jackbox"] = "玩偶匣僵尸",
            ["Zombie_propeller"] = "螺旋桨僵尸",
            ["Zombie_gargantuar"] = "巨人僵尸",
            ["Zombie_imp"] = "小鬼僵尸",
            ["Zombie_boss"] = "僵王博士",
            ["Zombie_ninja"] = "忍者僵尸",
            ["Zombie_monk"] = "武僧僵尸",
        };

        static string ZombieLabel(string key)
        {
            var custom = Lang.Zombie(key);
            if (custom.Length > 0) return custom + " (" + key + ")";
            return ZombieCn.TryGetValue(key, out var cn) ? cn + " (" + key + ")" : key;
        }

        // 观赏模式自己的状态：选中的东西、列表范围，跟其它三个模式互不影响
        string _watchKey = "";
        int _watchScope;               // 0=植物 1=僵尸 2=全部
        bool _watchZombie;
        string _watchZombieKey = "";
        readonly List<string> _watchKeys = new List<string>();
        string[] _watchDisp = Array.Empty<string>();
        int _watchSel;

        bool IsZombieKey(string k)
        {
            if (string.IsNullOrEmpty(k)) return false;
            foreach (var z in _zombieKeys) if (string.Equals(z, k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void RebuildWatchList()
        {
            _watchKeys.Clear();
            var q = (_watchSearch ?? "").Trim().ToLowerInvariant();
            var disp = new List<string>();
            void Add(string k)
            {
                var label = IsZombieKey(k) ? ZombieLabel(k) : CnNames.DisplayName(_names, k);
                if (q.Length > 0 && !label.ToLowerInvariant().Contains(q) && !k.ToLowerInvariant().Contains(q)) return;
                _watchKeys.Add(k); disp.Add(label);
            }
            if (_watchScope == 1) { foreach (var k in _zombieKeys) Add(k); }
            else if (_lists == null) { /* 还没载入游戏资源 */ }
            else if (_watchScope == 2) { foreach (var k in _lists.Keys) Add(k); }
            else { foreach (var k in _lists.Plants) Add(k); }
            _watchDisp = disp.ToArray();
            if (_watchSel >= _watchDisp.Length) _watchSel = Math.Max(0, _watchDisp.Length - 1);
        }

        void SelectWatch(int idx)
        {
            if (idx < 0 || idx >= _watchKeys.Count) return;
            _watchSel = idx;
            var k = _watchKeys[idx];
            _watchKey = k;
            LoadWatchIntoPreview(k);
        }

        /// <summary>把观赏选中的 key 装进预览：植物走 Spine 立绘，僵尸走 reanim。</summary>
        void ShareWatchSelection(string key)
        {
            _rvA?.Clear();
            _sheetA = null;
            _pvA.Clear();
            _watchKey = key;
            if (string.IsNullOrEmpty(key)) { _watchZombie = false; _watchZombieKey = ""; return; }
            if (IsZombieKey(key))
            {
                _watchZombie = true; _watchZombieKey = key;
                try
                {
                    _rvA.Load(key);
                    _rvA.Play("idle");
                    ApplyAccessoryFilter();
                }
                catch (Exception ex) { Say("僵尸载入失败：" + ex.Message); }
                RefreshReanimAnimNames();
                return;
            }
            _watchZombie = false; _watchZombieKey = "";
            _pvA.Load(key);
            SelectReanimFor(key);
            _sheetA = LoadPainting(key);
            RefreshAnimNames();
        }

        /// <summary>选中僵尸时的控制行（选择器在上面的统一行里，这里只放僵尸专属控件）。</summary>
        void BuildZombieControls()
        {
            if (_rvA == null || !_rvA.Ready) return;

            ImGui.SameLine();
            ImGui.Text("动作");
            ImGui.SameLine();
            var labels = _rvAnimLabels.Length > 0 ? _rvAnimLabels : new[] { "（没有动作）" };
            ImGui.SetNextItemWidth(220);
            if (ImGui.Combo("##rvanim", ref _rvAnimSel, labels, labels.Length))
                if (_rvAnimSel >= 0 && _rvAnimSel < _rvA.Animations.Count) _rvA.Play(_rvAnimSel);
            Tip("reanim 文件里的动画层（walk / idle / eat / dance …）");

            ImGui.SameLine();
            var accItems = new List<string> { "不显示配件", "全部配件" };
            foreach (var g in AccessoryGroups) accItems.Add(g.Label);
            ImGui.SetNextItemWidth(130);
            if (ImGui.Combo("##acc", ref _accSel, accItems.ToArray(), accItems.Count))
                ApplyAccessoryFilter();
            Tip("只显示某一套配件：路障 / 铁桶 / 旗子 / 纱门 / 鸭子圈…（全选会套一身，默认不显示）");

            ImGui.SameLine();
            if (ImGui.Button(_paused ? "播放" : "暂停")) _paused = !_paused;
            ImGui.SameLine();
            if (ImGui.Button("复位视图")) { _rvA.Zoom = 1f; _rvA.Pan = Vector2.Zero; }
            ImGui.SameLine();
            ImGui.TextDisabled((_rvA.Error ?? "") + "  " +
                               (_rvA.MissingRegions > 0 ? "  缺矩形 " + _rvA.MissingRegions : "") +
                               (_rvA.MissingTextures > 0 ? "  缺贴图 " + _rvA.MissingTextures : ""));

            _trackPanelOpen = ImGui.CollapsingHeader("轨道开关##rvtracks");
            if (_trackPanelOpen)
            {
                var names = _rvA.TrackNames;
                // 轨道可能有几十条，固定高度 + 滚动，否则下半截被窗口截掉
                if (ImGui.BeginChild("##tracklist", new System.Numerics.Vector2(0, 180), true))
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        bool vis = !_rvA.HiddenTracks.Contains(names[i]);
                        if (ImGui.Checkbox(names[i] + "##t" + i, ref vis))
                        {
                            if (vis) _rvA.HiddenTracks.Remove(names[i]);
                            else _rvA.HiddenTracks.Add(names[i]);
                        }
                    }
                }
                ImGui.EndChild();
            }
        }

        /// <summary>植物页里没有 Spine 的植物：显示 reanim 动作行；返回 true 表示已接管。</summary>
        bool BuildReanimRows()
        {
            if (_rvA == null || !_rvA.Ready || !ReanimVisible) return false;

            ImGui.Text("reanim 动作");
            ImGui.SameLine();
            var labels = _rvAnimLabels.Length > 0 ? _rvAnimLabels : new[] { "（没有动作）" };
            ImGui.SetNextItemWidth(240);
            if (ImGui.Combo("##rvanim", ref _rvAnimSel, labels, labels.Length))
                if (_rvAnimSel >= 0 && _rvAnimSel < _rvA.Animations.Count) _rvA.Play(_rvAnimSel);
            Tip("reanim 文件里的动画层（walk / idle / eat …）");

            ImGui.SameLine();
            bool acc = _rvA.ShowAccessories;
            if (ImGui.Checkbox("显示配件", ref acc)) _rvA.ShowAccessories = acc;
            ImGui.SameLine();
            if (ImGui.Button(_paused ? "播放" : "暂停")) _paused = !_paused;
            ImGui.SameLine();
            if (ImGui.Button("复位视图")) { _rvA.Zoom = 1f; _rvA.Pan = Vector2.Zero; }
            ImGui.SameLine();
            ImGui.TextDisabled((_rvA.Error ?? "") + "  " +
                               (_rvA.MissingRegions > 0 ? "  缺矩形 " + _rvA.MissingRegions : "") +
                               (_rvA.MissingTextures > 0 ? "  缺贴图 " + _rvA.MissingTextures : ""));
            return true;
        }

        /// <summary>面板角标文案：reanim 生效时说明是原版帧动画。</summary>
        string ReanimBadge()
        {
            if (_rvA == null || !_rvA.Ready || !ReanimVisible) return null;
            // 缺图块说明这个 reanim 用到了工具不认识的图集（自制素材常见），画出来会花，直接说清楚
            if (_rvA.MissingRegions >= 8)
                return "（缺 " + _rvA.MissingRegions + " 个图块，预览可能不完整）";
            if (_rvZombie) return "（原版僵尸 · reanim " + _rvA.CurrentAnimation + "）";
            return "（原版植物 · reanim " + _rvA.CurrentAnimation + "）";
        }

        /// <summary>开发/验收用：--zombie 直接进僵尸页，--anim walk 指定动作，--plant cherrybomb 看植物。</summary>
        void ApplyStartupArgs()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool zombie = false;
                string anim = null;
                string plant = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--zombie", StringComparison.OrdinalIgnoreCase)) zombie = true;
                    else if (string.Equals(args[i], "--anim", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) anim = args[++i];
                    else if (string.Equals(args[i], "--plant", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) plant = args[++i];
                }
                if (plant != null)
                {
                    _mode = 2;
                    OnModeChanged();
                    int pi = _keysA.FindIndex(k => string.Equals(k, plant, StringComparison.OrdinalIgnoreCase));
                    if (pi >= 0)
                    {
                        SelectIndex(pi, true);
                        Say(_pvA.Ready ? "已选植物：" + plant + "（Spine 立绘）"
                                       : "已选植物：" + plant + "（reanim " + _rvA.CurrentAnimation + "）");
                    }
                    else Say("找不到植物：" + plant);
                    return;
                }
                if (!zombie || _zombieKeys.Count == 0) return;
                _mode = 2;
                OnModeChanged();
                int idx = _zombieKeys.FindIndex(x => string.Equals(x, "Zombie", StringComparison.OrdinalIgnoreCase));
                if (idx < 0) idx = 0;
                _zombieSel = idx + 1;
                _watchScope = 1;
                RebuildWatchList();
                int zi = _watchKeys.FindIndex(k => string.Equals(k, _zombieKeys[idx], StringComparison.OrdinalIgnoreCase));
                SelectWatch(zi >= 0 ? zi : 0);
                _rvA.Load(_zombieKeys[idx]);
                RefreshReanimAnimNames();
                if (!string.IsNullOrEmpty(anim)) _rvA.Play(anim);
                for (int i = 0; i < _rvA.Animations.Count; i++)
                    if (string.Equals(_rvA.Animations[i], _rvA.CurrentAnimation, StringComparison.OrdinalIgnoreCase)) { _rvAnimSel = i; break; }

            }
            catch (Exception ex) { Say("启动参数处理失败：" + ex.Message); }
        }
    }
}
