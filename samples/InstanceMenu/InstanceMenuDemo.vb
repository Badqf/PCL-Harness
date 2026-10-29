' PCL Harness 示例插件：实例右键菜单（API v6.2）
' 这个示例演示三件事：
'   1. 实现 IPclPluginInstanceMenu，往「启动」页实例列表的右键菜单里加自己的操作；
'   2. 用基类 PclPluginBase 省掉一堆空实现；
'   3. 用 Host.Tasks.Run 在后台做一件事，进度会显示在右下角「任务管理」里。

Imports System.Collections.Generic

Namespace InstanceMenuDemo

    Public Class DemoPlugin
        Inherits PCL.PclPluginBase
        Implements PCL.IPclPluginInstanceMenu

        ' ===== 1. 实例右键菜单 =====
        ' 每次打开右键菜单都会被调用一次，可以按当前实例返回不同的项。
        Public Function GetInstanceMenuItems(Instance As PCL.PluginInstanceInfo) As IEnumerable(Of PCL.PluginMenuItem) _
            Implements PCL.IPclPluginInstanceMenu.GetInstanceMenuItems

            Return {
                New PCL.PluginMenuItem With {
                    .Text = "复制实例路径（示例插件）",
                    .Action = Sub()
                                  ' Action 里抛异常不会弄崩启动器：会被隔离并计入熔断。
                                  PCL.ModBase.ClipboardSet(Instance.Folder, False)
                                  Host.Hint("已复制：" & Instance.Folder)
                              End Sub
                },
                New PCL.PluginMenuItem With {
                    .Text = "实例信息（示例插件）",
                    .Action = Sub()
                                  Host.Message("实例信息", "名称：" & Instance.Name & vbCrLf &
                                                        "路径：" & Instance.Folder)
                              End Sub
                }
            }
        End Function

        ' ===== 2. 后台任务 =====
        Public Overrides Sub OnLauncherReady()
            Host.Tasks.Run("示例插件：清点实例", Sub(Ctx)
                                                   For i As Integer = 1 To 5
                                                       Ctx.ThrowIfCancelled()
                                                       Ctx.Report(i / 5, $"正在清点 {i}/5")
                                                       System.Threading.Thread.Sleep(200)
                                                   Next
                                                   Host.LogInfo("示例插件：清点完成，右键实例可以看到插件菜单项。")
                                               End Sub)
        End Sub

        ' ===== 3. 事件订阅（v6 事件总线，可删） =====
        Public Overrides Sub OnPageChanged(Page As Integer, SubType As Integer)
            Host.DevLog($"进入页面 {Page}/{SubType}")
        End Sub

    End Class

End Namespace
