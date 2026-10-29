Public Class PageLoginMs

    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        Dim IndexBefore = ComboAccounts.SelectedIndex
        '刷新下拉框列表
        ComboAccounts.Items.Clear()
        ComboAccounts.Items.Add(New MyComboBoxItem With {.Content = "添加新账号"})
        Try
            Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
            For Each Account In MsJson
                Dim Item As MyListItem = CType(FindResource("ComboBoxItemTemplateWithDelete"), DataTemplate).LoadContent()
                Item.Tag = Account.Value.ToString
                Item.Title = Account.Key
                CType(Item.Buttons(0), MyIconButton).Tag = Account.Key
                ComboAccounts.Items.Add(Item)
            Next
        Catch ex As Exception
            Logger.Error(ex, $"微软登录信息出错，登录信息已被重置", LogBehavior.Toast)
            Settings.Set("LoginMsJson", "{}")
        End Try
        '如果不保留输入，刷新列表后自动选择第一项
        ComboAccounts.SelectedIndex = If(KeepInput, Math.Max(0, IndexBefore), 0)
    
        '根据是否配置了客户端 ID 更新提示文案
        If String.IsNullOrWhiteSpace(OAuthClientId) Then
            LabMsNotWired.Text = "尚未配置微软登录客户端 ID：请在 PCLHarness\Secrets.json 中填写 MsClientId，或设置环境变量 PCL_MS_CLIENT_ID。也可以先使用「离线登录」或「第三方登录」。"
            LabMsNotWired.Foreground = CType(FindResource("ColorBrushRedLight"), System.Windows.Media.Brush)
            LabMsNotWired.Visibility = Visibility.Visible
        Else
            'AppID 已通过 Mojang 审核并加入白名单，登录流程默认可用，不再需要额外的长说明（步骤由登录弹窗给出）
            LabMsNotWired.Text = ""
            LabMsNotWired.Visibility = Visibility.Collapsed
        End If
End Sub
    Private Sub ComboAccounts_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboAccounts.SelectionChanged
        If AniControlEnabled <> 0 OrElse ComboAccounts.SelectedItem Is Nothing OrElse ComboAccounts.ContentPresenter Is Nothing Then Return
        If TypeOf ComboAccounts.SelectedItem Is MyListItem Then
            ComboAccounts.ContentPresenter.Content = CType(ComboAccounts.SelectedItem, MyListItem).Title
        ElseIf TypeOf ComboAccounts.SelectedItem Is MyComboBoxItem Then
            ComboAccounts.ContentPresenter.Content = CType(ComboAccounts.SelectedItem, MyComboBoxItem).Content
        End If
    End Sub

    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginMs
        If FrmLoginMs Is Nothing Then Return New McLoginMs With {.OAuthRefreshToken = Settings.Get(Of String)("CacheMsV2OAuthRefresh"), .UserName = Settings.Get(Of String)("CacheMsV2Name")}
        Dim Result As McLoginMs = Nothing
        RunInUiWait(
        Sub()
            If FrmLoginMs.ComboAccounts.SelectedIndex = 0 Then
                Result = New McLoginMs
            Else
                Dim Item As MyListItem = FrmLoginMs.ComboAccounts.SelectedItem
                Result = New McLoginMs With {.OAuthRefreshToken = Item.Tag, .UserName = Item.Title}
            End If
        End Sub)
        Return Result
    End Function
    ''' <summary>
    ''' 当前页面的登录信息是否有效。
    ''' </summary>
    Public Shared Function IsVaild(LoginData As McLoginMs) As String
        If LoginData.OAuthRefreshToken = "" Then
            Return "请在登录账号后再启动游戏！"
        Else
            Return ""
        End If
    End Function
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function

    Private Sub BtnLogin_Click(sender As Object, e As EventArgs) Handles BtnLogin.Click
        ' 未配置 Client ID 时直接给出明确提示，避免出现看不懂的 OAuth 报错
        If String.IsNullOrWhiteSpace(OAuthClientId) Then
            Hint("尚未配置微软登录的 Client ID（PCLHarness\Secrets.json 中的 MsClientId），暂时无法使用正版登录，请先用离线登录。", HintType.Red)
            Return
        End If
        ComboAccounts.IsEnabled = False
        BtnLogin.IsEnabled = False
        BtnLogin.Text = "0%"
        RunInNewThread(
        Sub()
            Try
                McLoginMsLoader.Start(GetLoginData(), IsForceRestart:=True)
                Do While McLoginMsLoader.State = LoadState.Loading
                    RunInUi(Sub() BtnLogin.Text = Math.Round(McLoginMsLoader.Progress * 100) & "%")
                    Thread.Sleep(50)
                Loop
                If McLoginMsLoader.State = LoadState.Finished Then
                    RunInUi(Sub() FrmLaunchLeft.RefreshPage(False, True))
                ElseIf McLoginMsLoader.State = LoadState.Canceled Then
                    Throw New OperationCanceledException
                ElseIf McLoginMsLoader.Error Is Nothing Then
                    Throw New Exception("未知错误！")
                Else
                    Throw New Exception(McLoginMsLoader.Error.Message, McLoginMsLoader.Error)
                End If
            Catch ex As Exception
                If ex.IsCanceled Then
                ElseIf ex.Message.StartsWithF("$") Then
                    Hint(ex.Message.TrimStart("$"), HintType.Red)
                ElseIf TypeOf ex Is Security.Authentication.AuthenticationException AndAlso ex.Message.Contains("SSL/TLS") Then
                    Logger.Error(ex, $"正版登录验证失败，请考虑在 [设置 → 其他] 中关闭 [在正版登录时验证 SSL 证书]，然后再试。{vbCrLf}{vbCrLf}原始错误信息：", LogBehavior.Alert)
                Else
                    Logger.Error(ex, "正版登录尝试失败", LogBehavior.Alert)
                End If
            Finally
                RunInUi(
                Sub()
                    ComboAccounts.IsEnabled = True
                    BtnLogin.IsEnabled = True
                    BtnLogin.Text = "登录"
                End Sub)
            End Try
        End Sub, "Ms Login")
    End Sub

End Class
