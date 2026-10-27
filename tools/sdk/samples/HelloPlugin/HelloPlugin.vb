Imports System
Imports PCL

''' <summary>API v5 示例插件：日志分级、事件订阅、强类型设置、网络白名单。</summary>
Public Class HelloPlugin
    Implements IPclPlugin

    Private HostRef As PluginHost

    Public Sub OnLoad(Host As PluginHost) Implements IPclPlugin.OnLoad
        HostRef = Host
        Host.LogInfo($"Hello 插件已加载，数据目录：{Host.DataFolder}")
        Host.LogDebug("调试日志只写启动器日志，不会弹提示")

        '订阅启动器事件（v5）：页面切换时收到页面名与子类型
        Host.Events.On("页面切换", New Action(Of String, String)(
            Sub(Page, SubType)
                Host.LogDebug($"页面切换：{Page} / {SubType}")
            End Sub))

        '强类型设置（v5）：读不到就用默认值，写入会自动保存
        Dim Count As Integer = Host.GetSetting("loadCount", 0) + 1
        Host.SetSetting("loadCount", Count)
        Host.LogInfo($"这是第 {Count} 次加载")

        Host.Hint("你好，PCL Harness！", HintType.Green)
    End Sub

    Public Sub OnUnload() Implements IPclPlugin.OnUnload
        '清理资源
    End Sub

    ''' <summary>示例：按 plugin.json 的 permissions.net.hosts 白名单访问网络。</summary>
    Public Function FetchExample() As String
        Try
            Return HostRef.HttpGetString("https://api.github.com/zen")
        Catch ex As Exception
            HostRef.LogWarn("请求失败：" & ex.Message)
            Return ""
        End Try
    End Function
End Class