# PCL Harness 插件开发说明（API v2）

> v2 在 v1（发现 / 启停 / 宿主 API）之上增加了三个 UI 扩展点：**自定义页面、标题栏入口、设置项**。
> 只实现 `IPclPlugin` 的旧插件不受影响，照常可用。

## 目录结构

```
<启动器目录>\PCL\Plugins\
    plugins.json            # 启用状态（自动维护，可手改）
    plugins.settings.json   # 插件设置值（自动维护，可手改）
    说明.txt                # 首次运行自动生成
    <你的插件文件夹>\
        plugin.json         # 必需：插件清单
        YourPlugin.dll      # 可选：实现 IPclPlugin 的程序集
        data\               # 可选：插件自己的数据目录（Host.DataFolder 自动创建）
```

## plugin.json

```json
{
  "id": "myplugin",
  "name": "我的插件",
  "version": "1.0.0",
  "author": "你",
  "description": "这个插件做什么",
  "entry": "MyPlugin.Main",
  "dll": "MyPlugin.dll"
}
```

- `entry` 可省略：会扫描 DLL 里第一个实现 `PCL.IPclPlugin` 的非抽象类。
- 只放 `plugin.json` 不放 DLL：声明型插件，只出现在插件页，不执行代码。
- `id` 用于启用状态与设置值的持久化，改动 id 等于重置该插件的状态。

## 接口一：IPclPlugin（必需）

```vb
Public Interface IPclPlugin
    Sub OnLoad(Host As PluginHost)
    Sub OnUnload()
End Interface
```

`PluginHost` 提供：

| 成员 | 说明 |
|---|---|
| `Host.Log(msg)` | 写启动器日志 |
| `Host.Hint(msg, Optional type)` | 左下角提示（`HintType.Blue/Red/...`） |
| `Host.Message(title, text)` | 模态对话框 |
| `Host.OpenUrl(url)` | 默认浏览器打开网址 |
| `Host.OpenFolder()` | 打开插件根目录 |
| `Host.DataFolder` | 本插件专属数据目录（自动创建） |
| `Host.Info` | 该插件的 `PluginInfo`（读取 id / 名称 / 版本） |

## 接口二：IPclPluginUi（可选，v2）

```vb
Public Interface IPclPluginUi
    Function GetPages() As IEnumerable(Of PluginPageInfo)
    Function GetNavItems() As IEnumerable(Of PluginNavItem)
    Function GetSettings() As IEnumerable(Of PluginSetting)
End Interface
```

三个方法都可以返回 `Nothing`。启动器在插件 `OnLoad` 之后立刻调用它们并登记返回值。

### 1) 自定义页面 PluginPageInfo

| 成员 | 说明 |
|---|---|
| `Id` | 页面 ID（插件内唯一，必填） |
| `Title` | 页面标题：显示在标题栏与页面里 |
| `Icon` | 标题栏图标 Path 数据（24×24 左右，可空） |
| `Factory` | `Func(Of FrameworkElement)`：返回要显示的用户控件，每次打开页面调用一次 |

- 返回值可以是任意 `FrameworkElement`（一般用 `UserControl`）。
- 若返回的不是 `MyScrollViewer`，宿主会自动套一层滚动容器。
- 页面会被包进 `PagePluginHost`（继承 `MyPageRight`），因此标题栏切换动画、返回顶部等行为与内置页面一致。
- 建议自己用 `PCL.MyButton`、`PCL.MyTextBox` 等控件，并通过 `SetResourceReference(..., "ColorBrush1")` 之类取主题色，暗色/亮色主题都会自动跟随。

### 2) 标题栏入口 PluginNavItem

| 成员 | 说明 |
|---|---|
| `Text` | 按钮文字（必填） |
| `Icon` | 图标 Path 数据（可空） |
| `Page` | 要打开的 `PluginPageInfo`；为 `Nothing` 时改为执行 `Action` |
| `Action` | 无页面时的点击行为 |

- 入口按注册顺序排在「插件」按钮后面，标签形如 `示例`，点击即切换页面。
- 一次最多建议 3 个入口；入口数量与顺序会随插件刷新一起重建。

### 3) 设置项 PluginSetting

| 成员 | 说明 |
|---|---|
| `Key` | 设置键（必填，存进 `plugins.settings.json`） |
| `Title` / `Description` | 显示在插件页下方 |
| `SettingType` | `PluginSettingType.Boolean` 或 `.Text` |
| `DefaultValue` | 未保存过时的默认值 |
| `Handler` | 值变化回调 `Action(Of Object)`（点保存或勾选后触发） |

读写设置值的公共 API（插件自己也能调用）：

```vb
Dim Value = PCL.ModPlugin.GetSetting(Host.Info, "greeting", "默认值")
PCL.ModPlugin.SetSetting(Host.Info, "greeting", "新的值")
```

设置值存放在 `PCL\Plugins\plugins.settings.json`：

```json
{
  "demo-harness": {
    "showHintOnLoad": false,
    "greeting": "你好，这里是插件注册的页面。"
  }
}
```

## 生命周期

1. 启动器启动后（初始化第三阶段）后台调用 `ModPlugin.Refresh(True)`：扫描目录并加载所有已启用插件；随后在 UI 线程刷新标题栏入口。
2. 进入「插件」页或点「重新扫描」会 **重新扫描并重载** 已启用插件（`OnUnload` → `OnLoad`），并清空再登记所有扩展点。
3. 在插件页点「禁用」会调用 `OnUnload` 并把状态写回 `plugins.json`。

## 完整示例（samples/DemoPlugin，已实测）

```vb
Imports System.Collections.Generic

Public Class DemoPlugin
    Implements PCL.IPclPlugin
    Implements PCL.IPclPluginUi

    Private Host As PCL.PluginHost
    Private PageInfo As PCL.PluginPageInfo

    Public Sub OnLoad(Host As PCL.PluginHost) Implements PCL.IPclPlugin.OnLoad
        Me.Host = Host
        Host.Log("示例插件已加载")
    End Sub

    Public Sub OnUnload() Implements PCL.IPclPlugin.OnUnload
        Host = Nothing
    End Sub

    Public Function GetPages() As IEnumerable(Of PCL.PluginPageInfo) Implements PCL.IPclPluginUi.GetPages
        PageInfo = New PCL.PluginPageInfo With {.Id = "demo.home", .Title = "示例页面", .Factory = Function() New DemoPage(Me)}
        Return New List(Of PCL.PluginPageInfo) From {PageInfo}
    End Function

    Public Function GetNavItems() As IEnumerable(Of PCL.PluginNavItem) Implements PCL.IPclPluginUi.GetNavItems
        Return New List(Of PCL.PluginNavItem) From {New PCL.PluginNavItem With {.Text = "示例", .Page = PageInfo}}
    End Function

    Public Function GetSettings() As IEnumerable(Of PCL.PluginSetting) Implements PCL.IPclPluginUi.GetSettings
        Return New List(Of PCL.PluginSetting) From {
            New PCL.PluginSetting With {.Key = "showHintOnLoad", .Title = "启用时弹出提示",
                .SettingType = PCL.PluginSettingType.Boolean, .DefaultValue = True}
        }
    End Function
End Class
```

编译（本机 Roslyn 4.14 的 vbc，目标 .NET Framework 4.8）：

```powershell
$vbc = "C:\Users\XYM_3\roslyn\tasks\vbc.exe"
$exe = "F:\PCL2-Source\Plain Craft Launcher 2\bin\PCL Harness.exe"
$fw  = "C:\Users\XYM_3\dotnet-fwreal\.NETFramework\v4.8"
& $vbc /nologo /target:library /out:"MyPlugin.dll" /optionstrict- /optionexplicit+ `
    /r:"$exe" /r:"$fw\mscorlib.dll" /r:"$fw\System.dll" /r:"$fw\System.Core.dll" `
    /r:"$fw\System.Xaml.dll" /r:"$fw\WindowsBase.dll" /r:"$fw\PresentationCore.dll" /r:"$fw\PresentationFramework.dll" `
    "MyPlugin.vb"
```

或直接运行 `samples\DemoPlugin\build.ps1`（自动找 exe 与框架目录）。

把 `MyPlugin.dll` 与 `plugin.json` 放进 `PCL\Plugins\myplugin\`，重启启动器（或进插件页点「重新扫描」）即可在列表里看到并启用。

## 限制与注意

- 插件跑在启动器进程内、完全信任（无沙箱）。只装你信任的插件。
- 目标框架 .NET Framework 4.8，语言版本按 Roslyn 4.14（VB 16.x / C# 12）。
- v2 的扩展点只有「页面 / 入口 / 设置项」三类；还没有事件挂钩（如启动流程、下载流程回调），后续可加。
- 插件页面没有独立的 Loader 机制：需要后台加载数据时请自己在页面里起线程，并用 `RunInUi` 回到 UI 线程。
