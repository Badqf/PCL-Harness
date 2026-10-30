'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLHarness"
    '用于微软登录的 ClientId（内置默认值，随启动器分发，陌生电脑无需配置）
    '优先级：环境变量 PCL_MS_CLIENT_ID > PCL\Secrets.json 的 MsClientId > 内置默认值
    Private Const BuiltinMsClientId As String = "ee041193-cb2f-471b-8a5b-561a8f3cf37c"
    Public ReadOnly Property OAuthClientId As String
        Get
            Return SecretValue("MsClientId", "PCL_MS_CLIENT_ID", BuiltinMsClientId)
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
    Private Function SecretValue(JsonKey As String, EnvironmentKey As String, Optional Builtin As String = "") As String
        Dim FromEnvironment As String = Environment.GetEnvironmentVariable(EnvironmentKey)
        If Not String.IsNullOrWhiteSpace(FromEnvironment) Then Return FromEnvironment.Trim()
        If Not _SecretsLoaded Then LoadSecrets()
        If _Secrets Is Nothing Then Return Builtin
        Dim Token As Object = _Secrets(JsonKey)
        Dim FromFile As String = If(Token Is Nothing, "", Token.ToString().Trim())
        Return If(FromFile = "", Builtin, FromFile)
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

    '=== 自编版自动更新（3.2.6）===
    '版本信息取自 GitHub Release；加速源优先 ghfast（release 资产），API 走 gh-proxy（ghfast 不支持 api.github.com），官方源兜底。
    '可用环境变量 PCL_UPDATE_JSON_URL 覆盖 API 地址（需返回与 GitHub Release 相同结构的 JSON）。
    Private Const UpdateRepo As String = "Badqf/PCL-Harness"
    Private Const UpdateAssetName As String = "PCL-Harness.exe"
    Private Const UpdateGhfastPrefix As String = "https://ghfast.top/"
    Private Const UpdateGhproxyPrefix As String = "https://gh-proxy.com/"

    Public Class UpdateInfo
        Public Version As String = ""
        Public Tag As String = ""
        Public HtmlUrl As String = ""
        Public Body As String = ""
        Public ReadOnly DownloadUrls As New List(Of String)
    End Class

    ''' <summary>更新信息 API 地址（加速源优先，官方兜底）。</summary>
    Private Function UpdateApiUrls() As List(Of String)
        Dim Result As New List(Of String)
        '只有显式打开调试开关时才认环境变量，避免被外部脚本悄悄指向别的更新源
        If IsDebugUpdate() Then
            Dim EnvUrl As String = Environment.GetEnvironmentVariable("PCL_UPDATE_JSON_URL")
            If Not String.IsNullOrWhiteSpace(EnvUrl) Then Result.Add(EnvUrl)
        End If
        Result.Add($"{UpdateGhproxyPrefix}https://api.github.com/repos/{UpdateRepo}/releases/latest")
        Result.Add($"https://api.github.com/repos/{UpdateRepo}/releases/latest")
        Return Result
    End Function

    ''' <summary>读取最新 Release；所有源都失败时返回 Nothing。</summary>
    Public Function FetchUpdateInfo() As UpdateInfo
        For Each Url As String In UpdateApiUrls()
            Try
                Dim Raw As String = NetRequestByLoader(Url, 12000, True)
                If String.IsNullOrWhiteSpace(Raw) Then Continue For
                Dim Json As JObject = JObject.Parse(Raw)
                Dim Info As New UpdateInfo With {
                    .Tag = If(Json("tag_name")?.ToString(), ""),
                    .Body = If(Json("body")?.ToString(), ""),
                    .HtmlUrl = If(Json("html_url")?.ToString(), "")
                }
                Info.Version = Info.Tag.TrimStart("v"c, "V"c)
                For Each Asset As JToken In If(Json("assets"), New JArray())
                    If Asset("name")?.ToString() = UpdateAssetName Then
                        Dim Download As String = Asset("browser_download_url")?.ToString()
                        If Not String.IsNullOrWhiteSpace(Download) Then
                            Info.DownloadUrls.Add(UpdateGhfastPrefix & Download)
                            Info.DownloadUrls.Add(Download)
                            Info.DownloadUrls.Add(UpdateGhproxyPrefix & Download)
                        End If
                    End If
                Next
                If String.IsNullOrWhiteSpace(Info.Version) Then Throw New Exception("JSON 缺少 tag_name")
                LatestCheckedVersion = Info.Version
                Logger.Info($"[Update] 已从 {Url} 获取最新版本 {Info.Version}（下载源 {Info.DownloadUrls.Count} 个）")
                Return Info
            Catch ex As Exception
                Logger.Warn(ex, $"[Update] 读取更新信息失败：{Url}")
            End Try
        Next
        Return Nothing
    End Function

    Private Function IsDebugUpdate() As Boolean
        Return Environment.GetEnvironmentVariable("PCL_UPDATE_DEBUG") = "1"
    End Function

    Private Function IsNewerVersion(Latest As String) As Boolean
        If IsDebugUpdate() Then Return True
        Return McUtils.CompareVersion(Latest, VersionBaseName) > 0
    End Function

    ''' <summary>手动点击「检查更新」。</summary>
    Friend Sub UpdateCheckByButton()
        Hint("正在检查更新……")
        RunInNewThread(Sub()
            Try
                Dim Info As UpdateInfo = FetchUpdateInfo()
                If Info Is Nothing Then
                    RunInUi(Sub() Hint("检查更新失败：加速源与 GitHub 都无法连接，请稍后再试"))
                    Return
                End If
                If Not IsNewerVersion(Info.Version) Then
                    RunInUi(Sub() Hint($"已是最新版本（{VersionBaseName}）"))
                    Return
                End If
                RunInUi(Sub() ShowUpdatePrompt(Info))
            Catch ex As Exception
                RunInUi(Sub() Hint("检查更新失败：" & ex.Message))
            End Try
        End Sub, "UpdateCheck")
    End Sub

    ''' <summary>启动后自动检查更新（受 设置 → 其他 → 启动器更新 控制）。</summary>
    Friend Sub StartAutoUpdateCheck()
        Dim Mode As Integer = Settings.Get(Of Integer)("SystemSystemUpdate")
        If Not IsDebugUpdate() Then
            If Mode = 3 Then Return '关闭更新提示
            If Mode = 2 Then Return '仅重大漏洞：自编版不区分严重级别，交给手动检查
        End If
        Dim Silent As Boolean = (Mode = 0) '在有新版本时自动下载：静默下载，退出时替换
        RunInNewThread(Sub()
            Threading.Thread.Sleep(1200)
            RunInUi(Sub() Hint("正在检测更新……", HintType.Blue, False))
            Try
                Dim Info As UpdateInfo = FetchUpdateInfo()
                If Info Is Nothing Then Return
                If Not IsNewerVersion(Info.Version) Then
                    RunInUi(Sub() Hint($"当前为最新版 {VersionBaseName}", HintType.Blue, False))
                    Return
                End If
                If SnoozedUpdateVersion = Info.Version AndAlso Not IsDebugUpdate() Then Return
                If Silent Then
                    DownloadUpdateSilently(Info)
                Else
                    RunInUi(Sub() ShowUpdatePrompt(Info))
                End If
            Catch ex As Exception
                Logger.Warn(ex, "[Update] 自动检查更新失败")
            End Try
        End Sub, "UpdateAutoCheck", Threading.ThreadPriority.BelowNormal)
    End Sub

    ''' <summary>上一次成功查到的线上最新版本号；没有查过则是空字符串。</summary>
    Friend LatestCheckedVersion As String = ""

    ''' <summary>本次运行中点过「稍后」的版本：不写设置，重启后照常提示。</summary>
    Private SnoozedUpdateVersion As String = ""

    ''' <summary>已在后台静默下载、等待退出时应用的更新包。</summary>
    Private PendingUpdateExe As String = ""
    Private PendingUpdateVersion As String = ""

    ''' <summary>当前正在进行的更新下载任务；Nothing 表示没有。</summary>
    Private UpdateDownloadTask As LoaderDownload = Nothing


    ''' <summary>
    ''' 把更新包作为普通下载任务放进任务栏：右下角显示进度圆环，点进去能看进度并手动取消。
    ''' 左下角只提示一次；下载完成后静默等待，退出启动器时替换，下次打开即为新版本（不自动重启）。
    ''' </summary>
    Private Sub StartUpdateDownload(Info As UpdateInfo, Manual As Boolean)
        If UpdateDownloadTask IsNot Nothing AndAlso UpdateDownloadTask.State = LoadState.Loading Then Return
        Dim Dir As String = PathTemp & "Update\"
        IO.Directory.CreateDirectory(Dir)
        Dim Target As String = Dir & "PCL-Harness-" & Info.Version & ".exe"
        Try
            IO.File.Delete(Target)
        Catch
        End Try
        Dim Downloader As New LoaderDownload($"PCL Harness {Info.Version} 更新", New List(Of NetFile) From {New NetFile(Info.DownloadUrls, Target)})
        Downloader.Show = True
        Logger.Info($"[Update] 开始{(If(Manual, "手动", "静默"))}下载 {Info.Version}（{Info.DownloadUrls.Count} 个下载源）")
        '只在开始时提示一次；进度看右下角的圆环与任务管理，不再常驻左下角
        Hint($"正在下载新版本 PCL Harness {Info.Version}…… 可在右下角「任务管理」查看进度或取消")
        AddHandler Downloader.OnStateChangedUi,
            Sub(L As LoaderBase, NewState As LoadState, OldState As LoadState)
                Try
                    Select Case NewState
                        Case LoadState.Finished
                            UpdateDownloadTask = Nothing
                            If Not IO.File.Exists(Target) Then
                                Logger.Warn($"[Update] 下载任务已结束，但没有找到更新文件：{Target}")
                                Return
                            End If
                            PendingUpdateExe = Target
                            PendingUpdateVersion = Info.Version
                            Logger.Info($"[Update] 已下载 {Target}（{New IO.FileInfo(Target).Length} 字节），退出启动器时替换，下次启动生效")
                            Hint($"新版本 {Info.Version} 已下载完成，退出启动器时静默替换；下次打开即为新版本", HintType.Green)
                        Case LoadState.Canceled
                            UpdateDownloadTask = Nothing
                            Try
                                IO.File.Delete(Target)
                            Catch
                            End Try
                            Logger.Info($"[Update] 已取消下载 {Info.Version}")
                            Hint("已取消下载新版本")
                        Case LoadState.Failed
                            UpdateDownloadTask = Nothing
                            Logger.Warn($"[Update] 下载新版本 {Info.Version} 失败")
                            Hint("下载新版本失败，可稍后在设置中手动重试", HintType.Red)
                    End Select
                Catch ex As Exception
                    Logger.Error(ex, "[Update] 处理更新下载状态失败")
                End Try
            End Sub
        UpdateDownloadTask = Downloader
        Downloader.Start()
        LoaderTaskbar.Add(Downloader)
    End Sub
    ''' <summary>设置项为「在有新版本时自动下载」：静默下载，退出启动器时替换并重启。</summary>
    Private Sub DownloadUpdateSilently(Info As UpdateInfo)
        Try
            StartUpdateDownload(Info, False)
        Catch ex As Exception
            Logger.Warn(ex, "[Update] 静默下载更新失败")
        End Try
    End Sub

    ''' <summary>退出启动器时静默应用已下载的更新（由 EndProgramForce 调用）：只替换文件，不重启进程。</summary>
    Friend Sub ApplyPendingUpdateOnExit()
        If PendingUpdateExe = "" Then Return
        If Not IO.File.Exists(PendingUpdateExe) Then Return
        Try
            Dim Args As String = $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PendingUpdateExe}"" ""False"""
            Logger.Info($"[Update] 退出时应用更新 {PendingUpdateVersion}：{Args}")
            '写标记：新版本启动时据此弹一次「PCL Harness 已更新至…」
            Try
                IO.Directory.CreateDirectory(PathTemp & "Update")
                IO.File.WriteAllText(PathTemp & "Update\Applied.txt", VersionBaseName)
            Catch ex As Exception
                Logger.Warn(ex, "[Update] 写入静默替换标记失败")
            End Try
            StartProcess(PendingUpdateExe, Args)
        Catch ex As Exception
            Logger.Error(ex, "[Update] 退出时启动更新进程失败")
        End Try
    End Sub

    ''' <summary>展示更新提示：立即下载并更新 / 打开发布页 / 稍后。</summary>
    Private Sub ShowUpdatePrompt(Info As UpdateInfo)
        Dim Text As String = $"发现新版本 {Info.Version}（当前 {VersionBaseName}）。"
        Dim Notes As String = TrimMarkdown(Info.Body, 700)
        If Notes <> "" Then Text &= vbCrLf & vbCrLf & Notes
        If Info.DownloadUrls.Count = 0 Then
            If MyMsgBox(Text & vbCrLf & vbCrLf & "发布页里没有找到 PCL-Harness.exe，请手动到 Release 页下载。", "检查更新", "打开发布页", "稍后") = 1 Then
                OpenWebsite(If(Info.HtmlUrl = "", $"https://github.com/{UpdateRepo}/releases/latest", Info.HtmlUrl))
            End If
            Return
        End If
        Select Case MyMsgBox(Text, "检查更新", "立即下载并更新", "打开发布页", "稍后")
            Case 1
                DownloadAndApplyUpdate(Info)
            Case 2
                OpenWebsite(If(Info.HtmlUrl = "", $"https://github.com/{UpdateRepo}/releases/latest", Info.HtmlUrl))
            Case Else
                '「稍后」只本次运行内不再提示，不写设置；下次启动还会照常提醒
                SnoozedUpdateVersion = Info.Version
        End Select
    End Sub

    ''' <summary>下载新版本 exe（作为任务栏里的普通任务），完成后静默替换，下次启动即为新版本。</summary>
    Private Sub DownloadAndApplyUpdate(Info As UpdateInfo)
        Try
            StartUpdateDownload(Info, True)
        Catch ex As Exception
            Logger.Error(ex, "[Update] 下载更新失败")
            Hint("下载更新失败：" & ex.Message, HintType.Red)
        End Try
    End Sub


    ''' <summary>把 Release 的 Markdown 粗略转成纯文本并截断。</summary>
    Private Function TrimMarkdown(Text As String, MaxLength As Integer) As String
        If String.IsNullOrWhiteSpace(Text) Then Return ""
        Dim Lines As New List(Of String)
        For Each Raw As String In Text.Replace(vbCrLf, vbLf).Split(vbLf)
            Dim Line As String = Raw.Trim()
            Do While Line.StartsWith("#")
                Line = Line.Substring(1).TrimStart()
            Loop
            Line = System.Text.RegularExpressions.Regex.Replace(Line, "\[([^\]]+)\]\([^)]+\)", "$1")
            Line = Line.Replace("**", "").Replace("`", "").Replace("- ", "· ")
            If Line.Length = 0 Then
                If Lines.Count > 0 AndAlso Lines(Lines.Count - 1) <> "" Then Lines.Add("")
                Continue For
            End If
            Lines.Add(Line)
        Next
        Dim Result As String = String.Join(vbCrLf, Lines).Trim()
        If Result.Length > MaxLength Then Result = Result.Substring(0, MaxLength).TrimEnd() & "……"
        Return Result
    End Function

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub

    ''' <summary>
    ''' 删除文件的「网络标记」（Zone.Identifier 备用数据流）。
    ''' .NET 的 File.Delete 不支持带冒号的 ADS 路径，这里直接调 Win32 DeleteFile。
    ''' </summary>
    Private Sub StripZoneIdentifier(FilePath As String)
        Dim AdsPath As String = FilePath & ":Zone.Identifier"
        If DeleteFileWin32(AdsPath) Then
            Logger.Info($"[Update] 已清除网络标记：{FilePath}")
        Else
            Dim Code As Integer = Runtime.InteropServices.Marshal.GetLastWin32Error()
            If Code <> 2 Then Logger.Warn($"[Update] 清除网络标记失败（错误 {Code}）：{FilePath}") '2 = 文件不存在，正常
        End If
    End Sub

    <Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint:="DeleteFileW", CharSet:=Runtime.InteropServices.CharSet.Unicode, SetLastError:=True)>
    Private Function DeleteFileWin32(lpFileName As String) As Boolean
    End Function
    
    ''' <summary>
    ''' 由新版本 exe 以 --update 参数启动时调用：等待旧进程退出、用自己的文件覆盖旧文件，可选重启。
    ''' </summary>
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Try
            Logger.Info($"[Update] 等待旧进程 {ProcessId} 退出，然后替换 {OldFileName}")
            For i As Integer = 1 To 600
                Try
                    If Process.GetProcessById(ProcessId).HasExited Then Exit For
                Catch
                    Exit For '进程已不存在
                End Try
                Threading.Thread.Sleep(100)
            Next
            Dim Done As Boolean = False
            For TryCount As Integer = 1 To 30
                Try
                    IO.File.Copy(NewFileName, OldFileName, True)
                    Done = True
                    Exit For
                Catch ex As Exception
                    Logger.Warn(ex, $"[Update] 第 {TryCount} 次替换失败，250ms 后重试")
                    Threading.Thread.Sleep(250)
                End Try
            Next
            If Not Done Then Throw New Exception($"无法写入 {OldFileName}")
            StripZoneIdentifier(OldFileName) '换掉的新文件同样可能带着「网络标记」，清掉才不会每次启动都被 SmartScreen 拦截
            Logger.Info($"[Update] 已更新到 {OldFileName}")
            If TriggerRestart Then StartProcess(OldFileName)
        Catch ex As Exception
            Logger.Error(ex, "[Update] 自动更新替换失败")
            Try
                IO.File.WriteAllText(OldFileName & ".update.log", ex.ToString())
            Catch
            End Try
        Finally
            '删除更新器自身副本（稍等本进程退出后再删）；全程隐藏窗口，避免闪出黑框
            Try
                Dim CmdPath As String = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
                Dim CmdInfo As New ProcessStartInfo With {
                    .FileName = CmdPath,
                    .Arguments = $"/c ping -n 3 127.0.0.1 >nul & del /f /q ""{NewFileName}""",
                    .UseShellExecute = False,
                    .CreateNoWindow = True,
                    .WindowStyle = ProcessWindowStyle.Hidden
                }
                Process.Start(CmdInfo)
            Catch
            End Try
        End Try
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
