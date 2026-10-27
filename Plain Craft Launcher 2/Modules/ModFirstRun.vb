Imports System.IO
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports System.Windows.Shapes
Imports Path = System.IO.Path
Imports Registry = Microsoft.Win32.Registry

''' <summary>
''' 首次启动指引：PCL Harness 使用独立的配置与缓存目录（PCLHarness / %AppData%\PCLHarness），
''' 第一次启动时引导用户把旧 PCL 的设置等迁移过来（原版 PCL 没有插件功能，不迁移插件目录）。
''' </summary>
Public Module ModFirstRun

    ''' <summary>首次启动标记文件，位于 PCLHarness 目录下。</summary>
    Public ReadOnly Property PathMarker As String = Paths.BaseThenName & "FirstRun.done"
    ''' <summary>
    ''' 上次点了「跳过」时写下的标记：下次启动若用户还没改过任何设置，会再给一次迁移机会。
    ''' </summary>
    Public ReadOnly Property PathSkip As String = Paths.BaseThenName & "FirstRun.skip"
    ''' <summary>新配置目录（exe 同级 PCLHarness\）。</summary>
    Public ReadOnly Property PathNewBase As String = Paths.BaseThenName
    ''' <summary>新数据目录（%AppData%\PCLHarness\）。</summary>
    Public ReadOnly Property PathNewAppData As String = Paths.AppDataThenName

    ''' <summary>
    ''' 把旧版（PCLDebug）的注册表设置一次性复制到本程序的独立根（PCLHarness），
    ''' 这样 PCL Harness 与原版 PCL 同时运行时不会互相覆盖设置；只在独立根还没有值时执行。
    ''' </summary>
    Public Sub MigrateRegistry()
        Try
            Using NewKey = Registry.CurrentUser.CreateSubKey("Software\PCLHarness")
                If NewKey Is Nothing OrElse NewKey.ValueCount > 0 Then Return
                Using OldKey = Registry.CurrentUser.OpenSubKey("Software\PCLDebug")
                    If OldKey Is Nothing Then Return
                    For Each ValueName In OldKey.GetValueNames()
                        Try
                            NewKey.SetValue(ValueName, OldKey.GetValue(ValueName), OldKey.GetValueKind(ValueName))
                        Catch
                        End Try
                    Next
                End Using
            End Using
        Catch
            '迁移失败不影响启动，设置会退回到默认值
        End Try
    End Sub

    Public Function IsFirstRun() As Boolean
        Return Not File.Exists(PathMarker)
    End Function

    ''' <summary>
    ''' 是否需要显示迁移指引：首次启动，或上次跳过后用户还没改过设置（再给一次机会）。
    ''' </summary>
    Public Function NeedWizard() As Boolean
        Return IsFirstRun() OrElse File.Exists(PathSkip)
    End Function

    ''' <summary>
    ''' 上次跳过、且此后没有改动过任何设置时返回 True（只给一次二次机会）。
    ''' 若用户已经改过设置，说明已经在正常使用，就不再打扰。
    ''' </summary>
    Public Function ShouldShowAgain() As Boolean
        Try
            If Not File.Exists(PathSkip) Then Return False
            Dim Touched As Boolean = False
            For Each Line In File.ReadAllLines(PathSkip)
                If String.Equals(Line.Trim(), "touched=1", StringComparison.Ordinal) Then Touched = True
            Next
            If Touched Then
                Logger.Info("首次启动：上次跳过后已经改过设置，不再重复提示迁移，已清除跳过标记")
                DeleteMark(PathSkip)
                Return False
            End If
            Return True
        Catch ex As Exception
            Logger.Error(ex, "读取首次启动跳过标记失败", LogBehavior.None)
            Return False
        End Try
    End Function

    ''' <summary>记录「用户点了跳过」，等待下次启动的一次二次机会。</summary>
    Public Sub MarkSkipped()
        Try
            Directory.CreateDirectory(Paths.BaseThenName)
            File.WriteAllText(PathSkip, $"skipped={Now:yyyy-MM-dd HH:mm:ss}{vbCrLf}touched=0{vbCrLf}", New Text.UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "写入首次启动跳过标记失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>
    ''' 用户改动任意设置时调用：把跳过标记标成「已动过设置」，下次启动不再提示迁移。
    ''' </summary>
    Public Sub NotifySettingsTouched()
        Try
            If Not File.Exists(PathSkip) Then Return
            If File.ReadAllText(PathSkip).IndexOf("touched=1", StringComparison.Ordinal) >= 0 Then Return
            '保留原来的「跳过时间」，只把 touched 改成 1
            Dim SkippedAt As String = $"skipped={Now:yyyy-MM-dd HH:mm:ss}"
            For Each Line In File.ReadAllLines(PathSkip)
                If Line.Trim().StartsWith("skipped=", StringComparison.Ordinal) Then SkippedAt = Line.Trim()
            Next
            File.WriteAllText(PathSkip, SkippedAt & vbCrLf & "touched=1" & vbCrLf, New Text.UTF8Encoding(False))
            Logger.Info("首次启动：用户已修改设置，之后不再提示数据迁移")
        Catch ex As Exception
            Logger.Error(ex, "更新首次启动跳过标记失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>清除跳过标记（迁移完成或二次机会已用掉）。</summary>
    Public Sub ClearSkip()
        DeleteMark(PathSkip)
    End Sub

    Private Sub DeleteMark(FilePath As String)
        Try
            If File.Exists(FilePath) Then File.Delete(FilePath)
        Catch ex As Exception
            Logger.Error(ex, $"删除标记文件失败：{FilePath}", LogBehavior.None)
        End Try
    End Sub

    Public Sub MarkDone()
        Try
            Directory.CreateDirectory(Paths.BaseThenName)
            File.WriteAllText(PathMarker, $"PCL Harness 首次启动于 {Now:yyyy-MM-dd HH:mm:ss}{vbCrLf}", New Text.UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "写入首次启动标记失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>一项可迁移的数据。</summary>
    Public Class MigrateItem
        Public Key As String = ""
        Public Title As String = ""
        Public Description As String = ""
        ''' <summary>源路径（文件或目录）。</summary>
        Public Source As String = ""
        ''' <summary>目标路径（文件或目录）。</summary>
        Public Target As String = ""
        Public IsFolder As Boolean
        Public Exists As Boolean
        ''' <summary>目标已存在时是否覆盖（仅用于首次启动时本就由程序新建的空设置文件）。</summary>
        Public Overwrite As Boolean
    End Class

    ''' <summary>列出旧 PCL 数据里可迁移的项。</summary>
    Public Function Detect() As List(Of MigrateItem)
        Dim OldBase As String = Paths.Base & "PCL\"
        Dim OldAppData As String = Paths.AppData & "PCL\"
        Dim Items As New List(Of MigrateItem) From {
            New MigrateItem With {.Key = "setup", .Title = "启动器设置", .Description = "Setup.ini：窗口大小、主题、下载源、启动参数等全部选项。",
                .Source = OldBase & "Setup.ini", .Target = PathNewBase & "Setup.ini", .Overwrite = True},
            New MigrateItem With {.Key = "link", .Title = "联机模块", .Description = "EasyTier 文件夹：联机用的核心程序与 CLI。",
                .Source = OldBase & "EasyTier", .Target = PathNewBase & "EasyTier", .IsFolder = True},
            New MigrateItem With {.Key = "pics", .Title = "自定义图片与音乐", .Description = "Pictures / Musics：自定义背景与启动音乐。",
                .Source = OldBase & "Pictures", .Target = PathNewBase & "Pictures", .IsFolder = True},
            New MigrateItem With {.Key = "appdata", .Title = "启动器数据", .Description = "%AppData%\PCL：联机配置、自定义皮肤等缓存数据。",
                .Source = OldAppData, .Target = PathNewAppData, .IsFolder = True},
            New MigrateItem With {.Key = "secrets", .Title = "凭据文件", .Description = "Secrets.json：微软登录 Client ID / CurseForge Key（有才会迁移）。",
                .Source = OldBase & "Secrets.json", .Target = PathNewBase & "Secrets.json"},
            New MigrateItem With {.Key = "custom", .Title = "自定义主页与图标", .Description = "Custom.xaml / Logo.png / hints.txt：自定义主页、图标与提示。",
                .Source = OldBase & "Custom.xaml", .Target = PathNewBase & "Custom.xaml"}
        }
        For Each Item In Items
            Item.Exists = If(Item.IsFolder, Directory.Exists(Item.Source), File.Exists(Item.Source))
        Next
        ' 自定义主页可能只有其中一部分文件，按存在情况补一项
        Dim ExtraLogos As String() = {"Logo.png", "hints.txt", "CustomSkin.png"}
        For Each Name In ExtraLogos
            Dim Src As String = OldBase & Name
            If File.Exists(Src) Then
                Items.Add(New MigrateItem With {.Key = "custom_" & Name, .Title = Name, .Description = "旧 PCL 的 " & Name & "。",
                    .Source = Src, .Target = PathNewBase & Name})
            End If
        Next
        Return Items
    End Function

    ''' <summary>
    ''' 执行迁移：文件类项按 Overwrite 决定是否覆盖，文件夹类项递归合并、只补缺失文件。返回 (成功, 跳过, 失败) 计数。
    ''' </summary>
    Public Function Run(Selected As IEnumerable(Of MigrateItem)) As (Copied As Integer, Skipped As Integer, Failed As Integer)
        Dim Copied As Integer = 0, Skipped As Integer = 0, FailedCount As Integer = 0
        For Each Item In Selected
            Try
                If Item.IsFolder Then
                    Dim Before As Integer = Copied
                    Directory.CreateDirectory(Item.Target)
                    For Each Entry In Directory.EnumerateFileSystemEntries(Item.Source)
                        Try
                            If Directory.Exists(Entry) Then
                                CopyFolder(Entry, Path.Combine(Item.Target, Path.GetFileName(Entry)))
                            Else
                                CopyFileIfMissing(Entry, Path.Combine(Item.Target, Path.GetFileName(Entry)))
                            End If
                            Copied += 1
                        Catch ex As Exception
                            FailedCount += 1
                            Logger.Warn(ex, $"迁移失败：{Entry}")
                        End Try
                    Next
                    If Copied = Before Then Skipped += 1
                Else
                    If File.Exists(Item.Target) AndAlso Not Item.Overwrite Then
                        Skipped += 1
                        Continue For
                    End If
                    Directory.CreateDirectory(Path.GetDirectoryName(Item.Target))
                    File.Copy(Item.Source, Item.Target, True)
                    Copied += 1
                End If
            Catch ex As Exception
                FailedCount += 1
                Logger.Error(ex, $"迁移失败：{Item.Title}", LogBehavior.None)
            End Try
        Next
        Logger.Info($"旧 PCL 数据迁移完成：复制 {Copied}，跳过 {Skipped}，失败 {FailedCount}")
        Return (Copied, Skipped, FailedCount)
    End Function

    Private Sub CopyFolder(From As String, [To] As String)
        Directory.CreateDirectory([To])
        For Each Entry In Directory.EnumerateFileSystemEntries(From)
            If Directory.Exists(Entry) Then
                CopyFolder(Entry, Path.Combine([To], Path.GetFileName(Entry)))
            Else
                CopyFileIfMissing(Entry, Path.Combine([To], Path.GetFileName(Entry)))
            End If
        Next
    End Sub

    Private Sub CopyFileIfMissing(From As String, [To] As String)
        If File.Exists([To]) Then Return
        Directory.CreateDirectory(Path.GetDirectoryName([To]))
        File.Copy(From, [To])
    End Sub

    ''' <summary>
    ''' 首次启动时显示指引窗口。用户完成后写入标记，之后不再出现。
    ''' 若上次是「跳过」且此后没改过设置，会再弹一次（二次机会），之后不再提示。
    ''' </summary>
    Public Sub ShowWizardIfNeeded()
        Dim SecondChance As Boolean = False
        If IsFirstRun() Then
            Logger.Info("首次启动：显示配置目录与数据迁移指引")
        ElseIf ShouldShowAgain() Then
            SecondChance = True
            Logger.Info("首次启动：上次点了跳过且之后没有改动设置，再显示一次迁移指引")
        Else
            Return
        End If
        Dim Items = Detect()
        '自动化验证用：设置环境变量 PCL_FIRSTRUN_AUTORUN=1 时静默迁移全部存在项后退出，不弹窗
        If Environment.GetEnvironmentVariable("PCL_FIRSTRUN_AUTORUN") = "1" Then
            Try
                Dim Picked = Items.Where(Function(I) I.Exists).ToList()
                Dim Result = Run(Picked)
                Logger.Info($"首次启动自动迁移：复制 {Result.Copied}，跳过 {Result.Skipped}，失败 {Result.Failed}")
            Catch ex As Exception
                Logger.Error(ex, "首次启动自动迁移失败", LogBehavior.None)
            End Try
            MarkDone()
            ClearSkip()
            Return
        End If
        Dim Window As FirstRunWindow
        Try
            Window = New FirstRunWindow(Items, SecondChance)
        Catch ex As Exception
            Logger.Error(ex, "打开首次启动指引失败", LogBehavior.None)
            MarkDone()
            ClearSkip()
            Return
        End Try
        If ModMain.FrmMain IsNot Nothing Then Window.Owner = ModMain.FrmMain
        Window.ShowDialog()
        If SecondChance Then
            '二次机会只用一次：这次无论迁移还是跳过，都不再重复提示
            MarkDone()
            ClearSkip()
        ElseIf Window.Skipped Then
            '跳过：留一个标记，下次启动再问一次（若期间没改过设置）
            MarkDone()
            MarkSkipped()
            Logger.Info("首次启动：已跳过，下次启动时若未改动设置会再提示一次")
        Else
            MarkDone()
            ClearSkip()
        End If
    End Sub

End Module

''' <summary>首次启动指引窗口（纯代码构建，跟随主题）。</summary>
Public Class FirstRunWindow
    Inherits Window

    Private ReadOnly Items As New List(Of ModFirstRun.MigrateItem)
    Private ReadOnly Boxes As New List(Of MyCheckBox)
    Private ReadOnly TargetItems As New List(Of ModFirstRun.MigrateItem)
    Private ReadOnly LabResult As TextBlock
    Private ReadOnly BtnRun As MyButton
    Private MigrationDone As Boolean
    Private _Skipped As Boolean = False
    ''' <summary>用户点了「跳过」（或等价操作）关闭了窗口。</summary>
    Public ReadOnly Property Skipped As Boolean
        Get
            Return _Skipped
        End Get
    End Property

    Public Sub New(Items As List(Of ModFirstRun.MigrateItem), Optional SecondChance As Boolean = False)
        Me.Items = Items
        Title = "欢迎使用 PCL Harness"
        WindowStyle = WindowStyle.None
        AllowsTransparency = True
        Background = Brushes.Transparent
        '窗口按屏幕工作区自适应，避免小屏上占太大
        Dim WorkArea = SystemParameters.WorkArea
        Width = Math.Min(500, WorkArea.Width * 0.9)
        Height = Math.Min(420, WorkArea.Height * 0.8)
        SizeToContent = SizeToContent.Manual
        WindowStartupLocation = WindowStartupLocation.CenterScreen
        ShowInTaskbar = False
        ResizeMode = ResizeMode.NoResize
        UseLayoutRounding = True

        Dim Root As New Border With {
            .CornerRadius = New CornerRadius(8),
            .Padding = New Thickness(18, 14, 18, 12),
            .Margin = New Thickness(12)
        }
        Root.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
        Dim Shadow As New Effects.DropShadowEffect With {.BlurRadius = 14, .ShadowDepth = 3, .RenderingBias = Effects.RenderingBias.Performance, .Opacity = 0.35}
        Shadow.Color = Color.FromArgb(90, 0, 0, 0)
        Root.Effect = Shadow

        Dim Main As New StackPanel
        Dim Scroller As New MyScrollViewer With {
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .Content = Main,
            .Margin = New Thickness(0, 0, 0, 4)
        }
        '内容区可滚动，按钮固定在底部，避免窗口放大
        Dim Layout As New Grid
        Layout.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
        Layout.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        Grid.SetRow(Scroller, 0)
        Layout.Children.Add(Scroller)
        Root.Child = Layout

        Dim TitleText As New TextBlock With {
            .Text = "欢迎使用 PCL Harness",
            .FontSize = 18,
            .Margin = New Thickness(2, 0, 0, 7)
        }
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        Main.Children.Add(TitleText)

        Dim Line As New Rectangle With {.Height = 2, .Fill = TitleText.Foreground}
        Main.Children.Add(Line)

        Dim Intro As New TextBlock With {
            .TextWrapping = TextWrapping.Wrap,
            .FontSize = 12,
            .LineHeight = 17,
            .Margin = New Thickness(1, 10, 1, 8),
            .Text = "这是 PCL Harness 的独立配置目录，不会和原版 PCL 互相覆盖：" & vbCrLf &
                    $"    配置与缓存：{ModFirstRun.PathNewBase}" & vbCrLf &
                    $"    用户数据：{ModFirstRun.PathNewAppData}" & vbCrLf &
                    If(SecondChance,
                       "上次你点了「跳过」。如果现在想迁移旧 PCL 的设置，可以在下面勾选后点「迁移并进入」；这次关掉后就不会再提示了。",
                       "如果以前用过 PCL，可以把它的设置等迁移过来；没用过就直接跳过。")
        }
        Intro.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Main.Children.Add(Intro)

        Dim HeadTitle As New TextBlock With {
            .Text = "可迁移的内容",
            .FontSize = 14,
            .FontWeight = FontWeights.SemiBold,
            .Margin = New Thickness(1, 6, 1, 4)
        }
        HeadTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        Main.Children.Add(HeadTitle)

        '只列出旧目录里确实存在的项
        For Each Item In Me.Items
            If Not Item.Exists Then Continue For
            Dim Box As New MyCheckBox With {.Text = "", .VerticalAlignment = VerticalAlignment.Top, .Margin = New Thickness(0, 3, 0, 3)}
            Box.Checked = True
            Dim Row As New Grid With {.Margin = New Thickness(0, 1, 0, 1)}
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(30)})
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Grid.SetColumn(Box, 0)
            Row.Children.Add(Box)
            Dim Text As New StackPanel
            Dim T As New TextBlock With {.Text = Item.Title, .FontSize = 12}
            T.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Dim D As New TextBlock With {.Text = Item.Description, .FontSize = 10.5, .TextWrapping = TextWrapping.Wrap, .Opacity = 0.72, .Margin = New Thickness(0, 0, 0, 0)}
            D.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Text.Children.Add(T)
            Text.Children.Add(D)
            Grid.SetColumn(Text, 1)
            Row.Children.Add(Text)
            Main.Children.Add(Row)
            Boxes.Add(Box)
            TargetItems.Add(Item)
        Next

        If TargetItems.Count = 0 Then
            Dim Empty As New TextBlock With {
                .Text = "没有在旧 PCL 目录里找到可迁移的数据（可能是第一次使用，或旧数据不在这台机器上）。",
                .FontSize = 13,
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(1, 10, 1, 6),
                .Opacity = 0.8
            }
            Empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Main.Children.Add(Empty)
        End If

        LabResult = New TextBlock With {
            .TextWrapping = TextWrapping.Wrap,
            .FontSize = 12.5,
            .Margin = New Thickness(1, 12, 1, 0),
            .Visibility = Visibility.Collapsed
        }
        LabResult.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Main.Children.Add(LabResult)

        Dim Btns As New StackPanel With {.Orientation = Orientation.Horizontal, .HorizontalAlignment = HorizontalAlignment.Right, .Margin = New Thickness(0, 12, 0, 0)}
        Dim BtnSkip As New MyButton With {.Text = If(SecondChance, "不再提示", "跳过"), .Height = 30, .MinWidth = 84, .Margin = New Thickness(8, 0, 0, 0)}
        Dim BtnOpen As New MyButton With {.Text = "打开旧目录", .Height = 30, .MinWidth = 96, .Margin = New Thickness(8, 0, 0, 0)}
        BtnRun = New MyButton With {.Text = "迁移并进入", .Height = 30, .MinWidth = 104, .Margin = New Thickness(8, 0, 0, 0), .ColorType = MyButton.ColorState.Highlight}
        If TargetItems.Count = 0 Then BtnRun.IsEnabled = False
        AddHandler BtnOpen.Click, Sub(sender As Object, e As Input.MouseButtonEventArgs) OpenLegacyFolder()
        AddHandler BtnRun.Click, Sub(sender As Object, e As Input.MouseButtonEventArgs)
                                            Logger.Info("[首次启动] 点击迁移/完成")
                                            RunMigration()
                                        End Sub
        AddHandler BtnSkip.Click, Sub(sender As Object, e As Input.MouseButtonEventArgs)
                                            SkipAndClose("点击跳过")
                                        End Sub
        Btns.Children.Add(BtnOpen)
        Btns.Children.Add(BtnSkip)
        Btns.Children.Add(BtnRun)
        '按钮固定在窗口底部，不随内容滚动
        Grid.SetRow(Btns, 1)
        Layout.Children.Add(Btns)

        AddHandler MouseLeftButtonDown, Sub(sender As Object, e As Input.MouseButtonEventArgs)
                                            If IsInteractiveSource(e.OriginalSource) Then Return
                                            Try
                                                DragMove()
                                            Catch
                                            End Try
                                        End Sub
        'Esc = 跳过，Enter = 迁移（方便键盘操作，也便于自动化验证）
        AddHandler PreviewKeyDown, Sub(sender As Object, e As Input.KeyEventArgs)
                                       Select Case e.Key
                                           Case Input.Key.Escape
                                               e.Handled = True
                                               SkipAndClose("按下 Esc")
                                           Case Input.Key.Enter
                                               e.Handled = True
                                               RunMigration()
                                       End Select
                                   End Sub
        Content = Root
        '与内置弹窗一致的淡入动画 + 主窗口背景调暗
        ModDialogFx.RegisterDialog(Me, Root)
    End Sub

    ''' <summary>判断点击落点是否属于需要自己处理鼠标的控件。</summary>
    Private Function IsInteractiveSource(Source As Object) As Boolean
        Dim Node = TryCast(Source, DependencyObject)
        While Node IsNot Nothing
            '滚动容器不算：在内容区按下仍然可以拖动窗口，滚动条自己会处理点击
            If TypeOf Node Is MyButton OrElse TypeOf Node Is MyCheckBox OrElse TypeOf Node Is MyTextBox OrElse TypeOf Node Is MyComboBox Then
                Return True
            End If
            Node = VisualTreeHelper.GetParent(Node)
        End While
        Return False
    End Function
    Private Sub OpenLegacyFolder()
        Dim OldBase As String = Paths.Base & "PCL\"
        If Directory.Exists(OldBase) Then
            OpenExplorer(OldBase)
        Else
            OpenExplorer(Paths.Base)
        End If
    End Sub

    Private Sub SkipAndClose(Reason As String)
        Logger.Info($"[首次启动] {Reason}")
        _Skipped = True
        ModDialogFx.CloseAnimated(Me)
    End Sub

    Private Sub RunMigration()
        '迁移完成后同一个按钮改为关闭
        If MigrationDone Then
            ModDialogFx.CloseAnimated(Me)
            Return
        End If
        Dim Selected As New List(Of ModFirstRun.MigrateItem)
        For i = 0 To TargetItems.Count - 1
            If Boxes(i).Checked Then Selected.Add(TargetItems(i))
        Next
        If Selected.Count = 0 Then
            SkipAndClose("未勾选任何项目")
            Return
        End If
        BtnRun.IsEnabled = False
        LabResult.Visibility = Visibility.Visible
        LabResult.Text = "正在迁移，请稍候……"
        Try
            Dim Result = ModFirstRun.Run(Selected)
            MigrationDone = True
            LabResult.Text = $"迁移完成：复制 {Result.Copied} 项，跳过 {Result.Skipped} 项" &
                             If(Result.Failed > 0, $"，失败 {Result.Failed} 项（详见日志）", "") & "。设置将在下次启动时生效。"
            BtnRun.Text = "完成"
            BtnRun.IsEnabled = True
        Catch ex As Exception
            Logger.Error(ex, "迁移旧 PCL 数据失败", LogBehavior.Toast)
            LabResult.Text = "迁移失败：" & ex.Message
            BtnRun.IsEnabled = True
        End Try
    End Sub

End Class
