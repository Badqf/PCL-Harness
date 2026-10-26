Imports System.Collections.Generic

''' <summary>
''' PCL Harness 示例插件：演示 v4 的能力——内嵌 UI（IPclPluginUi2）、生命周期/下载/联机事件、文件 API，以及 v3 的事件钩子（IPclPluginEvents）、
''' 三种设置项（勾选 / 数字 / 下拉），以及 v2 的三个 UI 扩展点（页面 / 标题栏入口 / 设置项）。
''' 编译见同目录 build.ps1，产物 Demo.Plugin.dll 放到 PCL\Plugins\demo-harness\ 下。
''' </summary>
Public Class DemoPlugin
    Implements PCL.IPclPlugin
    Implements PCL.IPclPluginUi
    Implements PCL.IPclPluginEvents
    Implements PCL.IPclPluginUi2
    Implements PCL.IPclPluginLifecycle
    Implements PCL.IPclPluginDownload
    Implements PCL.IPclPluginLink

    '演示页面和标题栏入口默认关闭：启用后会往顶栏塞一个「示例」按钮、并在插件页多出页面卡片，
    '想恢复整套演示时把下面的 False 改成 True 重新编译即可。
#Const DemoPageEnabled = False

    Private Host As PCL.PluginHost
    Private LoadCount As Integer
    Private PageInfo As PCL.PluginPageInfo

    Public Sub OnLoad(Host As PCL.PluginHost) Implements PCL.IPclPlugin.OnLoad
        Me.Host = Host
        LoadCount += 1
        Host.Log("示例插件已加载（第 " & LoadCount & " 次），数据目录：" & Host.DataFolder)
        If Host.HasPermission("file") Then
            Dim DataFile As String = System.IO.Path.Combine(Host.DataFolder, "demo-state.txt")
            Host.WriteTextFile(DataFile, $"last load = {Date.Now:yyyy-MM-dd HH:mm:ss}")
            Host.Log("file 权限演示，文件内容：" & Host.ReadTextFile(DataFile))
        Else
            Host.Log("未声明 file 权限，跳过文件读写演示")
        End If
        If GetBool("showHintOnLoad", False) Then
            Host.Hint("示例插件已启用：插件页的「扩展点」里能看到它注册的设置项。")
        End If
    End Sub

    Public Sub OnUnload() Implements PCL.IPclPlugin.OnUnload
        If Host IsNot Nothing Then Host.Log("示例插件已卸载。")
        Host = Nothing
    End Sub

    ' ==== v3 事件钩子：实现了 IPclPluginEvents，就会在对应时机被调用 ====

    ''' <summary>启动器初始化完成、插件全部加载后调用一次。</summary>
    Public Sub OnLauncherReady() Implements PCL.IPclPluginEvents.OnLauncherReady
        If Host Is Nothing Then Return
        Host.Log("收到事件：启动器就绪")
        '演示 RunLater：两秒后回 UI 线程再弹提示（默认关闭，去设置里把「加载时弹出提示」勾上才看得到）
        If GetBool("showHintOnLoad", False) Then
            Host.RunLater(1200, Sub() Host.Hint("来自 RunLater 的延时提示"))
        End If
    End Sub

    ''' <summary>主页面切换完成时调用。Page 是 FormMain.PageType 的数值。</summary>
    Public Sub OnPageChanged(Page As Integer, SubType As Integer) Implements PCL.IPclPluginEvents.OnPageChanged
        If Host Is Nothing Then Return
        Host.Log($"收到事件：页面切换到 {Page}（子类型 {SubType}）")
    End Sub

    ''' <summary>构建启动参数时调用，可以往 JVM / 游戏参数里追加内容。</summary>
    Public Sub OnGameLaunching(Info As PCL.PluginLaunchInfo) Implements PCL.IPclPluginEvents.OnGameLaunching
        If Host Is Nothing Then Return
        Host.Log($"收到事件：准备启动 {Info.InstanceName}（玩家 {Info.PlayerName}）")
        If GetBool("injectArgs", False) Then
            Info.ExtraJvmArgs.Add("-Dpcl.demo.plugin=true")
            Info.ExtraGameArgs.Add("--pcl-demo")
            Host.Log("已追加演示参数：-Dpcl.demo.plugin=true 与 --pcl-demo")
        End If
    End Sub

    ''' <summary>游戏进程刚启动后调用，Info.ProcessId 此时可用。</summary>
    Public Sub OnGameLaunched(Info As PCL.PluginLaunchInfo) Implements PCL.IPclPluginEvents.OnGameLaunched
        If Host Is Nothing Then Return
        Host.Log($"收到事件：游戏已启动，PID = {Info.ProcessId}")
    End Sub

    ''' <summary>注册一个自定义页面（默认关闭，见文件头的 DemoPageEnabled）。</summary>
    Public Function GetPages() As IEnumerable(Of PCL.PluginPageInfo) Implements PCL.IPclPluginUi.GetPages
#If DemoPageEnabled Then
        PageInfo = New PCL.PluginPageInfo With {
            .Id = "demo.home",
            .Title = "示例页面",
            .Factory = Function() New DemoPage(Me)
        }
        Return New List(Of PCL.PluginPageInfo) From {PageInfo}
#Else
        Return New List(Of PCL.PluginPageInfo)()
#End If
    End Function

    ''' <summary>注册一个标题栏入口（默认关闭）。</summary>
    Public Function GetNavItems() As IEnumerable(Of PCL.PluginNavItem) Implements PCL.IPclPluginUi.GetNavItems
#If DemoPageEnabled Then
        Return New List(Of PCL.PluginNavItem) From {
            New PCL.PluginNavItem With {
                .Text = "示例",
                .Icon = "M2,2 h9 v9 h-9 z M13,2 h9 v9 h-9 z M2,13 h9 v9 h-9 z M13,13 h9 v9 h-9 z",
                .Page = PageInfo
            }
        }
#Else
        Return New List(Of PCL.PluginNavItem)()
#End If
    End Function

    ''' <summary>注册设置项（页面关闭时只保留「加载时弹出提示」）。</summary>
    Public Function GetSettings() As IEnumerable(Of PCL.PluginSetting) Implements PCL.IPclPluginUi.GetSettings
        Dim Items As New List(Of PCL.PluginSetting) From {
            New PCL.PluginSetting With {
                .Key = "showHintOnLoad",
                .Title = "加载时弹出提示",
                .Description = "默认关闭。开启后，本插件每次加载都会在左下角弹一条提示（仅用于演示 Host.Hint）。",
                .SettingType = PCL.PluginSettingType.Boolean,
                .DefaultValue = False
            },
            New PCL.PluginSetting With {
                .Key = "injectArgs",
                .Title = "启动时注入演示参数",
                .Description = "开启后，启动游戏时会给 JVM 加 -Dpcl.demo.plugin=true、给游戏加 --pcl-demo（演示 OnGameLaunching）。",
                .SettingType = PCL.PluginSettingType.Boolean,
                .DefaultValue = False
            },
            New PCL.PluginSetting With {
                .Key = "logTimes",
                .Title = "事件日志上限",
                .Description = "设置项演示：整数类型，范围 1 ~ 5，超出会自动夹紧到端点。",
                .SettingType = PCL.PluginSettingType.Number,
                .DefaultValue = 3,
                .MinValue = 1,
                .MaxValue = 5
            },
            New PCL.PluginSetting With {
                .Key = "pageTheme",
                .Title = "提示颜色",
                .Description = "设置项演示：下拉类型，选中后立即写入 plugins.settings.json。",
                .SettingType = PCL.PluginSettingType.Choice,
                .DefaultValue = "蓝",
                .Options = New String() {"蓝", "紫", "绿"}
            }
        }
#If DemoPageEnabled Then
        Items.Add(New PCL.PluginSetting With {
            .Key = "greeting",
            .Title = "示例页面问候语",
            .Description = "显示在「示例页面」顶部的一句话。",
            .SettingType = PCL.PluginSettingType.Text,
            .DefaultValue = "你好，这里是插件注册的页面。"
        })
#End If
        Return Items
    End Function

    ' ==== v4：内嵌 UI（主页卡片 / 设置卡片 / 状态栏小组件） ====

    ''' <summary>注册「启动」页卡片。</summary>
    Public Function GetHomeCards() As IEnumerable(Of PCL.PluginCardInfo) Implements PCL.IPclPluginUi2.GetHomeCards
        Dim Card As New PCL.PluginCardInfo With {
            .Id = "demo.home.card",
            .Title = "示例插件卡片",
            .Description = "由示例插件在「启动」页渲染的自定义卡片。",
            .Factory = Function()
                           Dim Root As New System.Windows.Controls.StackPanel
                           Dim Txt As New System.Windows.Controls.TextBlock With {
                               .Text = "这段内容是插件直接构建的 WPF 控件，颜色会跟随启动器主题。",
                               .FontSize = 13,
                               .TextWrapping = System.Windows.TextWrapping.Wrap
                           }
                           Txt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ColorBrushGray2")
                           Root.Children.Add(Txt)
                           Dim Btn As New PCL.MyButton With {
                               .Text = "点我",
                               .MinWidth = 90,
                               .Height = 28,
                               .HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                               .Margin = New System.Windows.Thickness(0, 10, 0, 0)
                           }
                           AddHandler Btn.Click, Sub() Host.ShowNotification("示例卡片按钮被点击")
                           Root.Children.Add(Btn)
                           Return Root
                       End Function
        }
        Return New List(Of PCL.PluginCardInfo) From {Card}
    End Function

    ''' <summary>注册「设置」页卡片。</summary>
    Public Function GetSettingsCards() As IEnumerable(Of PCL.PluginCardInfo) Implements PCL.IPclPluginUi2.GetSettingsCards
        Dim Card As New PCL.PluginCardInfo With {
            .Id = "demo.settings.card",
            .Title = "示例插件设置说明",
            .Factory = Function()
                           Dim Txt As New System.Windows.Controls.TextBlock With {
                               .Text = "插件可以把自己的配置界面直接嵌进设置页，而不必只靠通用设置项。",
                               .FontSize = 13,
                               .TextWrapping = System.Windows.TextWrapping.Wrap
                           }
                           Txt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ColorBrushGray2")
                           Return Txt
                       End Function
        }
        Return New List(Of PCL.PluginCardInfo) From {Card}
    End Function

    ''' <summary>注册状态栏小组件，约每秒刷新一次。</summary>
    Public Function GetStatusWidgets() As IEnumerable(Of PCL.PluginStatusWidget) Implements PCL.IPclPluginUi2.GetStatusWidgets
        Dim Widget As New PCL.PluginStatusWidget With {
            .Id = "demo.status",
            .TextProvider = Function() "示例插件 · " & If(Host Is Nothing, "未加载", Host.InstanceName)
        }
        Return New List(Of PCL.PluginStatusWidget) From {Widget}
    End Function

    ' ==== v4：生命周期、下载与联机事件 ====

    Public Sub OnGameExited(Info As PCL.PluginLaunchInfo, ExitCode As Integer) Implements PCL.IPclPluginLifecycle.OnGameExited
        If Host Is Nothing Then Return
        Host.Log($"收到事件：游戏退出，{Info.InstanceName} 退出码 {ExitCode}")
    End Sub

    Public Sub OnInstanceChanged(InstanceName As String) Implements PCL.IPclPluginLifecycle.OnInstanceChanged
        If Host Is Nothing Then Return
        Host.Log($"收到事件：当前版本切换为 {InstanceName}")
    End Sub

    Public Sub OnThemeChanged(IsDark As Boolean) Implements PCL.IPclPluginLifecycle.OnThemeChanged
        If Host Is Nothing Then Return
        Host.Log("收到事件：主题切换为 " & If(IsDark, "深色", "浅色"))
    End Sub

    Public Sub OnDownloadStarted(Target As String) Implements PCL.IPclPluginDownload.OnDownloadStarted
        If Host Is Nothing Then Return
        Host.Log($"收到事件：开始下载 {Target}")
    End Sub

    Public Sub OnDownloadFinished(Target As String, Success As Boolean) Implements PCL.IPclPluginDownload.OnDownloadFinished
        If Host Is Nothing Then Return
        Host.Log($"收到事件：下载结束 {Target}，成功 = {Success}")
    End Sub

    Public Sub OnLinkRoomCreated(Invite As String) Implements PCL.IPclPluginLink.OnLinkRoomCreated
        If Host Is Nothing Then Return
        Host.Log("收到事件：房间已创建，邀请码 " & Invite)
    End Sub

    Public Sub OnLinkRoomJoined() Implements PCL.IPclPluginLink.OnLinkRoomJoined
        If Host Is Nothing Then Return
        Host.Log("收到事件：已加入房间")
    End Sub

    Public Sub OnLinkRoomClosed() Implements PCL.IPclPluginLink.OnLinkRoomClosed
        If Host Is Nothing Then Return
        Host.Log("收到事件：房间已关闭")
    End Sub

    ''' <summary>读设置（带默认值）。</summary>
    Friend Function GetBool(Key As String, DefaultValue As Boolean) As Boolean
        If Host Is Nothing Then Return DefaultValue
        Dim V = PCL.ModPlugin.GetSetting(Host.Info, Key, DefaultValue)
        If V Is Nothing Then Return DefaultValue
        Return CBool(V)
    End Function

    Friend Function GetText(Key As String, DefaultValue As String) As String
        If Host Is Nothing Then Return DefaultValue
        Dim V = PCL.ModPlugin.GetSetting(Host.Info, Key, DefaultValue)
        If V Is Nothing Then Return DefaultValue
        Return V.ToString()
    End Function

End Class

''' <summary>插件注册的页面：一个带按钮的简单用户控件。</summary>
Public Class DemoPage
    Inherits System.Windows.Controls.UserControl

    Private ReadOnly Owner As DemoPlugin

    Public Sub New(Owner As DemoPlugin)
        Me.Owner = Owner
        Dim Root As New System.Windows.Controls.StackPanel With {
            .Margin = New System.Windows.Thickness(25, 20, 25, 30)
        }
        Dim Title As New System.Windows.Controls.TextBlock With {
            .Text = "示例页面",
            .FontSize = 22,
            .Margin = New System.Windows.Thickness(0, 0, 0, 6)
        }
        Title.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ColorBrush1")
        Root.Children.Add(Title)

        Dim Desc As New System.Windows.Controls.TextBlock With {
            .Text = Owner.GetText("greeting", "你好，这里是插件注册的页面。"),
            .FontSize = 13,
            .TextWrapping = System.Windows.TextWrapping.Wrap,
            .Margin = New System.Windows.Thickness(0, 0, 0, 14)
        }
        Desc.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ColorBrushGray2")
        Root.Children.Add(Desc)

        Dim Btn As New PCL.MyButton With {
            .Text = "点我试试",
            .MinWidth = 110,
            .Height = 32,
            .HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        }
        AddHandler Btn.Click, Sub(s, e) PCL.ModMain.Hint("这是插件页面里的按钮：插件可以自己构建界面与交互。", PCL.HintType.Blue, True)
        Root.Children.Add(Btn)

        Content = Root
    End Sub

End Class
