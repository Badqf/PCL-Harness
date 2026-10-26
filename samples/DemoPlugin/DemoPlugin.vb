Imports System.Collections.Generic

''' <summary>
''' PCL Harness 示例插件：演示 v2 的三个扩展点（自定义页面 / 标题栏入口 / 设置项）。
''' 编译见同目录 build.ps1，产物 Demo.Plugin.dll 放到 PCL\Plugins\demo-harness\ 下。
''' </summary>
Public Class DemoPlugin
    Implements PCL.IPclPlugin
    Implements PCL.IPclPluginUi

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
        If GetBool("showHintOnLoad", False) Then
            Host.Hint("示例插件已启用：插件页的「扩展点」里能看到它注册的设置项。")
        End If
    End Sub

    Public Sub OnUnload() Implements PCL.IPclPlugin.OnUnload
        If Host IsNot Nothing Then Host.Log("示例插件已卸载。")
        Host = Nothing
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
