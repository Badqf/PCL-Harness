'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLHarness"
    '用于微软登录的 ClientId
    '优先级：环境变量 PCL_MS_CLIENT_ID > PCL\Secrets.json 的 MsClientId
    Public ReadOnly Property OAuthClientId As String
        Get
            Return SecretValue("MsClientId", "PCL_MS_CLIENT_ID")
        End Get
    End Property
    'CurseForge API Key
    '优先级：环境变量 PCL_CURSEFORGE_API_KEY > PCL\Secrets.json 的 CurseForgeApiKey
    Public ReadOnly Property CurseForgeAPIKey As String
        Get
            Return SecretValue("CurseForgeApiKey", "PCL_CURSEFORGE_API_KEY")
        End Get
    End Property
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

    ''' <summary>
    ''' 读取可选凭据：优先环境变量，其次 exe 同级 PCL\Secrets.json。
    ''' 文件不存在或解析失败时返回空字符串，只在日志里留一条警告，不影响启动。
    ''' </summary>
    Private Function SecretValue(JsonKey As String, EnvironmentKey As String) As String
        Dim FromEnvironment As String = Environment.GetEnvironmentVariable(EnvironmentKey)
        If Not String.IsNullOrWhiteSpace(FromEnvironment) Then Return FromEnvironment.Trim()
        If Not _SecretsLoaded Then LoadSecrets()
        If _Secrets Is Nothing Then Return ""
        Dim Token As Object = _Secrets(JsonKey)
        Return If(Token Is Nothing, "", Token.ToString().Trim())
    End Function
    Private _SecretsLoaded As Boolean = False
    Private _Secrets As Newtonsoft.Json.Linq.JObject = Nothing
    Private Sub LoadSecrets()
        _SecretsLoaded = True
        Try
            Dim SecretsPath As String = Paths.BaseThenName & "Secrets.json"
            If Not IO.File.Exists(SecretsPath) Then Return
            _Secrets = Newtonsoft.Json.Linq.JObject.Parse(IO.File.ReadAllText(SecretsPath))
            Logger.Info($"已读取可选凭据：{SecretsPath}")
        Catch ex As Exception
            Logger.Warn(ex, "读取 PCL\Secrets.json 失败，将忽略其中的凭据")
        End Try
    End Sub


#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    ''' <summary>
    ''' PCL Harness：启用蓝黑深色主题。
    ''' </summary>
    Public Property IsDarkMode As Boolean = True
    Public ColorHue As Integer = 220, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme

            If IsDarkMode Then
                '=== 蓝黑深色主题：文字反相为浅色，卡片/面板转为深色，色相保持一致 ===
                Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.12, 93)
                Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 72)
                Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 58 + ColorLightAdjust)
                Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 73 + ColorLightAdjust)
                Color5 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.5, 34)
                Color6 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.45, 26 + ColorLightAdjust * 0.2)
                Color7 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.4, 20)
                Color8 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.4, 12)
                ColorGray1 = New MyColor().FromHSL2(210, 12, 93)
                ColorGray2 = New MyColor().FromHSL2(210, 12, 78)
                ColorGray3 = New MyColor().FromHSL2(210, 12, 66)
                ColorGray4 = New MyColor().FromHSL2(210, 12, 54)
                ColorGray5 = New MyColor().FromHSL2(210, 14, 38)
                ColorGray6 = New MyColor().FromHSL2(210, 14, 27)
                ColorGray7 = New MyColor().FromHSL2(210, 16, 21)
                ColorGray8 = New MyColor().FromHSL2(210, 18, 15)
            Else
                Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
                Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
                Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
                Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
                Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
                Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
                Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
                Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
                ColorGray1 = New MyColor().FromHSL2(210, 14, 16)
                ColorGray2 = New MyColor().FromHSL2(210, 12, 28)
                ColorGray3 = New MyColor().FromHSL2(210, 10, 42)
                ColorGray4 = New MyColor().FromHSL2(210, 9, 56)
                ColorGray5 = New MyColor().FromHSL2(210, 9, 70)
                ColorGray6 = New MyColor().FromHSL2(210, 10, 82)
                ColorGray7 = New MyColor().FromHSL2(210, 12, 90)
                ColorGray8 = New MyColor().FromHSL2(210, 14, 96)
            End If
            ColorBg0 = If(IsDarkMode, Color4, Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2)
            ColorBg1 = New MyColor(If(IsDarkMode, 200, 190), Color7)
            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            '灰色阶与半透明底色：深色/浅色分别计算
            Application.Current.Resources("ColorBrushGray1") = New SolidColorBrush(ColorGray1)
            Application.Current.Resources("ColorBrushGray2") = New SolidColorBrush(ColorGray2)
            Application.Current.Resources("ColorBrushGray3") = New SolidColorBrush(ColorGray3)
            Application.Current.Resources("ColorBrushGray4") = New SolidColorBrush(ColorGray4)
            Application.Current.Resources("ColorBrushGray5") = New SolidColorBrush(ColorGray5)
            Application.Current.Resources("ColorBrushGray6") = New SolidColorBrush(ColorGray6)
            Application.Current.Resources("ColorBrushGray7") = New SolidColorBrush(ColorGray7)
            Application.Current.Resources("ColorBrushGray8") = New SolidColorBrush(ColorGray8)
            Application.Current.Resources("ColorBrushHalfWhite") = New SolidColorBrush(If(IsDarkMode, New MyColor(13, 255, 255, 255), New MyColor(34, 255, 255, 255)))
            Application.Current.Resources("ColorBrushHover") = New SolidColorBrush(If(IsDarkMode, New MyColor().FromHSL2(ColorHue, ColorSat * 0.4, 34), New MyColor().FromHSL2(ColorHue, 18, 90)))
            Application.Current.Resources("ColorBrushBackgroundTransparentSidebar") = New SolidColorBrush(New MyColor(If(IsDarkMode, 241, 226), Color8))
            Application.Current.Resources("ColorBrushTitleBarIcon") = New SolidColorBrush(If(IsDarkMode, New MyColor(232, 236, 244), New MyColor(52, 62, 78)))
            '深色主题下阴影色必须用近黑，否则用浅色文字色会变成白色发光
            Application.Current.Resources("ColorObject1") = CType(If(IsDarkMode, New MyColor(6, 9, 14), Color1), Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            If IsDarkMode Then
                '深色主题：自绘标题栏为近黑的深蓝渐变
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat * 0.5, 10)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat * 0.55, 14)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat * 0.5, 10)})
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            End If
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If IsDarkMode Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.35, 12)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.32, 10)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.35, 12)})
                FrmMain.PanForm.Background = Brush
            ElseIf Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    '=== 自编版：可选的更新检查源（开源版上游此处为占位实现，不连官方接口）===
    'JSON 格式：{"version":"2.13.2.0","url":"https://example.com/download","changelog":"本次更新内容……"}
    '配置方式：设置环境变量 PCL_UPDATE_JSON_URL 指向该 JSON（http/https/本地路径），或直接改下面的常量。
    '留空时点击“检查更新”只会提示未配置，不会发起任何网络请求。
    Private Const UpdateJsonUrlDefault As String = ""

    Private Function UpdateJsonUrl() As String
        Dim env As String = Environment.GetEnvironmentVariable("PCL_UPDATE_JSON_URL")
        Return If(String.IsNullOrWhiteSpace(env), UpdateJsonUrlDefault, env)
    End Function

    ''' <summary>
    ''' 自编版更新检查：读取自定义更新源 JSON，比较版本后提示并给出下载入口。
    ''' </summary>
    Friend Sub UpdateCheckByButton()
        Dim url As String = UpdateJsonUrl()
        If String.IsNullOrWhiteSpace(url) Then
            Hint("未配置自编版更新源：请设置环境变量 PCL_UPDATE_JSON_URL（指向版本信息 JSON）")
            Return
        End If
        Hint("正在检查更新……")
        RunInNewThread(Sub()
            Try
                Dim raw As String
                If url.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                    Using client As New HttpClient()
                        client.Timeout = TimeSpan.FromSeconds(10)
                        client.DefaultRequestHeaders.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
                        raw = client.GetStringAsync(url).Result
                    End Using
                Else
                    raw = IO.File.ReadAllText(url)
                End If
                Dim json As JObject = JObject.Parse(raw)
                Dim latest As String = json("version")?.ToString()
                Dim download As String = json("url")?.ToString()
                Dim changelog As String = json("changelog")?.ToString()
                If String.IsNullOrWhiteSpace(latest) Then Throw New Exception("更新源 JSON 缺少 version 字段")
                If McUtils.CompareVersion(latest, VersionBaseName) <= 0 Then
                    Hint($"已是最新自编版（{VersionBaseName}）")
                    Return
                End If
                Dim body As String = $"发现新版本 {latest}（当前 {VersionBaseName}）。"
                If Not String.IsNullOrWhiteSpace(changelog) Then body &= vbCrLf & changelog
                If String.IsNullOrWhiteSpace(download) Then
                    MyMsgBox(body, "检查更新", "我知道了")
                ElseIf MyMsgBox(body, "检查更新", "打开下载页", "取消") = 1 Then
                    OpenWebsite(download)
                End If
            Catch ex As Exception
                Hint("检查更新失败：" & ex.Message)
            End Try
        End Sub, "SelfUpdateCheck")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
