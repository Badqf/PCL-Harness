Imports System
Imports System.Threading

Namespace V6Demo
    Public Interface IGreeter
        Function Greet(Name As String) As String
    End Interface

    Public Class Greeter
        Implements IGreeter
        Public Function Greet(Name As String) As String Implements IGreeter.Greet
            Return "你好，" & Name
        End Function
    End Class

    Public Class V6DemoPlugin
        Inherits PCL.PclPluginBase

        Public Overrides Sub OnLoad(Host As PCL.PluginHost)
            MyBase.OnLoad(Host)
            DevLog("V6 演示插件开始加载")
            Host.Services.Register(Of IGreeter)(New Greeter())
            DevLog("已注册服务 IGreeter，现在读回：" & Host.Services.Get(Of IGreeter)().Greet("PCL"))
            Host.Events.On("页面切换", Sub(Page As Integer, SubType As Integer)
                                         DevLog($"收到页面切换事件：page={Page}")
                                     End Sub, Priority:=10)
            Host.Events.Once("启动器就绪", Sub() DevLog("收到一次性的「启动器就绪」事件"))
            Host.Events.On("游戏启动前", Sub(Info As PCL.PluginLaunchInfo)
                                           Info.ExtraJvmArgs.Add("-Dv6demo=1")
                                           DevLog("已往 JVM 参数里追加 -Dv6demo=1")
                                       End Sub)
            Host.Tasks.Run("v6 演示任务", Sub(Ctx As PCL.PluginTaskContext)
                                                For i = 1 To 10
                                                    Ctx.ThrowIfCancelled()
                                                    Ctx.Report(i / 10.0, $"进度 {i}/10")
                                                    Thread.Sleep(400)
                                                Next
                                                DevLog("后台任务完成")
                                            End Sub)
            DevLog("V6 演示插件加载完成")
        End Sub

        Public Overrides Sub OnUnload()
            DevLog("V6 演示插件卸载")
            MyBase.OnUnload()
        End Sub
    End Class
End Namespace