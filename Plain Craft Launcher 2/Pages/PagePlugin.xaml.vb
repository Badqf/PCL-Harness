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

    ''' <summary>当前分区：0 插件管理 / 1 扩展点 / 2 开发说明。</summary>
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
    Public Sub SwitchSection(Index As Integer, Optional Animate As Boolean = True)
        SectionIndex = Index
        If PanContent Is Nothing Then Return
        PanContent.Children.Clear()
        Select Case Index
            Case 1
                BuildExtend()
            Case 2
                BuildDoc()
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
        Dim GridList As New Grid
        GridList.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(300)})
        GridList.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        For i = 0 To Shown.Count - 1
            GridList.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            BuildPluginRow(GridList, i, Shown(i))
        Next
        ManageListBody.Children.Add(GridList)
    End Sub

    Private Sub BuildPluginRow(Table As Grid, RowIndex As Integer, Info As ModPlugin.PluginInfo)
        '左：名称 + 作者/状态 + 描述
        Dim Left As New StackPanel With {.Margin = New Thickness(0, 8, 24, 8), .VerticalAlignment = VerticalAlignment.Center}
        Dim LineName As New StackPanel With {.Orientation = Orientation.Horizontal}
        LineName.Children.Add(New TextBlock With {.Text = Info.DisplayName, .FontSize = 13.5, .FontWeight = FontWeights.SemiBold, .VerticalAlignment = VerticalAlignment.Center})
        If Not String.IsNullOrWhiteSpace(Info.Version) Then
            LineName.Children.Add(New TextBlock With {.Text = "  v" & Info.Version, .FontSize = 11.5, .Margin = New Thickness(6, 0, 0, 1), .VerticalAlignment = VerticalAlignment.Center, .Foreground = Res("ColorBrushGray2")})
        End If
        Left.Children.Add(LineName)

        Dim LineMeta As New TextBlock With {.FontSize = 11.5, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 2, 0, 0), .TextWrapping = TextWrapping.Wrap, .MaxWidth = 300}
        LineMeta.Inlines.Add(New Run("作者：" & If(String.IsNullOrWhiteSpace(Info.Author), "未知", Info.Author)))
        LineMeta.Inlines.Add(New Run("　·　" & Info.StatusText))
        Left.Children.Add(LineMeta)

        If Not String.IsNullOrWhiteSpace(Info.Description) Then
            Left.Children.Add(New TextBlock With {.Text = Info.Description, .FontSize = 11.5, .Foreground = Res("ColorBrushGray2"), .Margin = New Thickness(0, 3, 0, 0), .TextWrapping = TextWrapping.Wrap, .MaxWidth = 300})
        End If
        Table.SetRow(Left, RowIndex)
        Table.SetColumn(Left, 0)
        Table.Children.Add(Left)

        '右：启停复选框
        Dim Right As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 8, 0, 8)}
        Dim Chk As New MyCheckBox With {.VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 12, 0)}
        Chk.Text = If(Info.Enabled, "已启用", "已禁用")
        Chk.Checked = (Info.Enabled AndAlso String.IsNullOrEmpty(Info.[Error]))
        AddHandler Chk.Change, Sub(sender As Object, user As Boolean)
                                   Dim Value As Boolean = Chk.Checked
                                   Try
                                       ModPlugin.SetEnabled(Info, Value)
                                       If Value Then
                                           Hint(Info.DisplayName & " 已启用，重启后生效！", HintType.Green)
                                       Else
                                           Hint(Info.DisplayName & " 已禁用，重启后生效！", HintType.Blue)
                                       End If
                                   Catch ex As Exception
                                       Logger.Error(ex, "切换插件状态失败", LogBehavior.Toast)
                                   End Try
                                   RefreshNav()
                                   RefreshCurrentSection()
                               End Sub
        Right.Children.Add(Chk)


        Table.SetRow(Right, RowIndex)
        Table.SetColumn(Right, 1)
        Table.Children.Add(Right)
    End Sub

    ' ================= 分区二：扩展点（每个插件一张可展开卡片） =================

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
            Dim CardEmpty = NewCard("插件扩展点")
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
        AddDocText(Body, "每个插件是 " & ModPlugin.PathPluginRoot & " 下的一个文件夹，文件夹里必须有 plugin.json。只有 plugin.json 的插件是「声明型插件」，只出现在列表里，不执行任何代码；带 DLL 并实现了 PCL.IPclPlugin 的插件，启用后会由启动器反射加载。")

        AddDocTitle(Body, "plugin.json")
        AddDocCode(Body, "{""id"": ""demo"", ""name"": ""示例"", ""version"": ""1.0.0""," & vbCrLf & " ""author"": ""你"", ""description"": ""说明""," & vbCrLf & " ""entry"": ""Demo.Plugin"", ""dll"": ""Demo.Plugin.dll""}")

        AddDocTitle(Body, "宿主接口")
        AddDocCode(Body, "Public Interface IPclPlugin" & vbCrLf & "    Sub OnLoad(Host As PluginHost)" & vbCrLf & "    Sub OnUnload()" & vbCrLf & "End Interface")
        AddDocText(Body, "OnLoad 拿到的 Host 可以调用：HostLog（写日志）、HostHint（左下角提示）、HostMessage（弹窗）、HostOpenUrl、HostOpenFolder、HostDataFolder（插件自己的数据目录）。")

        AddDocTitle(Body, "扩展点（v2，可选）")
        AddDocCode(Body, "Public Interface IPclPluginUi" & vbCrLf & "    Function GetPages() As IEnumerable(Of PluginPageInfo)" & vbCrLf & "    Function GetNavItems() As IEnumerable(Of PluginNavItem)" & vbCrLf & "    Function GetSettings() As IEnumerable(Of PluginSetting)" & vbCrLf & "End Interface")
        AddDocText(Body, "页面：PluginPageInfo 需要一个唯一的 Id、标题 Title 与工厂 Factory（返回 UserControl）。标题栏入口：PluginNavItem 指定 Text 与 Page（或只给 Action）。设置项：PluginSetting 支持 Boolean 与 Text 两种类型，值自动保存到 plugins.settings.json 的 " & ModPlugin.PathSettingsFile & "。")

        AddDocTitle(Body, "设置值读写")
        AddDocCode(Body, "Dim Value = ModPlugin.GetSetting(Host.Info, ""Key"", 默认值)" & vbCrLf & "ModPlugin.SetSetting(Host.Info, ""Key"", 新值)")

        AddDocTitle(Body, "示例")
        AddDocText(Body, "完整示例见发布包 samples/DemoPlugin（DemoPlugin.vb + build.ps1），包含自定义页面、标题栏入口与两个设置项。编译：把 PCL Harness.exe 作为引用，用 vbc /target:library 生成 DLL，放进插件文件夹即可。")

        Body.Margin = New Thickness(25, 40, 25, 20)
        Card.Children.Add(Body)
        PanContent.Children.Add(Card)
    End Sub

    ' ================= 通用控件构造 =================

    Private Function NewCard(Title As String) As MyCard
        Dim Card As New MyCard With {.Title = Title, .Margin = New Thickness(0, 0, 0, 15)}
        Return Card
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
