'PCL Harness 介绍页
'在启动时展示本启动器与原版 PCL2 的区别与能力，可在 设置 → 启动 中重新打开
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports System.Windows.Shapes
Imports Path = System.IO.Path

Public Module ModIntro

    ''' <summary>
    ''' 若设置为显示介绍页，则弹出介绍窗口。
    ''' </summary>
    Public Sub ShowIfNeeded()
        If Not Settings.Get(Of Boolean)("HarnessShowIntro") Then Return
        ModBase.RunInUi(
            Sub()
                Try
                    Dim Win As New IntroWindow
                    If FrmMain IsNot Nothing AndAlso FrmMain.IsLoaded Then Win.Owner = FrmMain
                    Win.ShowDialog()
                Catch ex As Exception
                    Logger.Error(ex, "显示介绍页失败", LogBehavior.None)
                End Try
            End Sub)
    End Sub

    Public Class IntroWindow
        Inherits Window

        Public Sub New()
            '窗口自身
            WindowStyle = WindowStyle.None
            AllowsTransparency = True
            Background = Brushes.Transparent
            ResizeMode = ResizeMode.NoResize
            WindowStartupLocation = WindowStartupLocation.CenterScreen
            ShowInTaskbar = False
            SizeToContent = SizeToContent.Manual
            UseLayoutRounding = True
            '尺寸跟随主窗口：主窗口较小时不再顶满屏幕，内容改为竖向滚动
            Dim OwnerW As Double = If(FrmMain IsNot Nothing AndAlso FrmMain.ActualWidth > 240, FrmMain.ActualWidth, 900)
            Dim OwnerH As Double = If(FrmMain IsNot Nothing AndAlso FrmMain.ActualHeight > 220, FrmMain.ActualHeight, 620)
            Dim WorkArea = SystemParameters.WorkArea
            Width = Math.Max(360, Math.Min(Math.Min(540, OwnerW - 96), WorkArea.Width * 0.85))
            Height = Math.Max(300, Math.Min(Math.Min(430, OwnerH - 110), WorkArea.Height * 0.82))
            '背板
            Dim Root As New Border With {
                .CornerRadius = New CornerRadius(9),
                .Margin = New Thickness(12),
                .Padding = New Thickness(18, 14, 18, 12)
            }
            Root.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
            Content = Root
            Dim Shadow As New Effects.DropShadowEffect With {.BlurRadius = 14, .ShadowDepth = 3, .RenderingBias = Effects.RenderingBias.Performance, .Opacity = 0.35}
            Shadow.Color = Color.FromArgb(90, 0, 0, 0)
            Root.Effect = Shadow

            '内容区竖向滚动，按钮固定在底部（与内置弹窗的滚动条用法一致）
            Dim Main As New StackPanel
            Dim Scroller As New MyScrollViewer With {
                .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                .Content = Main,
                .Padding = New Thickness(2, 0, 12, 0),
                .DeltaMult = 0.7
            }
            Dim Layout As New Grid
            Layout.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
            Layout.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            Grid.SetRow(Scroller, 0)
            Layout.Children.Add(Scroller)
            Root.Child = Layout

            AddTitle(Main, "欢迎使用 PCL Harness")
            AddPara(Main, "基于 Plain Craft Launcher 2 源码二次开发的自用启动器。保留原版全部功能，同时做了面向本地的改造，并把界面与行为开放给插件。", 12, 18, New Thickness(1, 8, 1, 6))

            AddHead(Main, "和原版 PCL2 的区别")
            AddItem(Main, "独立配置目录", "数据统一放在 PCLHarness 文件夹，与原版 PCL 互不覆盖；首次启动可一键迁移旧设置与插件。")
            AddItem(Main, "外观可切换", "深色 / 浅色外观；主色调限定在蓝色系（合规要求），另有 4 个预设一键切换。")
            AddItem(Main, "插件框架（API v6）", "事件总线、服务注册、后台任务、崩溃熔断；可添加页面、顶栏入口与设置项。")
            AddItem(Main, "联机可完全直连", "P2P 直连：房主把「地址|邀请码」发给朋友即可，公网可用 UPnP 自动映射。")

            AddHead(Main, "能做什么")
            AddItem(Main, "下载与整理游戏", "游戏版本、Mod、整合包、资源包、光影，支持镜像优先与自动安装。")
            AddItem(Main, "联机开房", "建房并复制邀请，或输入邀请码加入；局域网可直接连。")
            AddItem(Main, "用插件扩展", "插件放进 PCLHarness\Plugins 即可加载；开发文档与示例见 Release 附件的开发版 SDK。")
            AddItem(Main, "原版能力照旧", "版本隔离、内存分配、Java 管理、自定义主页等全部保留。")


            '按钮
            Dim Bars As New StackPanel With {
                .Orientation = Orientation.Horizontal,
                .HorizontalAlignment = HorizontalAlignment.Right,
                .Margin = New Thickness(0, 10, 0, 0)
            }
            Dim BtnOpen As New MyButton With {.Text = "打开配置目录", .Height = 30, .MinWidth = 108, .Margin = New Thickness(8, 0, 0, 0)}
            Dim BtnOk As New MyButton With {.Text = "我知道了", .Height = 30, .MinWidth = 84, .Margin = New Thickness(8, 0, 0, 0), .ColorType = MyButton.ColorState.Highlight}
            Dim BtnNever As New MyButton With {.Text = "不再显示", .Height = 30, .MinWidth = 84, .Margin = New Thickness(8, 0, 0, 0)}
            AddHandler BtnOpen.Click, Sub()
                                          Try
                                              Process.Start("explorer.exe", ModFirstRun.PathNewBase)
                                          Catch ex As Exception
                                              Logger.Error(ex, "打开配置目录失败", LogBehavior.None)
                                          End Try
                                      End Sub
            AddHandler BtnOk.Click, Sub() ModDialogFx.CloseAnimated(Me)
            AddHandler BtnNever.Click, Sub()
                                           Settings.Set("HarnessShowIntro", False)
                                           Hint("已关闭启动时的介绍页，可在 设置 → 启动 中重新打开")
                                           ModDialogFx.CloseAnimated(Me)
                                       End Sub
            Bars.Children.Add(BtnOpen)
            Bars.Children.Add(BtnOk)
            Bars.Children.Add(BtnNever)
            Grid.SetRow(Bars, 1)
            Layout.Children.Add(Bars)

            AddHandler PreviewKeyDown, Sub(s, e)
                                           If e.Key = Input.Key.Escape Then
                                               ModDialogFx.CloseAnimated(Me)
                                           ElseIf e.Key = Input.Key.Enter Then
                                               ModDialogFx.CloseAnimated(Me)
                                           End If
                                       End Sub
            '与内置弹窗一致的淡入动画 + 主窗口背景调暗
            ModDialogFx.RegisterDialog(Me, Root)
        End Sub

        Private Shared Sub AddTitle(Parent As Panel, Text As String)
            Dim T As New TextBlock With {.Text = Text, .FontSize = 19, .Margin = New Thickness(2, 0, 0, 7)}
            T.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Parent.Children.Add(T)
            Dim Line As New Rectangle With {.Height = 2, .Fill = T.Foreground}
            Parent.Children.Add(Line)
        End Sub

        Private Shared Sub AddHead(Parent As Panel, Text As String)
            Dim T As New TextBlock With {.Text = Text, .FontSize = 13.5, .FontWeight = FontWeights.SemiBold, .Margin = New Thickness(1, 14, 1, 5)}
            T.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Parent.Children.Add(T)
        End Sub

        Private Shared Sub AddPara(Parent As Panel, Text As String, Size As Double, LineHeight As Double, Margin As Thickness, Optional Opacity As Double = 0.85)
            Dim T As New TextBlock With {.Text = Text, .FontSize = Size, .LineHeight = LineHeight, .TextWrapping = TextWrapping.Wrap, .Margin = Margin, .Opacity = Opacity}
            T.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Parent.Children.Add(T)
        End Sub

        Private Shared Sub AddItem(Parent As Panel, Title As String, Desc As String)
            Dim Row As New StackPanel With {.Margin = New Thickness(1, 0, 1, 8)}
            Dim T As New TextBlock With {.Text = "· " & Title, .FontSize = 12.5}
            T.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Dim D As New TextBlock With {.Text = Desc, .FontSize = 11, .LineHeight = 16, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(11, 1, 0, 0), .Opacity = 0.78}
            D.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Row.Children.Add(T)
            Row.Children.Add(D)
            Parent.Children.Add(Row)
        End Sub

    End Class

End Module
