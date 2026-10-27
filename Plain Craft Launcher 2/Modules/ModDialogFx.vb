'PCL Harness 独立窗口的弹窗动效
'让首次启动指引、介绍页这类独立窗口拥有与内置弹窗（MyMsgText）一致的淡入、位移、旋转，并把主窗口背景调暗
Imports System.Collections.Generic
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Public Module ModDialogFx

    Private Class FxState
        Public Root As Border
        Public Rotate As RotateTransform
        Public Translate As TranslateTransform
        Public Dimmed As Boolean
        Public Closing As Boolean
    End Class

    Private ReadOnly States As New Dictionary(Of Window, FxState)

    ''' <summary>
    ''' 当前有几个独立弹窗正在使用调暗背景（MyMsgBoxTick 据此保持 PanMsg 可见）。
    ''' </summary>
    Public DimmedCount As Integer = 0

    ''' <summary>
    ''' 为独立窗口挂上与内置弹窗一致的动效。需在窗口内容构建完成后调用一次。
    ''' </summary>
    Public Sub RegisterDialog(Win As Window, Root As Border)
        Root.RenderTransformOrigin = New Point(0, 0.5)
        Dim Rot As New RotateTransform(-4)
        Dim Pos As New TranslateTransform(0, 40)
        Dim Group As New TransformGroup
        Group.Children.Add(Rot)
        Group.Children.Add(Pos)
        Root.RenderTransform = Group
        Root.Opacity = 0
        Dim State As New FxState With {.Root = Root, .Rotate = Rot, .Translate = Pos}
        States(Win) = State
        AddHandler Win.Loaded,
            Sub()
                Try
                    '对齐到主窗口正中（此时整体还是透明的，位移不会被看到）
                    If Win.Owner IsNot Nothing Then
                        Try
                            Dim O = Win.Owner
                            If O.ActualWidth > 240 AndAlso O.ActualHeight > 220 Then
                                Win.Left = O.Left + (O.ActualWidth - Win.ActualWidth) / 2
                                Win.Top = O.Top + (O.ActualHeight - Win.ActualHeight) / 2
                            End If
                        Catch
                        End Try
                    End If
                    DimBackground(State)
                    AniStart({
                        AaOpacity(Root, 1, 120, 60),
                        AaDouble(Sub(i) Pos.Y += i, -Pos.Y, 300, 60, New AniEaseOutBack(AniEasePower.Weak)),
                        AaDouble(Sub(i) Rot.Angle += i, -Rot.Angle, 300, 60, New AniEaseOutFluent(AniEasePower.Weak))
                    }, "HarnessDialog " & GetUuid())
                Catch ex As Exception
                    Logger.Error(ex, "弹窗动效加载失败", LogBehavior.None)
                End Try
            End Sub
        AddHandler Win.Closed, Sub() OnDialogClosed(Win, State)
    End Sub

    Private Sub DimBackground(State As FxState)
        If FrmMain Is Nothing OrElse FrmMain.PanMsg Is Nothing Then Return
        If Not State.Dimmed Then
            State.Dimmed = True
            DimmedCount += 1
        End If
        FrmMain.PanMsg.Visibility = Visibility.Visible
        ' 启动阶段主窗口可能还没完成布局、动画也还没开始计时，这里直接给最终色，
        ' 保证「屏幕变暗」一定生效（关闭时再走淡出动画）。
        FrmMain.PanMsg.Background = New MyColor(90, 0, 0, 0)
    End Sub

    Private Sub OnDialogClosed(Win As Window, State As FxState)
        States.Remove(Win)
        If Not State.Dimmed Then Return
        State.Dimmed = False
        DimmedCount = Math.Max(0, DimmedCount - 1)
        If DimmedCount = 0 AndAlso FrmMain IsNot Nothing AndAlso FrmMain.PanMsg IsNot Nothing Then
            AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(0, 0, 0, 0) - FrmMain.PanMsg.Background, 200, Ease:=New AniEaseOutFluent(AniEasePower.Weak)), "HarnessDialog Background")
        End If
    End Sub

    ''' <summary>
    ''' 淡出后关闭窗口；未注册动效或已在关闭中时直接关闭。
    ''' </summary>
    Public Sub CloseAnimated(Win As Window)
        Dim State As FxState = Nothing
        If Not States.TryGetValue(Win, State) OrElse State.Closing Then
            Win.Close()
            Return
        End If
        State.Closing = True
        Win.IsHitTestVisible = False
        AniStart({
            AaOpacity(State.Root, -State.Root.Opacity, 80, 20),
            AaDouble(Sub(i) State.Translate.Y += i, 20 - State.Translate.Y, 150, 0, New AniEaseOutFluent),
            AaDouble(Sub(i) State.Rotate.Angle += i, 6 - State.Rotate.Angle, 150, 0, New AniEaseInFluent(AniEasePower.Weak)),
            AaCode(Sub() Win.Close(), , True)
        }, "HarnessDialog " & GetUuid())
    End Sub

End Module
