# PCL Harness 插件开发指南（API v8）

> 适用版本：PCL Harness 3.9.4 及以上（插件 API v8，上游 2.14.0.0 合并后的自编版）
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
| `permissions` | 可选。字符串数组只认 `net` `files` `args` 三种；v5 也可写成对象（`net.hosts` / `files.read` / `files.write`），见「API v5」一节 |
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

> **首次调试两步**：① 新插件默认是**停用**的——`plugins.json` 里没有它的记录就不会加载（改动前日志里连痕迹都没有）；到「插件」页找到它点启用即可，会自动热重载，不用重启。② 插件页顶部会显示「共 N 个，已启用 M 个」，M 为 0 就说明还没启用。

### 编译与编码

- 源码请存成 **UTF-8（建议带 BOM）**：`vbc` 默认按系统 ANSI 解码，中文会整片变成乱码。SDK 的 `build.ps1` 已加 `/codepage:65001`；自己拼 vbc 命令行务必也加上。
- `vbCrLf` / `vbLf` / `vbTab` 定义在 `Microsoft.VisualBasic` 程序集里，`vbc` 默认不引用它（报 `BC30451: 未声明名称`）。两种解法：给 vbc 加 `/r:Microsoft.VisualBasic.dll`（SDK 脚本已加），或改用 `Environment.NewLine`。

---

## 2. 权限模型

`plugin.json` 的 `permissions` 决定插件能做什么。**没写 `permissions` = 没有任何权限**（v7.4 起，旧插件同样需要声明；调用受保护的接口会抛 `UnauthorizedAccessException`）；写了之后按声明限制：

| 权限 | 允许的 API |
| --- | --- |
| `net` | `Host.HttpGetString` / `Host.HttpDownload`；v5 可再用 `net.hosts` 收缩到域名白名单 |
| `files` | 读写文件；v5 可再用 `files.read` / `files.write` 收缩到路径白名单。没有该权限时只能访问 `Host.DataFolder` 与当前版本目录 |
| `args` | `Host.AppendJvmArgs` / `Host.AppendGameArgs` |

> 权限名以宿主实际校验的字符串为准：**`files`（复数）、`net`、`args`**。`file`（单数）、`ui`、`process`、`events` 都不会被校验——写了既不报错也不产生任何限制；事件订阅、内嵌卡片、状态栏组件本身不需要权限。

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
Host.HttpDownload(url, localPath)           ' 需要 net + files，失败只返回 False
Host.HttpDownloadResult(url, localPath)     ' 同上，但返回 PluginDownloadResult 分类
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

## 5.5 隔离加载与下载结果（v7.5）

**每个插件拿到独立程序集**

v7.5 起宿主用 `Assembly.Load(IO.File.ReadAllBytes(dllPath))` 加载插件，而不是按路径 `LoadFrom`。两个插件各自带同名 `Plugin.dll`（同版本、无强名）也不会再命中同一份程序集，静态字段、单例、`data` 目录写入都不会串台。

插件之间的依赖用 `AppDomain.AssemblyResolve` 兜底：宿主按简单名到各插件目录找同名 DLL，同样以字节流加载。**注意**：

- 依赖不要跨插件复用同一份 DLL——即使加载成功了，两边也是各自的副本，静态状态不共享。
- 需要共享状态时请用文件或事件总线（`Host.Events`），不要指望静态字段。

**下载会告诉你失败原因**

```vb
Dim R = Host.HttpDownloadResult(url, localPath)
Select Case R
    Case PluginDownloadResult.Ok
    Case PluginDownloadResult.NotPermitted   ' 清单里没有 net 权限
    Case PluginDownloadResult.HostDenied     ' 主机不在白名单
    Case PluginDownloadResult.PathDenied     ' 目标路径不在 files.write 里
    Case PluginDownloadResult.TooLarge       ' 超过 512 MB
    Case Else                                ' 网络错误等，见事件记录
End Select
```

`Host.HttpDownload(url, localPath)` 保留为布尔包装（等价于 `R = Ok`），旧代码不用改。失败时宿主会先删掉半截文件再返回，不会留下「看起来下载成功」的残缺文件。
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
- 新插件默认**停用**：`plugins.json` 里没有记录就不会加载。到「插件」页点启用即可，启用/停用会立即热重载。日志会写「插件 X 默认未启用…」与「扫描完成：共 N 个插件，已启用 M 个」。
- 数据目录：`Host.DataFolder` = `PCLHarness\Plugins\<id>\data`（自动创建），不是 `PluginData\`。
- 插件页「已发现的插件」会显示状态、错误、事件记录（最近 40 条），加载失败时直接看这里。
- 日志文件：`PCLHarness\Log*.txt`，插件输出行形如
  `I [ModPlugin] [插件 示例插件] 收到事件：主题切换为 深色`。

---

## 8. 示例插件

完整示例（相对 SDK 根目录）：`samples\DemoPlugin\`；v5 最小示例在 `samples\HelloPlugin\`。

```
DemoPlugin/
├─ DemoPlugin.vb      演示 v4 全部接口（UI 卡片、设置项、事件、文件 / 网络）
├─ plugin.json        权限声明（默认生成 files / net / args）
├─ build.ps1          一键编译（引用启动器 exe）
└─ Demo.Plugin.dll    编译产物（可选）
```

`DemoPlugin.vb` 里包含了主页卡片、状态栏组件、设置项、页面注册、事件日志、文件读写、权限演示等用法，可直接复制改写；v5 新增的事件总线、强类型设置、日志分级见 `samples\HelloPlugin\HelloPlugin.vb`。

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

**插件目录放好了，插件页却看不到 / 日志里没有插件行？**
确认 `plugin.json` 和 DLL 都在 `PCLHarness\Plugins\<id>\` 这一层（不能多套一层）；新插件默认停用，要在插件页点启用才会加载。若目录里没有 `plugin.json` 或清单解析失败，启动器日志现在也会明确写出来。

## API v5（历史版本）

v5 在 v4 基础上补齐了「细粒度权限、完整性校验、事件、强类型设置、日志分级」，旧插件不需要任何改动。

### 清单新增字段

```json
{
  "minApiVersion": 5,
  "sha256": "DLL 的 SHA-256（小写十六进制，可省略）",
  "permissions": {
    "net": { "hosts": ["api.github.com", "*.example.com"] },
    "files": { "read": ["Downloads/"], "write": ["PCLHarness/PluginOut/"] }
  }
}
```

- `minApiVersion`：高于启动器支持的 API 版本时拒绝加载，并提示更新启动器。
- `sha256`：加载前校验 DLL，与清单不一致直接拒绝加载（防止文件损坏或被替换）。
- `permissions` 仍可写成字符串数组（v4 写法，表示整类权限）；写成对象时按下列规则细化：
  - `net.hosts`：允许访问的域名，支持 `*.example.com` 前缀通配与 `*`；未列出的域名会抛出 `UnauthorizedAccessException`。
  - `files.read` / `files.write`：允许读写的路径，相对路径按启动器目录解析，`*` 通配，末尾 `/` 表示整个目录。插件自己的数据目录（`Plugins\<id>\data`，即 `Host.DataFolder`，会自动创建）始终可访问。

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

## API v6（历史版本）

v6 在 v5 之上补齐「事件总线、服务注册、后台任务、开发者模式」，**v5 插件不改一行代码就能继续用**（`ApiVersionUi = 6`）。

### 6.1 事件总线

13 个内置事件（`ModPlugin.PluginEvents`）：`启动器就绪`、`页面切换`、`游戏启动前`、`游戏启动后`、`游戏退出`、`实例切换`、`主题切换`、`联机创建`、`联机加入`、`联机关闭`、`下载开始`、`下载完成`、`关闭前`。

```vb
'按优先级订阅（数字大的先执行），OnlyOnce 执行一次后自动移除
Host.Events.On("页面切换", Sub(Page As Integer, SubType As Integer)
                               Host.LogDebug($"进入 {Page}/{SubType}")
                           End Sub, Priority:=10)
Host.Events.Once("启动器就绪", Sub() Host.LogInfo("启动完成") End Sub)
Host.Events.Off("页面切换", Handler)

'手动触发（调试用）
Host.Events.Emit("主题切换", True)
Host.Events.EmitAsync("下载开始", "test.zip")
```

- 处理器抛出的异常会被隔离并记进开发日志，不会影响其他插件与启动器。
- 想列出全部事件、签名与订阅数：插件页 → 开发模式，输入 `events`。

### 6.2 服务注册（插件之间互相调用）

```vb
Public Interface IGreeter
    Function Greet(Name As String) As String
End Interface

Host.Services.Register(Of IGreeter)(New Greeter())      '注册（Owner 默认是插件 id）
Dim G = Host.Services.Get(Of IGreeter)()               '取用（不存在会抛异常）
If Host.Services.Has(Of IGreeter)() Then ... End If
Dim Maybe = Host.Services.TryGet(Of IGreeter)()         '取不到返回 Nothing
```

- 插件被禁用或卸载时，它注册的服务会自动注销。
- 列出当前所有服务：`services`。

### 6.3 后台任务与进度

```vb
Host.Tasks.Run("下载配置", Sub(Ctx As PluginTaskContext)
                                For I = 1 To 10
                                    Ctx.ThrowIfCancelled()          '用户点了取消就抛异常
                                    Ctx.Report(I / 10.0, $"第 {I} 步") '进度写进任务管理器
                                    Thread.Sleep(300)
                                Next
                            End Sub)
```

任务会出现在右下角「任务管理」里，可取消，进度用 `LoaderTask` 上报；列出运行中的任务用 `tasks`。

### 6.4 PclPluginBase（推荐基类）

```vb
Public Class MyPlugin
    Inherits PCL.PclPluginBase   '已实现全部 6 个接口，按需 Overrides 即可

    Public Overrides Sub OnLoad(Host As PCL.PluginHost)
        MyBase.OnLoad(Host)      '必须调用，否则 Host/DataFolder 不会赋值
        DevLog("加载完成")        '写入开发日志
        Hint("插件已就绪")        '左下角提示
    End Sub
End Class
```

### 6.5 依赖版本约束

`dependsOn` 支持 `"其它插件id@>=1.2"`（`>=` `<=` `>` `<` `=`，裸版本等于「大于等于」），版本不满足时插件不会加载并给出原因。

### 6.6 开发模式（插件页第三个分区）

插件页 → 控制台：实时日志窗口 + 命令回显，支持命令：

- `help` 帮助；`events` 事件/签名/订阅数；`plugins` 插件清单；`services` 已注册服务；`tasks` 运行中的任务；
- `emit <事件名>` 手动触发事件；`clear` 清空日志。

日志同时写进 `PluginDevLog`（内存环形缓冲，最多 4000 条），`Host.DevLog(...)` / `DevLog(...)` 都能往里写。

### 6.7.1 回滚点（v7.6 / 3.8.3）

插件页卡片上的「回滚到上一版」来自 `Plugins\.backup\`：

- **命名**：3.8.3 起统一按插件 **id** 命名（`<id>-yyyyMMdd-HHmmss`）；3.8.2 及更早按**目录名**命名。两者都能被找到——查找时先比目录名前缀，不匹配再回退读该备份内 `plugin.json` 的 `id`，所以**目录名与 id 不一致的老插件不需要手动改名**。
- **产生时机**：覆盖导入（安装同 id 的新版本）与「排队更新」（`.pending` 换入）各生成一份。只保留最近若干份，超过 30 天会被自动清走（进回收站）。
- **执行**：卡片按钮 → 确认框（`Info.Loaded` 为真时文案是「插件正在运行，回滚会排队到下次启动」，否则是「会立即把当前版本移入回收目录并换回备份」）→ 当前版本移进 `Plugins\.trash\`，备份换回原位，插件随即被停用；重启启动器后生效。
- **注意**：回滚后 `permissions` 若与 `trust.json` 里记录的指纹不同，会当作新插件重新询问。

### 6.7 权限指纹与签名（v6.3）

**启用前的权限确认**

- 插件首次启用会弹出确认框：逐条展示 `permissions` 及中文说明，未声明权限也会写明；同时显示作者与来源（未签名 / 发布者声明）。
- 只有点「仍然启用」才会写入状态并加载；点「取消」会立刻把勾选回滚。
- 确认结果存进 `Plugins\trust.json`：权限不变不会再问；`permissions` 有任何变化都会重新询问。

**`trust.json` 里存的是「权限指纹」，不是签名**

```json
{
  "schema": 2,
  "plugins": {
    "instance-menu-demo": { "permissionsHash": "events,files,ui" }
  }
}
```

- `permissionsHash` 的生成方式：把 `permissions` 全部小写、按字母排序、用逗号连接（`events,files,ui`）。
- 它**没有密钥、不是哈希摘要、也不加密**，唯一作用是「检测权限集合是否变化」，不能证明包是谁发布的。
- 旧格式（`{"id":"ui,events"}` 或 `{"id":{"signature":"ui,events"}}`）仍可读取，保存时会自动升级为 `schema: 2`。

**真实验签（v3.7.4 已实现）**

- 算法：`ECDsa` P-256 + SHA-256（.NET Framework 4.8 自带）。
- 签名载荷：`id + "\n" + version + "\n" + sha256(DLL) 的小写十六进制`。注意是 **DLL 的哈希**，不是整包：清单里没写 `sha256` 时启动器会按 `dll` 字段实测补算，所以你改 DLL 而不改清单也会立刻校验失败。
- 公钥存放：`Plugins\publishers.json`，格式 `{"发布者名":"Base64(SPKI DER)"}`（一个文件可放多个发布者）。
- 清单字段：`"signature": "<Base64 签名>"`。
- 显示四态：`签名有效` / `签名无效（文件可能被改动过）` / `签名来自未知发布者`（公钥表为空或没有匹配）/ `未签名（来源需自行确认）`。验签**不**替代权限确认，权限变化照旧重新询问。

生成签名（PowerShell + .NET，不依赖 SDK）：

```powershell
$dll = 'Plugins\hello\Hello.Plugin.dll'
$sha = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLower()
$payload = "hello`n1.1.0`n$sha"
$key = [System.Security.Cryptography.ECDsa]::Create()
$key.ImportFromPem((Get-Content 'private.pem' -Raw))   # 或 ImportParameters / CngKey
$sig = $key.SignData([Text.Encoding]::UTF8.GetBytes($payload), 'SHA256')
[Convert]::ToBase64String($sig)
```

导出公钥给用户/市场：

```powershell
$spki = $key.ExportSubjectPublicKeyInfo()
[Convert]::ToBase64String($spki)   # 放进 publishers.json 的值
```

### 6.7.1 更新通道、排队更新与回滚（v3.7.4）

**更新地址**

清单可选 `"update": "https://…/plugin.json"`：启动器只把它读到 `PluginInfo.UpdateUrl` 并在卡片上标注「已声明更新地址」，不会自己去抓；插件用 `Host.Plugins.UpdateUrl(id)` 取来比对版本，再用 `Host.HttpDownload` 下载、`Host.Plugins.ImportPackage` 安装。

**排队更新（`.pending`）**

覆盖安装一个**正在运行**的插件时，宿主不会强行替换目录（v7.5 起改按字节流加载，文件本身不再被独占，但已加载的程序集无法卸载），而是把它暂存到 `Plugins\.pending\<id>` 并返回 `PluginImportResult.NeedRestart = True`：

```vb
Dim r = Host.Plugins.ImportPackage(pkgPath, Overwrite:=True)
If r.Ok AndAlso r.NeedRestart Then
    Host.Message("更新已就绪，重启启动器后生效", "插件更新")
End If
```

重启时 `Refresh` 先应用 `.pending` 再扫描：换入成功写日志「待生效插件已替换：…」，旧目录进 `.backup\<id>-<时间戳>`。同一时间只保留一份 `.pending`，后装覆盖前装。

**回滚**

- `Host.Plugins.CanRollback(id)` → 是否存在上一版备份。
- `Host.Plugins.Rollback(id)` → 已加载时同样排队（复制备份到 `.pending\<id>`，重启生效）；未加载时立刻「当前目录进 `.trash`、备份回原位」。插件不能回滚自己。
- 界面入口：插件卡片「回滚到上一版」，确认框会说明「回滚会排队到下次启动」。

**目录回收**

- `Host.Plugins.Uninstall(id)` 会把整个插件目录移入 Windows 回收站，可从回收站还原；回收站不可用时由 `DirectoryUtils` 回退删除。卸载失败（例如文件被占用）时目录保留，返回 `False`。
- 启动时清理 `.trash` / `.backup` / `.tmp` 中修改时间超过 **30 天** 的目录；单份备份策略是「每次进备份只留最近一份」。所以回滚窗口是「最近一次升级之前」，更早的版本会被自动丢弃。

**热重载**

「重新加载插件」只重读清单与程序集元数据，**改了代码仍必须重启启动器**（程序集已加载，无法卸载重载）。
### 6.8 实例右键菜单（v6.2）

实现 `IPclPluginInstanceMenu` 后，插件可以往「版本选择」页里每个实例卡片的右键菜单里加自己的操作（菜单顺序：插件项 → 分隔线 → 「实例设置」）：

```vb
Public Class DemoPlugin
    Inherits PclPluginBase
    Implements IPclPluginInstanceMenu

    Public Function GetInstanceMenuItems(Instance As PluginInstanceInfo) As IEnumerable(Of PluginMenuItem) _
        Implements IPclPluginInstanceMenu.GetInstanceMenuItems
        Return {
            New PluginMenuItem With {
                .Text = "导出实例信息（Demo 插件）",
                .Action = Sub() Host.Message("实例信息", Instance.Name & vbCrLf & Instance.Folder)
            }
        }
    End Function
End Class
```

- 每次打开右键菜单时调用一次，按当前实例动态返回；返回 `Nothing` 或空集合表示不加项（此时该实例仍是原版的「实例设置」右键）。
- 菜单文字建议带插件名前缀，避免和启动器自带的「重命名 / 打开文件夹 / 删除」混淆。
- `Action` 抛出的异常会被隔离并计入熔断（连续 3 次自动禁用），不会打断启动器。
- 完整示例见开发版 SDK 里的 `samples/InstanceMenu`（`instance-menu-demo`），它同时演示了后台任务进度与 `DevLog`。

### 6.9 发布与导入（v6.4）

插件仍然是「一个文件夹 + plugin.json + DLL」，但发布和安装时不必让用户手动解压：启动器支持**插件包**。

**打包**

- 推荐用 `tools\pack-plugin.ps1`：`powershell -NoProfile -File tools\pack-plugin.ps1 -Project samples\InstanceMenu`，会生成 `instance-menu-demo-1.0.0.pclplugin`（本质是 zip，根目录里放 `plugin.json` 与 DLL）。
- 也可以自己压 zip：根目录直接放 `plugin.json`，或者只套一层同名文件夹；`__MACOSX/`、`.DS_Store`、`Thumbs.db` 会被忽略。
- 不要打进 `data\`（用户数据）、`.backup\`、`bin\`、`obj\` 与 `plugin.pdb`。

**安装**

- 插件页「插件管理 → 导入插件…」支持多选；把 `.pclplugin`、含 `plugin.json` 的 zip、或单个 DLL **拖进启动器窗口**也可以（和拖整合包一样，会先弹「正在导入插件……」再汇总结果）。
- 重名会询问「要覆盖安装吗？」；覆盖安装会保留原来的 `data\`、插件设置与启用状态，只有权限（`permissions` 指纹）变化时才重新弹权限确认。
- 单 DLL 安装要求 DLL 里带 `AssemblyMetadata`：`PclPluginId`、`PclPluginName`、`PclPluginVersion`、`PclPluginAuthor`、`PclPluginDescription`（可选），否则会提示「找不到 plugin.json」。

**错误码**

| Code | 含义 |
| --- | --- |
| `ERR_NOT_ARCHIVE` | 不是有效的 zip / 文件损坏 |
| `ERR_NO_MANIFEST` | 包里找不到 `plugin.json` |
| `ERR_AMBIGUOUS_MANIFEST` | 包里有多份 `plugin.json`，无法判断用哪个 |
| `ERR_BAD_JSON` | `plugin.json` 解析失败 |
| `ERR_BAD_ID` | `id` 不符合 `^[a-z0-9][a-z0-9_\-\.]{0,63}$` |
| `ERR_MIN_API` | 插件要求的最低 API 版本高于当前启动器 |
| `ERR_SHA256` | 清单里 `sha256` 与 DLL 实际内容不一致 |
| `ERR_MISSING_DEP` | 依赖的插件不存在或版本不满足 |
| `ERR_EXISTS` | 同名插件已存在且未选择覆盖 |
| `ERR_UNSAFE_PATH` | 包内路径越界（`..` 之类） |
| `ERR_WRITE_FAILED` | 落盘失败（磁盘、权限、文件占用） |

**注意**

- 导入只做结构、路径、`sha256` 与依赖校验，**不做代码安全审计**：插件 DLL 会在启动器进程内运行，只安装你信任来源的插件。
- 签名验证已实现（v6.3 起，ECDsa P-256 + SHA256，见 6.7）；`signature` 无效或与清单不符的插件**会被拒绝加载**（v7.4 起）。插件 DLL 仍在启动器进程内运行，宿主不做代码审计，只安装你信任来源的插件。

### 6.9.1 单文件与打包（v6.4）

开发期照旧用文件夹（`plugin.json` + DLL + 资源），运行时会照常加载，不需要任何迁移。

需要分发时，SDK 提供两种产物：

- **单 DLL**：`tools\sdk\build.ps1 -Project <目录> -SingleFile`
  会把清单写进程序集元数据（`PclPluginId` / `PclPluginName` / `PclPluginVersion` / `PclPluginAuthor` / `PclPluginDescription` / `PclPluginPermissions`），产出的 DLL 可以直接拖进启动器导入。注意单文件模式无法携带额外资源文件，也不支持 `sha256` 自校验。
- **插件包 `.pclplugin`**：`tools\pack-plugin.ps1 -Project <目录>`，或编译时加 `-Pack` 一步完成。
  本质是 zip：根目录（或单层子目录）下有 `plugin.json` 即可；会跳过 `data\`、`.backup\`、`bin\`、`obj\`、`*.pdb`。

导入方式：插件页「导入插件…」按钮、把文件拖进启动器窗口，或用 `--import-plugin`（后续版本）。重名会询问是否覆盖；覆盖会保留 `data\`、设置与启用状态，只有权限变化时才重新弹确认框。
### 6.10 兼容性

- `ApiVersionUi = 6`，User-Agent 为 `PCLHarness-Plugin/6`。

**二进制兼容（3.6.8 起强制遵守）**

- 插件会调用的公开成员**不要**用「给已有方法加 Optional 参数」的方式扩展：`Optional` 只在编译期生效，CLR 签名会变，已编译的旧插件会抛 `MissingMethodException`。正确做法是新增重载，并保留旧签名的精确版本（例如 `PluginEventBus.On(String, Delegate)` 与 `On(String, Delegate, Integer, Boolean, String)` 并存）。
- 事件参数**不要**更换类型。历史上「页面切换」从 `String` 改成了 `Integer`，旧插件按 `(String, String)` 声明就崩了；现在事件总线会按处理器声明的参数类型自动适配，但新事件请直接给出稳定类型。
- 删除公开成员前先标 `Obsolete`，至少保留一个大版本。
- 回归样本：`F:\PCL2-Release\PCLHarness\Plugins\hello`（v5「你好插件」）与 `samples\V6Demo`、`samples\InstanceMenu`。
- v5 插件无需改动；`minApiVersion` 写 6 才会用到 v6 能力。

### 6.11 插件管理接口与动态网络（v6.4）

插件市场不内置进本体，而是做成一个普通插件；本体为它（也为将来的同类插件）补了两项能力。

**权限**

| 权限 | 说明 |
| --- | --- |
| `plugins` | 查看、安装、启用、禁用、卸载其他插件 |
| `net.dynamic` | 访问清单 `net.hosts` 之外的主机时，运行时弹窗申请；允许后长期有效，可撤销 |

两者都要写进 `plugin.json` 的 `minApiVersion: 6` 插件的 `permissions` 数组。

**Host.Plugins**

```vb
Dim All As List(Of PCL.PluginSummary) = Host.Plugins.List()
For Each One In All
    Host.LogInfo($"{One.DisplayName} v{One.Version} 启用={One.Enabled} 已加载={One.Loaded}")
Next

Dim Target = Host.Plugins.Find("pcl-harness-market")
Dim Result = Host.Plugins.ImportPackage("D:\downloads\foo.pclplugin", Overwrite:=True)
If Not Result.Success Then Host.LogWarn($"安装失败：{Result.Code} {Result.Message}")

Host.Plugins.SetEnabled("v6demo", False)
Host.Plugins.Uninstall("v6demo")

' 3.7.4 新增
Dim MktUrl = Host.Plugins.UpdateUrl("pcl-harness-market")   ' 清单里声明的 update 地址，没写就是空串
If Host.Plugins.CanRollback("pcl-harness-market") Then Host.Plugins.Rollback("pcl-harness-market")
Dim Hosts = Host.Plugins.ApprovedHosts("pcl-harness-market") ' 该插件已获准的动态主机
```

- `PluginSummary`：`Id`、`Name`、`Version`、`Author`、`Description`、`Enabled`、`Loaded`、`MinApiVersion`、`Permissions`（`List(Of String)`）、`DisplayName`，以及 3.7.4 新增的 `UpdateUrl`、`SignatureStatus`（`PluginSignatureStatus` 枚举）、`HasBackup`。
- `ImportPackage` 走的是启动器自己的导入管线（zip/单 DLL、`sha256` 校验、覆盖安装保留 `data\`、原子落盘），返回 `PluginImportResult`，`Code` 是 `ERR_*`。
- `SetEnabled` / `Uninstall` 内部会 `Refresh(True)`，**不要缓存 `PluginSummary`**，每次用完就丢。
- 插件不能卸载自己（会抛 `InvalidOperationException`）。
- `List` / `Find` 带锁，任何线程都能调；`ImportPackage` / `SetEnabled` / `Uninstall` 会读写磁盘，必须放到后台线程（例如 `Host.Tasks.Run`），不要在 UI 线程直接调。

**net.dynamic**

访问一个不在 `net.hosts` 里的主机时，宿主会弹一次窗：

```
插件「XXX」想访问网络主机：api.example.com
它声明的固定主机：raw.githubusercontent.com、ghfast.top
批准后这个主机对该插件长期有效，可以在「插件设置」里撤销。
```

- 允许 → 写入 `Plugins\trust.json` 的 `hosts` 数组，之后同主机不再询问。
- 拒绝 → 抛 `UnauthorizedAccessException`，事件记录里能看到「用户拒绝访问主机」。
- 授权记录与权限指纹分开存：插件升级后权限没变，已批准的主机继续有效。

**新事件**

| 事件 | 参数 |
| --- | --- |
| `插件安装` | `(pluginId As String, version As String)` |
| `插件卸载` | `(pluginId As String, version As String)` |

```vb
Host.Events.On("插件安装", Sub(PluginId As String, Version As String)
                              DevLog($"装好了：{PluginId} v{Version}")
                          End Sub)
```

**别做这些**（本体不提供，也不打算提供）

- 给插件用的「商场页」控件：市场插件自己用 `IPclPluginUi` 画页面。
- 灰度与静默自动更新：宿主只提供 `ImportPackage`（排队到重启）与 `Rollback`，谁该更新、什么时候更新由市场插件自己决定。
- 索引抓取助手：`net.dynamic` 只解决「能不能连」，索引结构校验由插件自己做；包本身的 `sha256` 由导入管线强制校验，发布者签名由宿主验（见 6.7）。

### 6.12 权限 v7：显式声明、风险分级与实例/日志接口（v7）

**行为变更（只影响 `minApiVersion: 7` 的新插件）**

- `permissions` 必须显式写。不写等于**没有任何权限**，`ui` / `events` / `files` / `net` / `args` / `plugins` 全部不可用。
- `minApiVersion` 为 5 或 6 的老插件**同样**要求声明 `permissions`：v7.4 起未声明即视为零权限（安全审计 V1），界面不再显示「按默认权限运行」。
- `ui` 现在真的拦：没有 `ui` 权限就不会采集你的页面、导航入口、设置项与卡片，事件记录里写「没有 ui 权限，界面扩展已跳过」。
- `events` 两条路径都拦：既不会调 `OnLoad` 之后的 `IPclPluginEvents` 回调，也不允许 `Host.Events.On/Once` 订阅任何事件（会记「没有 events 权限，订阅被拒绝：…」）。

**权限表**

| 权限 | 风险 | 说明 |
| --- | --- | --- |
| `ui` | 低 | 添加页面、标题栏入口、设置项与卡片 |
| `events` | 低 | 订阅启动器事件（含事件总线） |
| `files` | 低 | 按 `files.read` / `files.write` 白名单读写文件 |
| `net` | 低 | 访问 `net.hosts` 里声明的主机 |
| `args` | 低 | 追加 JVM / 游戏参数 |
| `models` 等原有权限 | 低 | 见 6.7 |
| `instance.read` | 低 | 读实例名称、加载器、版本、目录、模组数量 |
| `logs.read` | 低 | 读游戏日志与崩溃报告 |
| `skin` | **中** | 改离线皮肤（会改变游戏内外观） |
| `instance.install` | **中** | 往 mods / resourcepacks / shaderpacks / datapacks 写入 |
| `launch` | **中** | 主动启动游戏 |
| `account` | **中** | 读用户名、UUID、登录方式 |
| `settings.global` | **中** | 读启动器全局设置（只读） |
| `plugins` | 中 | 管理其他插件（见 6.11） |
| `net.dynamic` | 中 | 运行时申请 `net.hosts` 之外的主机 |

**风险窗分级：高风险红、中风险黄（v3.7.7 起，v3.7.8 起不再重复确认）**

1. 中风险与高风险都会先弹同一道「风险提示」窗（框体、按钮、文案结构一样），区别只在配色与后果说明：含 `process` 是**红色**窗、标题「插件高风险权限提示」；其余含中风险的是**黄色**窗、标题「插件权限风险提示」（由 `WarnLevel` 0/1/2 控制，只改标题色、分隔线、首个按钮描边与窗口背后的蒙版，弹窗底色与正文字色不变）。
2. 窗里逐条列出该等级及以上的权限（黄窗列全部中风险，红窗只列 `process`）；**「可能的后果」只对 `process` / `instance.install` / `account` / `args` / `plugins` 写**（后两者因能注入参数、能启用别的插件，v7.4 起也归中风险），`launch` / `settings.global` / `skin` / `instance.select` / `mods.write` 不写。
3. 确认后**不再弹**常规权限确认框——权限已经在风险窗里逐条列出，直接记入 `trust.json`（v3.7.8 起）。只有**低风险**插件（未声明任何中/高风险权限）才走常规确认框；v7.5 起确认框只保留「仍然启用」/「取消」两个按钮，点一次就记住这份权限指纹。
4. 启动时：**只有高风险**（`process`）会弹这道红窗并强制再确认一次；中风险不参与启动检查——它只在启用那一刻用黄窗提醒，避免每次开启动器都被打扰。点「取消」自动禁用这些插件。
5. `skipPrompt` 只对**常规确认框**生效，风险窗每次启动都会出现；权限一变（指纹变化）`skipPrompt` 自动失效。

**新接口**

```vb
' logs.read
Dim L = Host.ListLogs()                       ' PluginLogFileList：Files As List(Of PluginLogFile)
For Each F In L.Files
    Host.LogInfo($"{F.Kind} {F.Name} {F.Size} {F.Modified}")
Next
Dim Text = Host.ReadLog(L.Files(0).Path)      ' 最多读 2 MiB，超出截断并标记

' instance.read
Dim I = Host.InstanceInfo()                    ' Name/Loader/Version/Path/ModCount/IsModable
Host.LogInfo($"{I.Name} · {I.Loader} · {I.Version} · {I.ModCount} 个模组")

' skin（中风险）
Host.SetBuiltInSkin(2)                        ' 1 = Steve，2 = Alex
Host.SetLocalSkin("D:\pics\me.png")           ' 复制为 CustomSkin.png

' instance.install（中风险，只许四个目录）
Dim R = Host.InstallContent("mods", "D:\dl\jei.jar")
If Not R.Success Then Host.LogWarn(R.Message)

' launch（中风险）
Host.LaunchGame()

' account（中风险，只读）
Dim A = Host.AccountInfo()                    ' Name/Uuid/Kind

' settings.global（中风险，只读）
Dim Lang = Host.GetGlobalSetting("UiLanguage")
```

- 所有新接口都会先 `EnsurePermission`，无权限直接抛 `UnauthorizedAccessException`，同时写一条事件记录。
- `InstallContent` 的目标目录只接受 `mods`、`resourcepacks`、`shaderpacks`、`datapacks`，其它名字抛 `ArgumentException`；文件仍走 `files.write` 白名单校验。
- 日志路径固定在 `<实例目录>\logs\latest.log`、`logs\debug.log` 与 `crash-reports\*.txt`，不接受任意路径，避免变成万能读文件接口。

**示例清单**

```json
{
  "id": "crash-helper",
  "name": "崩溃分析助手",
  "version": "1.0.0",
  "entry": "CrashHelper.Plugin",
  "dll": "CrashHelper.Plugin.dll",
  "minApiVersion": 7,
  "permissions": ["logs.read", "instance.read", "ui"]
}
```

### 6.13 实例与内容管理（v7.3）

**新增权限**

| 权限 | 风险 | 说明 |
| --- | --- | --- |
| `instance.select` | **中** | 切换当前选中的游戏实例（改变之后「启动游戏」用的版本） |
| `mods.read` | 低 | 查看 mods / resourcepacks / shaderpacks 列表 |
| `mods.write` | **中** | 启用或禁用这些目录里的文件（会重命名成 `.disabled`） |

`instance.select` 与 `mods.write` 都走 v7.2 起的黄色风险窗，其中只有 `mods.write` 带「可能的后果」；`instance.select` 不写。

**新接口**

```vb
' instance.read：列出全部实例（不返回绝对路径，只给名字与元信息）
For Each I In Host.ListInstances()
    Host.LogInfo($"{I.Name} · {I.Folder} · {I.VersionId} · {If(I.IsSelected, "当前", "")}")
Next

' instance.read：当前选中的实例（没选版本时返回 Nothing）
Dim Cur = Host.SelectedInstance()
If Cur IsNot Nothing Then Host.LogInfo($"当前：{Cur.Name}")

' instance.select（中风险）：切到别的版本，成功返回 True 并提示一次
If Host.SelectInstance("1.20.1-Fabric") Then
    Host.Hint("已切到 1.20.1-Fabric")
End If

' mods.read：列模组 / 资源包 / 光影
For Each M In Host.ListMods()
    Host.LogInfo($"{M.FileName} · {M.Name} · {If(M.Enabled, "启用", "禁用")} · {M.Size} 字节")
Next
Dim Packs = Host.ListResourcePacks()
Dim Shaders = Host.ListShaderPacks()

' mods.write（中风险）：启用或禁用，FileName 直接用上面返回的字段
Host.SetModEnabled("jei-1.20.1-forge.jar", False)     ' 改名成 .disabled
Host.SetModEnabled("jei-1.20.1-forge.jar", True)      ' 改回原名
Host.SetResourcePackEnabled("Faithful.zip", False)
Host.SetShaderPackEnabled("BSL.zip", True)
```

**规则**

- 底层是 `ListContents(Kind, InstanceName)` / `SetContentEnabled(FileName, Enabled, Kind, InstanceName)`，`ListMods` 之类只是包装；`mods` 只认 `.jar`，`resourcepacks` / `shaderpacks` 只认 `.zip`。
- `FileName` 永远是不带 `.disabled` / `.old` 的原名，可以直接回传给 `SetContentEnabled`；条目里的 `Enabled` 表示当前是否生效。
- 禁用 = 重命名成 `.disabled`，启用 = 改回原名；如果同名文件已有另一份禁用副本，接口返回 `False` 并在事件记录里说明，**不会覆盖**。
- 所有接口都可以选 `InstanceName` 指向任意实例（默认当前实例）；目标目录不存在时会自动创建。
- 切换实例只改「下次启动用哪个版本」，不会自动启动游戏；要启动请另外调用 `LaunchGame()`（需要 `launch`）。
- `instance.read` 是 v7 就有的老权限，`ListInstances` / `SelectedInstance` 复用它，不需要额外声明；`mods.read` 只读，`mods.write` 才会改文件。

### 6.14 安全加固（v7.4）

这一节对应《插件安全审计》的修复项。**前提不变**：插件是加载到启动器进程里的 .NET 程序集，权限系统是「知情同意 + 声明式约束」，不是沙箱——它约束的是「插件按文档索取了什么、宿主替它挡住了什么」，而不是能阻止恶意代码。只安装你信任来源的插件。

**1. 不声明 = 零权限**

v7.4 起，`plugin.json` 没有 `permissions`（或写成空数组）时，插件**没有任何权限**，包括 `ui` / `events` / `files` / `net` / `args` / `plugins`；`minApiVersion` 为 5、6 的老插件同样如此。调用受保护的接口会抛 `UnauthorizedAccessException`，并在「事件记录」里留一条「调用了未授权的接口：…」。

**2. files 的边界**

- 对象写法里 `"read": []` 表示**不能读**、`"write": []` 表示**不能写**——空列表是「该方向一律拒绝」，不是「不限制」。键名拼错（`"reed"`）同样得到空列表，因此是失败而不是放行。
- 完全没有 `files` 权限时，只能访问插件自己的 `data\` 目录与当前实例目录；路径比较按目录边界做（`...\versions\1.20` 不会放行 `...\versions\1.20.1`）。

**3. net 的白名单**

- `"net": true` 或 `"net": "任意"` 这类标量形式 = **允许访问任意主机**（等同于 `hosts: ["*"]`），启用时会在确认框里写明。
- `"net": {"hosts": [...]}` 只认 `hosts`；**`hosts` 为空 = 拒绝一切请求**（键名拼错也是空列表）。
- `HttpDownload` 现在同样要求 `net` 权限；两种请求都**不再自动跟随重定向**，最多 5 跳，每一跳都重新校验白名单。
- 响应体积上限：`HttpGetString` 8 MB、`HttpDownload` 512 MB，超过即中止。

**4. 签名覆盖权限清单**

- 签名载荷从 `id + version + sha256(DLL)` 扩展为再拼接 `sha256(权限指纹)`，其中指纹已包含 `net.hosts` / `files.read` / `files.write` 的具体值——改清单里的权限或路径同样会让签名失效。
- 清单里写了 `signature` 但验签失败（文件被改过、公钥不匹配）时，插件**直接拒绝加载**，不再只是界面上的一个徽章。没有签名仍然可以加载，但会显示「未签名（来源需自行确认）」。
- `trust.json` 的键改为 `id|插件文件夹名`，同 `id` 的两个不同插件不再共享一次确认。

**5. 权限变化一定会重新确认**

v7.5 起确认框只有「仍然启用」/「取消」两个按钮，**点一次就把这份权限指纹记进 `trust.json`**；之后重启、禁用再启用都不会再问。指纹一变（权限集合或作用域不同）自动失效，重新询问。插件卡片上的「重新确认权限」可以手动撤销这条记录（`ModPlugin.RevokeTrust`），下次启用会重新询问。

**完全没写 `permissions` 的插件不弹确认框**（v7.5，安全审计 R5）：启用时只出一条提示「它无法读写文件、联网或启动游戏」，卡片 meta 标注「权限：未声明（零权限）」，也不会写 `trust.json`。

**6. 加载期校验**

- `plugin.json` 的 `dll` 只能是文件名（不含 `\` `/` `..`），且解析后的路径必须落在插件目录内，否则拒绝加载。
- 清单写了 `sha256` 而实际算不出哈希（文件被占用、权限不足）时，按**校验失败**处理并拒绝加载，不再静默跳过。

**7. 风险分级调整**

`args`（可注入 `-javaagent` 等启动参数）与 `plugins`（可安装/启用其它插件）从「无提示」提升为**中风险**：启用时走黄窗，并写明「可能的后果」。它们不会参与启动检查，只有 `process` 会在每次启动时弹红窗。

**8. 删除进回收站**

插件目录的 `.trash` / `.backup` 过期清理、以及导入新版本时修剪的旧回滚点，改为移入 Windows 回收站（`MeloongCode` 的 `DirectoryUtils.Delete(path, True)`，失败自动回退永久删除）；`.tmp` 之类的临时目录仍然直接删除。

**9. SDK 工具链**

- `tools\sdk\build.ps1`：优先使用 Visual Studio 自带的 vbc，`%USERPROFILE%\roslyn` 只作兜底并打印警告；`dll` 字段与输出路径做校验；单个清单字段含换行/制表符直接报错（防止 `-SingleFile` 生成元数据时注入代码）；`-Pack` 同时支持仓库布局与 SDK 压缩包布局；vbc 失败时把编译器原始错误打印出来。
- `tools\pack-plugin-sdk.ps1` / `tools\pack-plugin.ps1`：版本号与 `id` 先按正则校验再拼路径（`id` 只允许小写字母数字与 `. _ -`，最长 64）。

**对已有插件的影响**：如果旧插件依赖「不写权限就全能」，升级到 v7.4 后需要补写 `permissions`；只写了 `files` / `net` 而用了对象写法的，空列表的语义从「不限制」变成「禁止」，请显式写上允许的路径与主机。

## API v7（历史版本）

v7 的能力没有单独成章，散在 v6 的后续小节里：

- 6.12 权限 v7：显式声明、风险分级、实例与日志接口；
- 6.13 实例与内容管理（v7.3）；
- 6.14 安全加固（v7.4，含「不声明 = 零权限」与签名覆盖权限清单）。

v8 继续兼容这些接口，v7 及更早的插件无需改代码即可加载。

## API v8（当前版本）

v8 只改一件事：**内置功能页默认不再随启动器提供**。`1 = 下载`、`2 = 联机` 两页的正文仍是启动器原版实现，只是默认隐藏、页面不可达；由插件显式启用后，标题栏入口才会出现。

### Host 成员（`PluginHost`，v8 新增）

| 成员 | 签名 | 作用 |
| --- | --- | --- |
| `EnableBuiltInPage` | `Sub EnableBuiltInPage(Page As Integer)` | 启用内置页，归属记为当前插件；插件被禁用 / 卸载时按归属回收 |
| `DisableBuiltInPage` | `Sub DisableBuiltInPage(Page As Integer)` | 停用内置页，入口隐藏并回到启动页 |
| `IsBuiltInPageEnabled` | `Function IsBuiltInPageEnabled(Page As Integer) As Boolean` | 查询该页当前是否可用 |

`Page` 只认 1（下载）和 2（联机）；其它值直接忽略，`IsBuiltInPageEnabled` 对其它值返回 `True`。

### 底层接口（`ModPlugin`）

- `SetBuiltInPageEnabled(Page As Integer, Enabled As Boolean, Optional OwnerId As String = "")`
- `IsBuiltInPageEnabled(Page As Integer) As Boolean`

`OwnerId` 记录「这页是谁开的」：插件被禁用或卸载时，宿主只回收它自己开的那一页，不会把别的插件开的入口一起关掉。用 `PluginHost` 的封装即可，不需要自己传 `OwnerId`。

### 兼容性

- 用到这些接口的插件，`minApiVersion` 建议声明为 8：声明 7 的插件在老启动器上也能装，但那时宿主没有这些成员，运行到会 `MissingMethodException`；声明 8 则老启动器会直接拒绝加载并提示 API 版本过低。
- 内置页开关与「下载页面」「联机页面」这类插件页是两回事：后者是插件自己的页面，不受该开关影响。
- 已有 v7 插件照常加载；如果它依赖「下载 / 联机」页存在，需要显式启用（或在说明里提示用户安装对应的插件页）。
