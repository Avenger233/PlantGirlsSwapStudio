# 第三方组件声明

本项目自身代码以 MIT 许可证发布（见 LICENSE）。
下列第三方组件各自遵循其原有许可证，本仓库仅为方便构建而包含或引用它们。

| 组件 | 许可证 | 在本仓库中的形式 |
| --- | --- | --- |
| MonoGame.Framework.DesktopGL | Microsoft Public License (Ms-PL) | NuGet 依赖 |
| ImGui.NET | MIT | NuGet 依赖 |
| MonoGame.ImGuiNet | MIT | NuGet 依赖（裸 DLL，需按 HintPath 引用） |
| Dear ImGui | MIT | 由 ImGui.NET 间接引入 |
| Spine Runtimes | Spine Runtimes License Agreement | **不包含在本仓库中**，需自行编译，见 docs/BUILD.md |
| SDL2 | zlib | src/PlantSwapStudio/native/SDL2.dll |
| OpenAL Soft | LGPL-2.1-or-later | src/PlantSwapStudio/native/soft_oal.dll（可自行替换为系统安装版本） |

## 关于游戏资源

本仓库**不包含**任何游戏原始资源、游戏程序集或其反编译结果。
运行本工具需要你自行拥有《植物娘大战僵尸 HD》游戏本体。

src/PlantSwap.Core/ReanimAtlasData.tsv 是一份**图集坐标表**（图片 id → 贴图矩形），
用于在预览 reanim 动画时把帧数据里的图片 id 定位到图集上的正确区域。
它不含任何像素数据，仅为互操作目的从游戏的图集资源描述中整理而来。
如果你是该资源的权利人且不希望它以这种形式存在，请开 issue，会改为运行期解析。

本项目与 PopCap Games / EA 以及该 Mod 的作者均无隶属关系。
