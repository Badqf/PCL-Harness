# PCL Harness 插件开发指南（API v4）

> 适用版本：PCL Harness 3.1.x（上游 2.14.0.0 合并后）
> 插件根目录：`PCLHarness\Plugins\`，每个插件一个子文件夹。

---

## 1. 最小可运行插件

### 目录结构

```
PCLHarness\Plugins\hello\
├─ plugin.json      清单
└─ Hello.dll        编译产物（.NET Framework 4.8 类库）
```

### plugin.json

```json
{
  "id": "hello",
  "name": "你好插件",
  "version": "1.0.0",
  "author": "你",
  "description": "最小示例：加载时弹一条提示。",
  "entry": "",
  "dll": "Hello.dll"
}
```

| 字段 | 说明 |
| --- | --- |
| `id` | 唯一标识，建议小写英文/短横线；依赖别人时写这个 |
| `name` | 显示名（插件页、日志、提示里都用它） |
| `version` | 版本号，纯展示 |
| `author` / `description` | 插件页显示 |
| `entry` | 实现 `IPclPlugin` 的类型全名；留空则自动搜索 |
| `dll` | DLL 文件名，相对插件文件夹 |
| `permissions` | 可选，字符串数组：`net` `files` `args` `ui` `process` |
| `deps` | 可选，依赖的插件 id 数组（加载顺序会被自动排好） |

### 代码（VB.NET）

```vb
Imports System
Imports PCL

Public Class Main
    Implements IPclPlugin

    Public Sub OnLoad(Host As PluginHost) Implements IPclPlugin.OnLoad
        Host.Log("Hello 已加载，数据目录：" & Host.DataFolder)
        Host.Hint("你好，PCL Harness！", HintType.Green)
    End Sub

    Public Sub OnUnload() Implements IPclPlugin.OnUnload
        ' 清理资源（一般留空即可）
    End Sub
End Class
```

编译后把 DLL 放进 `Plugins\hello\`，打开「插件」页点「重新扫描」，或直接改一下 `plugin.json` 的时间戳触发热重载。

---

## 2. 权限模型

`plugin.json` 的 `permissions` 决定插件能做什么。**旧插件（不写 permissions）默认全部放行**，保持兼容；写了之后按声明限制：

| 权限 | 允许的 API |
| --- | --- |
| `net` | `Host.HttpGetString` / `Host.HttpDownload` |
| `files` | 读写任意路径；没有该权限时只能访问 `Host.DataFolder` 与当前版本目录 |
| `args` | `Host.AppendJvmArgs` / `Host.AppendGameArgs` |
| `ui` | 内嵌卡片、状态栏组件（v4 新增） |
| `process` | 预留（启动外部进程，当前不强制校验） |

插件页「已发现的插件」会直接显示 `权限：…` 与 `依赖：…`，方便排查。

---

## 3. 接口一览

| 接口 | 作用 |
| --- | --- |
| `IPclPlugin` | **必须**。`OnLoad(Host)` / `OnUnload()` |
| `IPclPluginUi` | 自定义页面、标题栏入口、设置项 |
| `IPclPluginEvents` | 启动器就绪、页面切换、游戏启动前/后 |
| `IPclPluginLifecycle` | 游戏退出、切换版本、主题切换（v4） |
| `IPclPluginLink` | 联机建房/加入/断开（v4） |
| `IPclPluginDownload` | 下载开始/结束（v4） |
| `IPclPluginClosing` | 拦截启动器关闭（v4，返回 True 取消关闭） |
| `IPclPluginUi2` | 主页卡片、设置页卡片、状态栏组件（v4） |

所有可选接口的方法都必须实现，**不关心的方法留空体**即可。

### 事件示例

```vb
Public Class Main
    Implements IPclPlugin, IPclPluginLifecycle, IPclPluginDownload

    Public Sub OnLoad(Host As PluginHost) Implements IPclPlugin.OnLoad
        Me.Host = Host
    End Sub
    Public Sub OnUnload() Implements IPclPlugin.OnUnload
    End Sub

    Public Sub OnGameExited(Info As PluginLaunchInfo, ExitCode As Integer) _
        Implements IPclPluginLifecycle.OnGameExited
        Host.Log($"游戏退出，退出码 {ExitCode}")
    End Sub

    Public Sub OnInstanceChanged(InstanceName As String) _
        Implements IPclPluginLifecycle.OnInstanceChanged
        Host.ShowNotification("已切换到 " & InstanceName)
    End Sub

    Public Sub OnThemeChanged(IsDark As Boolean) _
        Implements IPclPluginLifecycle.OnThemeChanged
        Host.Log("主题切换为 " & If(IsDark, "深色", "浅色"))
    End Sub

    Public Sub OnDownloadStarted(Target As String) _
        Implements IPclPluginDownload.OnDownloadStarted
        Host.Log("开始下载：" & Target)
    End Sub

    Public Sub OnDownloadFinished(Target As String, Success As Boolean) _
        Implements IPclPluginDownload.OnDownloadFinished
        Host.Log($"下载结束：{Target}，成功={Success}")
    End Sub

    Private Host As PluginHost
End Class
```

---

## 4. 宿主 API（PluginHost）

### 基础

| 成员 | 说明 |
| --- | --- |
| `Info` | 本插件信息（id / name / 权限 / 事件记录） |
| `DataFolder` | 插件私有目录 `Plugins\<id>\data`，自动创建 |
| `Log(Text)` | 写启动器日志（`Log*.txt` 里带 `[插件 名字]` 前缀） |
| `Hint(Text, HintType)` | 左下角提示 |
| `ShowNotification(Text, HintType)` | 更醒目的通知 |
| `Message(Title, Text)` | 模态对话框 |
| `OpenUrl(Url)` / `OpenFolder()` | 打开网页 / 插件目录 |
| `InstanceName` / `InstanceFolder` / `PlayerName` | 当前版本与玩家 |
| `Navigate(Page)` | 跳主页面（1 = 启动） |
| `RunInUi(Action)` | 切到 UI 线程 |
| `RunLater(ms, Action)` | 延迟后回 UI 线程执行 |

### 文件与网络

```vb
Host.WriteTextFile(Host.DataFolder & "\counter.txt", "42")
Dim Text = Host.ReadTextFile(path)          ' 需要 files 权限（data 目录除外）
Host.HttpGetString(url)                     ' 需要 net 权限
Host.HttpDownload(url, localPath)           ' 需要 net + files
```

### 参数注入

```vb
Host.AppendJvmArgs("-Xmx6G")     ' 需要 args 权限
Host.AppendGameArgs("--demo")
```

参数会在构建启动命令时按插件加载顺序拼接。

### 主题感知

```vb
If Host.ThemeIsDark Then ...     ' 深色主题
Dim Hue = Host.ThemeHue          ' 0-360
```

---

## 5. 内嵌 UI（v4）

### 5.1 主页卡片 / 设置页卡片

```vb
Imports System.Collections.Generic
Imports System.Windows.Controls
Imports PCL

Partial Public Class Main
    Implements IPclPlugin, IPclPluginUi2

    Public Function GetHomeCards() As IEnumerable(Of PluginCardInfo) _
        Implements IPclPluginUi2.GetHomeCards
        Return {New PluginCardInfo With {
            .Id = "hello-card",
            .Title = "我的卡片",
            .Description = "这段文字由插件提供。",
            .Factory = Function() BuildCard()
        }}
    End Function

    Public Function GetSettingsCards() As IEnumerable(Of PluginCardInfo) _
        Implements IPclPluginUi2.GetSettingsCards
        Return Nothing      ' 不注册设置页卡片
    End Function

    Public Function GetStatusWidgets() As IEnumerable(Of PluginStatusWidget) _
        Implements IPclPluginUi2.GetStatusWidgets
        Return {New PluginStatusWidget With {
            .Id = "hello-status",
            .TextProvider = Function() "Hello · " & Host.InstanceName,
            .OnClick = Sub() Host.Hint("点到了状态栏")
        }}
    End Function

    Private Function BuildCard() As Object
        Dim Panel As New StackPanel
        Panel.Children.Add(New TextBlock With {.Text = "插件直接构建的 WPF 控件", .FontSize = 13})
        Dim Btn As New MyButton With {.Text = "点我", .Height = 30, .Margin = New Thickness(0, 8, 0, 0)}
        AddHandler Btn.Click, Sub() Host.ShowNotification("按钮被点击")
        Panel.Children.Add(Btn)
        Return Panel
    End Function
End Class
```

> 卡片控件会被启动器**缓存并复用**，不要在里面写每次重新创建才正确的逻辑；需要刷新时自己在控件里放按钮。
> 颜色请用 `Res("ColorBrush1")` / `Res("ColorBrushGray2")` 等主题资源，或直接 `{DynamicResource}`，不要硬编码黑白色。

### 5.2 自定义页面

```vb
Public Function GetPages() As IEnumerable(Of PluginPageInfo) Implements IPclPluginUi.GetPages
    Return {New PluginPageInfo With {
        .Id = "hello-page",
        .Title = "Hello",
        .Icon = Nothing,
        .Factory = Function() New PageHello()
    }}
End Function
```

页面类继承 `MyPageRight`（与其他 PCL 页面一致）。

### 5.3 设置项

```vb
Public Function GetSettings() As IEnumerable(Of PluginSetting) Implements IPclPluginUi.GetSettings
    Return {New PluginSetting With {
        .Key = "greeting",
        .Title = "问候语",
        .Description = "加载时显示的文本",
        .SettingType = PluginSettingType.Text,
        .DefaultValue = "你好"
    }}
End Function
```

读取与保存：

```vb
Dim Greeting = ModPlugin.GetSetting(Host.Info, "greeting", "你好").ToString()
ModPlugin.SetSetting(Host.Info, "greeting", "新值")
```

支持 `Boolean` / `Text` / `Number` / `Choice`（`Options` 配合 `Choice`）。

---

## 6. 依赖与加载顺序

```json
{
  "id": "child",
  "deps": ["base"],
  ...
}
```

启动器会按依赖拓扑排序加载；缺失依赖或出现环时报错：

- `缺少依赖插件：xxx`
- `插件依赖存在循环或无法满足`

依赖只影响加载顺序与检查，不会自动下载插件。

---

## 7. 热重载与调试

- 修改 `plugin.json` 或 DLL 的时间戳后，启动器约 1 秒内自动重载该插件，并提示「插件已热重载：名字」。
- 插件页「已发现的插件」会显示状态、错误、事件记录（最近 40 条），加载失败时直接看这里。
- 日志文件：`PCLHarness\Log*.txt`，插件输出行形如
  `I [ModPlugin] [插件 示例插件] 收到事件：主题切换为 深色`。

---

## 8. 示例插件

完整示例：`F:\PCL2-Source\samples\DemoPlugin\`

```
DemoPlugin/
├─ DemoPlugin.vb      演示 v4 全部接口
├─ DemoPlugin.vbproj
├─ plugin.json
└─ build.ps1          一键编译并把 DLL 部署到 PCLHarness\Plugins\demo-harness\
```

`DemoPlugin.vb` 里包含了主页卡片、状态栏组件、设置项、页面注册、事件日志、文件读写、权限演示等全部用法，可直接复制改写。

---

## 9. 常见问题

**插件加载了但界面没变化？**
主页卡片在插件加载完成后统一渲染；若卡片依赖数据，请在 `Factory` 内自行处理「暂无数据」。设置页卡片每次进入页面会重新渲染。

**提示「没有 files 权限」？**
在 `plugin.json` 里加 `"permissions": ["files"]`，或只读写 `Host.DataFolder`。

**提示「没有 net 权限」？**
同上，加 `"net"`。

**改了代码要重启吗？**
不用。保存 DLL 后等约 1 秒自动热重载；若插件正在占用文件可能失败，看日志「热重载失败」。

**能用 C# 写吗？**
可以，引用 `PCL Harness.exe`（类型都在 `PCL` 命名空间），实现同一套接口即可。

## API v5（当前版本）

v5 在 v4 基础上补齐了「细粒度权限、完整性校验、事件、强类型设置、日志分级」，旧插件不需要任何改动。

### 清单新增字段

```json
{
  "minApiVersion": 5,
  "sha256": "DLL 的 SHA-256（小写十六进制，可省略）",
  "permissions": {
    "net": { "hosts": ["api.github.com", "*.example.com"] },
    "files": { "read": ["Downloads/"], "write": ["PluginData/demo/"] }
  }
}
```

- `minApiVersion`：高于启动器支持的 API 版本时拒绝加载，并提示更新启动器。
- `sha256`：加载前校验 DLL，与清单不一致直接拒绝加载（防止文件损坏或被替换）。
- `permissions` 仍可写成字符串数组（v4 写法，表示整类权限）；写成对象时按下列规则细化：
  - `net.hosts`：允许访问的域名，支持 `*.example.com` 前缀通配与 `*`；未列出的域名会抛出 `UnauthorizedAccessException`。
  - `files.read` / `files.write`：允许读写的路径，相对路径按启动器目录解析，`*` 通配，末尾 `/` 表示整个目录。插件自己的数据目录（PluginData/<id>/）始终可访问。

### 宿主 API

```vb
'事件订阅：内置事件「页面切换」(Page, SubType)
Host.Events.On("页面切换", New Action(Of String, String)(
    Sub(Page, SubType) Host.LogDebug($"进入 {Page} / {SubType}") End Sub))
Host.Events.Off("页面切换", Handler)
Host.Events.SubscriberCount("页面切换")

'强类型设置（底层仍是 plugins.settings.json）
Dim Count As Integer = Host.GetSetting("loadCount", 0)
Host.SetSetting("loadCount", Count + 1)

'日志分级（写启动器日志，Warn/Error 同时记入插件事件）
Host.LogDebug("调试")
Host.LogInfo("普通")
Host.LogWarn("警告")
Host.LogError("错误")

'对话框：返回点下的按钮文字
Dim Answer = Host.Ask("要现在打开吗？", "插件提示", "打开", "取消")
```

### 兼容性

- 未声明 `permissions` 的插件：保持 v4 行为（默认放行），不做限制。
- 只写了 v4 字符串数组权限的插件：仍按整类权限放行，不启用白名单。
- API 版本：`ApiVersionUi = 5`，User-Agent 为 `PCLHarness-Plugin/5`。
