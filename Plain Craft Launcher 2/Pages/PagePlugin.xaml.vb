Imports System.Linq
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media

''' <summary>
''' 插件页右侧内容区。
''' 左侧栏由 PagePluginLeft（原生 MyPageLeft）负责，本页只负责按分区重建内容。
''' 分区切换采用「清空后重建」，避免控件在 Collapsed 状态下经过 MyCard 的加载动画而变灰、消失。
''' </summary>
Public Class PagePlugin

    ''' <summary>当前分区：0 插件管理 / 1 插件设置 / 2 开发说明 / 3 控制台。</summary>
    Public SectionIndex As Integer = 0

    Private IsRebuilding As Boolean
    Private HasInitialized As Boolean

    ' ================= 生命周期 =================

    Private Sub PagePlugin_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If HasInitialized Then Return
        HasInitialized = True
        Try
            ModPlugin.Refresh(True)
        Catch ex As Exception
            Logger.Error(ex, "扫描插件失败", LogBehavior.Toast)
        End Try
        RefreshNav()
        ModPlugin.WarnFailed()
        SwitchSection(SectionIndex, Animate:=False)
    End Sub

    ''' <summary>
    ''' 切换分区并重建右侧内容。左栏与外部调用都走这里。
    ''' </summary>
    ''' <param name="Animate">是否播放入场动画；首次加载由页面自身动画负责，这里传 False。</param>
    ''' <summary>导入插件后刷新页面（导航计数 + 当前分区）。</summary>
    Public Sub RefreshAfterImport()
        Try
            RefreshNav()
            RefreshCurrentSection()
        Catch ex As Exception
            Logger.Warn(ex, "刷新插件页失败")
        End Try
    End Sub

    ''' <summary>用文件对话框选插件包导入（.pclplugin / zip / dll）。</summary>
    Private Sub ImportPluginsFromDialog()
        Try
            Dim Dialog As New Microsoft.Win32.OpenFileDialog With {
                .Title = "选择插件包",
                .Filter = "插件包 (*.pclplugin;*.zip;*.dll)|*.pclplugin;*.zip;*.dll|所有文件 (*.*)|*.*",
                .Multiselect = True
            }
            If Dialog.ShowDialog() <> True Then Return
            ModPlugin.ImportPluginsWithUi(Dialog.FileNames)
        Catch ex As Exception
            Logger.Error(ex, "打开导入插件对话框失败", LogBehavior.Toast)
        End Try
    End Sub

    Public Sub SwitchSection(Index As Integer, Optional Animate As Boolean = True)
        SectionIndex = Index
        If PanContent Is Nothing Then Return
        If DevTimer IsNot Nothing Then DevTimer.Stop()
        DevLogBox = Nothing
        PanContent.Children.Clear()
        Select Case Index
            Case 1
                BuildExtend()
            Case 2
                BuildDoc()
            Case 3
                BuildDevMode()
            Case Else
                BuildManage()
        End Select
        If PanBack IsNot Nothing Then PanBack.ScrollToVerticalOffset(0)
        '和 PCL 原生页面切换一致：卡片依次淡入 + 上浮（不套 MyPageRight 的状态机，避免 PageOnEnterAnimationFinished 冲突）
        If Animate AndAlso IsLoaded Then AnimateContent()

    End Sub

    ''' <summary>模仿 MyPageRight.TriggerEnterAnimation 的逐项入场动画。</summary>
    Private Sub AnimateContent()
        Try
            Dim AniList As New List(Of AniData)
            Dim Delay As Integer = 0
            For Each Element As FrameworkElement In PanContent.Children.OfType(Of FrameworkElement)()
                Element.Opacity = 0
                Element.RenderTransform = New TranslateTransform(0, -16)
                AniList.Add(AaOpacity(Element, 1, 100, Delay, New AniEaseOutFluent(AniEasePower.Weak)))
                AniList.Add(AaTranslateY(Element, 5, 250, Delay, New AniEaseOutFluent))
                AniList.Add(AaTranslateY(Element, 11, 350, Delay, New AniEaseOutBack))
                Delay += 25
            Next
            If AniList.Count > 0 Then AniStart(AniList.ToArray())
        Catch ex As Exception
            '动画失败不影响内容展示
            For Each Element As FrameworkElement In PanContent.Children.OfType(Of FrameworkElement)()
                Element.Opacity = 1
                Element.RenderTransform = Nothing
            Next
            Logger.Error(ex, "插件页切换动画失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>重建当前分区（带重入锁，供启用/禁用、重新扫描后调用）。</summary>
    Private Sub RefreshCurrentSection()
        If IsRebuilding Then Return
        IsRebuilding = True
        Try
            SwitchSection(SectionIndex, Animate:=False)
        Finally
            IsRebuilding = False
        End Try
    End Sub

    Private Sub RefreshNav()
        If FrmMain IsNot Nothing Then FrmMain.RefreshPluginNav()
    End Sub

    ' ================= 分区一：插件管理 =================

    Private ManageSearch As String = ""
    Private ManageListBody As StackPanel

    Private Sub BuildManage()
        '搜索栏（与扩展点一致）
        Dim Search As New MySearchBox With {.HintText = "搜索插件名称 / 作者 / 说明", .Margin = New Thickness(0, 4, 0, 12)}
        AddHandler Search.TextChanged, Sub(sender As Object, e As EventArgs)
                                           ManageSearch = If(Search.Text, "").Trim()
                                           RefreshManageRows()
                                       End Sub
        PanContent.Children.Add(Search)

        ' ---- 插件目录 ----
        Dim CardPath = NewCard("插件目录")
        Dim Body As New StackPanel
        Dim GridPath As New Grid
        GridPath.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(120)})
        GridPath.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        GridPath.RowDefinitions.Add(New RowDefinition)
        GridPath.RowDefinitions.Add(New RowDefinition)
        AddPlainRow(GridPath, 0, "路径", ModPlugin.PathPluginRoot)
        Dim EnabledCount = ModPlugin.Plugins.Where(Function(p) p.Enabled).Count()
        AddPlainRow(GridPath, 1, "已发现", $"共 {ModPlugin.Plugins.Count} 个，已启用 {EnabledCount} 个")
        Body.Children.Add(GridPath)

        Dim PanBtn As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 10, 0, 0)}
        PanBtn.Children.Add(MkButton("导入插件…", AddressOf ImportPluginsFromDialog))
        PanBtn.Children.Add(MkButton("打开插件目录", Sub() ModPlugin.OpenFolder()))
        PanBtn.Children.Add(MkButton("重新扫描", Sub()
                                                    Try
                                                        ModPlugin.Refresh(True)
                                                    Catch ex As Exception
                                                        Logger.Error(ex, "扫描插件失败", LogBehavior.Toast)
                                                    End Try
                                                    RefreshNav()
                                                    ModPlugin.WarnFailed()
                                                    RefreshCurrentSection()
                                                End Sub))
        Body.Children.Add(PanBtn)
        Body.Margin = New Thickness(25, 40, 25, 20)
        CardPath.Children.Add(Body)
        PanContent.Children.Add(CardPath)

        ' ---- 已发现的插件 ----
        Dim CardList = NewCard("已发现的插件")
        Dim BodyList As New StackPanel
        ManageListBody = BodyList
        BodyList.Margin = New Thickness(25, 40, 25, 20)
        CardList.Children.Add(BodyList)
        PanContent.Children.Add(CardList)
        RefreshManageRows()
    End Sub

    ' ================= 控制台（API v6）：实时日志 + 命令回显 =================

    Private DevTimer As System.Windows.Threading.DispatcherTimer
    Private DevLogBox As MyTextBox

    Private Sub BuildDevMode()
        Dim Card = NewCard("控制台")
        Dim Body As New StackPanel With {.Margin = New Thickness(25, 40, 25, 20)}
        Body.Children.Add(MkHint("实时显示插件事件、服务注册、后台任务和宿主调用。支持的命令：help / events / plugins / services / tasks / emit 事件名 / clear"))

        Dim Input_ As New MyTextBox With {
            .HintText = "输入命令后回车，例如 events",
            .Margin = New Thickness(0, 12, 0, 8)
        }
        Body.Children.Add(Input_)

        Dim Box As New MyTextBox With {
            .IsReadOnly = True,
            .AcceptsReturn = True,
            .TextWrapping = TextWrapping.NoWrap,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            .Height = 260,
            .VerticalContentAlignment = VerticalAlignment.Top,
            .FontFamily = New Media.FontFamily("Consolas, Microsoft YaHei UI"),
            .FontSize = 12,
            .Margin = New Thickness(0, 0, 0, 8)
        }
        DevLogBox = Box
        Body.Children.Add(Box)

        Dim PanBtn As New StackPanel With {.Orientation = Orientation.Horizontal}
        PanBtn.Children.Add(MkButton("执行", Sub() RunDevCommand(Input_.Text, Input_)))
        PanBtn.Children.Add(MkButton("清空日志", Sub()
                                                ModPlugin.PluginDevLog.Clear()
                                                RefreshDevLog()
                                            End Sub))
        PanBtn.Children.Add(MkButton("查看事件订阅", Sub() RunDevCommand("events", Input_)))
        Body.Children.Add(PanBtn)

        Card.Children.Add(Body)
        PanContent.Children.Add(Card)

        RefreshDevLog()
        '命令输入框支持回车执行
        AddHandler Input_.KeyDown, Sub(Sender As Object, E As KeyEventArgs)
                                       If E.Key = Key.Enter Then
                                           E.Handled = True
                                           RunDevCommand(Input_.Text, Input_)
                                       End If
                                   End Sub

        If DevTimer Is Nothing Then
            DevTimer = New System.Windows.Threading.DispatcherTimer With {.Interval = New TimeSpan(0, 0, 0, 0, 600)}
            AddHandler DevTimer.Tick, Sub() RefreshDevLog()
            AddHandler ModPlugin.PluginDevLog.Changed, Sub() RefreshDevLog()
        End If
        DevTimer.Start()
    End Sub

    ''' <summary>把开发日志刷到文本框（保留滚动位置）。</summary>
    Private Sub RefreshDevLog()
        If DevLogBox Is Nothing Then Return
        Try
            Dim Items = ModPlugin.PluginDevLog.Snapshot(ModPlugin.PluginDevLog.LevelTrace, Nothing, 400)
            Dim Text As String = String.Join(vbCrLf, Items.Select(Function(I) I.Display))
            If DevLogBox.Text = Text Then Return
            DevLogBox.Text = Text
            DevLogBox.ScrollToEnd() '新日志总在最底部，视图跟着滚到最新一行
        Catch
            '开发日志刷新失败不影响主流程
        End Try
    End Sub

    ''' <summary>执行开发控制台命令，结果写进开发日志。</summary>
    Private Sub RunDevCommand(Raw As String, Input_ As MyTextBox)
        Dim Command As String = If(Raw, "").Trim()
        If Command = "" Then Return
        If Input_ IsNot Nothing Then Input_.Text = ""
        ModPlugin.PluginDevLog.Info("控制台", "> " & Command)
        Try
            Dim Parts = Command.Split(New Char() {" "c}, 2)
            Dim Name As String = Parts(0).ToLowerInvariant()
            Dim Arg As String = If(Parts.Length > 1, Parts(1).Trim(), "")
            Select Case Name
                Case "help", "?"
                    For Each Line In {"help            显示这条帮助",
                                      "events          列出事件名、参数签名和当前订阅数",
                                      "plugins         列出已发现的插件与状态",
                                      "services        列出插件间注册的服务",
                                      "tasks           列出正在运行的插件任务",
                                      "emit <事件名>   手动触发一次事件（测试插件响应）",
                                      "clear           清空开发日志"}
                        ModPlugin.PluginDevLog.Info("控制台", Line)
                    Next
                Case "events"
                    For Each EventName In ModPlugin.PluginEvents.All()
                        Dim Total As Integer = 0
                        For Each Info In ModPlugin.Plugins
                            If Info.Loaded AndAlso Info.Host IsNot Nothing Then Total += Info.Host.Events.SubscriberCount(EventName)
                        Next
                        ModPlugin.PluginDevLog.Info("控制台", $"{EventName}{ModPlugin.PluginEvents.GetSignature(EventName)} → {Total} 个订阅")
                    Next
                Case "plugins"
                    If ModPlugin.Plugins.Count = 0 Then
                        ModPlugin.PluginDevLog.Info("控制台", "还没有发现任何插件")
                    Else
                        For Each Info In ModPlugin.Plugins
                            ModPlugin.PluginDevLog.Info("控制台", $"{Info.DisplayName} {Info.Version}（{Info.Id}）— {Info.StatusText}")
                        Next
                    End If
                Case "services"
                    Dim Lines = ModPlugin.PluginServices.Describe()
                    If Lines.Count = 0 Then
                        ModPlugin.PluginDevLog.Info("控制台", "当前没有插件注册服务")
                    Else
                        Lines.ForEach(Sub(L) ModPlugin.PluginDevLog.Info("控制台", L))
                    End If
                Case "tasks"
                    Dim Running = LoaderTaskbar.Where(Function(L) L.Name IsNot Nothing AndAlso L.Name.StartsWith("插件任务：", StringComparison.Ordinal)).
                                                Select(Function(L) $"{L.Name}（{L.State}，进度 {L.Progress:P0}）").ToList()
                    If Running.Count = 0 Then
                        ModPlugin.PluginDevLog.Info("控制台", "当前没有插件任务在运行")
                    Else
                        Running.ForEach(Sub(L) ModPlugin.PluginDevLog.Info("控制台", L))
                    End If
                Case "emit"
                    If Arg = "" Then
                        ModPlugin.PluginDevLog.Warn("控制台", "用法：emit 事件名")
                    Else
                        Dim Count = ModPlugin.EmitPluginEvent(Arg)
                        ModPlugin.PluginDevLog.Info("控制台", $"已触发 {Arg}，执行了 {Count} 个处理器")
                    End If
                Case "clear"
                    ModPlugin.PluginDevLog.Clear()
                Case Else
                    ModPlugin.PluginDevLog.Warn("控制台", $"未知命令：{Name}，输入 help 查看可用命令")
            End Select
        Catch ex As Exception
            ModPlugin.PluginDevLog.[Error]("控制台", "命令执行失败：" & ex.GetBaseException().Message)
        End Try
        RefreshDevLog()
    End Sub

    ''' <summary>
    ''' 按搜索关键词重建插件列表。
    ''' </summary>
    Private Sub RefreshManageRows()
        If ManageListBody Is Nothing Then Return
        ManageListBody.Children.Clear()
        Dim Key As String = If(ManageSearch, "").Trim().ToLowerInvariant()
        Dim Shown = If(Key = "", ModPlugin.Plugins.ToList(),
                       ModPlugin.Plugins.Where(Function(p)
                                                   Return (p.DisplayName & " " & If(p.Author, "") & " " & If(p.Description, "") & " " & p.StatusText).ToLowerInvariant().Contains(Key)
                                               End Function).ToList())
        If ModPlugin.Plugins.Count = 0 Then
            ManageListBody.Children.Add(MkHint(If(String.IsNullOrEmpty(ModPlugin.ScanError), "还没有发现任何插件。把插件文件夹放进上面的目录后点「重新扫描」。", "扫描失败：" & ModPlugin.ScanError)))
            Return
        End If
        If Shown.Count = 0 Then
            ManageListBody.Children.Add(MkHint($"没有匹配「{ManageSearch}」的插件，换个关键词试试。"))
            Return
        End If
        For Each Info In Shown
            ManageListBody.Children.Add(BuildPluginCard(Info))
        Next
    End Sub

    ''' <summary>把一个插件渲染成一张卡片（仿 Mod 管理的卡片式列表）。</summary>
    Private Function BuildPluginCard(Info As ModPlugin.PluginInfo) As MyCard
        Dim Card As New MyCard With {.Margin = New Thickness(0, 0, 0, 10)}
        Dim GridMain As New Grid With {.Margin = New Thickness(18, 14, 18, 14)}
        GridMain.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        GridMain.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        ' ---- 左：名称 / 作者 / 状态 / 说明 ----
        Dim Left As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 16, 0)}

        Dim LineName As New StackPanel With {.Orientation = Orientation.Horizontal}
        LineName.Children.Add(New TextBlock With {.Text = If(Info.DisplayName, ""), .FontSize = 14.5, .FontWeight = FontWeights.SemiBold, .VerticalAlignment = VerticalAlignment.Center})
        If Not String.IsNullOrWhiteSpace(Info.Version) Then
            LineName.Children.Add(New TextBlock With {.Text = "v" & Info.Version, .FontSize = 11.5, .Margin = New Thickness(8, 2, 0, 0), .VerticalAlignment = VerticalAlignment.Center, .Foreground = Res("ColorBrushGray2")})
        End If
        Left.Children.Add(LineName)

        Dim LineMeta As New TextBlock With {.FontSize = 11.5, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 3, 0, 0), .TextWrapping = TextWrapping.Wrap}
        LineMeta.Inlines.Add(New Run(If(String.IsNullOrWhiteSpace(Info.Author), "未知作者", Info.Author)))
        LineMeta.Inlines.Add(New Run("　·　"))
        LineMeta.Inlines.Add(New Run(Info.StatusText) With {.Foreground = If(IsPluginFused(Info) OrElse Not String.IsNullOrEmpty(Info.[Error]), Res("ColorBrushRedLight"), Res("ColorBrushGray2"))})
        Left.Children.Add(LineMeta)

        If Not String.IsNullOrWhiteSpace(Info.Description) Then
            Left.Children.Add(New TextBlock With {.Text = Info.Description, .FontSize = 12, .Margin = New Thickness(0, 5, 0, 0), .TextWrapping = TextWrapping.Wrap, .Foreground = Res("ColorBrushGray2")})
        End If

        Dim MetaExtra As New List(Of String)
        If Info.Permissions.Count > 0 Then MetaExtra.Add("权限：" & String.Join(", ", Info.Permissions))
        If Info.Dependencies.Count > 0 Then MetaExtra.Add("依赖：" & String.Join(", ", Info.Dependencies))
        If Info.MinApiVersion > 0 Then MetaExtra.Add($"最低 API v{Info.MinApiVersion}")
        If String.IsNullOrWhiteSpace(Info.Signature) Then
            MetaExtra.Add("未签名（来源需自行确认）")
        Else
            MetaExtra.Add("发布者声明（未验证）")
        End If
        If MetaExtra.Count > 0 Then
            Left.Children.Add(New TextBlock With {.Text = String.Join("　·　", MetaExtra), .FontSize = 11, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 4, 0, 0), .TextWrapping = TextWrapping.Wrap})
        End If

        Dim FailCount As Integer = ModPlugin.PluginFailCount(Info)
        If Not String.IsNullOrEmpty(Info.[Error]) Then
            Left.Children.Add(New TextBlock With {.Text = Info.[Error], .FontSize = 11.5, .Foreground = Res("ColorBrushRedLight"), .Margin = New Thickness(0, 5, 0, 0), .TextWrapping = TextWrapping.Wrap})
        If FailCount > 0 Then
            Left.Children.Add(New TextBlock With {.Text = $"健康：本次会话失败 {FailCount} 次（连续 {ModPlugin.FuseThreshold} 次会自动禁用，重新加载成功后自动清零）", .FontSize = 11, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 4, 0, 0), .TextWrapping = TextWrapping.Wrap})
        End If
        End If

        Dim LastEvents As New List(Of String)
        SyncLock Info.Events
            LastEvents = Info.Events.Skip(Math.Max(0, Info.Events.Count - 3)).ToList()
        End SyncLock
        If LastEvents.Count > 0 Then
            Left.Children.Add(New TextBlock With {.Text = String.Join(vbCrLf, LastEvents), .FontSize = 10.5, .Foreground = Res("ColorBrushGray3"), .Margin = New Thickness(0, 6, 0, 0), .TextWrapping = TextWrapping.Wrap, .MaxHeight = 46, .TextTrimming = TextTrimming.CharacterEllipsis})
        End If

        Grid.SetColumn(Left, 0)
        GridMain.Children.Add(Left)

        ' ---- 右：开关 + 按钮 ----
        Dim Right As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Right}
        Dim Chk As New MyCheckBox With {.VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Right}
        Chk.Text = If(Info.Enabled, "已启用", "已禁用")
        Chk.Checked = (Info.Enabled AndAlso String.IsNullOrEmpty(Info.[Error]))
        AddHandler Chk.Change, Sub(sender As Object, user As Boolean)
                                   Dim Value As Boolean = Chk.Checked
                                   If Value AndAlso Not ModPlugin.IsTrusted(Info) Then
                                       Dim Lines As New List(Of String)
                                       Lines.Add($"插件「{Info.DisplayName}」请求以下权限：")
                                       Lines.Add("")
                                       If Info.Permissions.Count = 0 Then
                                           Lines.Add("· 未声明权限（按默认权限运行，不额外放行）")
                                       Else
                                           For Each Perm In Info.Permissions.OrderBy(Function(P) P)
                                               Lines.Add("· " & Perm & "：" & ModPlugin.DescribePermission(Perm))
                                           Next
                                       End If
                                       Lines.Add("")
                                       Lines.Add($"作者：{If(String.IsNullOrWhiteSpace(Info.Author), "未知", Info.Author)}")
                                       If String.IsNullOrWhiteSpace(Info.Signature) Then
                                           Lines.Add("来源：未签名，无法验证发布者身份；只启用你信任来源的插件。")
                                       Else
                                           Lines.Add($"发布者声明（未验证）：{Info.Signature}")
                                           Lines.Add("注意：启动器还没有校验这份声明，任何人都能填写它，不要当作身份证明。")
                                       End If
                                       Lines.Add("")
                                       Lines.Add("确认后才会加载；以后权限有变化会再次询问。")
                                       Dim Choice = MyMsgBox(String.Join(vbCrLf, Lines), "启用插件前确认权限", "仍然启用", "取消")
                                       If Choice <> 1 Then
                                           Chk.Checked = False
                                           Return
                                       End If
                                       ModPlugin.TrustPlugin(Info)
                                   End If
                                   Try
                                       ModPlugin.SetEnabled(Info, Value)
                                       Hint(Info.DisplayName & If(Value, " 已启用", " 已禁用"), If(Value, HintType.Green, HintType.Blue))
                                   Catch ex As Exception
                                       Logger.Error(ex, "切换插件状态失败", LogBehavior.Toast)
                                   End Try
                                   RefreshNav()
                                   RefreshCurrentSection()
                               End Sub
        Right.Children.Add(Chk)

        Dim PanBtn As New StackPanel With {.Orientation = Orientation.Vertical, .HorizontalAlignment = HorizontalAlignment.Right, .Margin = New Thickness(0, 8, 0, 0)}
        If Not String.IsNullOrWhiteSpace(Info.Folder) AndAlso IO.Directory.Exists(Info.Folder) Then
            PanBtn.Children.Add(MkButtonSmall("打开文件夹", Sub()
                                                     Try
                                                         Process.Start("explorer.exe", """" & Info.Folder & """")
                                                     Catch ex As Exception
                                                         Logger.Error(ex, "打开插件文件夹失败", LogBehavior.Toast)
                                                     End Try
                                                 End Sub))
        End If
        If Info.Events.Count > 0 Then
            PanBtn.Children.Add(MkButtonSmall("事件记录", Sub()
                                                     Dim Lines As String
                                                     SyncLock Info.Events
                                                         Lines = String.Join(vbCrLf, Info.Events.Skip(Math.Max(0, Info.Events.Count - 20)))
                                                     End SyncLock
                                                     MyMsgBox(Lines, "插件事件：" & Info.DisplayName, "确定")
                                                 End Sub))
        End If

        If PanBtn.Children.Count > 0 Then Right.Children.Add(PanBtn)

        Grid.SetColumn(Right, 1)
        GridMain.Children.Add(Right)

        Card.Children.Add(GridMain)
        Return Card
    End Function

    ' ================= 分区二：插件设置（每个插件一张可展开卡片） =================

    Private ExtendSearch As String = ""
    Private ExtendFilter As Integer = 0 '0 全部 / 1 已启用 / 2 已禁用
    Private ExtendList As StackPanel
    Private LabExtendStat As TextBlock

    Private Sub BuildExtend()
        '搜索栏（照抄「版本设置 → Mod 管理」）
        Dim Search As New MySearchBox With {.HintText = "搜索插件名称 / 设置项", .Margin = New Thickness(0, 4, 0, 12)}
        AddHandler Search.TextChanged, Sub(sender As Object, e As EventArgs)
                                           ExtendSearch = If(Search.Text, "").Trim()
                                           RebuildExtendList()
                                       End Sub
        PanContent.Children.Add(Search)

        '过滤单选 + 加载统计
        Dim FilterCard As New MyCard With {.Margin = New Thickness(0, 0, 0, 12)}
        Dim FilterBody As New Grid With {.Margin = New Thickness(15, 12, 15, 12)}
        FilterBody.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        FilterBody.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Dim Radios As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim Names = {"全部", "已启用", "已禁用"}
        AniControlEnabled += 1
        For i = 0 To Names.Length - 1
            Dim R As New MyRadioButton With {.Text = Names(i), .Tag = i, .Margin = New Thickness(0, 0, 14, 0), .Checked = (i = ExtendFilter)}
            AddHandler R.Check, Sub(sender As Object, raiseByMouse As Boolean)
                                    If AniControlEnabled > 0 Then Return
                                    Dim Tag = CType(sender, MyRadioButton).Tag
                                    If Tag Is Nothing Then Return
                                    ExtendFilter = CInt(Tag)
                                    RebuildExtendList()
                                End Sub
            Radios.Children.Add(R)
        Next
        AniControlEnabled -= 1
        Grid.SetColumn(Radios, 0)
        FilterBody.Children.Add(Radios)
        LabExtendStat = New TextBlock With {.FontSize = 12.5, .Foreground = Res("ColorBrushGray2"), .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Right, .TextTrimming = TextTrimming.CharacterEllipsis}
        Grid.SetColumn(LabExtendStat, 1)
        FilterBody.Children.Add(LabExtendStat)
        FilterCard.Children.Add(FilterBody)
        PanContent.Children.Add(FilterCard)

        ExtendList = New StackPanel
        PanContent.Children.Add(ExtendList)
        RebuildExtendList()
    End Sub

    Private Sub RebuildExtendList()
        If ExtendList Is Nothing Then Return
        ExtendList.Children.Clear()

        Dim OkCount As Integer = ModPlugin.Plugins.Where(Function(p) String.IsNullOrEmpty(p.Error)).Count()
        Dim FailCount As Integer = ModPlugin.Plugins.Count - OkCount
        If LabExtendStat IsNot Nothing Then LabExtendStat.Text = $"加载状态：成功 {OkCount} 个，失败 {FailCount} 个，总计 {ModPlugin.Plugins.Count} 个"

        Dim Owners As New List(Of ModPlugin.PluginInfo)
        For Each P In ModPlugin.PluginPages
            If P.Owner IsNot Nothing AndAlso Not Owners.Contains(P.Owner) Then Owners.Add(P.Owner)
        Next
        For Each S In ModPlugin.PluginSettings
            If S.Owner IsNot Nothing AndAlso Not Owners.Contains(S.Owner) Then Owners.Add(S.Owner)
        Next
        For Each N In ModPlugin.PluginNavItems
            If N.Owner IsNot Nothing AndAlso Not Owners.Contains(N.Owner) Then Owners.Add(N.Owner)
        Next

        Dim Shown = Owners.Where(Function(o) ExtendMatches(o)).ToList()
        If Shown.Count = 0 Then
            Dim CardEmpty = NewCard("插件设置")
            Dim Body As New StackPanel With {.Margin = New Thickness(25, 40, 25, 20)}
            If Owners.Count = 0 Then
                Body.Children.Add(MkHint("当前没有任何插件注册扩展点。"))
                Body.Children.Add(New TextBlock With {.Text = "插件实现 PCL.IPclPluginUi 后，可以注册自定义页面、标题栏入口与设置项，它们会分别出现在这里（每个插件一张卡片）。", .FontSize = 12, .Foreground = Res("ColorBrushGray2"), .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 6, 0, 0)})
            Else
                Body.Children.Add(MkHint("没有匹配「" & ExtendSearch & "」的插件，换个关键词或切换过滤条件试试。"))
            End If
            CardEmpty.Children.Add(Body)
            ExtendList.Children.Add(CardEmpty)
            Return
        End If

        For Each Owner In Shown
            AddExtendCard(Owner)
        Next
    End Sub

    Private Function ExtendMatches(Owner As ModPlugin.PluginInfo) As Boolean
        If ExtendFilter = 1 AndAlso Not Owner.Enabled Then Return False
        If ExtendFilter = 2 AndAlso Owner.Enabled Then Return False
        If String.IsNullOrEmpty(ExtendSearch) Then Return True
        Dim Q = ExtendSearch.ToLowerInvariant()
        If (Owner.DisplayName & "").ToLowerInvariant().Contains(Q) Then Return True
        For Each P In ModPlugin.PluginPages.Where(Function(x) x.Owner Is Owner)
            If (P.Title & "").ToLowerInvariant().Contains(Q) Then Return True
        Next
        For Each S In ModPlugin.PluginSettings.Where(Function(x) x.Owner Is Owner)
            If ((S.Title & " ") & " " & (S.Description & "")).ToLowerInvariant().Contains(Q) Then Return True
        Next
        For Each N In ModPlugin.PluginNavItems.Where(Function(x) x.Owner Is Owner)
            If (N.Text & "").ToLowerInvariant().Contains(Q) Then Return True
        Next
        Return False
    End Function

    Private Sub AddExtendCard(Owner As ModPlugin.PluginInfo)
        Dim Pages = ModPlugin.PluginPages.Where(Function(p) p.Owner Is Owner).ToList()
        Dim Navs = ModPlugin.PluginNavItems.Where(Function(n) n.Owner Is Owner).ToList()
        Dim Sets = ModPlugin.PluginSettings.Where(Function(s) s.Owner Is Owner).ToList()

        Dim Card = NewCard($"{Owner.DisplayName}  v{Owner.Version}")
        Card.CanSwap = True

        Dim Body As New StackPanel
        Dim Sum As New TextBlock With {.Text = $"页面 {Pages.Count} 个 · 标题栏入口 {Navs.Count} 个 · 设置项 {Sets.Count} 个", .FontSize = 12, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 0, 0, 6)}
        Body.Children.Add(Sum)

        If Pages.Count + Navs.Count + Sets.Count = 0 Then
            Body.Children.Add(MkHint("这个插件启用了，但没有注册任何扩展点。"))
        Else
            Dim G As New Grid
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(300)})
            G.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Dim RowIndex As Integer = 0

            For Each Page In Pages
                G.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
                AddExtendRow(G, RowIndex, Page.Title, $"自定义页面（ID：{Page.Id}）", BuildOpenPageButton(Owner, Page))
                RowIndex += 1
            Next

            For Each NavItem In Navs
                If NavItem.Page IsNot Nothing Then Continue For '入口指向页面时，页面行已经覆盖
                G.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
                AddExtendRow(G, RowIndex, NavItem.Text, "标题栏入口：点击执行插件代码", Nothing)
                RowIndex += 1
            Next

            For Each Setting In Sets
                G.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
                AddExtendRow(G, RowIndex, Setting.Title, Setting.Description, BuildSettingControl(Owner, Setting))
                RowIndex += 1
            Next

            Body.Children.Add(G)
        End If

        Body.Margin = New Thickness(25, 46, 25, 20)
        Card.Children.Add(Body)
        Card.SwapControl = Body
        Card.IsSwapped = True 'True = 折叠，默认收起每张插件卡片
        ExtendList.Children.Add(Card)
    End Sub

    ''' <summary>
    ''' 参照「版本设置 → Mod 管理」的列表样式画一行：左标题/说明、右控件、悬停高亮、底部分隔线。
    ''' </summary>
    Private Sub AddExtendRow(Table As Grid, RowIndex As Integer, Title As String, Description As String, RightControl As FrameworkElement)
        Dim Row As New Border With {
            .CornerRadius = New CornerRadius(5),
            .Padding = New Thickness(10, 7, 10, 7),
            .Margin = New Thickness(0, 1, 0, 1),
            .Background = Brushes.Transparent,
            .BorderThickness = New Thickness(0, 0, 0, 1)
        }
        Row.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6")
        AddHandler Row.MouseEnter, Sub() Row.SetResourceReference(Border.BackgroundProperty, "ColorBrush5")
        AddHandler Row.MouseLeave, Sub() Row.Background = Brushes.Transparent

        Dim Inner As New Grid
        Inner.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Inner.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim Left As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 24, 0)}
        Left.Children.Add(New TextBlock With {.Text = Title, .FontSize = 13, .TextWrapping = TextWrapping.Wrap})
        If Not String.IsNullOrWhiteSpace(Description) Then
            Left.Children.Add(New TextBlock With {.Text = Description, .FontSize = 11.5, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 2, 0, 0), .TextWrapping = TextWrapping.Wrap})
        End If
        Grid.SetColumn(Left, 0)
        Inner.Children.Add(Left)

        If RightControl IsNot Nothing Then
            Dim Host As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center, .MinWidth = 150, .HorizontalAlignment = HorizontalAlignment.Right}
            Host.Children.Add(RightControl)
            Grid.SetColumn(Host, 1)
            Inner.Children.Add(Host)
        End If

        Row.Child = Inner
        Table.SetRow(Row, RowIndex)
        Table.SetColumnSpan(Row, 2)
        Table.Children.Add(Row)
    End Sub

    Private Function BuildOpenPageButton(Owner As ModPlugin.PluginInfo, Page As ModPlugin.PluginPageInfo) As FrameworkElement
        Dim Btn = MkButton("打开页面", Nothing)
        AddHandler Btn.Click, Sub(sender As Object, e As RoutedEventArgs) OpenPluginPage(Page)
        Return Btn
    End Function

    Private Sub OpenPluginPage(Page As ModPlugin.PluginPageInfo)
        Try
            Dim Index As Integer = 0
            For Each Item In ModPlugin.PluginNavItems
                If Item.Page IsNot Nothing AndAlso Item.Page Is Page Then
                    FrmMain.PageChange(CType(100 + Index, FormMain.PageType))
                    Return
                End If
                Index += 1
            Next
            Hint("这个页面没有对应的标题栏入口，无法直接打开。")
        Catch ex As Exception
            Logger.Error(ex, "打开插件页面失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Function BuildSettingControl(Owner As ModPlugin.PluginInfo, Setting As ModPlugin.PluginSetting) As FrameworkElement
        Dim Value = ModPlugin.GetSetting(Owner, Setting.Key, Setting.DefaultValue)
        Select Case Setting.SettingType
            Case ModPlugin.PluginSettingType.Boolean
                Dim Chk As New MyCheckBox With {.VerticalAlignment = VerticalAlignment.Center}
                Chk.Text = ""
                Chk.Checked = If(Value Is Nothing, False, CBool(Value))
                AddHandler Chk.Change, Sub(sender As Object, user As Boolean)
                                           Dim NewValue As Boolean = Chk.Checked
                                           ModPlugin.SetSetting(Owner, Setting.Key, NewValue)
                                           Try
                                               If Setting.Handler IsNot Nothing Then Setting.Handler.Invoke(NewValue)
                                           Catch ex As Exception
                                               Logger.Error(ex, "插件设置回调失败：" & Setting.Title, LogBehavior.Toast)
                                           End Try
                                       End Sub
                Return Chk
            Case ModPlugin.PluginSettingType.Number
                Dim PanNum As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
                Dim TxtNum As New MyTextBox With {.Width = 110, .Height = 30, .Text = If(Value Is Nothing, "0", CStr(Value)), .VerticalAlignment = VerticalAlignment.Center}
                PanNum.Children.Add(TxtNum)
                PanNum.Children.Add(New TextBlock With {.Text = $"（{Setting.MinValue} ~ {Setting.MaxValue}）", .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(8, 0, 0, 0), .Opacity = 0.6})
                Dim BtnNum = MkButton("保存", Nothing)
                BtnNum.Margin = New Thickness(8, 0, 0, 0)
                AddHandler BtnNum.Click, Sub(sender As Object, e As RoutedEventArgs)
                                             Dim Num As Integer
                                             If Not Integer.TryParse(TxtNum.Text, Num) Then
                                                 Hint("请输入整数", HintType.Red)
                                                 Return
                                             End If
                                             If Num < Setting.MinValue Then Num = Setting.MinValue
                                             If Num > Setting.MaxValue Then Num = Setting.MaxValue
                                             TxtNum.Text = Num.ToString()
                                             ModPlugin.SetSetting(Owner, Setting.Key, Num)
                                             Try
                                                 If Setting.Handler IsNot Nothing Then Setting.Handler.Invoke(Num)
                                             Catch ex As Exception
                                                 Logger.Error(ex, "插件设置回调失败：" & Setting.Title, LogBehavior.Toast)
                                             End Try
                                             Hint("已保存：" & Setting.Title)
                                         End Sub
                PanNum.Children.Add(BtnNum)
                Return PanNum
            Case ModPlugin.PluginSettingType.Choice
                Dim Cmb As New MyComboBox With {.Width = 200, .Height = 30, .VerticalAlignment = VerticalAlignment.Center}
                Dim Options = If(Setting.Options, New String() {})
                For Each Item In Options
                    Cmb.Items.Add(New MyComboBoxItem With {.Content = Item})
                Next
                Dim CurrentText = If(Value Is Nothing, If(Options.Length > 0, Options(0), ""), CStr(Value))
                Dim SelectedIndex As Integer = Array.IndexOf(Options, CurrentText)
                If SelectedIndex < 0 Then SelectedIndex = 0
                If Cmb.Items.Count > 0 Then Cmb.SelectedIndex = SelectedIndex
                AddHandler Cmb.SelectionChanged, Sub(sender As Object, e As SelectionChangedEventArgs)
                                                     Dim Now = Cmb.SelectedItem
                                                     Dim NewValue As String = If(TypeOf Now Is MyComboBoxItem, CType(Now, MyComboBoxItem).Content, CStr(Now))
                                                     If String.IsNullOrEmpty(NewValue) Then Return
                                                     ModPlugin.SetSetting(Owner, Setting.Key, NewValue)
                                                     Try
                                                         If Setting.Handler IsNot Nothing Then Setting.Handler.Invoke(NewValue)
                                                     Catch ex As Exception
                                                         Logger.Error(ex, "插件设置回调失败：" & Setting.Title, LogBehavior.Toast)
                                                     End Try
                                                 End Sub
                Return Cmb
            Case Else
                Dim Pan As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
                Dim Txt As New MyTextBox With {.Width = 300, .Height = 30, .Text = If(Value Is Nothing, "", CStr(Value)), .VerticalAlignment = VerticalAlignment.Center}
                Pan.Children.Add(Txt)
                Dim Btn = MkButton("保存", Nothing)
                Btn.Margin = New Thickness(8, 0, 0, 0)
                AddHandler Btn.Click, Sub(sender As Object, e As RoutedEventArgs)
                                          ModPlugin.SetSetting(Owner, Setting.Key, Txt.Text)
                                          Try
                                              If Setting.Handler IsNot Nothing Then Setting.Handler.Invoke(Txt.Text)
                                          Catch ex As Exception
                                              Logger.Error(ex, "插件设置回调失败：" & Setting.Title, LogBehavior.Toast)
                                          End Try
                                          Hint("已保存：" & Setting.Title)
                                      End Sub
                Pan.Children.Add(Btn)
                Return Pan
        End Select
    End Function

    ' ================= 分区三：开发说明 =================

    Private Sub BuildDoc()
        Dim Card = NewCard("插件开发说明")
        Dim Body As New StackPanel

        AddDocTitle(Body, "插件放在哪里")
        AddDocText(Body, "每个插件是 " & ModPlugin.PathPluginRoot & " 下的一个文件夹，文件夹里必须有 plugin.json。只有 plugin.json 的插件是「声明型插件」，只出现在列表里，不执行任何代码；带 DLL 并实现了 PCL.IPclPlugin 的插件，启用后会由启动器反射加载。新建的插件默认是「停用」状态，不会加载、日志里也没有插件行；在列表里点「启用」即可，启用后自动热重载，不用重启。")

        AddDocTitle(Body, "plugin.json")
        AddDocCode(Body, "{""id"": ""demo"", ""name"": ""示例"", ""version"": ""1.0.0""," & vbCrLf & " ""author"": ""你"", ""description"": ""说明""," & vbCrLf & " ""entry"": ""Demo.Plugin"", ""dll"": ""Demo.Plugin.dll""}")

        AddDocTitle(Body, "宿主接口")
        AddDocCode(Body, "Public Interface IPclPlugin" & vbCrLf & "    Sub OnLoad(Host As PluginHost)" & vbCrLf & "    Sub OnUnload()" & vbCrLf & "End Interface")
        AddDocText(Body, "OnLoad 拿到的 Host 可以调用：HostLog（写日志）、HostHint（左下角提示）、HostMessage（弹窗）、HostOpenUrl、HostOpenFolder、HostDataFolder（插件自己的数据目录，真实路径 PCLHarness\Plugins\<id>\data）。")

        AddDocTitle(Body, "扩展点（v2，可选）")
        AddDocCode(Body, "Public Interface IPclPluginUi" & vbCrLf & "    Function GetPages() As IEnumerable(Of PluginPageInfo)" & vbCrLf & "    Function GetNavItems() As IEnumerable(Of PluginNavItem)" & vbCrLf & "    Function GetSettings() As IEnumerable(Of PluginSetting)" & vbCrLf & "End Interface")
        AddDocText(Body, "页面：PluginPageInfo 需要一个唯一的 Id、标题 Title 与工厂 Factory（返回 UserControl）。标题栏入口：PluginNavItem 指定 Text 与 Page（或只给 Action）。设置项：PluginSetting 支持 Boolean 与 Text 两种类型，值自动保存到 plugins.settings.json 的 " & ModPlugin.PathSettingsFile & "。")

        AddDocTitle(Body, "事件总线（v6）")
        AddDocText(Body, "13 个内置事件：启动器就绪、页面切换、游戏启动前、游戏启动后、游戏退出、实例切换、主题切换、联机创建、联机加入、联机关闭、下载开始、下载完成、关闭前。Host.Events.On(""事件名"", 处理器, Priority:=10) 按优先级执行（数字大的先跑）；Host.Events.Once 只执行一次；处理器抛异常不会影响启动器。")
        AddDocCode(Body, "Host.Events.On(""页面切换"", Sub(Page As Integer, SubType As Integer)" & vbCrLf & "                               Host.LogDebug($""进入 {Page}/{SubType}"")" & vbCrLf & "                           End Sub, Priority:=10)")

        AddDocTitle(Body, "服务注册与后台任务（v6）")
        AddDocText(Body, "Host.Services.Register(Of 接口)(实现) 让插件之间互相调用，插件卸载时自动注销；Host.Tasks.Run(""任务名"", Sub(Ctx) ... End Sub) 跑后台任务，Ctx.Report(进度, 说明) 会显示在任务管理器里，用户可取消（Ctx.ThrowIfCancelled）。")
        AddDocCode(Body, "Public Class MyPlugin" & vbCrLf & "    Inherits PCL.PclPluginBase   '已实现全部接口，按需 Overrides" & vbCrLf & "End Class")

        AddDocTitle(Body, "控制台（v6）")
        AddDocText(Body, "插件页左侧「控制台」提供实时日志窗口与命令回显：help / events / plugins / services / tasks / emit 事件名 / clear。Host.DevLog(...) 或基类的 DevLog(...) 都能往这个窗口写内容；插件抛出的异常也会显示在这里。")

        AddDocTitle(Body, "权限指纹与签名（v6.3）")
        AddDocText(Body, "插件首次启用前会弹出权限确认：逐条展示 permissions 及说明，未声明权限也会写明，未签名会提示来源未知；确认后才加载。确认记录存在 Plugins\trust.json（schema 2），其中 permissionsHash 是把权限名小写、排序、逗号连接得到的字符串（如 events,files,ui）——它只是权限指纹，不含密钥、不是哈希摘要、也不是密码学签名，作用仅是权限集合变了就再问一次。")
        AddDocText(Body, "清单里的 signature 字段目前【只读不验】：界面显示为「发布者声明（未验证）」，任何人填任意字符串都会原样显示，请不要把它当作身份证明，也不要基于它做安全判断。计划中的真实验签：ECDsa P-256 + SHA-256，签 sha256(包字节)，清单增加 publisher（公钥）与 signature，验签通过才显示「已验证发布者」并在 trust.json 记录公钥指纹；SDK 会提供 tools\sign-plugin.ps1。可选字段还有 homepage、license。")
        AddDocTitle(Body, "实例右键菜单（v6.2）")
        AddDocText(Body, "实现 IPclPluginInstanceMenu 就能往「版本选择」页里每个实例卡片的右键菜单里加操作（插件项 → 分隔线 → 「实例设置」）：每次打开菜单时按当前实例调用一次，返回 PluginMenuItem（Text + Action），Action 抛异常会被隔离并计入熔断。完整示例见开发版 SDK 的 samples\InstanceMenu。")
        AddDocCode(Body, "Public Class MyPlugin" & vbCrLf & "    Inherits PCL.PclPluginBase" & vbCrLf & "    Implements PCL.IPclPluginInstanceMenu" & vbCrLf & "    Public Function GetInstanceMenuItems(Instance As PCL.PluginInstanceInfo) As IEnumerable(Of PCL.PluginMenuItem) _" & vbCrLf & "        Implements PCL.IPclPluginInstanceMenu.GetInstanceMenuItems" & vbCrLf & "        Return {New PCL.PluginMenuItem With {.Text = ""导出实例信息（我的插件）"", .Action = Sub() Host.Message(Instance.Folder)}}" & vbCrLf & "    End Function" & vbCrLf & "End Class")
        AddDocTitle(Body, "设置值读写")
        AddDocCode(Body, "Dim Value = ModPlugin.GetSetting(Host.Info, ""Key"", 默认值)" & vbCrLf & "ModPlugin.SetSetting(Host.Info, ""Key"", 新值)")

        AddDocText(Body, "同一个插件连续出错 3 次（加载失败、事件处理抛异常、界面扩展点抛异常都算）会自动禁用，并在插件列表里标成「已熔断」；重新勾选启用即清零重试。")

        AddDocTitle(Body, "v5 新增（历史版本）")
        AddDocCode(Body, "{""minApiVersion"": 5," & vbCrLf & " ""sha256"": ""DLL 的 SHA-256""," & vbCrLf & " ""permissions"": {" & vbCrLf & "   ""net"": {""hosts"": [""api.github.com"", ""*.example.com""]}," & vbCrLf & "   ""files"": {""read"": [""Downloads/""], ""write"": [""PCLHarness/PluginOut/""]}}}")
        AddDocText(Body, "permissions 写成对象可以做细化：net.hosts 是允许访问的域名（支持 *. 前缀通配与 *），files.read / files.write 是允许读写的路径（插件自己的数据目录始终可访问）。sha256 用于 DLL 完整性校验，与 plugin.json 不一致时拒绝加载；minApiVersion 高于启动器支持的版本时也会拒绝加载并提示更新。")
        AddDocCode(Body, "Host.Events.On(""页面切换"", Sub(Page As Integer, SubType As Integer) Host.LogInfo($""{Page}/{SubType}"") End Sub, Priority:=10)" & vbCrLf & "Dim N As Integer = Host.GetSetting(""loadCount"", 0)" & vbCrLf & "Host.SetSetting(""loadCount"", N + 1)" & vbCrLf & "Host.LogWarn(""这是一条警告"")")
        AddDocText(Body, "v5 宿主 API：Host.Events.On / Off（事件订阅，内置事件 ""页面切换""(Page, SubType)）、Host.GetSetting(Of T) / SetSetting(Of T)（强类型设置）、Host.LogDebug / LogInfo / LogWarn / LogError（日志分级）、Host.Ask（带按钮的对话框，返回按钮文字）。未声明 permissions 的老插件行为完全不变。")
        AddDocTitle(Body, "示例")
        AddDocText(Body, "两个完整示例都在开发版 SDK 里（Release 附件的 SDK 包，仓库里对应 samples/V6Demo 与 samples/DemoPlugin）：V6Demo 是当前版本示例，演示服务注册、优先级事件、一次性事件、后台任务；DemoPlugin 是 v5 示例，演示自定义页面、标题栏入口与两个设置项。编译：用 tools/sdk/build.ps1 指定 -Project 插件目录、-Launcher 启动器路径，它会处理引用、UTF-8 与 vbCrLf；也可以手动把 PCL Harness.exe 作为引用，用 vbc /target:library 生成 DLL，放进插件文件夹即可（源码存 UTF-8，自拼命令行要加 /codepage:65001，否则中文乱码；vbCrLf 需要引用 Microsoft.VisualBasic，否则改用 Environment.NewLine）。")
        AddDocTitle(Body, "发布与导入（v6.4）")
        AddDocText(Body, "插件包（.pclplugin）就是 zip：根目录放 plugin.json 与 DLL（或只套一层同名文件夹），不要带 data\、.backup\、bin\、obj\。打包可用 tools\pack-plugin.ps1 -Project 插件目录，或自己压 zip。安装时点「插件管理 → 导入插件…」多选，或直接把 .pclplugin / 含清单的 zip / 带 AssemblyMetadata 的 DLL 拖进启动器窗口；重名会问是否覆盖，覆盖会保留 data\、设置与启用状态，只有权限指纹变化时才重新弹确认。单 DLL 需要在程序集里写 PclPluginId / PclPluginName / PclPluginVersion / PclPluginAuthor（可选 PclPluginDescription）。导入只做结构与 sha256 校验，不做代码审计。")

        Body.Margin = New Thickness(25, 40, 25, 20)
        Card.Children.Add(Body)
        PanContent.Children.Add(Card)
    End Sub

    ' ================= 通用控件构造 =================

    Private Function NewCard(Title As String) As MyCard
        Dim Card As New MyCard With {.Title = Title, .Margin = New Thickness(0, 0, 0, 15)}
        Return Card
    End Function

    ''' <summary>插件卡片右侧的小按钮（比工具条按钮更紧凑）。</summary>
    Private Function MkButtonSmall(Text As String, Action As Action) As MyButton
        Dim Btn As New MyButton With {.Text = Text, .Height = 26, .Padding = New Thickness(12, 0, 12, 0), .Margin = New Thickness(0, 6, 0, 0), .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Right}
        If Action IsNot Nothing Then AddHandler Btn.Click, Sub(sender As Object, e As RoutedEventArgs) Action.Invoke()
        Return Btn
    End Function

    Private Sub AddPlainRow(Table As Grid, RowIndex As Integer, Name As String, Value As String)
        Dim Lab As New TextBlock With {.Text = Name, .FontSize = 13, .Margin = New Thickness(0, 7, 16, 7), .VerticalAlignment = VerticalAlignment.Center}
        Table.SetRow(Lab, RowIndex)
        Table.SetColumn(Lab, 0)
        Table.Children.Add(Lab)
        Dim Val As New TextBlock With {.Text = Value, .FontSize = 13, .Margin = New Thickness(0, 7, 0, 7), .VerticalAlignment = VerticalAlignment.Center, .TextWrapping = TextWrapping.Wrap, .Foreground = Res("ColorBrushGray2")}
        Table.SetRow(Val, RowIndex)
        Table.SetColumn(Val, 1)
        Table.Children.Add(Val)
    End Sub

    Private Function MkHint(Text As String) As TextBlock
        Return New TextBlock With {.Text = Text, .FontSize = 12, .Foreground = Res("ColorBrushGray2"), .TextWrapping = TextWrapping.Wrap}
    End Function

    Private Function MkButton(Text As String, Action As Action) As MyButton
        Dim Btn As New MyButton With {.Text = Text, .Height = 30, .Padding = New Thickness(16, 0, 16, 0), .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center}
        If Action IsNot Nothing Then AddHandler Btn.Click, Sub(sender As Object, e As RoutedEventArgs) Action.Invoke()
        Return Btn
    End Function

    Private Sub AddDocTitle(Panel As StackPanel, Text As String)
        Panel.Children.Add(New TextBlock With {.Text = Text, .FontSize = 14.5, .FontWeight = FontWeights.SemiBold, .Margin = New Thickness(0, 14, 0, 6)})
    End Sub

    Private Sub AddDocText(Panel As StackPanel, Text As String)
        Panel.Children.Add(New TextBlock With {.Text = Text, .FontSize = 12.5, .TextWrapping = TextWrapping.Wrap, .LineHeight = 21, .Margin = New Thickness(0, 0, 0, 6), .Foreground = Res("ColorBrushGray2")})
    End Sub

    Private Sub AddDocCode(Panel As StackPanel, Code As String)
        Dim Box As New Border With {
            .Background = Res("ColorBrush6"),
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(14, 10, 14, 10),
            .Margin = New Thickness(0, 2, 0, 8),
            .HorizontalAlignment = HorizontalAlignment.Left
        }
        Box.Child = New TextBlock With {
            .Text = Code,
            .FontFamily = New FontFamily("Consolas, Microsoft YaHei UI"),
            .FontSize = 12.5,
            .LineHeight = 20,
            .TextWrapping = TextWrapping.NoWrap
        }
        Panel.Children.Add(Box)
    End Sub

    Private Function Res(Key As String) As Brush
        Return CType(FindResource(Key), Brush)
    End Function

End Class
