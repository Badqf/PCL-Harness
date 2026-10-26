# PCL Harness 改动清单（基于 PCL2 v2.13.1.1 上游源码）

> 源码：`F:\PCL2-Source`（上游 tag `2.13.1.1`，MeloongCore 子模块用 2.14.0.0）
> 产物：`F:\PCL2-Source\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe`
> 构建：`F:\PCL2-Source\build.ps1`（MSBuild 4.0 + Roslyn 4.14，见 BUILD-NOTES.md）

## 1. 品牌

| 项 | 位置 | 内容 |
|---|---|---|
| 窗口标题 | `FormMain.xaml:7` | `PCL Harness　自编版` |
| 程序集标题 | `My Project\AssemblyInfo.vb:11` | `AssemblyTitle("PCL Harness 启动器")` |
| 产品名 | `My Project\AssemblyInfo.vb:14` | `AssemblyProduct("PCL Harness")` |
| 版本显示 | `Modules\Base\ModBase.vb:23` | `BuildTypeDisplay = "自编版"` → 版本串显示为 `自编版 2.13.1.1` |
| 关于页尾注 | `Pages\PageOther\PageOtherAbout.xaml.vb:14` | 追加 `（PCL Harness 自编修改版，非官方发布）` |

## 2. 顶部「插件」入口 + 插件框架（核心新增）

| 项 | 位置 | 内容 |
|---|---|---|
| 导航按钮 | `FormMain.xaml:131` | 新增 `BtnTitleSelect5`，`Text="插件"` `Tag="10"`，图标为插头路径 |
| 页面类型 | `FormMain.xaml.vb` | `PageType.Plugin = 10`；`BtnTitleSelect_Click` 改为按 `Tag` 查找按钮；`PageChangeActual` 增加插件分支 |
| 页面宿主 | `FormMain.xaml.vb` | 新增 `FrmPlugin As PagePlugin` 字段 |
| 框架实现 | `Modules\ModPlugin.vb`（新文件，325 行） | `ModPlugin` 模块 + `PluginInfo` + `IPclPlugin` 接口 + `PluginHost` 宿主 API |
| 插件页 | `Pages\PagePlugin.xaml` + `.xaml.vb`（新文件） | 展示插件列表、启用/禁用、重新扫描、打开目录、开发说明弹窗 |
| 启动加载 | `FormMain.xaml.vb:434` 附近 | 启动后台线程调用 `ModPlugin.Refresh(True)`，已启用插件随启动器一起加载 |
| 工程条目 | `Plain Craft Launcher 2.vbproj` | 显式加入 `Modules\ModPlugin.vb`、`Pages\PagePlugin.xaml`（Page）与其 `.xaml.vb` |

插件目录：`<启动器目录>\PCL\Plugins\<插件文件夹>\plugin.json`，可选 `dll`。
启用状态存 `PCL\Plugins\plugins.json`；首次运行自动写 `说明.txt`。
核心 API（`Module\ModPlugin.vb`）：`Refresh(LoadEnabled)`、`LoadOne`、`UnloadAll`、`SetEnabled`、`OpenFolder`、`PathPluginRoot`、`Plugins`、`ScanError`。
插件接口：`Public Interface IPclPlugin : Sub OnLoad(Host As PluginHost) : Sub OnUnload() : End Interface`
宿主 API：`Host.Log / Host.Hint / Host.Message / Host.OpenUrl / Host.OpenFolder / Host.DataFolder`。
示例插件（可运行）：`F:\PCL2-Source\samples\DemoPlugin\`（源码 + 编译命令见 `PLUGIN-DEV.md`）。

## 3. 暗色蓝黑主题

| 项 | 位置 | 内容 |
|---|---|---|
| 暗色开关 | `Modules\ModSecret.vb:43` 附近 | `Public Const IsDarkMode As Boolean = True` |
| 主色相 | `Modules\ModSecret.vb:64` | `ColorHue = 220`（蓝） |
| 调色板 | `Modules\ModSecret.vb` `ThemeRefresh` | 新增 `If IsDarkMode` 分支：Color1 文字 93% 亮度、Color3 强调 58%、Color6/7/8 卡片与背景 21%/16%/11%；灰阶 Gray1..8 反转为深色阶 |
| 主界面底色 | `Modules\ModSecret.vb` `ThemeRefreshMain` | 深色模式下 `PanTitle` 渐变用 10/14/10% 亮度、`PanForm` 渐变用 12/10/12% 亮度 |
| 资源覆盖 | 同上 `ThemeRefresh` 末尾 | 暗色时重写 `ColorBrushGray1..8`、`ColorBrushHalfWhite = A18 白`、`ColorBrushBackgroundTransparentSidebar` |
| 硬编码白卡片 | `Controls\MyCard.vb:70` | `New SolidColorBrush(Color.FromArgb(245,255,255,255))` → `SetResourceReference(BackgroundProperty, "ColorBrush7")` |
| 弹窗底/正文 | `Controls\MyMsg\MyMsgText.xaml` | `#FBFBFB` → `{DynamicResource ColorBrush7}`；`#FF5C5C5C` → `{DynamicResource ColorBrushGray2}` |
| 滑块底 | `Controls\MySlider.xaml:21` | `Background="White"` → `{DynamicResource ColorBrush6}` |
| 应用级灰阶 | `Application.xaml` | Gray1..8、`HalfWhite`、`BackgroundTransparentSidebar`、`RedBack` 全部改为深色/半透明白；3 处 `Background="White"`（ComboBox 弹层 ×2、ContextMenu ×1）改为 `ColorBrush6/7` |

对比度约束：正文 `ColorBrush1`（≈#E6EDF3 系）落在 `ColorBrush7/8`（≈#1B2333/#131A26）上，对比度 > 10:1；状态文本用 `ColorBrushGray2`；错误用 `#FF7878`，成功用 `#7ED68F`。

## 4. 更新检查（自建 JSON 源）

| 项 | 位置 | 内容 |
|---|---|---|
| 源地址 | `Modules\ModSecret.vb` | `UpdateJsonUrlDefault = ""`，运行时读环境变量 `PCL_UPDATE_JSON_URL`（http/https 或本地文件路径） |
| 检查逻辑 | `Modules\ModSecret.vb` `UpdateCheckByButton` | 未配置 → `Hint("未配置自编版更新源…")` 且**不发任何网络请求**；配置后后台线程取 JSON → `CompareVersion(latest, VersionBaseName)` → 有新版弹「发现新版本 x（当前 y）」+「打开下载页」 |
| 示例 | `F:\PCL2-Release\update.example.json` | `{ "latest": "2.99.0.0", "download": "https://example.com/pcl-harness" }` |

## 5. 验证记录

- 构建：`& F:\PCL2-Source\build.ps1` → exit 0，产物 5,682,176 B，FileVersion 2.13.1.1，FileDescription `PCL Harness 启动器`。
- 二进制取证：UTF-16LE 字节级搜索命中 `PCL Harness`、`自编版`、`PCL_UPDATE_JSON_URL`；临时自测钩子串 `PCL_TEST_PAGE` **确认不存在**。
- 运行时：标题 `PCL Harness　自编版`，暗色主题各页可读（截图见 `F:\PCL2-Release\screenshots\`）。
- 插件：启动日志 `[插件 示例插件] 示例插件已加载（第 1 次）`，插件页显示「已启用并加载」，左下角提示出现。

## 6. 回滚

```powershell
cd F:\PCL2-Source
git status            # 若保留 git 仓库
# 或按上表逐条反向替换；最关键的三处：
#   FormMain.xaml:7 标题、FormMain.xaml:131 导航项、ModSecret.vb IsDarkMode=False
```

许可证：`F:\PCL2-Source\LICENCE`《PCL 分发有限许可》——基于本仓库修改属「重度使用」，对外分发需满足：标明第三方二次创作、名称以 PCL 开头并加后缀、关于页首位署名龙腾猫跃、公开源码、保留 LICENSE、保留正版购买劝导、界面主色相为蓝色。当前暗色主题主色相仍为蓝（220），符合该条。