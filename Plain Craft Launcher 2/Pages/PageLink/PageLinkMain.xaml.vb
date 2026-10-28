Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Net.Sockets
Imports System.Reflection
Imports PCL
Imports System.Security.AccessControl
Imports System.Security.Principal

Public Class PageLinkMain
    Private Const INVITE_CODE_VERSION As Integer = 2

    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        '触发状态切换
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    '让加载器的 成功/失败 事件触发状态切换
    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    Telemetry("联机失败", "Exception", FilterUserName(Loader.Error.GetDisplay(True), "*"))
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
    End Sub

    '页面动画兼容
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter
        FrmLinkMain.PanSelect.Visibility = If(LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        FrmLinkMain.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '页面切换
        If OldState <> LinkStates.Finished Then
            '从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
            '如果在现在提前调用了，会丢失完成页面的退出动画
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
        RefreshSelectPanel()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '创建
    Private Sub Create_MouseLeftButtonUp() Handles PanSelectCreate.MouseLeftButtonUp
        '输入端口号
        Dim Port As String = MyMsgBoxInput("输入端口", $"在单人游戏的暂停菜单选择 {vbLQ}对局域网开放{vbRQ}，然后输入端口数字。{vbCrLf}甚至可以输入其他游戏的端口……嗯……",
            ValidateRules:=New Collection(Of Validate) From {New ValidateInteger(1024, 65535)},
            HintText:=If(Settings.Get(Of Integer)("LinkLastPort") > 0, "使用上一次的端口", "端口号"))
        If Port Is Nothing OrElse Port.Trim.Length = 0 Then Return
        CreateWithPort(CInt(Port))
    End Sub

    ''' <summary>
    ''' 用指定端口直接开房（跳过输入框，供「上次端口」按钮使用）。
    ''' </summary>
    Public Sub CreateWithPort(Port As Integer)
        'TUN 模式需要管理员：没提权就先把启动器重启成管理员身份
        If Not EnsureAdminForTun() Then Return
        '基础信息
        IsServerSide = True
        ServerPort = Port
        Settings.Set("LinkLastPort", Port) '记住端口，下次启动可以直接一键开房
        Dim GenerateRandomCode =
        Function() As String
            Dim Result As String = ""
            For i = 1 To 5
                Result &= RandomInteger(0, 35).ConvertRadix(36)
            Next
            Return Result.Replace("O", "0").Replace("I", "1")
        End Function
        NetworkName = $"P{ServerPort.ConvertRadix(16).PadLeft(4, "0"c)}-{GenerateRandomCode()}"
        NetworkSecret = GenerateRandomCode()
        DiscoverNodeID = -1
        Logger.Info($"尝试创建房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}")
        '启动
        ChangeState(LinkStates.Loading)
    End Sub

    '模式开关：TUN 勾选状态
    Private Sub ChkTunMode_Change(sender As Object, user As Boolean) Handles ChkTunMode.Change
        If Not IsLoadedUi Then Return '初始化刷新时不要弹提示
        If ChkTunMode.Checked Then
            Dim Choice As Integer = MyMsgBox("开启虚拟局域网（TUN）模式？" & vbCrLf & vbCrLf &
                                             "开启后，第一次创建或加入房间时启动器会自动以管理员身份重启，Windows 会弹出权限确认窗口，请点「是」。" & vbCrLf & vbCrLf &
                                             "之后双方通过 10.114.114.x 虚拟地址互连，任意游戏、任意端口都能直接连。",
                                             "虚拟局域网（TUN）", "开启", "取消")
            If Choice = 1 Then '第一个按钮「开启」
                Settings.Set("LinkTunMode", True)
                Hint("已开启 TUN 模式，创建/加入房间时会自动提权重启启动器", HintType.Green)
            Else
                IsLoadedUi = False
                ChkTunMode.Checked = False
                IsLoadedUi = True
            End If
        Else
            Settings.Set("LinkTunMode", False)
            Hint("已关闭 TUN 模式，仅使用直连", HintType.Blue, False)
        End If
    End Sub

    '一键用上次端口开房
    Private Sub BtnLastPort_Click() Handles BtnLastPort.Click
        Dim Port As Integer = Settings.Get(Of Integer)("LinkLastPort")
        If Port < 1024 OrElse Port > 65535 Then Return
        CreateWithPort(Port)
    End Sub

    Private IsLoadedUi As Boolean = False
    ''' <summary>
    ''' 刷新模式开关与「上次端口」按钮的状态。
    ''' </summary>
    Public Sub RefreshSelectPanel()
        IsLoadedUi = False
        ChkTunMode.Checked = Settings.Get(Of Boolean)("LinkTunMode")
        IsLoadedUi = True
        Dim Port As Integer = Settings.Get(Of Integer)("LinkLastPort")
        If Port >= 1024 AndAlso Port <= 65535 Then
            BtnLastPort.Text = $"用上次端口 {Port} 直接开房"
            BtnLastPort.Visibility = Visibility.Visible
        Else
            BtnLastPort.Visibility = Visibility.Collapsed
        End If
    End Sub

    '加入
    Private Shared LastCode As String = Nothing
    ''' <summary>
    ''' 输入邀请码，切换到联机页并立即加入房间。
    ''' </summary>
    Public Shared Sub Join() Handles PanSelectJoin.MouseLeftButtonUp
        Dim Code As String = MyMsgBoxInput("输入邀请码", "输入房主发给你的邀请码。",
            HintText:=If(String.IsNullOrEmpty(LastCode), "", "使用上一次的邀请码"))
        If Not String.IsNullOrEmpty(LastCode) AndAlso Code IsNot Nothing AndAlso Code = "" Then Code = LastCode
        If Code Is Nothing Then Return
        Join(Code)
    End Sub
    Public Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        Code = Code.Between("【", "】").Trim() '从完整消息中提取
        '直连邀请：房主地址|邀请码（地址可以是用逗号分开的多个，外网在前、局域网在后）
        DirectPeerAddress = Nothing
        DirectPeerAddresses.Clear()
        If Code.Contains("|") Then
            Dim Parts = Code.Split("|"c)
            If Parts.Length <> 2 Then
                Hint("直连邀请格式不对，应为「房主IP:端口|邀请码」", HintType.Red)
                Return
            End If
            For Each Address In Parts(0).Split(","c)
                Dim One = Address.Trim()
                If One.Length = 0 Then Continue For
                If Not One.Contains(":") Then
                    Hint("直连邀请格式不对，应为「房主IP:端口|邀请码」", HintType.Red)
                    Return
                End If
                DirectPeerAddresses.Add(One)
            Next
            If DirectPeerAddresses.Count = 0 Then
                Hint("直连邀请格式不对，应为「房主IP:端口|邀请码」", HintType.Red)
                Return
            End If
            DirectPeerAddress = String.Join(",", DirectPeerAddresses)
            Code = Parts(1).Trim
            Logger.Info($"使用直连模式，房主地址：{DirectPeerAddress}")
        End If
        '基础格式校验
        Dim ValidateResult = ValidateCodeFormat(Code)
        If ValidateResult IsNot Nothing Then
            Hint(ValidateResult, HintType.Red)
            Return
        End If
        Code = FixCodeFormat(Code)
        Logger.Info($"实际使用的邀请码：{Code}")
        'TUN 模式需要管理员：没提权就先把启动器重启成管理员身份
        If Not EnsureAdminForTun() Then Return
        '基础信息
        IsServerSide = False
        ServerPort = Code.Substring(1, 4).Upper.ConvertRadix(16, 10)
        NetworkName = Code.Substring(0, 11)
        NetworkSecret = Code.Substring(12, 5)
        If Code.Substring(20, 3) = "000" Then
            DiscoverNodeID = -2
        Else
            DiscoverNodeID = Code.Substring(20, 3).Upper.ConvertRadix(16, 10)
        End If
        Logger.Info($"尝试加入房间，网络名 {NetworkName}，网络密码 {NetworkSecret}，端口 {ServerPort}，发现节点 {DiscoverNodeID}")
        '启动
        LastCode = Code
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        If Code Is Nothing Then Return "邀请码为空！"
        Code = FixCodeFormat(Code)
        '判断类型
        If Not (Code.Length >= 14 AndAlso Code(0) = "P"c AndAlso Code(5) = "-"c AndAlso Code(11) = "-"c) Then
            If Code.StartsWithF("U/") Then 'HMCL
                Return "请让房主使用 PCL 创建房间！"
            ElseIf Code.Length = 10 Then 'PCL CE
                Return "请让房主使用非社区版的 PCL 创建房间！"
            Else
                Return "邀请码有误，请让房主使用 PCL 创建房间！"
            End If
        End If
        '校验版本
        If Code.Length >= 23 AndAlso Code(17) = "-"c AndAlso
            Val(Code.Substring(18, 2)) > INVITE_CODE_VERSION Then Return "你的 PCL 版本太老了，请在更新 PCL 之后再联机！"
        Return Nothing
    End Function
    Private Shared Function FixCodeFormat(Code As String) As String
        Code = Code.Between("【", "】").Between("[", "]") '从完整消息中提取
        Code = Code.Upper.Replace("O", "0").Replace("I", "1") '输入修正
        '版本 1 兼容
        If Code.Length >= 17 AndAlso (Code.Length < 23 OrElse (Code.Length >= 18 AndAlso Code(17) <> "-"c)) Then
            Code = Code.Substring(0, 17) & "-0105E"
        End If
        Return Code
    End Function

    '自动加入

    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    Private Sub PanSelectJoin_Loaded() Handles PanSelectJoin.Loaded
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        '清理变量
        Peers = If(IsDirectMode, New List(Of Peer), Nothing)
        FailCount = 0
        '加载器与进程状态
        LinkLoader.Start(IsForceRestart:=True)
        '页面切换会由 Loader 调用 MyPageRight 来触发
        'UI 更新
        UpdateProgressBar(0)
        LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        UpdateLoadingPage("正在初始化……", "准备初始化")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        ProcessStop()
        'UI 更新
        UpdateProgressBar(1)
        '显示错误信息
        LabLoadTitle.Text = FailReason
        Dim Brief As String = LinkLoader.Error.GetDisplay(False)
        Dim InnerEx As Exception = LinkLoader.Error
        If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Do Until InnerEx.InnerException Is Nothing
            InnerEx = InnerEx.InnerException
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Loop
        LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        Logger.Warn(LinkLoader.Error, LabLoadTitle.Text)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        '由于只可能从加载器的完成事件触发，不需要管加载器
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发
        '插件事件（v4）：房间创建/加入
        If IsServerSide Then
            ModPlugin.RaiseLinkRoomCreated(If(IsDirectMode, GetDirectInvite(), GetInviteCode()))
        Else
            ModPlugin.RaiseLinkRoomJoined()
        End If

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            If IsDirectMode Then
                Dim Tip As String = If(IsTunMode,
                    $"虚拟局域网（TUN）已启用：让朋友在游戏里连接 {TunHostIp}:{ServerPort}。路由器只需放行 EasyTier 监听端口 {DirectListenPort}（UPnP 会自动尝试）。",
                    If(String.IsNullOrEmpty(DirectPublicEndpoint),
                    "没有映射到公网，这条邀请只能在同一局域网里用；要留给外网的朋友，得让路由器开 UPnP 或手动做端口映射。",
                    $"已经带上外网地址 {DirectPublicEndpoint}，外网的朋友也能直接加入（前一个地址优先，后一个给同一局域网用）。"))
                LabFinishDesc.Text = $"直连模式已开启，把下面这串发给朋友：{vbCrLf}{GetDirectInvite()}{vbCrLf}{Tip}"
                RefreshAddressPanel()
                If Settings.Get(Of Boolean)("LinkAutoPublicIp") AndAlso String.IsNullOrEmpty(DirectPublicIp) Then DetectPublicIpCore()
                WriteAddressFileIfEnabled()
            Else
                LabFinishDesc.Text = $"把邀请码发给朋友，让大家加入房间吧！{vbCrLf}邀请码：{GetInviteCode()}"
            End If
            BtnFinishExit.Text = "关闭"
            BtnFinishPing.ToolTip = "网络延迟"
            BtnFinishCopy.Visibility = Visibility.Visible
            Copy() '立即复制邀请码
            '下边栏
            BtnFinishPort.Visibility = Visibility.Visible
            LabFinishPort.Visibility = Visibility.Visible
            BtnFinishIp.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Collapsed
            LabFinishPort.Text = ServerPort
        Else
            LabFinishTitle.Text = "已加入房间"
            If IsTunMode Then
                LabFinishDesc.Text = $"已加入虚拟局域网！{vbCrLf}在游戏里直接连接：{TunHostIp}:{ServerPort}"
            Else
                LabFinishDesc.Text = $"在多人游戏页面的最下方就能找到联机房间！{vbCrLf}注意：使用离线登录时不要手动输入 IP！"
            End If
            PanAddress.Visibility = Visibility.Collapsed
            BtnFinishExit.Text = "离开"
            BtnFinishPing.ToolTip = "与房主的延迟"
            BtnFinishCopy.Visibility = Visibility.Collapsed
            '下边栏
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LabFinishIp.Text = ClientAddress
            BtnFinishIp.Visibility = Visibility.Visible
            LabFinishIp.Visibility = Visibility.Visible
        End If
        Update() '立即刷新
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        ModPlugin.RaiseLinkRoomClosed()
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then Return False
        If Not Silent Then
            If IsServerSide AndAlso PeopleCount > 1 Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入邀请码才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        DeleteAddressFile()
        ChangeState(LinkStates.Waiting)
        Return False
    End Function

    '复制邀请码
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim CodeText As String = $"在 PCL 启动器中输入邀请【{If(IsDirectMode, GetDirectInvite(), GetInviteCode())}】，即可加入联机房间！"
        ClipboardSet(CodeText, False)
        Settings.Set("LinkLastAutoJoinInviteCode", CodeText)
        Hint("已复制邀请！", HintType.Green)
    End Sub
    Private Function GetInviteCode() As String
        Return $"{NetworkName}-{NetworkSecret}-{INVITE_CODE_VERSION.ToString.PadLeft(2, "0"c)}{ _
            If(DiscoverNodeID = -1, 0, DiscoverNodeID).ConvertRadix(16).PadLeft(3, "0"c)}"
    End Function

    ''' <summary>
    ''' 直连模式下发给朋友的完整内容：房主地址|邀请码。
    ''' 外网地址（UPnP 映射成功时）排在前面，这样不在同一局域网的朋友也能直接用；
    ''' 局域网地址附在后面，同局域网时更稳。
    ''' </summary>
    Private Function GetDirectInvite() As String
        If Not IsDirectMode OrElse Not IsServerSide Then Return GetInviteCode()
        Dim Addresses As New List(Of String)
        If Not String.IsNullOrEmpty(DirectPublicEndpoint) Then Addresses.Add(DirectPublicEndpoint)
        Dim LanIp As String = ModUpnp.GetLanIPv4()
        If String.IsNullOrEmpty(LanIp) Then LanIp = "127.0.0.1"
        Addresses.Add($"{LanIp}:{DirectListenPort}")
        Return $"{String.Join(",", Addresses.Distinct())}|{GetInviteCode()}"
    End Function


    '复制 IP
    Private Sub BtnFinishIp_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishIp.MouseLeftButtonUp
        ClipboardSet(ClientAddress, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

    '===============================
    '  后端
    '===============================

    ''' <summary>
    ''' EasyTier 所在的文件夹路径，以 \ 结尾。
    ''' </summary>
    Private PathEasyTier As String = Paths.AppDataThenName & "EasyTier\"
    ''' <summary>
    ''' 当前是服务端还是客户端。
    ''' </summary>
    Private IsServerSide As Boolean
    ''' <summary>
    ''' 服务端映射前的端口号。
    ''' </summary>
    Private ServerPort As Integer
    ''' <summary>
    ''' 客户端映射后的端口号。
    ''' </summary>
    Private ClientPort As Integer
    ''' <summary>
    ''' RPC 端口号。
    ''' </summary>
    Private RPCPort As Integer
    ''' <summary>
    ''' 在客户端映射后的地址。
    ''' 除非已作为客户端启动 EasyTier，否则一直为 Nothing。
    ''' </summary>
    Private ClientAddress As String = Nothing
    ''' <summary>
    ''' 发现节点的 ID。
    ''' 若必须设定自定义节点则为 -2；若等待选择则为 -1；选择回退节点则为 0；否则为对应节点的 ID。
    ''' </summary>
    Private DiscoverNodeID As Integer = -1
    ''' <summary>
    ''' 网络信息。
    ''' </summary>
    Private NetworkName As String, NetworkSecret As String

    ''' <summary>
    ''' 是否启用直连模式（不经过任何公共服务器）。
    ''' </summary>
    Public ReadOnly Property IsDirectMode As Boolean
        Get
            Return Settings.Get(Of Boolean)("LinkDirectMode")
        End Get
    End Property
    ''' <summary>
    ''' 直连模式下加入者拿到的房主地址列表（IP:端口），可能同时包含外网与局域网。
    ''' </summary>
    Private ReadOnly DirectPeerAddresses As New List(Of String)
    ''' <summary>
    ''' 直连模式下加入者填写的房主地址（IP:端口），多个地址用逗号连接，仅用于日志与展示。
    ''' </summary>
    Private DirectPeerAddress As String = Nothing
    ''' <summary>
    ''' 直连模式下房主的监听端口（TCP + UDP）。
    ''' </summary>
    Private DirectListenPort As Integer
    ''' <summary>
    ''' 直连模式下 UPnP 映射出的公网端点；没有则为空。
    ''' </summary>
    Private DirectPublicEndpoint As String = Nothing
    ''' <summary>本机公网 IP。仅在用户点击「检测公网 IP」或开启自动检测后才有值，不写入日志。</summary>
    Private DirectPublicIp As String = Nothing
    ''' <summary>公网 IP 的来源描述（路由器 UPnP 或 HTTPS 查询）。</summary>
    Private DirectPublicIpSource As String = Nothing

    ''' <summary>
    ''' 是否启用虚拟局域网（TUN）模式：需要管理员权限，双方通过虚拟 IP 互联，可支持任意游戏与端口。
    ''' 必须在直连模式基础上启用。
    ''' </summary>
    Public ReadOnly Property IsTunMode As Boolean
        Get
            Return IsDirectMode AndAlso Settings.Get(Of Boolean)("LinkTunMode")
        End Get
    End Property
    ''' <summary>房主在虚拟局域网中的固定 IP。</summary>
    Private Const TunHostIp As String = "10.114.114.1"
    ''' <summary>加入者在虚拟局域网中的本机 IP。</summary>
    Private TunLocalIp As String = Nothing
    ''' <summary>TUN 提权启动时，联机模块控制台输出写入的文件。</summary>
    Private Shared TunLogFile As String = Nothing
    Private Shared TunLogOffset As Long = 0
    Private Shared TunLogThread As Threading.Thread = Nothing
    ''' <summary>TUN 模式下「游戏端口是否已经能被对端连上」的探测结果。</summary>
    Private TunPortOk As Boolean = False
    ''' <summary>TUN 端口探测的节流计数（每 3 秒探一次，避免每秒建连接）。</summary>
    Private TunProbeCounter As Integer = 0

#Region "加载"

    Private WithEvents LinkLoader As New LoaderCombo(Of Integer)("联机", {
        New LoaderTask(Of Integer, Integer)("获取配置", AddressOf InitConfig) With {.ProgressWeight = 8},
        New LoaderTask(Of Integer, List(Of NetFile))("准备下载联机模块", AddressOf InitPrepareDownload) With {.ProgressWeight = 2},
        New LoaderDownload("下载联机模块", New List(Of NetFile)) With {.ProgressWeight = 40},
        New LoaderTask(Of Integer, Integer)("启动联机模块", AddressOf InitLaunch) With {.ProgressWeight = 50}
    })

    '1. 获取服务器配置
    '开源版本体自带一份默认联机配置，首次使用时自动写到数据目录，用户仍可自行修改
    Private Const DefaultServerConfig As String = "{""Link"":{""EasyTierVersion"":264,""Downloads"":[""https://ghfast.top/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip"",""https://gh-proxy.com/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip"",""https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip""],""Argument"":""--no-tun --relay-all-peer-rpc"",""Peers"":[""tcp://public.easytier.cn:11010""],""MandatoryPeers"":[],""PeersBlackList"":[],""RandomPeer"":0,""DiscoverPeerId"":0,""DiscoverPeer"":""tcp://public.easytier.cn:11010""}}"

    Private Sub InitConfig(Task As LoaderTask(Of Integer, Integer))
        If VersionBranchMain <> "Official" Then
            '开源版没有联网配置，改为读取本地的 ServerConfig.json
            UpdateLoadingPage("正在读取本地联机配置……", "读取配置")
            Dim ConfigPath As String = Paths.AppDataThenName & "ServerConfig.json"
            If Not FileUtils.Exists(ConfigPath) Then
                '首次使用（只有本体、没有配置文件）时自动生成一份默认配置
                Try
                    IO.Directory.CreateDirectory(Paths.AppDataThenName)
                    IO.File.WriteAllText(ConfigPath, DefaultServerConfig, New Text.UTF8Encoding(False))
                    Logger.Info($"未找到联机配置，已写入默认配置：{ConfigPath}")
                Catch ex As Exception
                    Logger.Warn(ex, $"写入默认联机配置失败：{ConfigPath}")
                End Try
            End If
            If Not FileUtils.Exists(ConfigPath) Then
                Throw New Exception($"$未找到联机配置文件，且自动生成失败。{vbCrLf}请手动把 ServerConfig.json 放到 {vbLQ}{ConfigPath}{vbRQ} 后再试，格式见 samples\LinkConfig\ServerConfig.example.json。")
            End If
            Try
                ServerConfig = JObject.Parse(IO.File.ReadAllText(ConfigPath))
            Catch ex As Exception
                Throw New Exception($"$联机配置文件解析失败：{ex.Message}{vbCrLf}文件：{ConfigPath}")
            End Try
            If ServerConfig Is Nothing OrElse ServerConfig("Link") Is Nothing Then
                Throw New Exception($"$联机配置文件缺少 Link 节点。{vbCrLf}文件：{ConfigPath}")
            End If
            Logger.Info($"已从本地加载联机配置：{ConfigPath}")
        Else
            UpdateLoadingPage("正在联网获取配置……", "联网获取配置")
            ServerLoader.WaitForExit(LoaderToSyncProgress:=Task)
        End If
        If ServerConfig Is Nothing Then Throw New Exception("无法从服务器获取配置")
        '检查是否已禁用联机功能
        Dim DisableReason = ServerConfig("Link")?("DisableReason2")?.ToString
        If Not String.IsNullOrEmpty(DisableReason) Then Throw New Exception("$" & DisableReason) 'TODO: 可能不会显示错误原因？
        If CType(ServerConfig("Link"), JObject).ContainsKey("MinVersionCode") AndAlso
           VersionCode < ServerConfig("Link")("MinVersionCode").ToObject(Of Integer) Then
            Throw New Exception("$你的 PCL 版本太老了，请在更新 PCL 之后再联机！")
        End If
    End Sub

    '2. 获取需要下载的文件
    Private ServerVersion As Integer
    Private Sub InitPrepareDownload(Task As LoaderTask(Of Integer, List(Of NetFile)))
        '获取 CPU 架构
        UpdateLoadingPage("正在获取 CPU 架构……", "获取 CPU 架构")
        Dim Architecture As String = GetType(String).Assembly.GetName().ProcessorArchitecture
        Select Case Architecture
            Case ProcessorArchitecture.X86
                Architecture = "i686"
            Case ProcessorArchitecture.Amd64
                Architecture = "x86_64"
            Case ProcessorArchitecture.Arm
                Architecture = "arm64"
            Case Else
                Logger.Warn($"CPU 是不支持的 {Architecture} 架构，这可能会导致联机模块无法启动！")
                Architecture = "arm64"
        End Select
        Logger.Info($"CPU 架构：{Architecture}")
        Telemetry("联机开始")
        '检查 EasyTier 版本
        UpdateLoadingPage("正在检查联机模块版本……", "检查联机模块版本")
        If Not (FileUtils.Exists(PathEasyTier & "联机模块 CLI.exe") AndAlso FileUtils.Exists(PathEasyTier & "联机模块.exe") AndAlso
                FileUtils.Exists(PathEasyTier & "Packet.dll")) Then
            Settings.Set("LinkEasyTierVersion", -1)
        End If
        Dim LocalVersion As Integer = Settings.Get(Of Integer)("LinkEasyTierVersion")
        ServerVersion = ServerConfig("Link")("EasyTierVersion")
        Dim RequiredFiles As New List(Of NetFile)
        Logger.Info($"EasyTier 本地版本：{LocalVersion}，需求版本：{ServerVersion}")
        If LocalVersion < ServerVersion Then
            RequiredFiles.Add(New NetFile(
                ServerConfig("Link")("Downloads").Select(Function(UrlEntry) UrlEntry.ToString.Replace("{arch}", Architecture)),
                PathEasyTier & "EasyTier.zip",
                New FileChecker With {.MinSize = 1024 * 1024 * 2}))
        End If
        '开始下载
        UpdateLoadingPage("正在下载联机模块……", "下载联机模块")
        Task.Output = RequiredFiles
    End Sub

    '3. 启动联机模块
    Private Sub InitLaunch(Task As LoaderTask(Of Integer, Integer))
        '每次开房间重新探测 NAT（换网络后旧结果就不准了）
        NatProbeResult = Nothing
        NatProbeStarted = False
        '解压文件
        UpdateLoadingPage("正在解压联机模块……", "解压联机模块")
        If FileUtils.Exists(PathEasyTier & "EasyTier.zip") Then
            '解压
            Dim ExtractPath As String = RequestTaskTempFolder()
            IO.Compression.ZipFile.ExtractToDirectory(PathEasyTier & "EasyTier.zip", ExtractPath)
            '把压缩包里那一层文件夹的内容拷到 EasyTier 目录（开源版没有 DirectoryUtils.Copy，这里自行实现）
            Dim SrcDirs As String() = IO.Directory.GetDirectories(ExtractPath)
            Dim SrcDir As String = If(SrcDirs.Length > 0, SrcDirs(0), ExtractPath)
            For Each SrcFile As String In IO.Directory.GetFiles(SrcDir, "*", IO.SearchOption.AllDirectories)
                Dim RelativePath As String = SrcFile.Substring(SrcDir.Length).TrimStart("\"c)
                Dim DestFile As String = PathEasyTier & RelativePath
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(DestFile))
                IO.File.Copy(SrcFile, DestFile, True)
            Next
            '重命名
            FileUtils.Move(PathEasyTier & "easytier-core.exe", PathEasyTier & "联机模块.exe")
            FileUtils.Move(PathEasyTier & "easytier-cli.exe", PathEasyTier & "联机模块 CLI.exe")
            '清理
            FileUtils.Delete(PathEasyTier & "EasyTier.zip")
            Settings.Set("LinkEasyTierVersion", ServerVersion)
            '解压后必须拿到可执行文件，否则给出可读的错误
            If Not (FileUtils.Exists(PathEasyTier & "联机模块.exe") AndAlso FileUtils.Exists(PathEasyTier & "联机模块 CLI.exe")) Then
                Throw New Exception($"$联机模块解压失败：{PathEasyTier} 中没有找到可执行文件。{vbCrLf}请检查网络能否访问 GitHub，或手动下载 EasyTier 2.6.4 并将 easytier-core.exe / easytier-cli.exe 重命名后放入该目录。")
            End If
        End If
        Task.Progress = 0.07
        '获取节点列表
        UpdateLoadingPage("正在获取节点列表……", "获取节点列表")
        Dim RawPeers As List(Of String)
        If IsDirectMode Then
            '直连模式：不查询任何服务器，只连接邀请里给出的房主地址
            RawPeers = New List(Of String)
            If Not IsServerSide Then
                If String.IsNullOrEmpty(DirectPeerAddress) Then
                    Panic("缺少房主地址", "直连模式需要「房主地址|邀请码」格式的邀请，请向房主重新索要。")
                    Return
                End If
                '邀请里可能同时带外网与局域网地址，逐个尝试（外网在前）
                For Each Address In DirectPeerAddresses
                    RawPeers.Add($"tcp://{Address}")
                    RawPeers.Add($"udp://{Address}")
                Next
            End If
            Logger.Info($"直连模式：{(If(IsServerSide, "房主仅监听", "连接 " & String.Join("、", RawPeers)))}")
        Else
            Dim CustomPeers As String = Settings.Get(Of String)("LinkCustomPeer")
            If String.IsNullOrWhiteSpace(CustomPeers) Then
                If DiscoverNodeID = -2 AndAlso Not IsServerSide Then
                    Panic("未填写自定义节点设置", $"$你必须在 {vbLQ}自定义节点{vbRQ} 设置中填写与房主相同的内容，{vbCrLf}才能进入该房间！")
                    Return
                End If
                RawPeers = GetTargetPeers()
            Else
                If DiscoverNodeID <> -2 AndAlso Not IsServerSide Then Hint("房主可能没有使用自定义节点设置，请确认你们的自定义节点设置是否一致！")
                RawPeers = CustomPeers.Split("，,".ToCharArray).Select(Function(p) p.Trim).Where(Function(p) Not String.IsNullOrEmpty(p)).ToList()
                Logger.Info($"使用自定义节点：{CustomPeers}")
            End If
        End If
        Task.Progress = 0.13
        '获取空闲端口
        UpdateLoadingPage("正在启动联机模块……", "启动联机模块")
        Dim FreePorts = PickBindablePorts(5, ServerPort)
        ClientPort = FreePorts(0)
        RPCPort = FreePorts(1)
        Dim ListenersPort As Integer = PickBindablePorts(3)(0) '它会占用【当前、当前 +1、当前 +2】共三个端口
        '获取启动参数
        Dim Arguments As String = ServerConfig("Link")("Argument")
        If IsTunMode Then Arguments = Arguments.Replace("--no-tun", "").Replace("  ", " ").Trim()
        Arguments += $" --network-name={NetworkName} --network-secret={NetworkSecret} --rpc-portal {RPCPort} --private-mode true"
        If IsDirectMode Then
            '直连模式：只用 TCP + UDP 监听，且禁止 STUN 与中继
            DirectListenPort = PickBindablePorts(1, 11010)(0)
            Arguments += $" --listeners tcp://0.0.0.0:{DirectListenPort} udp://0.0.0.0:{DirectListenPort}"
            Arguments += " --p2p-only true --stun-servers """""
            If IsTunMode Then Arguments += " --enable-udp-broadcast-relay true"
            Logger.Info($"直连模式监听端口：{DirectListenPort}")
        Else
            Arguments += $" --listeners {ListenersPort}"
        End If
        Dim HostName = If(IsServerSide, "Server-", "Client-") & Identify.GetStableHashCode().ConvertRadix(36).TrimStart("-"c).EnsureLength("0", 6)
        If IsServerSide Then
            If IsTunMode Then
                Arguments += $" -i {TunHostIp}/24 --hostname={HostName} --tcp-whitelist=0 --udp-whitelist=0"
                Logger.Info($"TUN 模式：房主虚拟 IP {TunHostIp}")
            Else
                Arguments += $" -i 10.114.114.114 --hostname={HostName} --tcp-whitelist={ServerPort} --udp-whitelist={ServerPort}"
            End If
        Else
            If IsTunMode Then
                Dim GameRnd As New Random()
                TunLocalIp = $"10.114.114.{GameRnd.Next(2, 255)}"
                Arguments += $" -i {TunLocalIp}/24 --hostname={HostName} --tcp-whitelist=0 --udp-whitelist=0"
                ClientAddress = $"{TunHostIp}:{ServerPort}"
                Logger.Info($"TUN 模式：加入者虚拟 IP {TunLocalIp}，游戏内连接 {ClientAddress}")
            Else
                Arguments += $" -d --hostname={HostName} --tcp-whitelist=0 --udp-whitelist=0"
                '端口转发
                ClientAddress = $"localhost:{ClientPort}"
                Arguments += $" --port-forward tcp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}"
                Arguments += $" --port-forward udp://[{IPAddress.IPv6Loopback}]:{ClientPort}/10.114.114.114:{ServerPort}"
                Arguments += $" --port-forward tcp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}"
                Arguments += $" --port-forward udp://{IPAddress.Loopback}:{ClientPort}/10.114.114.114:{ServerPort}"
            End If
        End If
        For Each Peer As String In RawPeers
            Arguments += $" -p=""{Peer}"""
        Next
        If Settings.Get(Of Integer)("LinkLatencyMode") = 1 Then Arguments += " --latency-first"
        '直连模式且是房主：尝试在路由器上开洞
        If IsDirectMode AndAlso IsServerSide Then
            Dim ExtEndpoint As String = Nothing, Reason As String = ""
            If ModUpnp.TryMapPort(DirectListenPort, ExtEndpoint, Reason) Then
                DirectPublicEndpoint = ExtEndpoint
            Else
                Logger.Info($"UPnP 不可用：{Reason}，仅同一局域网可直连")
            End If
        End If
        '启动进程
        ProcessStart(Arguments)

        Task.Progress = 0.15
        '等待连接（直连模式的房主只负责监听，不需要等任何人）
        If IsDirectMode AndAlso IsServerSide Then
            If IsTunMode Then WaitTunReady() Else Thread.Sleep(1200)
            '核心如果因为端口被占用/被系统预留（os error 10048）退出，自动换端口重来
            Dim Retry As Integer = 0
            Do While Retry < 2 AndAlso Not WaitPortListening(DirectListenPort, 3000)
                Retry += 1
                Dim OldPort As Integer = DirectListenPort
                DirectListenPort = PickBindablePorts(1, 11010, OldPort)(0)
                Logger.Warn($"监听端口 {OldPort} 没能启动，换用 {DirectListenPort} 重试（第 {Retry} 次）")
                Arguments = Arguments.Replace($"--listeners tcp://0.0.0.0:{OldPort} udp://0.0.0.0:{OldPort}",
                                              $"--listeners tcp://0.0.0.0:{DirectListenPort} udp://0.0.0.0:{DirectListenPort}")
                Dim NewEndpoint As String = Nothing, NewReason As String = ""
                If ModUpnp.TryMapPort(DirectListenPort, NewEndpoint, NewReason) Then DirectPublicEndpoint = NewEndpoint
                ProcessStop()
                Thread.Sleep(300)
                ProcessStart(Arguments)
                If IsTunMode Then WaitTunReady() Else Thread.Sleep(1200)
            Loop
            If Not IsLocalPortListening(DirectListenPort) Then
                Panic("监听端口失败",
                      $"联机模块没法监听端口 {DirectListenPort}。{vbCrLf}常见原因：这个端口被系统预留（Hyper-V、WSL、Docker 会预留端口段）或被别的程序占着。{vbCrLf}已经自动换过 {Retry} 次端口还是不行，建议重启电脑后再试。{vbCrLf}近期日志：{vbCrLf}{LogHistory.Join(vbCrLf)}")
                Exit Sub
            End If
            Task.Progress = 1
            Exit Sub
        End If
        Dim TimeoutCounter As Integer = 0
        Do
            Thread.Sleep(500) '为避免还没启动好先跑 CLI 了，先等半秒
            '刷新信息
            CheckCrash()
            RefreshPeerLoader.WaitForExit(IsForceRestart:=True)
            '查找目标节点
            Dim Ping = GetPeerPing()
            If Ping > 0 Then
                Logger.Info($"已与目标建立连接，当前 Ping 为 {Ping:0.0}ms")
                Telemetry("联机成功", "Server", IsServerSide, "NAT", NATType)
                Exit Do '退出循环
            End If
            '更新进度
            Dim LastProgress = Task.Progress
            Dim PeerCount As Integer = If(Peers Is Nothing, -1, Peers.Where(Function(p) p.Ping > 0).Count)
            Select Case PeerCount
                Case -1 'CLI 无返回
                    Task.Progress = (Task.Progress + 0.02).Clamp(0.15, 0.25)
                Case 0 'CLI 有返回，但未连接到任何节点
                    UpdateLoadingPage("正在连接到节点……", "连接节点")
                    Task.Progress = (Task.Progress + 0.02).Clamp(If(IsServerSide, 0.5, 0.3), If(IsServerSide, 0.95, 0.5))
                Case Else '已连接到节点，但未连接到房主
                    UpdateLoadingPage("正在连接到房主……", "连接房主")
                    Task.Progress = (Task.Progress + 0.02).Clamp(Math.Min(0.45 + PeerCount * 0.05, 0.65), 0.95)
            End Select
            '超时判定
            If LastProgress <> Task.Progress Then
                TimeoutCounter = 0
            ElseIf TimeoutCounter < 30 * 2 Then
                TimeoutCounter += 1
            Else '进度停滞超过 30s
                Select Case PeerCount
                    Case -1 'CLI 无返回
                        Panic("无法启动联机模块", "近期日志：" & vbCrLf & LogHistory.Join(vbCrLf))
                    Case 0 'CLI 有返回，但未连接到任何节点
                        If IsDirectMode Then
                            Dim LanOnly As Boolean = DirectPeerAddresses.Count > 0 AndAlso DirectPeerAddresses.All(Function(a) IsPrivateEndpoint(a))
                            Dim Extra As String = If(LanOnly,
                                $"{vbCrLf}{vbCrLf}注意：这条邀请里只有内网地址（{DirectPeerAddress}），只能和房主在同一个局域网时使用。{vbCrLf}如果你们不在同一个网络，需要房主那边的路由器开 UPnP 或手动做端口映射，重新创建房间后再把新邀请发给你。",
                                "")
                            Panic("无法连接到节点", $"没有连上邀请码里的地址（{DirectPeerAddress}）。请确认：{vbCrLf}- 房主的电脑和端口 {ServerPort} 可以访问（房主需放行防火墙）{vbCrLf}- 邀请里的地址和端口没有被路由器挡住{vbCrLf}- 邀请码没有输错、房间还没关闭{Extra}")
                        Else
                            Panic("无法连接到节点", $"当前没开直连模式，联机需要通过中继节点 {String.Join("、", RawPeers)}，而这些节点目前连不上。{vbCrLf}{vbCrLf}建议：设置 → 联机 → 勾选「打开直连模式」，然后用邀请码直连（不需要中继节点）。")
                        End If
                    Case Else '已连接到节点，但未连接到房主
                        Panic("无法连接到房主", $"可能的原因：{vbCrLf}- 你或者房主的网络环境不佳{vbCrLf}- 房主已关闭房间{vbCrLf}- 邀请码输错了")
                End Select
            End If
        Loop Until Task.IsCanceled
        If Task.IsCanceled Then Throw New OperationCanceledException
        If IsTunMode Then WaitTunReady()
        '等待连接稳定，最多 5s
        If IsServerSide Then Return
        UpdateLoadingPage("连接优化中……", "优化连接")
        Task.Progress = 0.999
        For i = 1 To 50
            Dim Server = GetTargetPeer()
            If Server IsNot Nothing AndAlso Not Server.Relay AndAlso Server.Ping < 100 Then Return '结束
            If Task.IsCanceled Then Throw New OperationCanceledException
            Thread.Sleep(100)
        Next
    End Sub
    ''' <summary>
    ''' 从在线配置和 API 获取需要连接的节点列表，并更新发现节点 ID。
    ''' 根据发现节点 ID，会有以下行为变化：
    '''   -1：作为房主，根据负载均衡选择一个发现节点。
    '''   0：作为加入者，但邀请码未提供发现节点 ID，使用回退发现节点。
    '''   >0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点。
    ''' </summary>
    Private Function GetTargetPeers() As List(Of String)
        Dim FinalPeers As New List(Of String), FinalDiscoverID As Integer = -1
        Dim FallbackDiscoverID = ServerConfig("Link")("DiscoverPeerId").ToObject(Of Integer)
        Dim FallbackDiscoverAddress As String = ServerConfig("Link")("DiscoverPeer").ToString()
        Try
            '从 API 获取节点列表
            Dim RawNodes As JObject
            Dim IsFallbackRawList As Boolean = False
            Try
                RawNodes = NetRequestByClient("https://uptime.easytier.cn/api/nodes?page=1&per_page=1000", RequireJson:=True).DeserializeJson()
            Catch exx As Exception
                Logger.Warn(exx, "从源站获取节点列表失败，将使用 CDN 缓存")
                RawNodes = NetRequestByClientRetry("https://easytier.meloong.com/?page=1&per_page=1000", RequireJson:=True).DeserializeJson()
                IsFallbackRawList = True
            End Try
            '分析节点列表
            Dim Nodes As New List(Of JObject) '负载会添加在 load 字段上
            Dim BlackList As List(Of String) = ServerConfig("Link")("PeersBlackList").Select(Function(p) p.ToString).ToList()
            For Each Node As JObject In RawNodes("data")("items")
                '状态检查
                Dim ID = Node("id").ToObject(Of Integer)
                If Not Node("is_active").ToObject(Of Boolean) OrElse Not Node("is_approved").ToObject(Of Boolean) Then Continue For
                If ID = DiscoverNodeID Then GoTo ForcedPass '若为指定的发现节点，忽略后续检查
                If ID = FallbackDiscoverID Then Continue For '不主动选取回退发现节点
                If BlackList.Contains(Node("address").ToString) Then Continue For 'ServerConfig 黑名单
                If Node("usage_percentage").ToObject(Of Double) = 0 AndAlso RandomInteger(1, 100) <> 1 Then Continue For '或许节点有问题才导致是 0 负载，让它只有 1% 概率被选中
                '标签检查
                Dim Tags = Node("tags").Select(Function(t) t.ToString).ToList
                If Not Tags.Contains("国内") OrElse Not Tags.Contains("MC中继") Then Continue For
                '计算负载并加入列表
ForcedPass:
                Dim Load As Double = Node("usage_percentage").ToObject(Of Double) '负载
                Load *= 110 - Node("health_percentage_24h").ToObject(Of Double) '可用率
                Node("load") = Load
                Nodes.Add(Node)
            Next
            '排序
            If Not IsFallbackRawList Then
                Nodes = Nodes.OrderBy(Function(n) n("load").ToObject(Of Double)).ToList() '按负载从低到高排序
            Else
                Nodes = Nodes.Shuffle().ToList() '回退到 CDN 缓存时，由于负载数据可能过期，直接随机选择
            End If
            '选取发现节点
            Dim SelectedDiscoverNode As JObject = Nothing
            If DiscoverNodeID = -1 Then '-1：作为房主，根据负载均衡选择一个发现节点
                SelectedDiscoverNode = Nodes.FirstOrDefault(Function(n) Not n("allow_relay").ToObject(Of Boolean))
            ElseIf DiscoverNodeID > 0 Then '>0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点
                SelectedDiscoverNode = Nodes.FirstOrDefault(Function(n) n("id").ToObject(Of Integer) = DiscoverNodeID)
                If SelectedDiscoverNode Is Nothing Then
                    Logger.Warn($"未找到 ID {DiscoverNodeID} 的发现节点")
                    Panic("房间已过期", "请让房主重新创建房间！")
                    Throw New OperationCanceledException
                End If
            End If
            If SelectedDiscoverNode Is Nothing Then '使用回退发现节点
                SelectedDiscoverNode = New JObject From {{"address", FallbackDiscoverAddress}, {"id", FallbackDiscoverID}}
                Logger.Warn("将使用回退发现节点")
            End If
            FinalPeers.Add(SelectedDiscoverNode("address").ToString())
            FinalDiscoverID = SelectedDiscoverNode("id").ToObject(Of Integer)
            Logger.Info($"发现节点：{SelectedDiscoverNode("address")} (ID: {FinalDiscoverID})")
            '选取中继节点
            If ModeDebug OrElse Not IsServerSide Then '房主只连接发现节点，不连接中继节点
                Dim RelayCount As Integer = ServerConfig("Link")("RandomPeer").ToObject(Of Integer)
                Dim RelayNodes = Nodes.Where(Function(n) n("allow_relay").ToObject(Of Boolean)).ToList()
                If RelayNodes.Count < RelayCount Then Throw New Exception($"可用的中继节点数量不足，需要 {RelayCount} 个，实际 {RelayNodes.Count} 个")
                FinalPeers.AddRange(RelayNodes.Take(RelayCount).Select(Function(n) n("address").ToString()))
            End If
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Logger.Error(ex, "获取节点列表失败，联机质量可能受到影响", LogBehavior.Toast)
            FinalPeers.AddRange(ServerConfig("Link")("Peers").Select(Function(p) p.ToString))
            If FinalDiscoverID <= 0 Then
                FinalPeers.Add(FallbackDiscoverAddress)
                FinalDiscoverID = FallbackDiscoverID
            End If
        End Try
        '版本 1 兼容
        If CType(ServerConfig("Link"), JObject).ContainsKey("EnableForcedPeer") Then
            FinalPeers.Add("tcp://mc1.easytier.cn:55558")
            If FinalDiscoverID <= 0 Then FinalDiscoverID = 94
        End If
        '强制添加的节点
        FinalPeers.AddRange(ServerConfig("Link")("MandatoryPeers").Select(Function(p) p.ToString))
        '结束
        DiscoverNodeID = FinalDiscoverID
        Return FinalPeers.Distinct.ToList
    End Function

#End Region

#Region "进程与日志"

    ''' <summary>
    ''' EasyTier Core 进程。
    ''' </summary>
    Private Shared ProcessCore As Process
    Private Shared ProcessOutputHandle As AutoResetEvent, ProcessErrorHandle As AutoResetEvent

    ''' <summary>
    ''' 若程序正在运行，则结束程序进程。
    ''' 即使失败也不会抛出异常。
    ''' </summary>
    Public Shared Sub ProcessStop()
        Try
            StopTunLogPump()
            '关闭未捕获的进程
            For Each ProcessObject In Process.GetProcesses
                If ProcessObject.Id = ProcessCore?.Id Then Continue For
                If ProcessObject.ProcessName <> "联机模块" AndAlso ProcessObject.ProcessName <> "联机模块 CLI" Then Continue For
                Try
                    Logger.Info($"停止残留的联机模块，PID：{ProcessObject.Id}，进程名：{ProcessObject.ProcessName}")
                    ProcessObject.Kill()
                    ProcessObject.Close()
                Catch exx As Exception
                    Logger.Warn(exx, $"结束进程失败（{ProcessObject.ProcessName}，PID {ProcessObject.Id}）")
                End Try
            Next
            '关闭由自身启动的进程
            If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then
                Logger.Info($"停止所启动的联机模块，PID：{ProcessCore.Id}")
                ProcessCore.Kill()
                ProcessCore.Close()
                ProcessCore = Nothing
            End If
            '释放资源
            ProcessOutputHandle?.Dispose()
            ProcessErrorHandle?.Dispose()
        Catch ex As Exception
            Logger.Warn(ex, "停止联机模块失败")
        End Try
    End Sub
    ''' <summary>
    ''' 出现意外错误，给出错误信息并结束联机。
    ''' </summary>
    Private Sub Panic(Brief As String, Detail As String)
        ModPlugin.RaiseLinkRoomClosed()
        DeleteAddressFile()
        '常见原因分析
        If Detail.Contains("failed to listen on ") Then
            Detail = $"监听端口失败。{vbCrLf}请点击重试，如果还是出现此错误，可以重启电脑解决。{vbCrLf}{vbCrLf}{Detail}"
        End If
        '显示信息
        If LinkState = LinkStates.Loading Then
            FailReason = Brief
            LinkLoader.Failed(New Exception(Detail))
        Else
            MyMsgBox(Detail, "联机出错：" & Brief, IsWarn:=True)
            ChangeState(LinkStates.Waiting)
            Telemetry("联机失败", "Exception", FilterUserName(Brief & "：" & Detail, "*"))
        End If
    End Sub

    ''' <summary>
    ''' 运行指定程序并读取标准输出（带超时）。开源版没有内置该工具函数，这里自行实现。
    ''' </summary>
    Private Function RunCliAndGetOutput(FileName As String, Arguments As String, TimeoutMilliseconds As Integer) As String
        Dim Info = New ProcessStartInfo With {
            .FileName = FileName,
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Using Proc As Process = Process.Start(Info)
            Dim OutTask = Proc.StandardOutput.ReadToEndAsync()
            Dim ErrTask = Proc.StandardError.ReadToEndAsync()
            If Not Proc.WaitForExit(TimeoutMilliseconds) Then
                Try
                    Proc.Kill()
                Catch
                End Try
                Throw New Exception($"调用 {IO.Path.GetFileName(FileName)} 超时（{TimeoutMilliseconds}ms）")
            End If
            Dim Result As String = If(OutTask.Wait(1000), OutTask.Result, "")
            Dim ErrText As String = If(ErrTask.Wait(1000), ErrTask.Result, "")
            If Not String.IsNullOrWhiteSpace(ErrText) Then Logger.Warn($"CLI stderr：{ErrText}")
            Return Result
        End Using
    End Function

    ''' <summary>
    ''' 当前进程是否具有管理员权限。
    ''' </summary>
    Private Function IsAdmin() As Boolean
        Try
            Dim Id = Security.Principal.WindowsIdentity.GetCurrent()
            Return New Security.Principal.WindowsPrincipal(Id).IsInRole(Security.Principal.WindowsBuiltInRole.Administrator)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' TUN 模式需要管理员权限。启动器本身没提权时，直接以管理员身份重启启动器，
    ''' 让联机模块、防火墙、网卡操作统统继承管理员权限，避免「网卡装好了但后续操作没权限」。
    ''' 返回 False 表示已经开始重启流程，调用方应当直接返回。
    ''' </summary>
    Private Function EnsureAdminForTun() As Boolean
        If Not IsTunMode OrElse IsAdmin() Then Return True
        '退出前先讲清楚会发生什么：用户需要知道接下来会弹 UAC，并且要点「是」
        Dim Choice As Integer = MyMsgBox(
            "TUN 模式需要管理员权限，启动器现在会以管理员身份重启。" & vbCrLf & vbCrLf &
            "重启时 Windows 会弹出「是否允许此应用对你的设备进行更改」，请点「是」；点了「否」的话这次联机就会取消。" & vbCrLf & vbCrLf &
            "重启不会丢掉你刚才的选择，重启完成后直接再点一次「创建房间」或「加入房间」即可。",
            "即将以管理员身份重启", "继续", "取消")
        If Choice <> 1 Then Return False '只有第一个按钮「继续」才提权重启
        Logger.Info("TUN 模式需要管理员权限，正在以管理员身份重启启动器")
        Hint("正在以管理员身份重启启动器，请在弹窗里点「是」……", HintType.Blue)
        '不能直接 start 提权后的新实例：PCL 是单实例程序，新实例一启动就会把老实例置顶然后自己退出。
        '所以借一个提权的 cmd 等几秒（等当前实例彻底退出、互斥体释放），再由它启动启动器，子进程继承管理员权限。
        Dim StartCmd As String = "/c ping -n 4 127.0.0.1 >nul & start """" """ & PathExe & """"
        Dim Info As New ProcessStartInfo With {
            .FileName = "cmd.exe",
            .Arguments = StartCmd,
            .UseShellExecute = True,
            .Verb = "runas",
            .WindowStyle = ProcessWindowStyle.Hidden
        }
        Try
            Process.Start(Info)
        Catch ex As ComponentModel.Win32Exception
            If ex.NativeErrorCode = 1223 Then
                Panic("需要管理员权限", "虚拟局域网（TUN）模式需要管理员权限，但你在 UAC 提示里点了「否」。" & vbCrLf & vbCrLf &
                      "请重新创建房间并在系统提示时选「是」；如果不想每次都被询问，可以右键启动器 →「属性」→「兼容性」→ 勾选「以管理员身份运行此程序」。")
                Return False
            End If
            Throw
        End Try
        '立刻退出当前实例，把位置让给提权后的新实例
        RunInNewThread(
        Sub()
            Threading.Thread.Sleep(600)
            RunInUi(Sub() FrmMain.Close())
        End Sub)
        Return False
    End Function

    ''' <summary>
    ''' 刷新选择页上的模式开关与「上次端口」按钮。
    ''' </summary>
    Private Sub InitSelectPanel() Handles Me.Loaded
        RefreshSelectPanel()
    End Sub

    ''' <summary>
    ''' 启动进程。
    ''' </summary>
    Private Sub ProcessStart(Arguments As String)
        If IsTunMode AndAlso Not IsAdmin() Then
            ProcessStartElevated(Arguments)
            Return
        End If
        Dim Info = New ProcessStartInfo With {
            .FileName = PathEasyTier & "联机模块.exe",
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardError = True,
            .RedirectStandardOutput = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Logger.Info($"正在启动 EasyTier：{Arguments}")
        If IsTunMode Then Logger.Info("TUN 模式：当前启动器已有管理员权限，联机模块直接继承，不会再弹 UAC")
        ProcessCore = New Process With {.StartInfo = Info}
        Dim LogLineHandler =
        Sub(sender As Object, e As DataReceivedEventArgs, Handle As AutoResetEvent)
            Try
                If e.Data Is Nothing Then
                    Handle.[Set]()
                    Return
                End If
                ProcessLogLine(e.Data)
            Catch unused As ObjectDisposedException
            Catch ex As Exception
                Logger.Warn(ex, "读取 EasyTier 信息失败")
            End Try
        End Sub
        ProcessOutputHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.OutputDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessOutputHandle)
        ProcessErrorHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.ErrorDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessErrorHandle)
        ProcessCore.Start()
        ProcessCore.BeginOutputReadLine()
        ProcessCore.BeginErrorReadLine()
    End Sub
    Private Sub ProcessLogLine(Line As String)
        '记录日志
        Logger.Info(Line)
        LogHistory.Enqueue(Line)
        If LogHistory.Count >= 10 Then LogHistory.Dequeue()
        '检查日志内容
        If Line.ContainsIgnoreCase("new peer connection added") Then
            Logger.Warn($"已建立连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        ElseIf Line.ContainsIgnoreCase("peer connection removed") Then
            Logger.Warn($"已断开连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        End If
    End Sub
    Private LogHistory As New Queue(Of String)(10)

    ''' <summary>
    ''' TUN 模式需要管理员权限创建虚拟网卡。用 cmd 包装一层并把输出写进文件再回读，
    ''' 既保留 ProcessLogLine 的解析，也不用给启动器本体提权。
    ''' </summary>
    Private Sub ProcessStartElevated(Arguments As String)
        Dim ExePath As String = PathEasyTier & "联机模块.exe"
        TunLogFile = PathEasyTier & "link-console.log"
        TunLogOffset = 0
        Try
            IO.File.Delete(TunLogFile)
        Catch
        End Try
        Dim ScriptPath As String = PathEasyTier & "link-elevated.cmd"
        Dim Script As String = "@echo off" & vbCrLf &
            "chcp 65001 >nul" & vbCrLf &
            "cd /d ""%~dp0""" & vbCrLf &
            """" & ExePath & """ " & Arguments & " > """ & TunLogFile & """ 2>&1" & vbCrLf
        IO.File.WriteAllText(ScriptPath, Script, New UTF8Encoding(False))
        Dim Info As New ProcessStartInfo With {
            .FileName = "cmd.exe",
            .Arguments = "/c """"" & ScriptPath & """""",
            .UseShellExecute = True,
            .Verb = "runas",
            .WindowStyle = ProcessWindowStyle.Hidden,
            .WorkingDirectory = PathEasyTier
        }
        Logger.Info($"正在以管理员权限启动 EasyTier（TUN 模式）：{Arguments}")
        Try
            ProcessCore = Process.Start(Info)
        Catch ex As ComponentModel.Win32Exception
            If ex.NativeErrorCode = 1223 Then
                Panic("需要管理员权限", "虚拟局域网（TUN）模式需要管理员权限来创建虚拟网卡，但你在 UAC 提示里点了「否」。" & vbCrLf & vbCrLf & "请重新创建房间，并在系统提示时选择「是」。")
                Return
            End If
            Throw
        End Try
        StartTunLogPump()
    End Sub

    Private Sub StartTunLogPump()
        TunLogThread = New Threading.Thread(
            Sub()
                While ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited
                    Try
                        If TunLogFile IsNot Nothing AndAlso IO.File.Exists(TunLogFile) Then
                            Using Fs As New IO.FileStream(TunLogFile, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                                Fs.Seek(TunLogOffset, IO.SeekOrigin.Begin)
                                Using Reader As New IO.StreamReader(Fs, Encoding.UTF8)
                                    Dim Text As String = Reader.ReadToEnd()
                                    TunLogOffset = Fs.Position
                                    For Each Line As String In Text.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                                        ProcessLogLine(Line)
                                    Next
                                End Using
                            End Using
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, "读取提权联机模块输出失败")
                    End Try
                    Threading.Thread.Sleep(300)
                End While
            End Sub) With {.IsBackground = True, .Name = "联机模块日志回读"}
        TunLogThread.Start()
    End Sub

    Private Shared Sub StopTunLogPump()
        TunLogFile = Nothing
        TunLogOffset = 0
        TunLogThread = Nothing
    End Sub

    ''' <summary>等待虚拟网卡就绪：轮询本机是否已分配到 10.114.114.x 地址。</summary>
    Private Sub WaitTunReady()
        Dim Expect As String = If(IsServerSide, TunHostIp, TunLocalIp)
        UpdateLoadingPage("正在创建虚拟网卡……", "等待虚拟网卡")
        For i As Integer = 1 To 60
            If TunAdapterReady(Expect) Then
                Logger.Info($"虚拟网卡已就绪：{Expect}")
                Return
            End If
            If ProcessCore Is Nothing OrElse ProcessCore.HasExited Then
                Panic("虚拟网卡创建失败", "联机模块在创建虚拟网卡前就退出了。" & vbCrLf & "常见原因：UAC 提权被拒绝、缺少 wintun.dll、或被安全软件拦截。" & vbCrLf & vbCrLf & "近期日志：" & vbCrLf & String.Join(vbCrLf, LogHistory))
                Return
            End If
            Threading.Thread.Sleep(250)
        Next
        Panic("虚拟网卡创建超时", $"15 秒内没有检测到虚拟网卡地址 {Expect}。" & vbCrLf & "请确认系统提示里允许了管理员权限，并允许联机模块通过防火墙。如果装了 ZeroTier、Radmin VPN 等虚拟网卡软件，先退出它们再试。" & vbCrLf & vbCrLf & "近期日志：" & vbCrLf & String.Join(vbCrLf, LogHistory))
    End Sub

    Private Function TunAdapterReady(Expect As String) As Boolean
        If String.IsNullOrEmpty(Expect) Then Return False
        Try
            For Each Nic As System.Net.NetworkInformation.NetworkInterface In System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                If Nic.OperationalStatus <> System.Net.NetworkInformation.OperationalStatus.Up Then Continue For
                For Each Addr As System.Net.NetworkInformation.UnicastIPAddressInformation In Nic.GetIPProperties().UnicastAddresses
                    If Addr.Address.AddressFamily = Net.Sockets.AddressFamily.InterNetwork AndAlso Addr.Address.ToString() = Expect Then Return True
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "检测虚拟网卡失败")
        End Try
        Return False
    End Function

#End Region

#Region "CLI 节点信息获取"

    Private Class Peer
        ''' <summary>
        ''' 该节点的类型。
        ''' </summary>
        Public ReadOnly Type As Types
        Public Enum Types
            ''' <summary>
            ''' 客户端。
            ''' </summary>
            Client
            ''' <summary>
            ''' 服务器。
            ''' </summary>
            Server
            ''' <summary>
            ''' 公共节点。
            ''' </summary>
            Misc
            ''' <summary>
            ''' 自己，或者自己的重复（它可能返回多个自己）。
            ''' </summary>
            Self
        End Enum
        ''' <summary>
        ''' 与该节点的延迟 (ms)。
        ''' 若是自己则为 0。
        ''' </summary>
        Public ReadOnly Ping As Double
        ''' <summary>
        ''' 名称。
        ''' </summary>
        Public ReadOnly Name As String
        ''' <summary>
        ''' 是否通过中继连接。
        ''' </summary>
        Public ReadOnly Relay As Boolean
        ''' <summary>
        ''' NAT 类型。
        ''' </summary>
        Public ReadOnly NATType As NATTypes

        ''' <summary>
        ''' 从 CLI 给出的信息分析对应的数据。
        ''' </summary>
        Public Sub New(Info As JObject)
            '类别
            Dim PeerName = Info("hostname").ToString
            If Info("cost").ToString = "Local" Then
                Type = Types.Self
            ElseIf PeerName.StartsWithF("Client") Then
                Type = Types.Client
            ElseIf Info("ipv4") = "10.114.114.114" Then '只允许这个 IP 作为房主，避免网络被多个房主搞炸的情况
                Type = Types.Server
            Else
                Type = Types.Misc
            End If
            '基础信息
            Double.TryParse(Info("lat_ms"), NumberStyles.Any, CultureInfo.InvariantCulture, Ping)
            Name = PeerName
            Relay = Info("cost").ToString.ContainsIgnoreCase("relay")
            NATType = EnumUtils.FromString(Of NATTypes)(Info("nat_type"))
        End Sub
        Public Overrides Function ToString() As String
            Return $"{Type} - {Name} - Ping {Ping:0.0}ms [中继? {Relay}] - NAT {NATType}"
        End Function
    End Class
    Private Enum NATTypes
        'https://github.com/EasyTier/EasyTier/blob/6bb2fd9a15ab2499bdeabdcc3a925e9bd9aebf50/easytier/src/proto/common.proto#L129
        Unknown = 0
        OpenInternet = 1
        NoPAT = 2
        FullCone = 3
        Restricted = 4
        PortRestricted = 5
        Symmetric = 6
        SymUdpFirewall = 7
        SymmetricEasyInc = 8
        SymmetricEasyDec = 9
        ''' <summary>
        ''' 尚未获取。
        ''' </summary>
        Pending = 10
    End Enum

    ''' <summary>
    ''' 当前的节点列表，使用 RefreshPeerLoader 来刷新。
    ''' 若尚未成功获取过则为 Nothing，但保证在加载完成后至少是一个列表。
    ''' </summary>
    Private Peers As List(Of Peer) = Nothing
    ''' <summary>
    ''' 自己的 NAT 类型。
    ''' </summary>
    Private NATType As NATTypes = NATTypes.Pending

    ''' <summary>
    ''' 调用 EasyTier CLI 获取已连接节点信息。
    ''' 这会过滤掉尚未完成连接的其他节点以及自己。
    ''' 若在加载完成后连续获取失败 3 次，则会强制断开连接。
    ''' </summary>
    Private RefreshPeerLoader As New LoaderTask(Of Integer, List(Of Peer))("EasyTier CLI", AddressOf RefreshPeer)
    Private Sub RefreshPeer()
        Try
            Dim CliResult = RunCliAndGetOutput(PathEasyTier & "联机模块 CLI.exe", $"-o json -p 127.0.0.1:{RPCPort} peer", 2000)
            '解析
            If Not CliResult.Contains("lat_ms") Then Throw New Exception("CLI 调用失败：" & vbCrLf & CliResult)
            If GetUuid() Mod If(ModeDebug, 23, 103) = 0 Then Logger.Info($"CLI 输出抽样：{vbCrLf}{CliResult}")
            Dim NewPeers As New List(Of Peer)
            For Each Line As JObject In CType(CliResult.DeserializeJson(), JArray)
                Try
                    Dim Peer = New Peer(Line)
                    If Peer.Type = Peer.Types.Self Then
                        NATType = Peer.NATType '记录自己的 NAT
                    Else
                        NewPeers.Add(Peer)
                    End If
                Catch exx As Exception
                    Logger.Warn(exx, $"错误的信息（{Line}）")
                End Try
            Next
            '完成
            Peers = NewPeers
            FailCount = 0
        Catch ex As Exception
            Logger.Warn(ex, "获取节点信息失败")
            If LinkState = LinkStates.Finished Then
                FailCount += 1
                If FailCount >= 4 Then Panic("获取节点信息失败", ex.Message)
            End If
        End Try
    End Sub
    ''' <summary>
    ''' CLI 调用连续失败的次数。
    ''' </summary>
    Private FailCount As Integer = 0

    ''' <summary>
    ''' 根据当前的节点列表获取 Ping 值。
    ''' 服务端会返回所有节点中最低的 Ping，客户端会返回服务端的 Ping。
    ''' 0 代表尚不可用。
    ''' </summary>
    Private Function GetPeerPing() As Double
        Dim Peer = GetTargetPeer()
        If Peer Is Nothing Then Return 0
        Return Peer.Ping
    End Function
    ''' <summary>
    ''' 服务端会返回所有节点中 Ping 大于 0 最低的那一个，客户端会返回服务端。
    ''' 若没有则为 Nothing。
    ''' </summary>
    Private Function GetTargetPeer() As Peer
        If Peers Is Nothing Then Return Nothing
        Dim Targets As IEnumerable(Of Peer)
        If IsServerSide Then
            Targets = Peers.Where(Function(p) p.Ping > 0)
        Else
            Targets = Peers.Where(Function(p) p.Type = Peer.Types.Server AndAlso p.Ping > 0)
        End If
        If Not Targets.Any Then Return Nothing
        '返回 Ping 且最低的那个
        Dim MinPing = Targets.Min(Function(p) p.Ping)
        Return Targets.First(Function(p) p.Ping = MinPing)
    End Function

    ''' <summary>
    ''' 获取当前房间中的人数，包括自己。
    ''' 0 代表不可用。
    ''' </summary>
    Private ReadOnly Property PeopleCount As Integer
        Get
            If Peers Is Nothing Then Return 0
            Return Peers.Where(Function(p) p.Type <> Peer.Types.Misc).Count() + 1 '加上自己
        End Get
    End Property

#End Region

#Region "定时任务"

    '启动
    Private Sub StartTimerThread() Handles Me.Loaded
        Static IsTimerStarted As Boolean = False
        If IsTimerStarted Then Return
        RunInNewThread(
        Sub()
            Dim Counter As Integer = 0
            Do While True
                Try
                    Thread.Sleep(200)
                    Counter += 200
                    '每秒执行一次
                    If Counter >= 1000 Then
                        Counter = 0
                        Update()
                    End If
                    '每 200ms 更新进度条
                    If LinkState = LinkStates.Loading Then RunInUi(AddressOf UpdateProgressBar)
                Catch ex As Exception
                    Logger.Error(ex, "联机模块主时钟出错")
                    Thread.Sleep(10000)
                End Try
            Loop
        End Sub, "Link Timer")
    End Sub

    '每秒或进入页面时触发
    Private Sub Update() Handles Me.Loaded
        If LinkState <> LinkStates.Finished Then Return
        'TUN：每 3 秒探一次「游戏端口是否已经通了」，结果给 UI 用（探测本身不占 UI 线程）
        If IsTunMode Then
            TunProbeCounter += 1
            If TunProbeCounter >= 3 Then
                TunProbeCounter = 0
                Dim GamePort As Integer = 0
                If Integer.TryParse(ServerPort, GamePort) AndAlso GamePort > 0 Then
                    Try
                        TunPortOk = If(IsServerSide,
                            IsLocalPortListening(GamePort),
                            CanReachTcp(TunHostIp, GamePort, 700))
                    Catch ex As Exception
                        Logger.Warn(ex, "探测 TUN 游戏端口失败")
                    End Try
                End If
            End If
        End If
        '重新获取信息
        SyncLock RefreshPeerLoader.LockState
            If RefreshPeerLoader.State <> LoadState.Loading Then RefreshPeerLoader.Start(IsForceRestart:=True)
        End SyncLock
        '更新 UI
        If FrmMain.PageCurrent = FormMain.PageType.Link Then
            RunInUi(
            Sub()
                'Logo 旋转动画
                AniStart(AaRotateTransform(ImgFinishLogo, 500, 5000), "Link Logo Rotation")
                'Ping
                Dim Ping As Double = GetPeerPing()
                Dim Connecting As Boolean = Ping Mod 500 = 0 OrElse $"{Ping:0.0}" = "1.0" OrElse FailCount > 0
                If IsTunMode AndAlso PeopleCount <= 1 Then
                    LabFinishPing.Text = "等待连接"
                ElseIf Connecting Then
                    LabFinishPing.Text = "连接优化中"
                Else
                    LabFinishPing.Text = If(Ping >= 10, $"{Ping:0} ms", $"{Ping:0.0} ms")
                End If
                '人数显示
                LabFinishPlayer.Text = PeopleCount & "  人"
                '------------------------
                '  提示条
                '------------------------
                HintFinish.Visibility = Visibility.Collapsed
                If IsTunMode Then
                    UpdateTunStatus(Connecting)
                    Return 'TUN 模式使用自己的状态提示，不再走中继/NAT 那套判断
                End If
                If IsServerSide Then Return '服务端没有需要显示的提示
                If Connecting Then Return '连接稳定过程中不显示提示
                '中继提示
                Dim Server As Peer = GetTargetPeer()
                If Server Is Nothing Then Return
                If Server.Relay Then
                    If NATType >= NATTypes.Symmetric AndAlso Server.NATType >= NATTypes.Symmetric Then
                        HintFinish.Text = "你和房主的网络环境都不太好，"
                    ElseIf NATType >= NATTypes.Symmetric Then
                        HintFinish.Text = "你的网络环境不太好，"
                    ElseIf Server.NATType >= NATTypes.Symmetric Then
                        HintFinish.Text = "房主的网络环境不太好，"
                    Else
                        HintFinish.Text = "你或者房主的网络环境不太好，"
                    End If
                    If String.IsNullOrWhiteSpace(Settings.Get(Of String)("LinkCustomPeer")) Then
                        HintFinish.Text &= "正使用社区节点进行中继。"
                    Else
                        HintFinish.Text &= "正通过自定义节点进行中继。"
                    End If
                    HintFinish.Visibility = Visibility.Visible
                    HintFinish.Theme = MyHint.Themes.Yellow
                    Return
                End If
                '环境比房主更好的提示
                If NATType >= NATTypes.PortRestricted Then Return '自身的 NAT 为 2 或更好
                If Server.NATType < NATTypes.PortRestricted Then Return '房主的 NAT 为 3 或更差
                Dim OtherPlayers = Peers.Where(Function(p) p.Type = Peer.Types.Client).ToList()
                If Not OtherPlayers.Any() Then Return '房间里还有其他玩家
                If OtherPlayers.Any(Function(p) p.Relay) Then Return '自己可以与所有玩家打洞
                If OtherPlayers.All(Function(p) p.NATType < NATTypes.PortRestricted) Then Return '任意其他玩家的 NAT 为 3 或更差
                HintFinish.Visibility = Visibility.Visible
                HintFinish.Theme = MyHint.Themes.Blue
                HintFinish.Text = "你的网络环境比房主更好！如果你来当房主，其他玩家或许能更加流畅！"
            End Sub)
        End If
        '检查核心状态
        CheckCrash()
        '检查节点状态
        If Not Peers.Any Then
            If IsDirectMode AndAlso IsServerSide Then Return '直连房主在等待玩家加入，此时没有节点是正常的
            Panic("网络连接已断开", "请检查你的网络环境是否正常。")
            Return
        End If
        If Not IsServerSide AndAlso Not Peers.Any(Function(p) p.Type = Peer.Types.Server) Then
            MyMsgBox("房主已离开房间！", "联机结束")
            ChangeState(LinkStates.Waiting)
            Return
        End If
        '广播联机房间端口（TUN 模式下走虚拟网卡直连，不需要本机端口广播）
        If Not IsServerSide AndAlso Not IsTunMode Then
            Try
                Static BroadcastSocket As New Socket(SocketType.Dgram, ProtocolType.Udp)
                BroadcastSocket.SendTo(
                    Encoding.UTF8.GetBytes($"[MOTD]PCL 联机房间[/MOTD][AD]{ClientPort}[/AD]"),
                    SocketFlags.None,
                    New IPEndPoint(IPAddress.Loopback, 4445))
            Catch ex As Exception
                Logger.Warn(ex, "广播联机房间端口失败")
            End Try
        End If
    End Sub
    ''' <summary>
    ''' 检查联机模块是否崩溃。
    ''' </summary>
    Private Sub CheckCrash()
        If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then Return
        Panic("联机模块已崩溃", "近期日志：" & vbCrLf & LogHistory.Join(vbCrLf))
    End Sub

    ''' <summary>
    ''' TUN 模式的状态提示：连了几个人、直连还是中继、游戏端口通没通。
    ''' 作用是把「房间建好了」和「朋友真的能进游戏」区分开。
    ''' </summary>
    Private Sub UpdateTunStatus(Connecting As Boolean)
        Dim Others As List(Of Peer) = If(Peers, New List(Of Peer)).Where(Function(p) p.Type <> Peer.Types.Misc).ToList()
        Dim RelayCount As Integer = Others.Where(Function(p) p.Relay).Count()
        Dim Target As Peer = GetTargetPeer()
        Dim GamePort As Integer = 0
        Dim HasPort As Boolean = Integer.TryParse(ServerPort, GamePort) AndAlso GamePort > 0
        '工具提示：连接方式 + RTT + 双方 NAT
        If Target Is Nothing Then
            BtnFinishPing.ToolTip = "正在等待对端……"
        Else
            Dim SelfNat As String = GetSelfNatDescription()
            Dim PeerNat As String = If($"{Target.NATType}" = "Unknown" OrElse $"{Target.NATType}" = "Pending", "", $" / 对方 {DescribeNat(Target.NATType)}")
            BtnFinishPing.ToolTip = $"{If(RelayCount > 0, "经中继", "P2P 直连")} · RTT {Target.Ping:0.0} ms · NAT {SelfNat}{PeerNat}"
        End If
        If IsServerSide Then
            If Others.Count = 0 Then
                HintFinish.Text = $"等待朋友加入 · 虚拟地址 {TunHostIp}:{GamePort}"
                HintFinish.Theme = MyHint.Themes.Blue
            ElseIf HasPort AndAlso Not TunPortOk Then
                HintFinish.Text = $"已连上 {Others.Count} 位朋友，但游戏端口 {GamePort} 还没开放：进游戏后点「对局域网开放」即可"
                HintFinish.Theme = MyHint.Themes.Yellow
            Else
                HintFinish.Text = $"已连上 {Others.Count} 位朋友 · {If(RelayCount > 0, $"{RelayCount} 位走中继", "P2P 直连")} · 游戏端口 {GamePort} 已开放"
                HintFinish.Theme = MyHint.Themes.Blue
            End If
        Else
            If Connecting Then Return
            If Target Is Nothing Then
                HintFinish.Text = "已进入虚拟局域网，正在等待房主出现……"
                HintFinish.Theme = MyHint.Themes.Blue
            ElseIf HasPort AndAlso Not TunPortOk Then
                HintFinish.Text = $"已进入虚拟局域网，但房主的游戏端口 {GamePort} 还没开放（等房主进游戏开房间）"
                HintFinish.Theme = MyHint.Themes.Yellow
            Else
                HintFinish.Text = $"已连上房主 {TunHostIp}:{GamePort} · {If(Target.Relay, "经中继", "P2P 直连")} · {Target.Ping:0} ms"
                HintFinish.Theme = MyHint.Themes.Blue
            End If
        End If
        HintFinish.Visibility = Visibility.Visible
    End Sub

    ''' <summary>本机是否有 TCP 端口在监听（房主侧判断「游戏开没开」）。</summary>
    Private Shared Function IsLocalPortListening(Port As Integer) As Boolean
        Try
            Return Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(Function(e) e.Port = Port)
        Catch ex As Exception
            Logger.Warn(ex, "检测本机端口监听失败")
            Return True '检测失败时不要误报「游戏没开」
        End Try
    End Function

    ''' <summary>在超时内尝试建立 TCP 连接（加入者侧判断「房主游戏端口通没通」）。</summary>
    Private Shared Function CanReachTcp(Host As String, Port As Integer, TimeoutMs As Integer) As Boolean
        Try
            Using Client As New Net.Sockets.TcpClient()
                Dim TryConnect As Threading.Tasks.Task = Client.ConnectAsync(Host, Port)
                If Not TryConnect.Wait(TimeoutMs) Then Return False
                Return Client.Connected
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 判断「IP:端口」是不是内网地址（10/8、172.16/12、192.168/16、127/8、169.254/16）。
    ''' 邀请里只有内网地址时，跨网络是连不上的，得在报错里直接说清楚。
    ''' </summary>
    Private Shared Function IsPrivateEndpoint(Endpoint As String) As Boolean
        If String.IsNullOrEmpty(Endpoint) Then Return True
        Dim Host As String = Endpoint.Split(":"c).FirstOrDefault()
        If String.IsNullOrEmpty(Host) Then Return True
        Dim Ip As Net.IPAddress = Nothing
        If Not Net.IPAddress.TryParse(Host, Ip) Then Return False
        Dim Bytes = Ip.GetAddressBytes()
        If Bytes.Length <> 4 Then Return Ip.IsIPv6LinkLocal OrElse Ip.IsIPv6SiteLocal
        If Bytes(0) = 10 OrElse Bytes(0) = 127 Then Return True
        If Bytes(0) = 192 AndAlso Bytes(1) = 168 Then Return True
        If Bytes(0) = 172 AndAlso Bytes(1) >= 16 AndAlso Bytes(1) <= 31 Then Return True
        If Bytes(0) = 169 AndAlso Bytes(1) = 254 Then Return True
        Return False
    End Function

    ''' <summary>
    ''' 这个端口在本机能否真正绑定（TCP + UDP）。
    ''' 只查「有没有程序在监听」不够：Hyper-V、WSL、Docker 会预留整段端口，
    ''' 这些端口查不到监听者，但绑定会失败（os error 10048），EasyTier 一旦踩中就直接退出。
    ''' </summary>
    Private Shared Function CanBindPort(Port As Integer) As Boolean
        Try
            Dim Listener As New Net.Sockets.TcpListener(Net.IPAddress.Any, Port)
            Listener.Start()
            Listener.Stop()
        Catch ex As Exception
            Return False
        End Try
        Try
            Using Client As New Net.Sockets.UdpClient(Port)
            End Using
        Catch ex As Exception
            Return False
        End Try
        Return True
    End Function

    ''' <summary>
    ''' 等端口进入监听状态，最多等 TimeoutMs，避免核心刚启动就被判失败。
    ''' </summary>
    Private Shared Function WaitPortListening(Port As Integer, TimeoutMs As Integer) As Boolean
        Dim Watch As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
        Do
            If IsLocalPortListening(Port) Then Return True
            If Watch.ElapsedMilliseconds >= TimeoutMs Then Return False
            Thread.Sleep(100)
        Loop
    End Function

    ''' <summary>
    ''' 挑一组连续编号、且本机真的能绑定的端口。
    ''' </summary>
    Private Shared Function PickBindablePorts(Count As Integer, ParamArray Excludes As Integer()) As List(Of Integer)
        Dim Used As New List(Of Integer)
        Try
            Used.AddRange(GetUsedPorts())
        Catch ex As Exception
            Logger.Warn(ex, "获取占用端口列表失败，改用绑定测试")
        End Try
        Used.AddRange(Excludes)
        Dim StartPort As Integer = RandomInteger(20000, 30000)
        For Offset As Integer = 0 To 19000
            Dim First As Integer = StartPort + Offset
            If First + Count - 1 > 65535 Then Exit For
            Dim Range = Enumerable.Range(First, Count)
            If Range.Any(Function(p) Used.Contains(p)) Then Continue For
            If Range.Any(Function(p) Not CanBindPort(p)) Then Continue For
            Return Range.ToList()
        Next
        '兜底：退回上游的实现
        Return FindFreePorts(Count, Excludes)
    End Function

    ''' <summary>
    ''' 自己测一次 NAT 类型。
    ''' 直连模式下我们给 EasyTier 传了 --stun-servers ""（不去连公共服务器），
    ''' 它就不会做 STUN，nat_type 永远是 Unknown，所以这里自己发 STUN 绑定请求：
    ''' 问两个不同的 STUN 服务器，看它们各自看到的公网映射。
    ''' 端口一致 → 锥形 NAT（能打洞）；端口不一致 → 对称型（打洞容易失败）。
    ''' </summary>
    Private Shared Function ProbeNatByStun() As String
        Dim Servers As String() = {"stun.miwifi.com:3478", "stun.hitv.com:3478", "stun.cloudflare.com:3478", "stun.l.google.com:19302"}
        Dim Mapped As New List(Of String)
        For Each Server As String In Servers
            Dim One As String = TryStunQuery(Server)
            If One IsNot Nothing AndAlso Not Mapped.Contains(One) Then Mapped.Add(One)
            If Mapped.Count >= 2 Then Exit For
        Next
        If Mapped.Count = 0 Then Return "未知（STUN 服务器都没有响应，可能是 UDP 被封或网络限制）"
        If Mapped.Count = 1 Then Return $"锥形 NAT 或公网直连（只拿到一份映射 {Mapped(0)}，没做交叉验证）"
        Dim PortA As String = Mapped(0).Split(":"c).Last()
        Dim PortB As String = Mapped(1).Split(":"c).Last()
        If PortA = PortB Then
            Return $"锥形 NAT（两个服务器看到同一个映射 {Mapped(0)}，多数情况能打洞）"
        Else
            Return $"对称型 NAT（两个服务器看到不同映射 {Mapped(0)} / {Mapped(1)}，打洞容易失败，建议由房主做端口映射）"
        End If
    End Function

    ''' <summary>
    ''' 向一个 STUN 服务器发 Binding Request，返回它看到的公网映射（IP:端口）。
    ''' 失败返回 Nothing。
    ''' </summary>
    Private Shared Function TryStunQuery(Server As String) As String
        Try
            Dim Parts As String() = Server.Split(":"c)
            Dim Host As String = Parts(0)
            Dim Port As Integer = If(Parts.Length > 1, CInt(Parts(1)), 3478)
            Using Client As New Net.Sockets.UdpClient()
                Client.Client.ReceiveTimeout = 1200
                Client.Connect(Host, Port)
                Dim TxId As Byte() = Guid.NewGuid().ToByteArray() '取前 12 字节当事务号
                Dim Request(19) As Byte
                Request(0) = 0 : Request(1) = 1 'Binding Request
                Request(2) = 0 : Request(3) = 0 '长度 0
                Request(4) = &H21 : Request(5) = &H12 : Request(6) = &HA4 : Request(7) = &H42 'Magic Cookie
                Array.Copy(TxId, 0, Request, 8, 12)
                Client.Send(Request, Request.Length)
                Dim Remote As New Net.IPEndPoint(Net.IPAddress.Any, 0)
                Dim Response As Byte() = Client.Receive(Remote)
                If Response.Length < 20 Then Return Nothing
                If Response(0) <> 1 OrElse Response(1) <> 1 Then Return Nothing '必须是 Binding Success Response（0x0101）
                For i As Integer = 0 To 11
                    If Response(8 + i) <> TxId(i) Then Return Nothing '事务号对不上，忽略
                Next
                Dim Limit As Integer = Math.Min(Response.Length, 20 + (CInt(Response(2)) << 8) + Response(3))
                Dim Offset As Integer = 20
                Do While Offset + 4 <= Limit
                    Dim AttrType As Integer = (CInt(Response(Offset)) << 8) + Response(Offset + 1)
                    Dim AttrLen As Integer = (CInt(Response(Offset + 2)) << 8) + Response(Offset + 3)
                    Dim ValueStart As Integer = Offset + 4
                    If ValueStart + AttrLen > Response.Length Then Exit Do
                    If (AttrType = &H20 OrElse AttrType = &H1) AndAlso AttrLen >= 8 AndAlso Response(ValueStart + 1) = 1 Then
                        'XOR-MAPPED-ADDRESS（0x0020）或 MAPPED-ADDRESS（0x0001），只处理 IPv4
                        Dim MappedPort As Integer = (CInt(Response(ValueStart + 2)) << 8) + Response(ValueStart + 3)
                        If AttrType = &H20 Then MappedPort = MappedPort Xor &H2112
                        Dim Mask As Byte() = If(AttrType = &H20, New Byte() {&H21, &H12, &HA4, &H42}, New Byte() {0, 0, 0, 0})
                        Dim IpBytes(3) As Byte
                        For k As Integer = 0 To 3
                            IpBytes(k) = Response(ValueStart + 4 + k) Xor Mask(k)
                        Next
                        Return $"{New Net.IPAddress(IpBytes)}:{MappedPort}"
                    End If
                    Offset = ValueStart + AttrLen + ((4 - (AttrLen Mod 4)) Mod 4)
                Loop
            End Using
        Catch ex As Exception
            '探测失败不记日志，避免每次进房间都刷一片
        End Try
        Return Nothing
    End Function

    ''' <summary>后台 NAT 探测的结果，供状态条与诊断共用。</summary>
    Private NatProbeResult As String = Nothing
    Private NatProbeStarted As Boolean = False

    ''' <summary>
    ''' 当 EasyTier 没给出有效 NAT 类型时，后台补一次 STUN 探测（只做一次，不阻塞 UI）。
    ''' </summary>
    Private Sub EnsureNatProbe()
        If NatProbeStarted Then Return
        If NATType <> NATTypes.Unknown AndAlso NATType <> NATTypes.Pending Then Return
        NatProbeStarted = True
        RunInNewThread(
        Sub()
            NatProbeResult = ProbeNatByStun()
            Logger.Info($"NAT 探测结果：{NatProbeResult}")
        End Sub)
    End Sub

    ''' <summary>
    ''' 自己的 NAT 类型描述：优先用 EasyTier 报的，没报就退回自己探测的结果。
    ''' WaitProbe=True 时在调用线程里直接把 STUN 探测做完（诊断报告用，已在后台线程）。
    ''' </summary>
    Private Function GetSelfNatDescription(Optional WaitProbe As Boolean = False) As String
        If NATType <> NATTypes.Unknown AndAlso NATType <> NATTypes.Pending Then Return DescribeNat(NATType)
        If NatProbeResult Is Nothing AndAlso WaitProbe Then
            NatProbeResult = ProbeNatByStun()
            NatProbeStarted = True
            Logger.Info($"NAT 探测结果：{NatProbeResult}")
        End If
        EnsureNatProbe()
        Return If(NatProbeResult, "检测中……")
    End Function

#End Region

#Region "联机诊断"

    ''' <summary>
    ''' 生成一份联机诊断报告，方便排查「为什么连不上」。
    ''' </summary>
    Private Sub BtnLinkDiag_Click() Handles BtnLinkDiag.Click
        If Not BtnLinkDiag.IsEnabled Then Return
        BtnLinkDiag.IsEnabled = False
        RunInNewThread(
        Sub()
            Dim Report As String
            Try
                Report = BuildLinkDiagnosis()
            Catch ex As Exception
                Report = "生成诊断报告失败：" & vbCrLf & ex.Message
                Logger.Error(ex, "生成联机诊断报告失败")
            End Try
            RunInUi(
            Sub()
                BtnLinkDiag.IsEnabled = True
                MyMsgBox(Report, "联机诊断", "复制全部", "关闭",
                         Button1Action:=Sub() ClipboardSet(Report, False))
            End Sub)
        End Sub, "Link Diagnosis")
    End Sub

    ''' <summary>
    ''' 收集诊断信息。会做一次 TCP 探测，所以必须放在后台线程调用。
    ''' </summary>
    Private Function BuildLinkDiagnosis() As String
        Dim Sb As New System.Text.StringBuilder()
        Dim GamePort As Integer = 0
        Dim HasPort As Boolean = Integer.TryParse(ServerPort, GamePort) AndAlso GamePort > 0
        Dim SelfIp As String = If(IsTunMode, If(IsServerSide, TunHostIp, If(TunLocalIp, "尚未分配")), ModUpnp.GetLanIPv4())
        Dim Target As Peer = GetTargetPeer()
        Dim Others As List(Of Peer) = If(Peers, New List(Of Peer)).Where(Function(p) p.Type <> Peer.Types.Misc).ToList()

        Sb.AppendLine("PCL Harness 联机诊断")
        Sb.AppendLine($"时间：{Now:yyyy-MM-dd HH:mm:ss}")
        Sb.AppendLine($"版本：{VersionBaseName}（VersionCode {VersionCode}）")
        Sb.AppendLine()
        Sb.AppendLine("— 房间 —")
        Sb.AppendLine($"角色：{If(IsServerSide, "房主", "加入者")}")
        Sb.AppendLine($"模式：{If(IsTunMode, "虚拟局域网（TUN）", If(IsDirectMode, "直连", "普通"))}")
        Sb.AppendLine($"网络名：{If(String.IsNullOrEmpty(NetworkName), "尚未生成", NetworkName)}")
        Sb.AppendLine($"游戏端口：{If(HasPort, GamePort.ToString(), "尚未确定")}")
        Sb.AppendLine($"本机{(If(IsTunMode, "虚拟", ""))}地址：{SelfIp}:{If(HasPort, GamePort.ToString(), "?")}")
        Sb.AppendLine($"EasyTier 监听端口：{If(DirectListenPort > 0, DirectListenPort.ToString(), "尚未分配")}（{If(DirectListenPort > 0 AndAlso IsLocalPortListening(DirectListenPort), "正在监听", "未监听")}）")
        Sb.AppendLine()
        Sb.AppendLine("— 本机 —")
        Sb.AppendLine($"启动器权限：{If(IsAdmin(), "管理员", "普通用户（TUN 模式会自动以管理员身份重启启动器）")}")
        Sb.AppendLine($"自己的 NAT 类型：{GetSelfNatDescription(WaitProbe:=True)}")
        Sb.AppendLine($"最近一次 CLI 调用失败次数：{FailCount}")
        Sb.AppendLine()
        Sb.AppendLine("— 对端 —")
        Sb.AppendLine($"房间人数：{PeopleCount}")
        If Others.Count = 0 Then
            Sb.AppendLine("暂无其他节点（房主在等待朋友加入，或加入者还没找到房主）")
        Else
            For Each Item As Peer In Others
                Sb.AppendLine($"- {Item.Name}｜{If(Item.Relay, "经中继", "P2P 直连")}｜{Item.Ping:0.0} ms｜NAT {DescribeNat(Item.NATType)}")
            Next
        End If
        Sb.AppendLine()
        Sb.AppendLine("— 连通性 —")
        If IsTunMode AndAlso HasPort Then
            If IsServerSide Then
                Sb.AppendLine($"本机 {GamePort} 端口：{If(IsLocalPortListening(GamePort), "有程序在监听（游戏已开房间）", "没有程序在监听（游戏还没开房间）")}")
            Else
                Sb.AppendLine($"到房主 {TunHostIp}:{GamePort} 的 TCP：{If(CanReachTcp(TunHostIp, GamePort, 1500), "可以连上", "连不上（1.5 秒超时，可能是房主没开房间，或隧道没通）")}")
            End If
        Else
            Sb.AppendLine("非 TUN 模式，跳过虚拟网卡的连通性探测")
        End If
        Sb.AppendLine()
        Sb.AppendLine("— 最近的内核日志 —")
        Dim Lines As String() = LogHistory.ToArray()
        If Lines.Length = 0 Then
            Sb.AppendLine("（暂无）")
        Else
            For Each Line As String In Lines
                Sb.AppendLine(Line)
            Next
        End If
        Return Sb.ToString()
    End Function

    Private Shared Function DescribeNat(Nat As NATTypes) As String
        Select Case Nat
            Case NATTypes.Pending
                Return "尚未获取"
            Case NATTypes.Unknown
                Return "未知（没有 STUN，或对端没报）"
            Case NATTypes.OpenInternet
                Return "公网直连"
            Case NATTypes.NoPAT
                Return "无 PAT"
            Case NATTypes.FullCone
                Return "全锥形"
            Case NATTypes.Restricted
                Return "受限锥形"
            Case NATTypes.PortRestricted
                Return "端口受限锥形"
            Case NATTypes.Symmetric
                Return "对称型（打洞容易失败）"
            Case NATTypes.SymUdpFirewall
                Return "对称型 + UDP 防火墙"
            Case NATTypes.SymmetricEasyInc
                Return "对称型（易增量）"
            Case NATTypes.SymmetricEasyDec
                Return "对称型（易减量）"
            Case Else
                Return $"{Nat}"
        End Select
    End Function

#End Region


    '===============================
    '  联机地址（本机 / 外网 / 公网 IP）
    '===============================

    Private Const LinkAddressFileName As String = "LinkAddress.json"

    ''' <summary>刷新完成页的地址面板。只有直连模式的房主才显示。</summary>
    Private Sub RefreshAddressPanel()
        If Not (IsDirectMode AndAlso IsServerSide) Then
            PanAddress.Visibility = Visibility.Collapsed
            Return
        End If
        PanAddress.Visibility = Visibility.Visible
        If IsTunMode Then
            'TUN：唯一该发给朋友的是虚拟地址，LAN/公网地址在虚拟网络里没有意义
            LabAddrLanLabel.Text = "虚拟地址"
            LabAddrLan.Text = $"{TunHostIp}:{ServerPort}"
            LabAddrWanLabel.Visibility = Visibility.Collapsed
            LabAddrWan.Visibility = Visibility.Collapsed
            BtnAddrWanCopy.Visibility = Visibility.Collapsed
            LabAddrPublicLabel.Visibility = Visibility.Collapsed
            LabAddrPublic.Visibility = Visibility.Collapsed
            BtnAddrPublicCopy.Visibility = Visibility.Collapsed
            PanAddrDetect.Visibility = Visibility.Collapsed
            Return
        End If
        LabAddrLanLabel.Text = "本机地址"
        LabAddrWanLabel.Visibility = Visibility.Visible
        LabAddrWan.Visibility = Visibility.Visible
        BtnAddrWanCopy.Visibility = Visibility.Visible
        LabAddrPublicLabel.Visibility = Visibility.Visible
        LabAddrPublic.Visibility = Visibility.Visible
        BtnAddrPublicCopy.Visibility = Visibility.Visible
        PanAddrDetect.Visibility = Visibility.Visible
        Dim LanIp As String = ModUpnp.GetLanIPv4()
        If String.IsNullOrEmpty(LanIp) Then LanIp = "127.0.0.1"
        LabAddrLan.Text = $"{LanIp}:{DirectListenPort}"
        LabAddrWan.Text = If(String.IsNullOrEmpty(DirectPublicEndpoint), "未映射（需要路由器 UPnP 或手动端口映射）", DirectPublicEndpoint)
        BtnAddrWanCopy.IsEnabled = Not String.IsNullOrEmpty(DirectPublicEndpoint)
        If String.IsNullOrEmpty(DirectPublicIp) Then
            LabAddrPublic.Text = "未检测（点下方「检测公网 IP」）"
            BtnAddrPublicCopy.IsEnabled = False
        Else
            LabAddrPublic.Text = $"{ModUpnp.MaskIp(DirectPublicIp)}（来源：{DirectPublicIpSource}）"
            BtnAddrPublicCopy.IsEnabled = True
        End If
    End Sub

    Private Sub BtnAddrLanCopy_Click() Handles BtnAddrLanCopy.Click
        ClipboardSet(LabAddrLan.Text, False)
        Hint(If(IsTunMode, "已复制虚拟地址", "已复制本机地址"))
    End Sub

    Private Sub BtnAddrWanCopy_Click() Handles BtnAddrWanCopy.Click
        If String.IsNullOrEmpty(DirectPublicEndpoint) Then Return
        ClipboardSet(DirectPublicEndpoint, False)
        Hint("已复制外网地址")
    End Sub

    Private Sub BtnAddrPublicCopy_Click() Handles BtnAddrPublicCopy.Click
        If String.IsNullOrEmpty(DirectPublicIp) Then Return
        ClipboardSet(DirectPublicIp, False)
        Hint("已复制公网 IP")
    End Sub

    ''' <summary>用户主动点击「检测公网 IP」。会先问路由器，再（可选）访问纯文本 IP 接口。</summary>
    Private Sub DetectPublicIp() Handles BtnDetectIp.Click
        DetectPublicIpCore()
    End Sub

    Private Sub DetectPublicIpCore()
        If Not (IsDirectMode AndAlso IsServerSide) Then Return
        RunInNewThread(
            Sub()
                Dim Ip As String = Nothing, Src As String = Nothing, Reason As String = Nothing
                Dim Ok As Boolean = ModUpnp.TryGetPublicIp(Ip, Src, Reason, True)
                RunInUi(
                    Sub()
                        If Ok Then
                            DirectPublicIp = Ip
                            DirectPublicIpSource = Src
                            RefreshAddressPanel()
                            WriteAddressFileIfEnabled()
                            Hint($"已获取公网 IP（来源：{Src}）。为避免截图泄露，界面已打码，点「复制」可取得完整地址。")
                        Else
                            Hint($"没能获取公网 IP：{Reason}", HintType.Red)
                        End If
                    End Sub)
                '日志里绝不写入 IP 本身
                Logger.Info($"公网 IP 检测结束：{If(Ok, "成功（来源 " & Src & "，值已隐藏）", "失败（" & Reason & "）")}")
            End Sub, "检测公网 IP")
    End Sub

    Private Function LinkAddressPath() As String
        Return Paths.AppDataThenName & LinkAddressFileName
    End Function

    Private Function JsonStr(Value As String) As String
        If Value Is Nothing Then Return """"""""
        Return """" & Value.Replace("\", "\\").Replace("""", "\""") & """"
    End Function

    Private Sub WriteAddressFileIfEnabled()
        If Settings.Get(Of Boolean)("LinkWriteAddressFile") Then WriteAddressFile()
    End Sub

    ''' <summary>
    ''' 把当前房间地址写到本机文件，供 Radmin 等外部工具读取。
    ''' 文件权限收紧为「仅当前用户」，不含任何密钥；退出联机时删除。
    ''' </summary>
    Private Sub WriteAddressFile()
        Try
            Dim LanIp As String = ModUpnp.GetLanIPv4()
            If String.IsNullOrEmpty(LanIp) Then LanIp = "127.0.0.1"
            Dim WanIp As String = ""
            Dim WanPort As String = ""
            If Not String.IsNullOrEmpty(DirectPublicEndpoint) Then
                Dim Parts As String() = DirectPublicEndpoint.Split(":"c)
                WanIp = Parts(0)
                If Parts.Length > 1 Then WanPort = Parts(1)
            End If
            Dim Sb As New Text.StringBuilder()
            Sb.AppendLine("{")
            Sb.AppendLine("  ""lan"": { ""ip"": " & JsonStr(LanIp) & ", ""port"": " & DirectListenPort & ", ""invite"": " & JsonStr(GetDirectInvite()) & " },")
            Sb.AppendLine("  ""wan"": { ""ip"": " & JsonStr(WanIp) & ", ""port"": " & If(WanPort = "", "null", WanPort) & ", ""mapped"": " & If(String.IsNullOrEmpty(DirectPublicEndpoint), "false", "true") & " },")
            Sb.AppendLine("  ""public_ip"": " & JsonStr(If(DirectPublicIp, "")) & ",")
            Sb.AppendLine("  ""updated"": " & JsonStr(Date.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")))
            Sb.AppendLine("}")
            Dim Target As String = LinkAddressPath()
            IO.File.WriteAllText(Target, Sb.ToString(), New Text.UTF8Encoding(False))
            Dim Acl As New FileSecurity()
            Acl.SetAccessRuleProtection(True, False)
            Acl.AddAccessRule(New FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.FullControl, AccessControlType.Allow))
            IO.File.SetAccessControl(Target, Acl)
            Logger.Info("已写入联机地址文件（含公网 IP，内容不记录到日志）")
        Catch ex As Exception
            Logger.Warn(ex, "写入联机地址文件失败")
        End Try
    End Sub

    Private Sub DeleteAddressFile()
        Try
            Dim Target As String = LinkAddressPath()
            If IO.File.Exists(Target) Then
                IO.File.Delete(Target)
                Logger.Info("已删除联机地址文件")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "删除联机地址文件失败")
        End Try
    End Sub
End Class
