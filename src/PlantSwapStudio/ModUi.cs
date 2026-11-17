using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using PvZPlantSwap.Core;
using CoreSwap = PvZPlantSwap.Core.PlantSwap;

namespace PlantSwap.App
{
    public partial class AppGame
    {
        public sealed class ModItem
        {
            public string Dir = "";
            public string Plant = "";
            public string Name = "";
            public int Files;
            public string Time = "";
        }

        readonly List<ModItem> _mods = new();
        int _modSel = -1;
        string _modSelDir = "";
        string _modImportPath = "";
        string _modMsg = "尚未读取";

        string _modPlantKey => _modSel >= 0 && _modSel < _mods.Count ? _mods[_modSel].Plant : "";

        /// <summary>选中作品时把「当前」预览也切到同一只植物。</summary>
        void SelectModPlant(string plant)
        {
            if (string.IsNullOrEmpty(plant)) return;
            _keyA = plant;
            _pvA.Load(plant);
            SelectReanimFor(plant);
            _sheetA = LoadPainting(plant);
            int idx = _keysA.IndexOf(plant);
            if (idx >= 0) _selA = idx;
            RefreshAnimNames();
        }

        void RefreshModPreview()
        {
            try
            {
                if (_modSel < 0 || _modSel >= _mods.Count) { _pvMod.Clear(); return; }
                var m = _mods[_modSel];
                if (string.IsNullOrEmpty(m.Plant)) { _pvMod.Clear(); return; }
                _pvMod.ContentDir = Path.Combine(m.Dir, "spanim");
                _pvMod.Load(m.Plant);
            }
            catch { }
        }

        string PlantCn(string key) =>
            _names.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : CnNames.DisplayName(_names, key);

        static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        static void OpenDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        void RefreshMods()
        {
            _mods.Clear();
            try
            {
                if (!Directory.Exists(ModRoot)) { _modMsg = "还没有作品（先「导出为作品」）"; return; }
                foreach (var dir in Directory.GetDirectories(ModRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ModItem { Dir = dir, Name = Path.GetFileName(dir) };
                    var mf = Path.Combine(dir, "mod.json");
                    if (File.Exists(mf))
                    {
                        try
                        {
                            var jo = System.Text.Json.JsonDocument.Parse(File.ReadAllText(mf)).RootElement;
                            if (jo.TryGetProperty("plant", out var p)) item.Plant = p.GetString() ?? "";
                            if (jo.TryGetProperty("name", out var n2)) item.Name = n2.GetString() ?? item.Name;
                            if (jo.TryGetProperty("time", out var t)) item.Time = t.GetString() ?? "";
                            if (jo.TryGetProperty("files", out var f) && f.TryGetInt32(out int fn)) item.Files = fn;
                        }
                        catch { }
                    }
                    if (item.Files == 0)
                        item.Files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
                    _mods.Add(item);
                }
                _modMsg = _mods.Count == 0 ? "作品目录是空的" : "共 " + _mods.Count + " 个作品";
                // 尽量保持原来选中的那一份（跨模式/跨启动都记）
                if (!string.IsNullOrEmpty(_modSelDir))
                {
                    int wi = _mods.FindIndex(x => string.Equals(x.Dir, _modSelDir, StringComparison.OrdinalIgnoreCase));
                    if (wi >= 0) _modSel = wi;
                }
                if (_mods.Count > 0 && (_modSel < 0 || _modSel >= _mods.Count)) _modSel = 0;
                if (_modSel >= 0 && _modSel < _mods.Count)
                {
                    _modSelDir = _mods[_modSel].Dir;
                    SelectModPlant(_mods[_modSel].Plant);
                }
                RefreshModPreview();
            }
            catch (Exception ex) { _modMsg = "读取作品失败：" + ex.Message; }
        }

        void ExportMod()
        {
            if (string.IsNullOrEmpty(_keyA)) { Say("先选一个植物"); return; }
            try
            {
                var folders = CurrentFolders();
                string cn = PlantCn(_keyA);
                string dir = Path.Combine(ModRoot, SafeName(cn) + "_" + _keyA);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                int n = 0;
                foreach (var f in folders)
                    foreach (var e in CoreSwap.Family(_lists.Table, f, _keyA))
                    {
                        var rel = Path.GetRelativePath(_content, e.Path);
                        var dst = Path.Combine(dir, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? dir);
                        File.Copy(e.Path, dst, true);
                        n++;
                    }
                var json = "{\n  \"plant\": \"" + _keyA + "\",\n  \"name\": \"" + cn +
                           "\",\n  \"time\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                           "\",\n  \"folders\": [" + string.Join(", ", folders.Select(x => "\"" + x + "\"")) +
                           "],\n  \"files\": " + n + "\n}";
                File.WriteAllText(Path.Combine(dir, "mod.json"), json);
                Say("已导出作品「" + cn + "」：" + n + " 个文件 → " + dir);
                RefreshMods();
            }
            catch (Exception ex) { Say("导出失败：" + ex.Message); }
        }

        void ImportModDir(string dir, string label)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { Say("目录不存在：" + dir); return; }
            try
            {
                EnsureOriginal();
                int n = 0;
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(dir, file);
                    if (string.Equals(rel, "mod.json", StringComparison.OrdinalIgnoreCase)) continue;
                    var dst = Path.Combine(_content, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? _content);
                    File.Copy(file, dst, true);
                    n++;
                }
                Say("已导入「" + label + "」：" + n + " 个文件写回游戏");
                _pvA.Load(_keyA); _pvB.Load(_keyB);
                _sheetA = LoadSheet(_keyA); _sheetB = LoadSheet(_keyB);
                RefreshMods();
            }
            catch (Exception ex) { Say("导入失败：" + ex.Message); }
        }

        void BuildModRow()
        {
            foreach (var k in _folders.Keys.ToList())
            {
                bool v = _folders[k];
                if (ImGui.Checkbox(k, ref v)) _folders[k] = v;
                Tip(FolderTip(k));
                ImGui.SameLine();
            }
            ImGui.NewLine();
            if (ImGui.Button("导出为作品")) ExportMod();
            Tip("把植物 A 在勾选目录里的文件整份复制到「资源修改器/Mod」，拿去改贴图，改完再导入");
            ImGui.SameLine();
            if (ImGui.Button("打开作品目录")) OpenDir(ModRoot);
            Tip("用资源管理器打开作品目录，直接改里面的 png / ogg 等文件");
            ImGui.SameLine();
            if (ImGui.Button("刷新作品列表")) RefreshMods();
            ImGui.SameLine();
            if (ImGui.Button("还原原始")) RestoreOriginalAction();
            Tip("把所有文件还原到最初保存的原始备份");
            ImGui.SameLine();
            ImGui.TextDisabled(_mods.Count > 0 ? "作品 " + _mods.Count + " 个" : "作品：暂无（先导出一份）");
        }

        void DrawModList(Rectangle r)
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(r.X, r.Y), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(r.Width, r.Height), ImGuiCond.Always);
            ImGui.Begin("##modlist", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
            ImGui.TextDisabled("作品列表（双击一行 = 导入到游戏）");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("##modpath", ref _modImportPath, 512);
            ImGui.SameLine();
            if (ImGui.Button("导入此文件夹")) ImportModDir(_modImportPath, _modImportPath);
            Tip("填别人给你的作品文件夹（里面有 spanim / sounds 等子目录），直接导进游戏");
            if (ImGui.Button("导入选中作品"))
            {
                if (_modSel >= 0 && _modSel < _mods.Count) ImportModDir(_mods[_modSel].Dir, _mods[_modSel].Name);
                else Say("先在列表里点一行选中作品");
            }
            Tip("把选中作品的文件写回游戏；右边「作品」预览就是它的样子");
            ImGui.SameLine();
            ImGui.TextDisabled(_modMsg);

            ImGui.BeginChild("##modscroll", new System.Numerics.Vector2(0, 0), true);
            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                ImGui.PushID(i);
                if (ImGui.Selectable("  " + m.Name, _modSel == i))
                {
                    _modSel = i; _modSelDir = m.Dir;
                    RefreshModPreview();
                    SelectModPlant(m.Plant);   // 左边「当前」自动切到同一只植物
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip((string.IsNullOrEmpty(m.Plant) ? "?" : PlantCn(m.Plant) + " (" + m.Plant + ")")
                        + "\n" + m.Files + " 个文件\n" + m.Time + "\n双击 = 导入");
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { _modSel = i; ImportModDir(m.Dir, m.Name); }
                }
                ImGui.SameLine(290);
                ImGui.TextDisabled(m.Files + " 文件");
                ImGui.PopID();
            }
            if (_mods.Count == 0) ImGui.TextDisabled("（空）先在上面点「导出为作品」");
            ImGui.EndChild();
            ImGui.End();
        }
    }
}
