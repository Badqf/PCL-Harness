Imports System
Imports PCL

''' <summary>最小可运行插件：加载时弹一条提示，卸载时写一行日志。</summary>
Public Class HelloPlugin
    Implements IPclPlugin

    Public Sub OnLoad(Host As PluginHost) Implements IPclPlugin.OnLoad
        Host.Log("Hello 已加载，数据目录：" & Host.DataFolder)
        Host.Hint("你好，PCL Harness！", HintType.Green)
    End Sub

    Public Sub OnUnload() Implements IPclPlugin.OnUnload
        '清理资源，一般留空
    End Sub
End Class
