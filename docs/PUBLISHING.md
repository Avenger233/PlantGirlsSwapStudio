# 发布到 GitHub

## 1. 建仓库并推上去

```powershell
cd PlantGirlsSwapStudio
git remote add origin https://github.com/<你的账号>/PlantGirlsSwapStudio.git
git branch -M main
git push -u origin main
```

用 GitHub CLI 更省事：

```powershell
gh repo create PlantGirlsSwapStudio --public --source=. --remote=origin --push
```

## 2. 仓库设置建议

- **Description**：带实时预览的《植物娘大战僵尸 HD》资源替换 / 互换 / 观赏 / Mod 工具
- **Topics**：`plants-vs-zombies` `modding` `spine` `monogame` `imgui` `game-tools` `csharp`
- **License**：选 MIT（仓库里已有 LICENSE）
- 关掉 Wiki / Projects（如果不用），Issues 保持开启

## 3. 出 Release

先在本机构建单文件版：

```powershell
$env:SPINE_MONOGAME_DIR = "<spine-monogame 编译输出目录>"
dotnet publish src/PlantSwapStudio/PlantSwapStudio.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

然后：

```powershell
gh release create v0.4.0 publish/资源修改器.exe `
  --title "v0.4.0" `
  --notes-file <(git log -1 --pretty=%B)
```

或者网页上 New release → 上传 `资源修改器.exe` → 写上 CHANGELOG 里对应的段落。

## 4. 发布前自查

- [ ] `git status` 干净，没有 `Content/`、`bin/`、`obj/`、备份目录
- [ ] 没有个人的绝对路径（`git grep -i "D:"` 看一眼）
- [ ] `dotnet build PlantGirlsSwapStudio.sln -c Release` 通过
- [ ] CLI 能跑：`dotnet run --project src/PlantSwap.Core -- --help`
- [ ] README 里的链接都点得开
- [ ] 有截图（可选，注意素材版权，见 screenshots/README.md）
- [ ] 如果你不想公开自己的提交邮箱，仓库设置里勾上 "Keep my email addresses private"

## 5. 关于 CI

仓库自带 `.github/workflows/build.yml`，但它**只编译 Core 和 ReanimDump**——
因为 PlantSwapStudio 依赖 Spine 运行时，而 Spine 的许可证不允许把 DLL 提交到公开仓库。

想让 CI 也构建 GUI，两种做法：

1. 在 workflow 里 `git clone --branch 4.2` spine-runtimes 并编译（构建产物只留在 runner 上，不进仓库）
2. 用 self-hosted runner，本机已有编译好的运行时

第 1 种可以这样加：

```yaml
      - name: Build Spine runtime
        run: |
          git clone --depth 1 --branch 4.2 https://github.com/EsotericSoftware/spine-runtimes.git third_party/spine-runtimes
          dotnet build third_party/spine-runtimes/spine-monogame/spine-monogame/spine-monogame.csproj -c Release
      - name: Build GUI
        run: dotnet build src/PlantSwapStudio/PlantSwapStudio.csproj -c Release
```

## 6. 提醒

- 仓库里**不要**放游戏资源、反编译产物、或者别人作品的导出件
- 玩家发的「作品」应当走 Release 附件或单独分发，不要进代码仓库
