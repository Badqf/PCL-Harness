# PCL Harness 插件开发说明（API v3）

> v2 在 v1（发现 / 启停 / 宿主 API）之上增加了三个 UI 扩展点：**自定义页面、标题栏入口、设置项**；v3 又加了**事件接口**与 **Number / Choice 设置类型**，详见文末「API v3 追加内容」。
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
- v3 已有事件挂钩（就绪 / 页面切换 / 游戏启动前后）；下载流程回调仍未提供。
- 插件页面没有独立的 Loader 机制：需要后台加载数据时请自己在页面里起线程，并用 `RunInUi` 回到 UI 线程。

---

# API v3 追加内容

> v3 在 v2（页面 / 标题栏入口 / 设置项）之上补了两块：**事件接口** 与 **更丰富的设置类型**，并给 `Host` 增加了当前实例信息、导航与线程辅助。
> 只实现 `IPclPlugin` 或 v2 接口的插件不受影响。

## 1. 事件：`IPclPluginEvents`（可选接口）

```vb
Public Interface IPclPluginEvents
    Sub OnLauncherReady()                                   ' 启动器初始化完成、插件全部加载后调用一次
    Sub OnPageChanged(Page As Integer, SubType As Integer)  ' 主页面切换完成（Page = FormMain.PageType 数值）
    Sub OnGameLaunching(Info As PluginLaunchInfo)           ' 构建启动参数时，可追加 JVM / 游戏参数
    Sub OnGameLaunched(Info As PluginLaunchInfo)            ' 进程刚启动，Info.ProcessId 可用
End Interface
```

`PluginLaunchInfo` 字段：

| 成员 | 说明 |
| --- | --- |
| `InstanceName` / `InstanceFolder` | 要启动的版本名 / 版本隔离目录 |
| `PlayerName` | 当前选择的玩家名 |
| `ExtraJvmArgs` | 追加到 JVM 参数区（mainClass 之前） |
| `ExtraGameArgs` | 追加到游戏参数区末尾 |
| `ProcessId` | 仅 `OnGameLaunched` 有效，失败为 -1 |

示例：

```vb
Public Class MyPlugin
    Implements PCL.IPclPlugin
    Implements PCL.IPclPluginEvents

    Public Sub OnGameLaunching(Info As PCL.PluginLaunchInfo) Implements PCL.IPclPluginEvents.OnGameLaunching
        Info.ExtraJvmArgs.Add("-Dmyplugin.instance=" & Info.InstanceName)
        Info.ExtraGameArgs.Add("--demo-flag")
    End Sub

    Public Sub OnGameLaunched(Info As PCL.PluginLaunchInfo) Implements PCL.IPclPluginEvents.OnGameLaunched
        Logger.Info("游戏已启动，PID = " & Info.ProcessId)
    End Sub
End Class
```

注意：`OnGameLaunching` 抛异常会被吞掉并记日志，不会中断启动；请自行 try/catch 处理关键逻辑。

## 2. 设置类型扩展

`PluginSettingType` 现有四种：

| 值 | 类型 | 界面 | 额外字段 |
| --- | --- | --- | --- |
| `Boolean` | 勾选 | `MyCheckBox` | — |
| `Text` | 文本 | `MyTextBox` + 保存 | — |
| `Number` | 整数 | 输入框 + 范围提示 + 保存 | `MinValue` / `MaxValue`（默认 0 ~ 100，越界自动夹紧） |
| `Choice` | 单选下拉 | `MyComboBox` | `Options As String()` |

```vb
New PCL.PluginSetting With {
    .Key = "maxRetry", .Title = "重试次数", .Description = "下载失败时的重试上限。",
    .SettingType = PCL.PluginSettingType.Number, .DefaultValue = 2,
    .MinValue = 1, .MaxValue = 5,
    .Handler = Sub(value) Logger.Info("重试次数改为 " & value)
}

New PCL.PluginSetting With {
    .Key = "theme", .Title = "页面主题",
    .SettingType = PCL.PluginSettingType.Choice, .DefaultValue = "蓝",
    .Options = New String() {"蓝", "紫", "绿"}
}
```

值统一存在 `PCL\Plugins\plugins.settings.json`，读取仍用 `ModPlugin.GetSetting(Info, Key, DefaultValue)`。

## 3. `Host` 新增成员

| 成员 | 说明 |
| --- | --- |
| `InstanceName` / `InstanceFolder` / `PlayerName` | 启动器当前选中的版本与玩家 |
| `Navigate(Page As Integer)` | 切到主页面，参数用 `FormMain.PageType` 数值 |
| `RunInUi(Action)` | 从后台线程回到 UI 线程 |
| `RunLater(Milliseconds, Action)` | 延时执行（内部转 `RunInUi`） |

```vb
Host.RunLater(1500, Sub() Host.Hint("两秒后提醒"))
Host.Navigate(CInt(PCL.FormMain.PageType.Setup))
```

## 4. 版本对照

| 版本 | 能力 |
| --- | --- |
| v1 | 发现 / 启停 / `Host` 基础 API |
| v2 | 自定义页面、标题栏入口、设置项（Boolean / Text） |
| v3 | 事件接口（就绪 / 页面切换 / 游戏启动前后）、Number / Choice 设置、`Host` 导航与线程辅助 |
