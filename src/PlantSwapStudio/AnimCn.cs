using System.Collections.Generic;

namespace PlantSwap.App
{
    /// <summary>Spine 动作名的中文说明：显示成「待机 (idle)」，认不出的原样显示。</summary>
    public static class AnimCn
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // 常用
            ["idle"] = "待机",
            ["idle1"] = "待机1",
            ["idle2"] = "待机2",
            ["idle3"] = "待机3",
            ["idle-2"] = "待机-2",
            ["idle-3"] = "待机-3",
            ["idle1_idle2"] = "待机1→2",
            ["idle2_idle3"] = "待机2→3",
            ["idle3_xiaoshi"] = "待机3消失",
            ["idle_test"] = "测试待机",
            ["idle_garden"] = "花园待机",
            ["idle_aquarium"] = "水族箱待机",
            ["garden1"] = "花园待机1",
            ["garden2"] = "花园待机2",
            ["garden_idle"] = "花园待机",
            ["garden2_1"] = "花园2-1",
            ["garden2_2"] = "花园2-2",
            ["zengarden"] = "禅境花园",
            // 战斗
            ["shooting"] = "射击",
            ["shooting2"] = "射击2",
            ["shooting_pre"] = "射击前",
            ["shooting_after"] = "射击后",
            ["shooting2_pre"] = "射击2前",
            ["shooting2_after"] = "射击2后",
            ["splitpea_shooting"] = "分裂豆射击",
            ["splitpea_idle"] = "分裂豆待机",
            ["attack"] = "攻击",
            ["attack2"] = "攻击2",
            ["attack3"] = "攻击3",
            ["attack5"] = "攻击5",
            ["attack-2"] = "攻击-2",
            ["attack-3"] = "攻击-3",
            ["attack2-2"] = "攻击2-2",
            ["attack2-3"] = "攻击2-3",
            ["attack1_all"] = "全身攻击1",
            ["attack2_all"] = "全身攻击2",
            ["atk1"] = "攻击1",
            ["atk2"] = "攻击2",
            ["atk17"] = "攻击17",
            ["atk2_1"] = "攻击2-1",
            ["atk2_2"] = "攻击2-2",
            ["bite"] = "咬",
            ["bite3"] = "咬3",
            ["bite_pose"] = "咬·定帧",
            ["chew"] = "咀嚼",
            ["swallow"] = "吞咽",
            ["block"] = "格挡",
            ["blow"] = "吹风",
            ["blow2"] = "吹风2",
            ["special"] = "特殊",
            ["explode"] = "爆炸",
            ["explode_particle"] = "爆炸粒子",
            ["burning"] = "燃烧",
            ["die"] = "死亡",
            // 状态/过渡
            ["enter"] = "出场",
            ["debut"] = "登场",
            ["head_enter"] = "头部出场",
            ["head_idle"] = "头部待机",
            ["head_idle1"] = "头部待机1",
            ["head_garden_idle"] = "头部花园待机",
            ["stem"] = "茎干",
            ["sleep"] = "睡眠",
            ["sleep_to_idle"] = "睡醒",
            ["idle_to_sleep"] = "入睡",
            ["idle_to_prepare"] = "待机→准备",
            ["prepare"] = "准备",
            ["prepare_to_idle"] = "准备→待机",
            ["ready"] = "就绪",
            ["ready2idle"] = "就绪→待机",
            ["idle2ready"] = "待机→就绪",
            ["henshin"] = "变身",
            ["grow"] = "生长",
            ["producing"] = "生产",
            ["producing2"] = "生产2",
            ["producing3"] = "生产3",
            ["repair"] = "修理",
            ["repair2"] = "修理2",
            ["touch"] = "抚摸",
            ["touch2"] = "抚摸2",
            ["touch3"] = "抚摸3",
            ["scared"] = "害怕",
            ["scaredidle"] = "害怕待机",
            ["normal"] = "常态",
            ["caidan"] = "彩蛋",
            ["dun1"] = "蹲1",
            ["dun2"] = "蹲2",
            ["dun3"] = "蹲3",
            ["jumpup"] = "跳起",
            ["jumpup_left"] = "左跳起",
            ["jumpdown"] = "跳下",
            ["jumpdown_left"] = "左跳下",
            ["jumpdown_water"] = "跳入水",
            ["jumpdown_left_water"] = "左跳入水",
            ["land"] = "落地",
            ["land_treasure"] = "落地·宝藏",
            ["lookleft"] = "看左",
            ["lookright"] = "看右",
            ["1"] = "动作1",
        };

        /// <summary>内置的全部动作名（用来预填 actions.txt）。</summary>
        public static IReadOnlyDictionary<string, string> All => Map;

        /// <summary>内置表里有没有这个动作名（用来挑出还需要用户翻译的）。</summary>
        public static bool Known(string name) => !string.IsNullOrEmpty(name) && Map.ContainsKey(name);

        public static string Label(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var custom = PvZPlantSwap.Core.Lang.Animation(name);
            if (custom.Length > 0) return custom + " (" + name + ")";
            return Map.TryGetValue(name, out var cn) ? cn + " (" + name + ")" : name;
        }
    }
}
