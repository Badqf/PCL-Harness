# PCL Harness 第二轮改动（版本 3.0.0）

> 在第一轮（改名 / 插件框架 / 暗色蓝黑主题 / 自建更新源）基础上的第二轮打磨。
> 目标：解决「悬浮效果不明显」「弹窗阴影发白」「logo 缺少 Harness 后缀」「文件名与版本号」四个问题。

## 1. 版本与命名

| 项 | 文件:行 | 改动 |
|---|---|---|
| 版本号 | `Plain Craft Launcher 2\Modules\Base\ModBase.vb:9` | `VersionBaseName = "2.13.1.1"` → `"3.0.0"` |
| 程序集版本 | `Plain Craft Launcher 2\My Project\AssemblyInfo.vb` | `AssemblyVersion/AssemblyFileVersion` → `3.0.0.0` |
| 窗口标题 | `Plain Craft Launcher 2\FormMain.xaml:7` | `Title="PCL Harness　自编版"` |
| 标题栏 logo 后缀 | `FormMain.xaml:120` + `FormMain.xaml.vb:781-793` | 新增 `LabTitleSuffix` TextBlock（文本 `Harness`），在 `UiLogoType` 为 1/2 时显示，文本模式下按实际宽度动态贴靠 |
| 文件名 | `F:\PCL2-Source\build.ps1` 末尾 | 构建后复制为 `PCL Harness.exe` |

> **为什么程序集内部名仍是 `Plain Craft Launcher 2`**：源码里大量运行时 XAML 与资源 URI 写成
> `pack://application:,,,/Plain Craft Launcher 2;component/...`、`xmlns:local="clr-namespace:PCL;assembly=Plain Craft Launcher 2"`（`ModBase.vb:46`、`PageLaunchRight.xaml.vb:238`、`PageOtherAbout.xaml` 等 15+ 处）。
> 改 `AssemblyName` 会让这些字符串解析失败，因此只改**输出文件名**，程序集标识保持原样。

## 2. 暗色主题可读性

| 项 | 文件 | 改动 |
|---|---|---|
| 表面色阶 | `Modules\ModSecret.vb:83-85` | 深色分支 `Color6` 亮度 21%→**26%**、`Color7` 16%→**20%**、`Color8` 11%→**12%** |
| 常态填充 | `Modules\ModSecret.vb:127` | `ColorBrushHalfWhite` alpha 18→**13**（14 DIP 的小控件上原值让填充太实） |
| 新增悬浮色 | `Application.xaml:29` + `ModSecret.vb:128-129` | 新增资源 `ColorBrushHover`：深色 = HSL(220, sat×0.4, **34%**)，浅色 = 原 `Color7` |
| 按钮悬浮 | `Controls\MyButton.xaml.vb:160` | `ColorBrush7` → `ColorBrushHover`（原先悬浮只比常态亮 3%，肉眼几乎无变化） |
| 输入框/下拉框悬浮与聚焦 | `Controls\MyTextBox.vb:248,252`、`Controls\MyComboBox.vb:72,76` | `ColorBrush7` → `ColorBrushHover` |
| 顶栏导航悬浮 | `Controls\MyRadioButton.xaml.vb:191` | 悬浮底色 `New MyColor(50, Color8)`（深色下几乎不可见）→ `New MyColor(70, 255, 255, 255)` |

## 3. 弹窗阴影

| 项 | 文件 | 改动 |
|---|---|---|
| 阴影色 | `Modules\ModSecret.vb:132-133` | 深色主题下 `ColorObject1` 改为近黑 `RGB(6,9,14)`；浅色主题不变（`Color1`） |
| 阴影强度 | `Controls\MyMsg\MyMsgText.xaml`、`MyMsgInput.xaml`、`MyMsgSelect.xaml`、`Pages\PageLaunch\MyMsgLogin.xaml` | `BlurRadius 20→14`、`ShadowDepth 4→3`、`Opacity 0.8→0.38` |

原来的发白原因：阴影用的是 `ColorObject1`（= 主文字色，深色主题下是 93% 亮度的浅色），浅色阴影叠在深底上就是一圈白雾。

## 4. 更新检查（确认无官方源）

`Modules\ModSecret.vb` 的 `UpdateCheckByButton` 只读 `PCL_UPDATE_JSON_URL`：

- 未设置 → 只弹提示，不发任何请求；
- 设置为 http(s) → `HttpClient` 取 JSON（10s 超时）；
- 设置为本地路径 → 直接读文件；
- 版本比较用 `CompareVersion(latest, VersionBaseName)`（`ModMinecraft.vb:2431`）。

上游开源版本来就没有官方更新接口（`ModSecret.vb` 里是 `Hint("该版本中不包含更新功能……")` 的占位），因此这里不需要「删除官方源」，只需保证默认不联网——已满足。

> 注意：版本号升到 3.0.0 后，若更新 JSON 里仍是 `2.99.0.0`，不会提示更新（3.0.0 更新）。测试时把 `latest` 写到 `9.9.9.9` 之类。

## 5. 验证记录（本轮）

- 构建：`& F:\PCL2-Source\build.ps1` → exit 0；产物 `bin\PCL Harness.exe` 5,682,688 B，`FileVersion 3.0.0.0`，`FileDescription PCL Harness 启动器`，`ProductName PCL Harness`。
- 运行时标题：`PCL Harness　自编版`；关于页显示「当前版本：自编版 3.0.0（408.50）（PCL Harness 自编修改版，非官方发布）」。
- 悬浮：`v3d-hover-nav.png`（顶栏「下载」明显浮起）、`v3d-hover-input.png`（输入框变亮）、`v3d-hover-btn.png`（按钮 `下载游戏` 与 `版本选择` 对比清晰）。
- 弹窗：`v3e-dialog.png` 阴影收敛为柔和暗影，不再发白。
- 截图均用 DPI 感知方式采集：`SetProcessDPIAware()` + `CopyFromScreen` + `GetWindowRect`。

## 6. 构建脚本

```powershell
cd F:\PCL2-Source
& .\build.ps1                 # 默认 Release
# 产物：
#   Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe   （MSBuild 原始输出）
#   Plain Craft Launcher 2\bin\PCL Harness.exe              （发布用文件名）
```

前置（均已就位）：`C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe`、
`C:\Users\XYM_3\dotnet-fwreal\.NETFramework\v4.8`（参照程序集）、
`C:\Users\XYM_3\roslyn\tasks\vbc.exe`（Roslyn 4.14.0）。

重建前必须先 `Stop-Process -Name "PCL Harness","Plain Craft Launcher 2"`，否则 `MSB3027/MSB3021` 文件占用。

## 7. 插件扩展点（API v2，本轮新增）

| 项 | 文件 | 改动 |
|---|---|---|
| 扩展点接口 | `Modules\ModPlugin.vb`（`End Module` 之后） | 新增 `Public Interface IPclPluginUi`：`GetPages` / `GetNavItems` / `GetSettings` |
| 扩展点类型 | `Modules\ModPlugin.vb`（`PluginInfo` 之后） | 新增 `PluginPageInfo`（Id/Title/Icon/Factory）、`PluginNavItem`（Text/Icon/Page/Action）、`PluginSetting` + `PluginSettingType`（Boolean/Text） |
| 登记表 | `Modules\ModPlugin.vb:20-32` | `PluginPages` / `PluginNavItems` / `PluginSettings` 三个模块级列表；`PathSettingsFile = PCL\Plugins\plugins.settings.json` |
| 收集 | `Modules\ModPlugin.vb` → `CollectUi(Info)`，在 `LoadOne` 里 `Info.Loaded = True` 之后调用 | 逐个 try/catch 读取三类扩展点并校验（页面必须有 Id+Factory；入口必须有 Text 且 Page/Action 二选一） |
| 设置读写 | `Modules\ModPlugin.vb` → `GetSetting(Info, Key, Default)` / `SetSetting(Info, Key, Value)` | JSON 持久化到 `plugins.settings.json`，按插件 Id 分组 |
| 清空 | `UnloadAll()` 开头 | 清空三个列表（重扫时先清后登记） |
| 页面容器 | `Pages\PagePluginHost.vb`（新增，`.vbproj:343` 注册 `Compile`） | `Inherits MyPageRight`；把插件 `UserControl` 套进 `MyScrollViewer` 再作为 `Child`，复用页面切换动画 |
| 页面路由 | `FormMain.xaml.vb` `PageChangeActual` 的 `Select Case` | 新增 `Case CType(100, PageType) To CType(199, PageType)`：按 `tag-100` 取 `PluginNavItems(i).Page`，经 `CreatePluginPage` 包成 `PagePluginHost` |
| 顶栏注入 | `FormMain.xaml.vb` | 新增 `RefreshPluginNav()`（清掉 Tag≥100 的旧按钮后按注册顺序重建 `MyRadioButton`，Tag=100+i）与 `RefreshPluginNavCheck()`；在 `ModPlugin.Refresh(True)` 之后 `RunInUi` 调用 |
| 点击分发 | `FormMain.xaml.vb` `BtnTitleSelect_Click` | Tag≥100 时走插件入口：有 Page 就 `PageChangeActual`，只有 Action 就执行并复位勾选 |
| 插件页展示 | `Pages\PagePlugin.xaml(.vb)` | 新增 `LabExtend`/`PanExtend` 与 `BuildExtensions`、`BuildPageCard`、`BuildSettingCard`（布尔用 `MyCheckBox`，文本用 `MyTextBox`+保存按钮）、`OpenPluginPage` |
| 说明文本 | `Pages\PagePlugin.xaml.vb` `BtnApi_Click` | 「插件开发说明」弹窗补充 v2 三个接口与设置文件位置 |
| 示例插件 | `samples\DemoPlugin\DemoPlugin.vb`（+ `build.ps1`） | 演示注册 1 个页面、1 个顶栏入口、2 个设置项（布尔 + 文本） |

为什么页面要包一层 `PagePluginHost`：`PageChangeAnim` 里对 `PanMainRight.Child` 做 `CType(..., MyPageRight)` 并调用 `PageOnEnter/PageOnExit/PageOnForceExit`，插件直接返回 `UserControl` 会抛 `InvalidCastException`（已实测复现）。

## 8. 本轮验证记录（扩展点）

- 构建：`& F:\PCL2-Source\build.ps1` → exit 0；示例插件 `samples\DemoPlugin\build.ps1` → 10,240 B。
- 顶栏注入：`v4-nav.png` 显示「插件」右侧多出带图标的「示例」；点击后 `v4-plugin-page2.png` 显示插件页面（标题「示例页面」、问候语、`点我试试` 按钮）。
- 插件页扩展区：`v4-plugin-list.png` 显示「插件扩展点：页面 1 个 · 标题栏入口 1 个 · 设置项 2 个」，含页面卡片、「打开页面」按钮、布尔勾选框与文本设置框。
- 设置持久化：点击勾选框后 `PCL\Plugins\plugins.settings.json` 出现 `{"demo-harness":{"showHintOnLoad":false}}`；重启后 `v4-setting-off.png` 勾选框回到未勾选状态，且加载时不再弹提示。
- 文本设置保存：点「保存」后文件写入 `"greeting": "你好，这里是插件注册的页面。"`（UTF-8 无 BOM）。
- 从扩展区打开页面：`v4-open-from-list.png` 正常切到「示例页面」。
---

## 9. 标题栏 Harness 徽标与窗口按钮配色

- 徽标：`FormMain.xaml:120` 起，用 `BorTitleSuffixDiv`（1px 渐变竖分隔线）+ `BorTitleSuffix`（圆角 4、1px 渐变描边、半透明渐变填充的“胶囊”）包住 `LabTitleSuffix`；文字用白→浅蓝→蓝的 `LinearGradientBrush` 填充，加 `DropShadowEffect`（Color `#3D86FF`、BlurRadius 10、ShadowDepth 0、Opacity 0.8）做外发光。
- 位置：默认 logo 下距左 68；「文本」logo 时按 `18 + 文本宽 + 11` 动态计算，分隔线同步左移 8（`FormMain.xaml.vb` 的 `RefreshTitleBar` 尾部）。
- 窗口按钮配色：`MyIconButton` 新增 `Themes.TitleBar` / `Themes.TitleBarClose`。原因：暗色主题里 `ColorBrush8` 被重映射为近黑背景色，原来 `Theme="White"` 会用 `ColorBrush8` 填充图标 → 最小化/关闭按钮几乎看不见。新主题固定图标为浅灰白 `rgb(232,236,244)`，悬浮时分别提亮为纯白（带 34/255 白圆）与转红 `rgb(255,92,92)`（带 56/255 红圆）。
- 验证截图：`screenshots/21-标题栏Harness徽标.png`、`22-窗口按钮悬浮-关闭转红.png`。
---

## 10. 标题栏徽标增补与插件页改版

**徽标三项增强（`FormMain.xaml`）**
- PCL 图形：`Stroke` 由纯白改为 `#FFFFFF → #D6ECFF → #7FC1FF` 对角渐变，叠加 `DropShadowEffect`（`#3D86FF`、BlurRadius 8、Opacity 0.55）淡蓝辉光。
- 版本角标：`Harness` 右侧新增 `LabTitleSuffixVer`（`Text="3.0"`、FontSize 9、`#FFC078`），上下对齐到基线（Margin 5,0,0,2）。
- 流动高光：`BorTitleSuffix` 内加 `RectSuffixShine`（20px 宽、`#3DFFFFFF` 白带），用 `Border.Triggers` 里的 `EventTrigger Loaded` 带动 `DoubleAnimation`（`(UIElement.RenderTransform).(TranslateTransform.X)`，-24→170，Duration 3.6s，RepeatBehavior Forever）。
  - 踩坑：把 Storyboard 写在 `Border.Resources` 里再用 `StaticResource` 引用时动画不生效（帧间像素完全一致），改为 `BeginStoryboard` 内联后生效（用逐帧采样 pill 亮度验证：亮像素数 325 → 357 → 325 循环）。
- 自适应：`FormMain.xaml.vb` 新增 `Public Sub RefreshTitleSuffixFit()`，按窗口宽（DIP）收缩徽标——`< 990` 隐藏版本角标、`< 860` 整枚隐藏，避免窄窗口下被 `1*` 星列裁掉。触发点：`FormMain_SizeChanged`、`PanTitle.SizeChanged`（Loaded 时挂 `AddHandler`）、`RefreshTitleBar` 末尾，另在 `Loaded` 里 `RunInUi` 补一次。
  - 注意：阈值必须用 DIP（125% 缩放下 1400px = 1120 DIP），写成像素阈值会在常规窗口下误判为窄。

**插件页（`Pages\PagePlugin.xaml` + `.xaml.vb`）改为设置页风格**
- 结构：`MyPageRight → MyScrollViewer(PanBack) → StackPanel(Margin 25,25,25,10)`，三张 `MyCard`（插件目录 / 已发现的插件 / 插件扩展点），卡片间距 `0,0,0,15`、卡内 `25,40,25,20`，与 `PageSetupLaunch.xaml` 一致。
- 卡片一：`路径 / 已发现` 两行「名称-值」+ 三个按钮（打开插件目录 / 重新扫描 / 插件开发说明）。
- 卡片二：每个插件一行，左列名称（`示例插件　v1.1.0`）+ 作者/状态/描述（12.5/11.5 灰阶），右列 `MyCheckBox`（已启用/已禁用）+ 目录 `MyIconButton`；不再用 `MyListItem`。
- 卡片三：插件扩展点按「名称 + 说明 / 右侧控件」两列排布（页面 → 说明 + 打开页面按钮；Boolean → 复选框；Text → 输入框 + 保存），行上下各留 8px。
- `PanList` 改为放一个代码构建的 `Grid`（Auto + 1* 两列），全部行由 `AddPluginRow` / `AddRow` 生成。

**验证**：发布包 1400x860 与 1250x820 均正常；截图 `screenshots/17-插件页-扩展点与设置项.png`、`21-标题栏Harness徽标.png` 已更新。
---

## 11. 第三轮微调：去掉版本角标、启动提示「不再显示」、插件页左侧栏

- **去掉 3.0 角标**：删除 `FormMain.xaml` 的 `LabTitleSuffixVer`，`RefreshTitleSuffixFit()` 只保留整枚徽标的显隐（`< 860 DIP` 隐藏），不再按宽度切换角标。
- **启动提示加「不再显示」**：
  - 新设置项 `HarnessOpenSourceTip`（Boolean，默认 True）登记在 `Pages\PageSetup\Settings.vb:25`，保存在 `PCL\Setup.ini`。
  - `FormMain.xaml.vb:441` 改为 `If VersionBranchMain = "OpenSource" AndAlso Settings.Get(Of Boolean)("HarnessOpenSourceTip")`，弹窗按钮为「我知道了 / 不再显示」，返回 2 时写入 False 并 `Hint` 提示。
  - `Pages\PageSetup\PageSetupLaunch.xaml` 的「启动选项」卡片末尾新增一行「开源版本说明」+ `MyCheckBox local:SettingService.Key="HarnessOpenSourceTip"`，随时可重新打开（走 PCL 原生 `SettingService` 绑定，无需手写存取）。
- **插件页左侧栏**（`Pages\PagePlugin.xaml` 重构）：
  - `MyPageRight.Child` 改为 `Grid`：左列 192 宽的 `PanNav`（三个 `MyListItem Type="RadioBox"`：插件管理 / 扩展点 / 开发说明），右列 `MyScrollViewer x:Name="PanBack"` 内放三个互斥 `StackPanel` 分区；`PanScroll="PanBack"` 仍指向滚动区。
  - 卡片布局不变（插件目录 / 已发现的插件 / 插件扩展点 / 插件开发说明），只是按分区拆开。
  - 「插件开发说明」从 `MyMsgBox` 改为内嵌文档分区（`BuildDoc()` 生成标题、正文、`Consolas` 代码块，深色底 `ColorBrushGray8`），底部保留「打开插件目录」按钮。
  - **踩坑 1**：`SwitchSection` 里调用 `item.SetChecked(...)` 会再次触发 `Check` 事件，形成无限递归 → 进程直接退出（无异常弹窗）。加 `IsSwitching` 重入保护后解决。
  - **踩坑 2**：`MyListItem` 的点击判定依赖 `IsMouseDirectlyOver`，点在文字/图标上（子元素命中）时不会触发切换，只有点空白处才行。在 `PanNav.PreviewMouseLeftButtonUp` 里向上查找 `MyListItem` 并转发 `SetChecked(True, True, True)` 修复。
---

## 12. 插件页改用原生左侧栏 + 扩展点改展开卡片

**为什么改**：第一版把左侧栏做在页面内部（`MyPageRight.Child` 里的 Grid），靠 `Visibility` 切换分区。结果：`MyCard` 在 `Collapsed` 状态下被加载时，`Init()` 里的虚拟化与高度动画按 0 尺寸算过一次，重新显示后卡片会变灰、内容消失。改成重建式切换后问题消失。

**左侧栏**
- 新增 `Pages\PagePluginLeft.xaml(.vb)`：`Inherits` 原生 `MyPageLeft`，`AnimatedControl="PanItem"`，三项 `MyListItem`（`Type="RadioBox"`、`Height="36"`、`MinPaddingRight="35"`），与 `PageSetupLeft` 完全同构，挂在主窗口 `PanMainLeft` 上。
- `ModMain.vb:400` 新增 `Public FrmPluginLeft As PagePluginLeft`；`FormMain.xaml.vb:1600` 的 `Case PageType.Plugin` 由 `PageChangeAnim(New MyPageLeft, FrmPlugin)` 改为 `PageChangeAnim(FrmPluginLeft, FrmPlugin)`。
- `PagePluginLeft.PageCheck` 直接调 `FrmPlugin.SwitchSection(Tag)`。

**右侧页面**
- `PagePlugin.xaml` 只剩一个宿主：`MyPageRight(PanScroll="PanBack")` → `MyScrollViewer(PanBack)` → `StackPanel(PanContent, Margin 25,25,25,10)`。
- `SwitchSection(Index)`：`PanContent.Children.Clear()` 后按分区重建（管理 / 扩展点 / 开发说明），带 `IsRebuilding` 重入锁，`ScrollToVerticalOffset(0)`。
- 卡片内容必须 `Card.Children.Add(Body)` 且 `Body.Margin = (25,40,25,20)`；用 `BorderChild` 会把内容塞进背景 Border（无内边距、与标题重叠）。

**扩展点改为每插件一张可展开卡片**
- `NewCard($"{DisplayName}  v{Version}")` + `CanSwap = True`、`SwapControl = Body`、`IsSwapped = False`（False 才是展开，MyCard 语义如此），点标题或右上箭头折叠。
- 卡内：一行统计（页面 N · 标题栏入口 M · 设置项 K）+ 每个页面/入口/设置项一行，右侧控件为「打开页面」按钮 / 复选框 / 输入框+保存。

**顺带修掉的重复注册 bug**
- `ModPlugin.SetEnabled` 重新启用插件时，`LoadOne → CollectUi` 会把页面、入口、设置项再追加一遍，标题栏出现两个「示例」。在 `CollectUi` 开头调新增的 `RemoveUi(Info)`（`PluginPages/PluginNavItems/PluginSettings` 各自 `RemoveAll`）修正。

**验证**：三区切换正常、扩展点卡片折叠/展开正常、启用→禁用→启用无重复入口、插件自定义页面仍可从标题栏打开。截图 `screenshots/28-插件页-左侧栏原生.png`、`29-扩展点-每插件展开卡片.png`、`30-扩展点-折叠态.png`、`31-插件开发说明.png`。
---

## 13. 插件状态提示与窗口按钮配色

**插件启停提示**（`Pages\PagePlugin.xaml.vb` 的复选框 `Change` 事件）
- 启用：`Hint("示例插件 已启用，重启后生效！", HintType.Green)`
- 禁用：`Hint("示例插件 已禁用，重启后生效！", HintType.Blue)`
- 提示走左下角 `Hint`，和 PCL 原生提示同一套样式与时长。

**启动时不再打扰**
- 示例插件 `OnLoad` 里的提示改为默认关闭：`GetBool("showHintOnLoad", False)`，设置项 `DefaultValue = False`，标题改为「加载时弹出提示」并说明这是演示 `Host.Hint` 用的。
- 启动时只有插件出问题才提示：`ModPlugin.WarnFailed()` 统计 `Enabled AndAlso Error <> ""` 的插件，红色提示「有 N 个插件加载失败：xxx、yyy，详见插件页。」；正常加载一律静默。
- 时序问题：`WarnFailed` 原本在后台 `Refresh` 里调用，会被启动时的「开源版本说明」模态弹窗挡住（提示已过期）。改为在 `FormMain` 的初始化线程里，等 `ModPlugin.LoadDone` 后（最多 5 秒，`Threading.Thread.Sleep(100)` 轮询）再 `RunInUi(Sub() ModPlugin.WarnFailed())`。同时在该线程末尾补一次 `RunInUi(Sub() RefreshPluginNav())`，否则早期刷新出的插件入口会被后续布局重建吞掉（表现为启动后没有「示例」按钮）。
- 插件页的「重新扫描」与页面首次加载也会调用 `WarnFailed()`，所以从插件页触发扫描后同样能看到红色提示。

**关闭按钮悬浮配色**（`Controls\MyIconButton.xaml.vb`）
- `Themes.TitleBarClose` 由红色改为主题蓝：`ColorTitleBarCloseHover = New MyColor(255, 150, 200, 255)`，悬浮底色 `New MyColor(56, 120, 190, 255)`（原来是 `(56, 255, 76, 76)` 的红色圆底）。最小化按钮仍是纯白 + 白圈，两个按钮靠颜色区分且都与蓝色主题一致。

**验证**：`screenshots/32-插件已禁用提示.png`、`33-插件已启用提示.png`、`34-关闭按钮-蓝色悬浮.png`、`35-插件加载失败警告.png`（后者用临时损坏插件 `broken-test` 故意制造 `BadImageFormatException` 触发，验证后已删除该测试插件）。
---

## 14. 版本选择返回按钮配色 + 恢复联机入口

**返回按钮与内页标题（`FormMain.xaml`）**
- `BtnTitleInner` 原为 `Theme="White"`，白色主题用 `ColorBrush8` 填充图标，而暗色模式里 `ColorBrush8 = HSL(220, sat*0.4, 12)`（近黑）→ 版本选择等内页左上角返回箭头几乎不可见。改为 `Theme="TitleBar"`（浅灰白图标 + 悬浮白圈）。
- 内页标题 `LabTitleInner` 的 `Foreground` 原为 `ColorBrush8`，同样近黑。新增资源 `ColorBrushTitleBarIcon`（`#e8ecf4`，`Application.xaml` 声明 + `ModSecret.vb` 启动时以 `MyColor(232,236,244)` 覆盖）并改用它。
- 注意暗色下 `Color1` 才是“文字色”（93% 亮度），`Color8` 是“背景色”（12%），两者在浅色主题里正好互换；对话框正文必须用 `ColorBrush1`。

**联机入口恢复**
- 两处硬编码：`Pages\PageSetup\PageSetupUI.xaml.vb:439/452` 把 `FrmMain.BtnTitleSelect2` 与 `FrmSetupLeft.ItemLink` 直接 `Visibility.Collapsed`（把原来的设置项判断注释掉了）；已还原为按 `UiHiddenPageLink` / `UiHiddenSetupLink` 判断（默认 False → 显示）。
- 更关键的一处：`Pages\PageSetup\Settings.vb:316-325` 的 `Settings.Get` 里有 `Static ForcedSettings` 字典把 `UiHiddenPageLink`、`UiHiddenSetupLink` 强行置 True，任何读取都返回 True。已把该字典清空（保留代码路径），联机页与联机设置因此恢复。
- 结果：顶栏出现「联机」，设置左栏出现「联机」，联机页可正常渲染「创建房间 / 加入房间」。

**联机功能实测结论（重要）**
- 创建房间 → 弹「输入端口」→ 确认后加载器 `获取配置` 立刻 Failed：`$开源版无法联网获取配置。`（`PageLinkMain.xaml.vb:429-431`）。
- 根因不是这一处判断，而是 `ModSecret.vb` 里 `ServerLoader` 在开源分支被替换成只打日志的占位任务（`"该版本中不包含更新通知功能……"`），`ServerConfig` 永远为 Nothing；即使删掉 429 行的分支，也会在 433 行报「无法从服务器获取配置」。
- 因此：界面可用、流程可走，但要真正建房还缺两样东西——① 一份 `ServerConfig`（`Link` 节：EasyTierVersion、Downloads、Argument、Peers、MandatoryPeers、PeersBlackList、DiscoverPeer、RandomPeer 等）；② 联机模块二进制（`联机模块.exe` / `联机模块 CLI.exe` / `Packet.dll`）。
- 若后续要做，建议加一个「本地配置」入口：检测 `PCL\ServerConfig.json`，存在就载入并跳过联网，绝不接触官方接口。截图 `36-版本选择页-返回按钮与标题白色.png`、`37-联机页.png`、`38-联机建房间-开源版配置失败.png`。

**顺带修掉的暗色弹窗问题**
- `MyMsgInput.xaml:12`、`MyMsgSelect.xaml:12`、`MyMsgLogin.xaml:12` 的 `Background="#FBFBFB"` 硬编码，暗色下弹窗是刺眼的白块；改为 `{DynamicResource ColorBrush7}`，正文 `Foreground` 由 `#FF5C5C5C` 改为 `{DynamicResource ColorBrush1}`。截图 `39-输入弹窗暗色.png`。
---

## 15. 联机功能恢复（方案 A：本地 ServerConfig + 自装 EasyTier 模块）

**恢复入口**
- `Settings.vb` 的 `ForcedSettings` 里有一条把 `UiHiddenSetupLink` 强制为 `True` 的条目，读任何设置都会先被它覆盖——已清空该字典（保留代码路径）。
- 设置左栏「联机」页随之可见：`PageSetupLeft` 的 `ItemLink` 不再被隐藏，页面为 `PageSetupLink`。

**本地配置（替代联网获取）**
- `PageLinkMain.xaml.vb` 的 `InitConfig`：开源版原本直接 `Throw "$开源版无法联网获取配置。"`，现改为读取 `%AppData%\PCL\ServerConfig.json`（路径即 `Paths.AppDataThenName & "ServerConfig.json"`）。
  - 文件不存在 / 解析失败 / 缺 `Link` 节点时，抛出的错误信息会直接给出应当放置的完整路径。
  - 存在时 `Logger.Info($"已从本地加载联机配置：{ConfigPath}")`，后续流程与官方版一致。
- 示例配置：`samples\LinkConfig\ServerConfig.example.json`，`Link` 节点包含 `EasyTierVersion / Downloads / Argument / Peers / MandatoryPeers / PeersBlackList / RandomPeer / DiscoverPeerId / DiscoverPeer`。
  - `Downloads` 支持 `{arch}` 占位（x86_64 / aarch64）；`EasyTierVersion` 用于判断是否需要重新下载。
  - 复制的目标位置：`%AppData%\PCL\ServerConfig.json`（也可以直接放到 exe 同级的 `PCL\ServerConfig.json`，程序会优先用 AppData 那份）。

**EasyTier 模块**
- 官方下载的 zip 里就是 `easytier-core.exe` / `easytier-cli.exe` / `Packet.dll` / `wintun.dll` / `WinDivert64.sys`。解压到 `%AppData%\PCL\EasyTier\` 后重命名为 `联机模块.exe` / `联机模块 CLI.exe` 即可被识别。
- 版本检查读的是注册表 `HKCU\Software\PCLDebug` 的 `LinkEasyTierVersion`（`Sources.Registry`），与 `Setup.ini` 无关；要跳过下载就把该值写成配置里的 `EasyTierVersion`。
- 开源版原解压代码调用的 `FileUtils.ExtractToDirectory` 与 `DirectoryUtils.Copy/EnumerateDirectories` 都被裁掉了（整段只剩注释），已改用 `IO.Compression.ZipFile.ExtractToDirectory` + 手写遍历复制；`System.IO.Compression.FileSystem` 引用本来就在 vbproj 里。

**CLI 调用（关键）**
- `RefreshPeer` 里原本是 `Dim CliResult = ""`，真正的 `StartProcessAndGetOutput(...)` 被注释掉——也就是说即使模块起来了，也永远拿不到节点列表。
- 开源版没有 `StartProcessAndGetOutput`，新增 `RunCliAndGetOutput(FileName, Arguments, TimeoutMilliseconds)`：`ProcessStartInfo` + 异步读取 stdout/stderr + `WaitForExit(timeout)`，超时则 `Kill()` 并抛出明确异常。
- 现在每次刷新都会真正执行 `联机模块 CLI.exe -o json -p 127.0.0.1:{RPCPort} peer`，返回的 JSON 里 `lat_ms` 字段用于判断连接是否建立。

**实测结果（本机）**
- 配置读取：通过，日志 `已从本地加载联机配置`。
- 模块启动：通过，`联机模块.exe` 按 PCL 生成的参数启动，`new listener added listener=tcp://0.0.0.0:12427` 等日志正常。
- CLI 解析：通过，`EasyTier CLI` 加载器以约 60ms 完成，没有出现 `CLI 调用失败`。
- 卡点：公共节点 `public.easytier.cn` 在本机 DNS 返回 NXDomain（`easytier.cn` 可解析但 11010 端口不可达），房间停在「正在连接到节点……」，约 40 秒后失败退出。
- 自建两节点互连对照实验：`--no-tun` 下 core 会尝试绑定虚拟 IP，报 `10049 AddrNotAvailable`（与 PCL 无关，属 EasyTier 在无 tun 模式下的行为）。真实使用需要管理员权限创建虚拟网卡，或把 `Peers` 换成可达的自建中继。

**结论**：PCL 侧的联机链路（入口 → 配置 → 下载/解压 → 启动 → CLI 解析）已经全部打通，剩下的只是「能连上的节点」这一外部条件。把可达的中继写进 `ServerConfig.json` 的 `Peers`（或在设置 → 联机 → 自定义节点里填）即可继续。
