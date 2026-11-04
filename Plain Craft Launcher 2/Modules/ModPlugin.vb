Imports System.IO
Imports System.Reflection
Imports System.Text
Imports Newtonsoft.Json.Linq
Imports System.Windows
Imports System.Windows.Controls

''' <summary>
''' PCL Harness 插件框架（v1）。
''' <para>插件目录：启动器目录\PCL\Plugins\插件文件夹\，其中必须包含 plugin.json；实现 DLL 可选。</para>
''' <para>plugin.json 示例：{ "id":"demo", "name":"示例插件", "version":"1.0.0", "author":"你", "description":"说明", "entry":"Demo.Plugin", "dll":"Demo.Plugin.dll" }</para>
''' </summary>
Public Module ModPlugin

    Public Const ApiVersion As Integer = 1
    Public Const ManifestFileName As String = "plugin.json"

    ''' <summary>
    ''' 插件根目录（%启动器目录%\PCLHarness\Plugins）。
    ''' </summary>
    Public ReadOnly Property PathPluginRoot As String = Path.Combine(Paths.BaseThenName.TrimEnd("\"c), "Plugins")
    Private ReadOnly Property PathEnabledFile As String = Path.Combine(PathPluginRoot, "plugins.json")
    Public ReadOnly Property PathSettingsFile As String = Path.Combine(PathPluginRoot, "plugins.settings.json")
    Private ReadOnly Property PathFuseFile As String = Path.Combine(PathPluginRoot, "fuse.json")

    Private ReadOnly Property PathTrustFile As String = Path.Combine(PathPluginRoot, "trust.json")
    Private ReadOnly Property PathPendingRoot As String = Path.Combine(PathPluginRoot, ".pending")
    Private ReadOnly Property PathBackupRoot As String = Path.Combine(PathPluginRoot, ".backup")
    Private ReadOnly Property PathTrashRoot As String = Path.Combine(PathPluginRoot, ".trash")
    Private ReadOnly Property PathPublishersFile As String = Path.Combine(PathPluginRoot, "publishers.json")

    ''' <summary>
    ''' 已确认过权限的插件：id → 权限指纹（权限变了要重新确认）。
    ''' 注意：这是权限集合的规范化字符串，**不是**密码学签名，不含任何密钥或哈希算法。
    ''' </summary>
    Private ReadOnly TrustedIds As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' net.dynamic 权限下用户逐次批准过的主机：插件 id → 主机集合。
    ''' 和权限指纹一起存在 trust.json 的 hosts 字段里；权限变了不会清空已批准的主机（那是单独授权）。
    ''' </summary>
    Private ReadOnly TrustedHosts As New Dictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 用户勾选过「不再提醒」的插件 id（权限指纹变化后会失效，重新弹窗）。
    ''' 和权限指纹一起存在 trust.json 的 skipPrompt 字段里。
    ''' </summary>
    Private ReadOnly SkipPromptIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 生成权限指纹，用于判断「插件被授予了新的权限」。
    ''' 实现是把权限名小写、排序后用逗号连接（例如 events,files,ui）；它只能检测变化，不能证明发布者身份。
    ''' </summary>
    Public Function PermissionFingerprint(Info As PluginInfo) As String
        If Info Is Nothing Then Return ""
        'v7.4（安全审计 V3）：指纹必须覆盖权限的「作用域」，否则把 hosts / 可读写路径从 A 改成 B 后
        '指纹不变，导入覆盖时会被静默地当成同一套授权继续启用。
        Dim Parts = Info.Permissions.Select(Function(P) P.ToLowerInvariant()).OrderBy(Function(P) P).ToList()
        Dim Builder As New Text.StringBuilder()
        Builder.Append("p=" & String.Join(",", Parts))
        Dim Detail = Info.Detail
        If Detail IsNot Nothing Then
            If Detail.NetAny Then Builder.Append(";net=*")
            'v7.5（安全审计 3.8 R8）：统一小写 + Ordinal 排序，避免不同区域设置算出不同指纹
            If Detail.Hosts.Count > 0 Then Builder.Append(";hosts=" & String.Join(",", Detail.Hosts.Select(Function(X) X.ToLowerInvariant()).OrderBy(Function(X) X, StringComparer.Ordinal)))
            If Detail.ReadPaths.Count > 0 Then Builder.Append(";read=" & String.Join(",", Detail.ReadPaths.Select(Function(X) X.ToLowerInvariant()).OrderBy(Function(X) X, StringComparer.Ordinal)))
            If Detail.WritePaths.Count > 0 Then Builder.Append(";write=" & String.Join(",", Detail.WritePaths.Select(Function(X) X.ToLowerInvariant()).OrderBy(Function(X) X, StringComparer.Ordinal)))
        End If
        Return Builder.ToString()
    End Function

    ''' <summary>旧名字，保留给已有调用方；请改用 PermissionFingerprint。</summary>
    Public Function PermissionSignature(Info As PluginInfo) As String
        Return PermissionFingerprint(Info)
    End Function

    ''' <summary>某个插件名下的权限是否已经被用户确认过（且权限没有变化）。</summary>
    ''' <summary>
    ''' 信任键（v7.4，安全审计 V9）：Id + 安装目录名。
    ''' 只按 Id 键控时，另一个目录里同 Id 的插件会共用同一份免确认记录。
    ''' </summary>
    Public Function TrustKey(Info As PluginInfo) As String
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) Then Return ""
        Dim Folder = ""
        Try
            If Not String.IsNullOrEmpty(Info.Folder) Then Folder = IO.Path.GetFileName(Info.Folder.TrimEnd("\"c)).ToLowerInvariant()
        Catch
            Folder = ""
        End Try
        Return Info.Id & "|" & Folder
    End Function

    Public Function IsTrusted(Info As PluginInfo) As Boolean
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) Then Return False
        Dim Key = TrustKey(Info)
        Dim Sig As String = Nothing
        If Not TrustedIds.TryGetValue(Key, Sig) Then Return False
        Return String.Equals(Sig, PermissionFingerprint(Info), StringComparison.Ordinal)
    End Function

    ''' <summary>记录用户已确认某插件的当前权限。</summary>
    Public Sub TrustPlugin(Info As PluginInfo)
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) Then Return
        Dim Key = TrustKey(Info)
        Dim NewSig = PermissionFingerprint(Info)
        Dim OldSig As String = Nothing
        'v7.4（安全审计 V5）：权限真的变了才清掉「不再提醒」，避免和「不再提醒并启用」的点击顺序打架
        If TrustedIds.TryGetValue(Key, OldSig) AndAlso Not String.Equals(OldSig, NewSig, StringComparison.Ordinal) Then
            SkipPromptIds.Remove(Key)
        End If
        TrustedIds(Key) = NewSig
        SaveTrustState()
    End Sub

    ''' <summary>清除某个插件的权限确认记录（v7.5，安全审计 3.8 R5），下次启用会重新询问。</summary>
    Public Sub RevokeTrust(Info As PluginInfo)
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) Then Return
        Dim Key = TrustKey(Info)
        If String.IsNullOrEmpty(Key) Then Return
        SyncLock TrustedIds
            TrustedIds.Remove(Key)
        End SyncLock
        SyncLock SkipPromptIds
            SkipPromptIds.Remove(Key)
        End SyncLock
        SaveTrustState()
    End Sub
    ''' <summary>某个主机是否已被用户单独批准给这个插件（net.dynamic）。</summary>
    Public Function IsHostApproved(Info As PluginInfo, Host As String) As Boolean
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) OrElse String.IsNullOrEmpty(Host) Then Return False
        Dim Approved As HashSet(Of String) = Nothing
        If Not TrustedHosts.TryGetValue(Info.Id, Approved) OrElse Approved Is Nothing Then Return False
        Return Approved.Contains(Host)
    End Function

    ''' <summary>为插件的 net.dynamic 权限批准一个主机（写进 trust.json）。</summary>
    Public Sub ApproveHost(Info As PluginInfo, Host As String)
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) OrElse String.IsNullOrEmpty(Host) Then Return
        If Not TrustedHosts.ContainsKey(Info.Id) Then
            TrustedHosts(Info.Id) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        End If
        TrustedHosts(Info.Id).Add(Host)
        SaveTrustState()
        Info.AddEvent($"已授权访问主机：{Host}")
    End Sub

    ''' <summary>撤销某个插件通过 net.dynamic 拿到的全部主机。</summary>
    Public Sub RevokeHosts(Id As String)
        If String.IsNullOrEmpty(Id) Then Return
        If TrustedHosts.Remove(Id) Then SaveTrustState()
    End Sub

    ''' <summary>列出某个插件已批准的主机（插件设置页展示用）。</summary>
    Public Function ListApprovedHosts(Id As String) As List(Of String)
        Dim Result As New List(Of String)
        Dim Approved As HashSet(Of String) = Nothing
        If Not String.IsNullOrEmpty(Id) AndAlso TrustedHosts.TryGetValue(Id, Approved) AndAlso Approved IsNot Nothing Then
            Result.AddRange(Approved.OrderBy(Function(S) S, StringComparer.OrdinalIgnoreCase))
        End If
        Return Result
    End Function

    Private Sub LoadTrustState()
        TrustedIds.Clear()
        SyncLock SkipPromptIds
            SkipPromptIds.Clear()
        End SyncLock
        Try
            If Not IO.File.Exists(PathTrustFile) Then Return
            Dim Json = JObject.Parse(IO.File.ReadAllText(PathTrustFile, Encoding.UTF8))
            ' 新格式：{"schema":2,"plugins":{"id":{"permissionsHash":"ui,events"}}}
            Dim PluginsNode = TryCast(Json("plugins"), JObject)
            If PluginsNode IsNot Nothing Then
                For Each P In PluginsNode.Properties()
                    Dim Obj = TryCast(P.Value, JObject)
                    Dim Hash As String = Nothing
                    If Obj IsNot Nothing Then Hash = Obj.Value(Of String)("permissionsHash")
                    If Hash Is Nothing AndAlso Obj IsNot Nothing Then Hash = Obj.Value(Of String)("signature")
                    TrustedIds(P.Name) = If(Hash, "")
                    ' net.dynamic 逐主机授权：{"id":{"permissionsHash":"…","hosts":["a.com"]}}
                    If Obj IsNot Nothing Then
                        Dim HostsNode = TryCast(Obj("hosts"), JArray)
                        If HostsNode IsNot Nothing Then
                            Dim Approved As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                            For Each H In HostsNode
                                Dim One As String = If(H, "").ToString().Trim()
                                If One <> "" Then Approved.Add(One)
                            Next
                            If Approved.Count > 0 Then TrustedHosts(P.Name) = Approved
                        End If
                        ' 「不再提醒」：{"id":{"permissionsHash":"…","skipPrompt":true}}
                        If Obj.Value(Of Boolean?)("skipPrompt") = True Then
                            SyncLock SkipPromptIds
                                SkipPromptIds.Add(P.Name)
                            End SyncLock
                        End If
                    End If
                Next
                Return
            End If
            ' 旧格式：{"id":"ui,events"} 或 {"id":{"signature":"ui,events"}}
            For Each P In Json.Properties()
                Dim Sig = If(P.Value, "").ToString()
                Dim Obj = TryCast(P.Value, JObject)
                If Obj IsNot Nothing Then Sig = Obj.Value(Of String)("signature")
                TrustedIds(P.Name) = If(Sig, "")
            Next
            ' 旧格式读进来后立刻按新结构回写一次，避免一直停留在旧格式
            SaveTrustState()
        Catch ex As Exception
            Logger.Error(ex, "读取插件信任记录失败", LogBehavior.None)
        End Try
    End Sub

    Private Sub SaveTrustState()
        Try
            Directory.CreateDirectory(PathPluginRoot)
            Dim PluginsNode As New JObject
            For Each Pair In TrustedIds
                Dim Item As New JObject
                Item("permissionsHash") = Pair.Value
                Dim Approved As HashSet(Of String) = Nothing
                If TrustedHosts.TryGetValue(Pair.Key, Approved) AndAlso Approved IsNot Nothing AndAlso Approved.Count > 0 Then
                    Dim HostsArray As New JArray
                    For Each One In Approved.OrderBy(Function(S) S, StringComparer.OrdinalIgnoreCase)
                        HostsArray.Add(One)
                    Next
                    Item("hosts") = HostsArray
                End If
                SyncLock SkipPromptIds
                    If SkipPromptIds.Contains(Pair.Key) Then Item("skipPrompt") = True
                End SyncLock
                PluginsNode(Pair.Key) = Item
            Next
            ' 只有主机授权、没有权限记录的插件也要写进文件
            For Each Pair In TrustedHosts
                If PluginsNode(Pair.Key) IsNot Nothing Then Continue For
                Dim Item As New JObject
                Dim HostsArray As New JArray
                For Each One In Pair.Value.OrderBy(Function(S) S, StringComparer.OrdinalIgnoreCase)
                    HostsArray.Add(One)
                Next
                Item("hosts") = HostsArray
                PluginsNode(Pair.Key) = Item
            Next
            Dim Json As New JObject
            Json("schema") = 2
            Json("plugins") = PluginsNode
            IO.File.WriteAllText(PathTrustFile, Json.ToString(), New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "保存插件信任记录失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>权限的中文说明（用于启用前的确认框）。</summary>
    Public Function DescribePermission(Name As String) As String
        Select Case If(Name, "").Trim().ToLowerInvariant()
            Case "ui" : Return "添加自定义页面、标题栏入口、设置项与卡片（会改动界面）"
            Case "events" : Return "订阅启动器事件（页面切换、游戏启动/退出、下载开始/结束等）"
            Case "files" : Return "在授权的目录里读写文件（插件自己的数据目录始终可用）"
            Case "net" : Return "访问清单里授权的主机（发网络请求）"
            Case "args" : Return "修改游戏启动参数（JVM 参数与游戏参数）"
            Case "process" : Return "启动外部进程"
            Case "plugins" : Return "查看、安装、启用、禁用或卸载其他插件（插件市场类插件需要）"
            Case "net.dynamic" : Return "在运行时申请访问新的主机（每次申请都会弹窗询问，可随时在「插件设置」里撤销）"
            Case "instance.read" : Return "读取当前游戏实例的信息（名称、加载器、版本号、模组数量等，只读）"
            Case "logs.read" : Return "读取游戏日志与崩溃报告（用于分析启动失败原因，只读）"
            Case "skin" : Return "修改离线皮肤（会改变你在游戏里看到的外观）"
            Case "instance.install" : Return "往当前实例安装内容（mods / resourcepacks / shaderpacks / datapacks，会改动游戏文件）"
            Case "launch" : Return "主动启动游戏（会在你未点「启动游戏」时也把游戏跑起来）"
            Case "account" : Return "读取当前账号信息（用户名、UUID、登录方式，只读）"
            Case "settings.global" : Return "读取启动器的全局设置（只读，包含你的路径、语言等偏好）"
            Case "instance.select" : Return "切换当前选中的游戏实例（会改变之后「启动游戏」用的版本）"
            Case "mods.read" : Return "查看当前实例的模组、资源包与光影列表（只读）"
            Case "mods.write" : Return "启用或禁用当前实例里的模组、资源包与光影（会改动游戏文件）"
            Case Else : Return If(Name, "") & "（未知权限）"
        End Select
    End Function

    ''' <summary>
    ''' 权限的风险等级：0 = 低（只影响插件自己）、1 = 中（会改动游戏、账号或启动行为）、2 = 高（会改动系统或启动器自身）。
    ''' 中高风险在启用与启动时都要弹出风险提示。
    ''' </summary>
    Public Function PermissionRisk(Name As String) As Integer
        Select Case If(Name, "").Trim().ToLowerInvariant()
            Case "skin", "instance.install", "launch", "account", "settings.global", "instance.select", "mods.write", "args", "plugins"
                'v7.4（安全审计 V9）：args 能注入 -javaagent、plugins 能安装/启用别的插件，二者都等价于执行任意代码
                Return 1
            Case "process"
                Return 2
            Case Else
                Return 0
        End Select
    End Function

    ''' <summary>
    ''' 权限会产生什么后果（风险弹窗正文用）。
    ''' v7.2：中风险也走醒目提示框，但只有真正动到文件/账号的才写后果；
    '''       纯界面或体验类的（skin / launch / settings.global）不写，避免噪音。
    ''' </summary>
    Public Function PermissionConsequence(Name As String) As String
        Select Case If(Name, "").Trim().ToLowerInvariant()
            Case "process" : Return "可以启动任意外部程序，等同于在你的电脑上执行代码。"
            Case "instance.install" : Return "可以把文件写进当前实例的 mods、resourcepacks、shaderpacks 或 datapacks 目录，放进来的内容会随游戏加载。"
            Case "account" : Return "可以读到当前登录账号的名称与 UUID（拿不到登录令牌），并能据此区分你和其他玩家。"
            Case "mods.write" : Return "可以改写当前实例 mods / resourcepacks / shaderpacks 里文件的启用状态（重命名成 .disabled），游戏下次启动会加载不同内容。"
            Case "args" : Return "可以往游戏启动命令里追加 JVM / 游戏参数（例如 -javaagent），等同于往游戏进程里注入代码。"
            Case "plugins" : Return "可以导入、启用或卸载其它插件，等于间接获得那些插件的全部权限。"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>某个插件声明里最高的风险等级（0/1/2）。</summary>
    Public Function HighestPermissionRisk(Info As PluginInfo) As Integer
        If Info Is Nothing Then Return 0
        Dim Top As Integer = 0
        For Each P In Info.Permissions
            Dim R = PermissionRisk(P)
            If R > Top Then Top = R
        Next
        Return Top
    End Function

    ''' <summary>用户是否对该插件勾选过「不再提醒」（权限有变化时视为没有再勾）。</summary>
    Public Function IsPromptSkipped(Info As PluginInfo) As Boolean
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) Then Return False
        'v7.5（安全审计 3.8 R4）：skipPrompt 的键必须和 trust.json 一致（id|文件夹名），否则重启后就丢失
        Dim Key = TrustKey(Info)
        SyncLock SkipPromptIds
            If Not SkipPromptIds.Contains(Key) Then Return False
        End SyncLock
        Return IsTrusted(Info)
    End Function

    ''' <summary>记录或取消「不再提醒」（只有能通过权限确认时才有意义）。</summary>
    Public Sub SetPromptSkipped(Info As PluginInfo, Value As Boolean)
        If Info Is Nothing OrElse String.IsNullOrEmpty(Info.Id) Then Return
        Dim Key = TrustKey(Info)
        SyncLock SkipPromptIds
            If Value Then
                SkipPromptIds.Add(Key)
            Else
                SkipPromptIds.Remove(Key)
            End If
        End SyncLock
        SaveTrustState()
    End Sub

    ''' <summary>
    ''' 已启用的插件 ID 集合（内存里的权威状态，见 WriteEnabled）。
    ''' </summary>
    Private ReadOnly EnabledState As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 插件根目录下的状态文件（plugins.json / plugins.settings.json / fuse.json）不算“插件内容”，
    ''' 否则启动器自己写这些文件会触发热重载，形成“重载→新对象→写回旧状态”的死循环。
    ''' </summary>
    Private Function IsPluginStateFile(FullPath As String) As Boolean
        Try
            Dim Dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(FullPath))
            If Dir Is Nothing Then Return False
            If Not Dir.TrimEnd("\"c).Equals(PathPluginRoot.TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then Return False
            Dim Name = IO.Path.GetFileName(FullPath)
            Return Name.Equals("plugins.json", StringComparison.OrdinalIgnoreCase) OrElse
                   Name.Equals("plugins.settings.json", StringComparison.OrdinalIgnoreCase) OrElse
                   Name.Equals("fuse.json", StringComparison.OrdinalIgnoreCase) OrElse
                   Name.Equals("trust.json", StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 扩展点版本：
    ''' v2 自定义页面 / 标题栏入口 / 设置项；
    ''' v3 事件钩子；
    ''' v4 生命周期与联机/下载事件、内嵌卡片与状态栏小组件、权限与依赖、热重载与事件日志；
    ''' v5 事件总线、强类型设置、日志分级、Ask；
    ''' v6 事件总线全覆盖（含优先级/一次性/异步）、插件服务注册与依赖版本约束、后台任务与进度、开发模式控制台；
    ''' v7 ui / events 权限真正生效、权限必须显式声明（新插件不再默认全开）、实例信息与日志读取、换肤、
    '''    内容安装、启动游戏、账号信息、全局设置只读，以及中高风险权限的风险确认。
    ''' </summary>
    Public Const ApiVersionUi As Integer = 7

    ''' <summary>插件注册的全部自定义页面。</summary>
    Public ReadOnly PluginPages As New List(Of PluginPageInfo)
    ''' <summary>插件注册的全部标题栏入口。</summary>
    Public ReadOnly PluginNavItems As New List(Of PluginNavItem)
    ''' <summary>插件注册的全部设置项。</summary>
    Public ReadOnly PluginSettings As New List(Of PluginSetting)
    ''' <summary>实现了 IPclPluginEvents 的插件及其所属插件信息。</summary>
    Public ReadOnly PluginEventHandlers As New List(Of (Handler As IPclPluginEvents, Owner As PluginInfo))
    ''' <summary>实现了 IPclPluginLifecycle 的插件（v4：游戏退出 / 实例切换 / 主题变化）。</summary>
    Public ReadOnly PluginLifecycleHandlers As New List(Of (Handler As IPclPluginLifecycle, Owner As PluginInfo))
    ''' <summary>实现了 IPclPluginLink 的插件（v4：联机房间事件）。</summary>
    Public ReadOnly PluginLinkHandlers As New List(Of (Handler As IPclPluginLink, Owner As PluginInfo))
    ''' <summary>实现了 IPclPluginDownload 的插件（v4：下载事件）。</summary>
    Public ReadOnly PluginDownloadHandlers As New List(Of (Handler As IPclPluginDownload, Owner As PluginInfo))
    ''' <summary>实现了 IPclPluginClosing 的插件（v4：关闭前拦截）。</summary>
    Public ReadOnly PluginClosingHandlers As New List(Of (Handler As IPclPluginClosing, Owner As PluginInfo))
    ''' <summary>插件注册的主页卡片。</summary>
    Public ReadOnly PluginHomeCards As New List(Of PluginCardInfo)
    ''' <summary>插件注册的设置页卡片。</summary>
    Public ReadOnly PluginSettingsCards As New List(Of PluginCardInfo)
    ''' <summary>插件注册的状态栏小组件。</summary>
    Public ReadOnly PluginStatusWidgets As New List(Of PluginStatusWidget)

    ''' <summary>
    ''' 已发现的一个插件。
    ''' </summary>
    Public Class PluginInfo
        Public Id As String = ""
        Public Name As String = ""
        Public Version As String = ""
        Public Author As String = ""
        Public Description As String = ""
        ''' <summary>实现 IPclPlugin 的类型全名（可留空，表示按接口自动搜索）。</summary>
        Public EntryType As String = ""
        ''' <summary>DLL 文件名（相对插件文件夹）。</summary>
        Public DllName As String = ""
        Public Folder As String = ""
        Public HasDll As Boolean
        Public Enabled As Boolean
        Public Loaded As Boolean
        ''' <summary>加载/运行失败原因，空表示正常。</summary>
        Public [Error] As String = ""
        Public Instance As Object
        Public Host As PluginHost
        ''' <summary>插件声明的权限（net / files / args / ui / process）。空表示只按默认权限运行。</summary>
        Public ReadOnly Permissions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>插件声明的依赖（其它插件的 id）。</summary>
        Public ReadOnly Dependencies As New List(Of String)
        ''' <summary>API v5：细化权限（permissions 写成对象时才有内容）。</summary>
        Public ReadOnly Detail As New PermissionDetail
        ''' <summary>API v5：plugin.json 里声明的 DLL 校验值（sha256 / sha256.dll），空表示不校验。</summary>
        Public DllSha256 As String = ""
        ''' <summary>API v5：插件要求的最低 API 版本（0 表示未声明）。</summary>
        Public MinApiVersion As Integer = 0
        ''' <summary>插件主页（可选）。</summary>
        Public Homepage As String = ""
        ''' <summary>插件许可证（可选）。</summary>
        Public License As String = ""
        ''' <summary>清单里声明的签名（可选；没有就视为未签名）。</summary>
        Public Signature As String = ""
        ''' <summary>更新检查地址（可选）：一个返回 {version,url,sha256,size} 的 JSON。</summary>
        Public UpdateUrl As String = ""
        ''' <summary>签名校验结果（未签名 / 有效 / 无效 / 未知发布者）。</summary>
        Public SignatureStatus As PluginSignatureStatus = PluginSignatureStatus.Unsigned
        ''' <summary>最近的事件/错误记录（最多 40 条，插件页可见）。</summary>
        Public ReadOnly Events As New List(Of String)
        ''' <summary>记录一条事件日志。</summary>
        Public Sub AddEvent(Text As String)
            Try
                SyncLock Events
                    Events.Add($"{Date.Now:HH:mm:ss} {Text}")
                    While Events.Count > 40
                        Events.RemoveAt(0)
                    End While
                End SyncLock
            Catch
                '忽略
            End Try
        End Sub
        ''' <summary>是否声明了某个权限。</summary>
        Public Function HasPermission(Name As String) As Boolean
            'v7.4（安全审计 V1）：没声明 permissions 的插件视为「没有任何权限」。
            '旧版的「不声明 = 全部放行」会让插件静默拿到所有能力，已移除。
            If Permissions.Count = 0 Then Return False
            Return Permissions.Contains(Name)
        End Function

        ''' <summary>没有该权限时抛统一的未授权异常（Host API 的统一入口）。</summary>
        Public Sub EnsurePermission(Name As String)
            If HasPermission(Name) Then Return
            AddEvent("调用了未授权的接口：" & Name)
            Throw New UnauthorizedAccessException($"插件 {DisplayName} 没有 {Name} 权限，请在清单里声明后重新启用")
        End Sub
        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace(Name), Id, Name)
            End Get
        End Property
        Public ReadOnly Property StatusText As String
            Get
                If IsPluginFused(Me) Then Return $"已熔断（连续失败 {FuseThreshold} 次，已自动禁用）：" & [Error]
                If Not String.IsNullOrEmpty([Error]) Then Return "加载失败：" & [Error]
                If Loaded Then Return "已启用并加载"
                If Enabled AndAlso Not HasDll Then Return "已启用（仅清单，未加载代码）"
                If Enabled Then Return "已启用（等待加载）"
                Return "已禁用"
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 插件注册的一个自定义页面。
    ''' </summary>
    Public Class PluginPageInfo
        ''' <summary>页面 ID（插件内唯一）。</summary>
        Public Id As String = ""
        ''' <summary>页面标题，显示在标题栏与页首。</summary>
        Public Title As String = ""
        ''' <summary>标题栏图标 Path 数据（24x24 左右，可留空）。</summary>
        Public Icon As String = ""
        ''' <summary>页面工厂：返回要显示的用户控件（每次打开都会调用一次）。</summary>
        Public Factory As Func(Of FrameworkElement)
        Public Owner As PluginInfo
    End Class

    ''' <summary>
    ''' 插件注册的一个标题栏入口（可打开页面，或只执行一段代码）。
    ''' </summary>
    Public Class PluginNavItem
        Public Text As String = ""
        Public Icon As String = ""
        ''' <summary>要打开的页面；为 Nothing 时点击只执行 Action。</summary>
        Public Page As PluginPageInfo
        ''' <summary>无页面时的点击动作。</summary>
        Public Action As Action
        Public Owner As PluginInfo
    End Class

    ''' <summary>插件设置项类型。</summary>
    Public Enum PluginSettingType
        [Boolean] = 0
        [Text] = 1
        ''' <summary>整数，范围由 MinValue / MaxValue 限定。</summary>
        [Number] = 2
        ''' <summary>下拉选择，候选项来自 Options。</summary>
        [Choice] = 3
    End Enum

    ''' <summary>
    ''' 插件注册的一个设置项（显示在插件页下方，值存 plugins.settings.json）。
    ''' </summary>
    Public Class PluginSetting
        Public Key As String = ""
        Public Title As String = ""
        Public Description As String = ""
        Public SettingType As PluginSettingType = PluginSettingType.Boolean
        Public DefaultValue As Object
        ''' <summary>Number 类型的最小值。</summary>
        Public MinValue As Integer = 0
        ''' <summary>Number 类型的最大值。</summary>
        Public MaxValue As Integer = 100
        ''' <summary>Choice 类型的候选项文本。</summary>
        Public Options As String() = New String() {}
        ''' <summary>值变化回调（新值）。</summary>
        Public Handler As Action(Of Object)
        Public Owner As PluginInfo
    End Class

    ''' <summary>
    ''' 启动游戏前后传给插件的信息。OnGameLaunching 阶段可以往 ExtraJvmArgs / ExtraGameArgs 里追加参数。
    ''' </summary>
    Public Class PluginLaunchInfo
        ''' <summary>要启动的版本名。</summary>
        Public Property InstanceName As String = ""
        ''' <summary>版本隔离目录。</summary>
        Public Property InstanceFolder As String = ""
        ''' <summary>启动器当前选择的玩家名。</summary>
        Public Property PlayerName As String = ""
        ''' <summary>追加到 JVM 参数区（mainClass 之前）的参数。</summary>
        Public ReadOnly Property ExtraJvmArgs As New List(Of String)
        ''' <summary>追加到游戏参数区末尾的参数。</summary>
        Public ReadOnly Property ExtraGameArgs As New List(Of String)
        ''' <summary>OnGameLaunched 阶段可读到进程 ID。</summary>
        Public Property ProcessId As Integer = -1
    End Class

    ''' <summary>
    ''' 当前发现的全部插件。
    ''' </summary>
    Public ReadOnly Plugins As New List(Of PluginInfo)

    ''' <summary>
    ''' 最近一次扫描的错误（例如目录无法访问）。
    ''' </summary>
    Public ScanError As String = ""

    ''' <summary>是否已完成过一次扫描加载（供启动提示等待）。</summary>
    Public LoadDone As Boolean

    ''' <summary>
    ''' 扫描插件目录并（可选）加载已启用的插件。返回插件数量。
    ''' </summary>
    ''' <summary>
    ''' v5 兼容：旧版本用它刷新右下角的插件状态文字。
    ''' v6.4 起界面不再显示这段文字，这里保留为空实现，避免已编译的旧插件调用时报 MissingMethodException。
    ''' </summary>
    <Obsolete("v6.4 起不再需要调用：界面已不再显示插件状态文字。")>
    Public Sub RefreshStatusWidgets()
    End Sub
    Public Function Refresh(Optional LoadEnabled As Boolean = True) As Integer
        UnloadAll()
        Plugins.Clear()
        ScanError = ""
        LoadDone = False
        Try
            Directory.CreateDirectory(PathPluginRoot)
            Dim EnabledIds = ReadEnabled()
            EnabledState.Clear()
            For Each Id In EnabledIds
                EnabledState.Add(Id)
            Next
            LoadFuseState()
            LoadTrustState()
            LoadTrustedPublishers()
            ApplyPendingUpdates()
            CleanUpOldDirectories(30)
            For Each Folder In Directory.GetDirectories(PathPluginRoot)
                '跳过导入用的临时目录与备份目录（以 . 开头）
                If IO.Path.GetFileName(Folder).StartsWith("."c) Then Continue For
                Dim Info = ReadManifest(Folder)
                If Info Is Nothing Then
                    If IO.File.Exists(Path.Combine(Folder, "plugin.json")) Then
                        Logger.Warn($"目录 {IO.Path.GetFileName(Folder)} 的 plugin.json 无法解析，已跳过。")
                    Else
                        Logger.Info($"目录 {IO.Path.GetFileName(Folder)} 里没有 plugin.json，已跳过（插件目录必须直接包含 plugin.json）。")
                    End If
                    Continue For
                End If
                Info.Enabled = EnabledIds.Contains(Info.Id.ToLowerInvariant())
                If Not Info.Enabled AndAlso String.IsNullOrEmpty(Info.[Error]) Then
                    Dim Fails As Integer = PluginFailCount(Info)
                    If Fails >= FuseThreshold Then Info.[Error] = $"上次会话连续失败 {Fails} 次，已自动禁用；确认修好后可在插件页重新启用（会重置计数）。"
                End If
                If Not Info.Enabled Then
                    Logger.Info($"插件 {Info.DisplayName}（{Info.Id}）默认未启用：到「插件」页点启用后才会加载，启用记录保存于 {PathEnabledFile}。")
                End If
                If String.IsNullOrEmpty(Info.Id) Then
                    Info.[Error] = "plugin.json 缺少 id"
                    Info.Enabled = False
                End If
                Plugins.Add(Info)
            Next
            Dim DiscoveredCount As Integer = Plugins.Count
            Dim EnabledCount As Integer = Plugins.Where(Function(P) P.Enabled).Count
            Logger.Info($"扫描完成：共 {DiscoveredCount} 个插件，已启用 {EnabledCount} 个。")
            If LoadEnabled Then
                '按依赖顺序加载：依赖先加载，缺依赖的插件标记失败
                Dim Pending = Plugins.Where(Function(P) P.Enabled AndAlso String.IsNullOrEmpty(P.[Error])).ToList()
                Dim Guard As Integer = 0
                Do While Pending.Count > 0 AndAlso Guard < 50
                    Guard += 1
                    Dim Progress As Boolean = False
                    For Each Info In Pending.ToList()
                        Dim Missing As String = Nothing
                        Dim Blocked As Boolean = False
                        For Each DepRaw In Info.Dependencies
                            'API v6：支持 "其它插件id@>=1.2" 形式的版本约束
                            Dim AtPos As Integer = DepRaw.IndexOf("@"c)
                            Dim DepId As String = If(AtPos >= 0, DepRaw.Substring(0, AtPos).Trim(), DepRaw.Trim())
                            Dim DepConstraint As String = If(AtPos >= 0, DepRaw.Substring(AtPos + 1).Trim(), "")
                            Dim Dep = Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, DepId, StringComparison.OrdinalIgnoreCase))
                            If Dep Is Nothing Then
                                Missing = DepRaw
                                Exit For
                            End If
                            If Not SatisfiesVersion(Dep.Version, DepConstraint) Then
                                Info.[Error] = $"依赖插件 {Dep.DisplayName} 的版本是 {If(Dep.Version, "未知")}，不满足要求 {DepConstraint}"
                                Missing = DepRaw
                                Exit For
                            End If
                            If Dep.Enabled AndAlso Not Dep.Loaded AndAlso Pending.Contains(Dep) Then
                                Blocked = True
                                Exit For
                            End If
                            If Dep.Enabled AndAlso Not String.IsNullOrEmpty(Dep.[Error]) Then
                                Missing = DepRaw
                                Exit For
                            End If
                        Next
                        If Missing IsNot Nothing Then
                            Info.[Error] = $"缺少依赖插件：{Missing}"
                            Pending.Remove(Info)
                            Progress = True
                        ElseIf Not Blocked Then
                            LoadOne(Info)
                            Pending.Remove(Info)
                            Progress = True
                        End If
                    Next
                    If Not Progress Then
                        For Each Info In Pending
                            Info.[Error] = "插件依赖存在循环或无法满足：" & String.Join("、", Info.Dependencies)
                        Next
                        Exit Do
                    End If
                Loop
            End If
            StartWatcher()
            WriteEnabledExample()
            WarnRiskyPlugins()
        Catch ex As Exception
            ScanError = ex.Message
            Logger.Error(ex, "扫描插件目录失败", LogBehavior.None)
        Finally
            LoadDone = True
        End Try
        Return Plugins.Count
    End Function

    ''' <summary>
    ''' 启动加载完成后，只有插件出问题时才在左下角提示（正常加载不打扰）。
    ''' </summary>
    Public Sub WarnFailed()
        Dim Failed As New List(Of String)
        For Each Info In Plugins
            If Info.Enabled AndAlso Not String.IsNullOrEmpty(Info.[Error]) Then Failed.Add(Info.DisplayName)
        Next
        If Failed.Count = 0 Then Return
        Dim Message As String = $"有 {Failed.Count} 个插件加载失败：{String.Join("、", Failed)}，详见插件页。"
        Try
            RunInUi(Sub() ModMain.Hint(Message, HintType.Red, True))
        Catch ex As Exception
            Logger.Error(ex, "提示插件失败信息时出错", LogBehavior.None)
        End Try
    End Sub

    Private RiskWarnShown As Boolean

    ''' <summary>
    ''' v7.2：启动时只对「已启用、声明了高风险权限」的插件弹一次醒目风险提示（v7.4 起不受「不再提醒」影响）。
    ''' 中风险不参与启动检查——它只在启用那一刻用黄色窗提醒（见 PagePlugin），避免每次开启动器都被打扰。
    ''' 取消时把这些插件就地禁用，避免高风险权限继续生效。
    ''' </summary>
    Public Sub WarnRiskyPlugins()
        If RiskWarnShown Then Return
        RiskWarnShown = True
        Try
            'v7.4（安全审计 V5）：「不再提醒」只对启用时的确认框生效，高风险插件每次启动都要再确认一次
            Dim Risky = Plugins.Where(Function(P) P.Enabled AndAlso HighestPermissionRisk(P) >= 2).ToList()
            If Risky.Count = 0 Then Return
            Dim Lines As New List(Of String)
            Lines.Add("以下插件已启用，并声明了高风险权限：")
            Lines.Add("")
            For Each Info In Risky
                Lines.Add($"【{Info.DisplayName}】 v{Info.Version}")
                For Each Perm In Info.Permissions.Where(Function(X) PermissionRisk(X) >= 2).OrderBy(Function(X) X)
                    Lines.Add("  · " & Perm & "：" & DescribePermission(Perm))
                    Dim Cons = PermissionConsequence(Perm)
                    If Not String.IsNullOrEmpty(Cons) Then Lines.Add("      可能的后果：" & Cons)
                Next
                Lines.Add("")
            Next
            Lines.Add("如果你不完全信任这些插件的来源，请点击「取消」，它们会被自动禁用。")
            Dim Choice As Integer = 0
            ModBase.RunInUiWait(Sub() Choice = ModMain.MyMsgBox(String.Join(vbCrLf, Lines), "插件高风险权限提示", "我已了解风险，并愿意继续", "取消", WarnLevel:=2))
            If Choice = 2 Then
                For Each Info In Risky
                    Try
                        SetEnabled(Info, False)
                    Catch ex As Exception
                        Logger.Error(ex, "禁用声明了风险权限的插件失败：" & Info.DisplayName, LogBehavior.None)
                    End Try
                Next
                ModMain.Hint("已禁用声明了高风险权限的插件", HintType.Blue, True)
            End If
        Catch ex As Exception
            Logger.Error(ex, "显示插件风险提示失败", LogBehavior.None)
        End Try
    End Sub

    ' ================= API v6 第二梯队：崩溃熔断 =================

    ''' <summary>同一个插件连续失败多少次后自动禁用（熔断）。</summary>
    Public Const FuseThreshold As Integer = 3

    Private ReadOnly FuseLock As New Object
    Private ReadOnly FuseCounts As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>插件当前的连续失败次数。</summary>
    Public Function PluginFailCount(Info As PluginInfo) As Integer
        If Info Is Nothing Then Return 0
        SyncLock FuseLock
            Dim Count As Integer = 0
            If FuseCounts.TryGetValue(Info.Id, Count) Then Return Count
        End SyncLock
        Return 0
    End Function

    ''' <summary>插件是否已经因为连续失败被自动禁用。</summary>
    Public Function IsPluginFused(Info As PluginInfo) As Boolean
        If Info Is Nothing Then Return False
        Return Not Info.Enabled AndAlso PluginFailCount(Info) >= FuseThreshold
    End Function

    ''' <summary>把插件的连续失败计数清零（用户重新启用时调用）。</summary>
    Public Sub ResetPluginFailures(Info As PluginInfo)
        If Info Is Nothing Then Return
        SyncLock FuseLock
            FuseCounts.Remove(Info.Id)
        End SyncLock
        SaveFuseState()
    End Sub

    ''' <summary>把失败计数写进 Plugins\fuse.json，重启后仍能看到「上次失败 N 次 / 已熔断」。</summary>
    Private Sub SaveFuseState()
        Try
            Dim Root As New JObject
            SyncLock FuseLock
                For Each Pair In FuseCounts
                    If Pair.Value <= 0 Then Continue For
                    Root(Pair.Key) = Pair.Value
                Next
            End SyncLock
            IO.Directory.CreateDirectory(PathPluginRoot)
            IO.File.WriteAllText(PathFuseFile, Root.ToString(Formatting.Indented), New Text.UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "保存插件失败计数失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>启动时读取上次的失败计数。</summary>
    Private Sub LoadFuseState()
        Try
            If Not IO.File.Exists(PathFuseFile) Then Return
            Dim Root As JObject = JObject.Parse(IO.File.ReadAllText(PathFuseFile))
            SyncLock FuseLock
                FuseCounts.Clear()
                For Each Prop As JProperty In Root.Properties()
                    Dim Value As Integer = 0
                    If Integer.TryParse(Prop.Value.ToString(), Value) AndAlso Value > 0 Then FuseCounts(Prop.Name) = Value
                Next
            End SyncLock
        Catch ex As Exception
            Logger.Error(ex, "读取插件失败计数失败", LogBehavior.None)
        End Try
    End Sub
    ''' <summary>
    ''' 记录一次插件失败（加载、事件处理、界面回调都算）。
    ''' 连续失败达到 FuseThreshold 次时自动禁用该插件并弹出提示，返回 True 表示这一次刚好熔断。
    ''' </summary>
    Public Function NotePluginFailure(Info As PluginInfo, Where As String, Ex As Exception) As Boolean
        If Info Is Nothing Then Return False
        Dim Reason As String = If(Ex Is Nothing, "", If(Ex.GetBaseException().Message, Ex.Message))
        Dim Count As Integer
        SyncLock FuseLock
            FuseCounts.TryGetValue(Info.Id, Count)
            Count += 1
            FuseCounts(Info.Id) = Count
        End SyncLock
        Info.AddEvent($"失败 {Count}/{FuseThreshold}（{Where}）：{Reason}")
        SaveFuseState()
        PluginDevLog.Warn(Info.DisplayName, $"失败 {Count}/{FuseThreshold}（{Where}）：{Reason}")
        Try
            Logger.Error(If(Ex, New Exception(Reason)), $"插件失败（{Where}）{Count}/{FuseThreshold}：{Info.DisplayName}", LogBehavior.None)
        Catch
            '日志失败不影响熔断
        End Try
        If Count < FuseThreshold Then Return False
        '连续失败达到阈值：熔断
        Info.[Error] = $"连续 {Count} 次出错，已自动禁用（最近一次：{Where}）：{Reason}"
        Info.AddEvent("已熔断：连续失败达到阈值，插件被自动禁用")
        Try
            SetEnabled(Info, False)
        Catch exc As Exception
            Logger.Error(exc, "熔断插件时出错：" & Info.DisplayName, LogBehavior.None)
        End Try
        Try
            RunInUi(Sub() ModMain.Hint($"插件「{Info.DisplayName}」连续出错 {Count} 次，已自动禁用；可在插件页重新启用。", HintType.Red, True))
        Catch exc As Exception
            Logger.Error(exc, "提示插件熔断时出错", LogBehavior.None)
        End Try
        Return True
    End Function
    ''' <summary>
    ''' API v6.2：统一的插件调用隔离。插件提供的任何回调（事件、界面、任务、扩展点）都从这里过一遍：
    ''' 抛异常只会记一次失败并计入熔断计数，不会把异常抛回启动器主流程。
    ''' </summary>
    Public Function GuardCall(Info As PluginInfo, Where As String, Work As Action) As Boolean
        If Info Is Nothing OrElse Work Is Nothing Then Return False
        Try
            Work()
            Return True
        Catch ex As Exception
            NotePluginFailure(Info, Where, ex)
            Return False
        End Try
    End Function

    ''' <summary>API v6.2：带返回值的插件调用隔离；失败时返回 Fallback。</summary>
    Public Function GuardCall(Of T)(Info As PluginInfo, Where As String, Work As Func(Of T), Optional Fallback As T = Nothing) As T
        If Info Is Nothing OrElse Work Is Nothing Then Return Fallback
        Try
            Return Work()
        Catch ex As Exception
            NotePluginFailure(Info, Where, ex)
            Return Fallback
        End Try
    End Function
    ''' <summary>
    ''' 加载单个插件。
    ''' </summary>
    Public Sub LoadOne(Info As PluginInfo)
        If Info Is Nothing OrElse Info.Loaded Then Return
        Info.[Error] = ""
        Try
            If Not Info.HasDll Then
                '纯清单插件：没有实现代码，仅作为功能声明展示
                Info.Loaded = False
                Return
            End If
            'v7.4（安全审计 V8）：签名校验为「无效」时说明有人在签名后改过文件，直接拒绝加载
            If Info.SignatureStatus = PluginSignatureStatus.Invalid Then
                Info.[Error] = "签名校验失败：文件在签名之后被修改过，已拒绝加载（可删除 signature 字段后重新导入）"
                Info.AddEvent("加载失败：签名无效")
                Return
            End If
            'v7.4（安全审计 V9）：dll 只能是插件目录里的文件名，禁止 ..\..\ 之类穿越
            Dim DllNameCandidate = If(String.IsNullOrEmpty(Info.DllName), Info.Id & ".dll", Info.DllName)
            If DllNameCandidate.Contains("\") OrElse DllNameCandidate.Contains("/") OrElse DllNameCandidate.Contains("..") OrElse DllNameCandidate.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 Then
                Info.[Error] = "plugin.json 的 dll 字段不合法（只能是文件名）：" & Info.DllName
                Info.AddEvent("加载失败：dll 字段不合法")
                Return
            End If
            Dim DllPath = Path.Combine(Info.Folder, DllNameCandidate)
            Dim PluginRoot = IO.Path.GetFullPath(Info.Folder).TrimEnd("\"c) & "\"
            If Not IO.Path.GetFullPath(DllPath).StartsWith(PluginRoot, StringComparison.OrdinalIgnoreCase) Then
                Info.[Error] = "plugin.json 的 dll 字段指向了插件目录之外，已拒绝加载"
                Info.AddEvent("加载失败：dll 越界")
                Return
            End If
            If Not File.Exists(DllPath) Then
                Info.[Error] = "找不到 DLL：" & DllPath
                Return
            End If
            If Info.MinApiVersion > ApiVersionUi Then
                Info.[Error] = $"插件要求 API v{Info.MinApiVersion}，本启动器最高支持 v{ApiVersionUi}，请更新启动器后再用"
                Info.AddEvent(Info.[Error])
                Return
            End If
            If Not String.IsNullOrWhiteSpace(Info.DllSha256) Then
                Dim ActualHash = Sha256OfFile(DllPath)
                If String.IsNullOrEmpty(ActualHash) Then
                    'v7.4（安全审计 V9）：算不出哈希时不能静默跳过校验
                    Info.[Error] = "DLL 校验失败：无法读取文件计算 sha256，已拒绝加载"
                    Info.AddEvent("校验失败：无法计算 sha256")
                    Return
                End If
                If Not String.Equals(ActualHash, Info.DllSha256, StringComparison.OrdinalIgnoreCase) Then
                    Dim Want = Info.DllSha256.Substring(0, Math.Min(12, Info.DllSha256.Length))
                    Dim Got = ActualHash.Substring(0, Math.Min(12, ActualHash.Length))
                    Info.[Error] = "DLL 校验失败：sha256 与 plugin.json 不一致，已拒绝加载（文件可能损坏或被替换）"
                    Info.AddEvent($"校验失败：清单 {Want}…，实际 {Got}…")
                    Return
                End If
            End If
            'v7.5（安全审计 3.8 R10）：按字节流加载，两个插件用同名 DLL 时不再共享同一份程序集
            EnsurePluginResolver()
            Dim Asm As Reflection.Assembly
            Try
                Asm = Reflection.Assembly.Load(IO.File.ReadAllBytes(DllPath))
            Catch ex As Exception
                Info.[Error] = "加载失败：" & ex.GetBaseException().Message
                Info.AddEvent("加载失败：" & ex.GetBaseException().Message)
                Logger.Warn(ex, "插件加载失败：" & Info.DisplayName)
                Return
            End Try
            Dim Type As Type = Nothing
            If Not String.IsNullOrEmpty(Info.EntryType) Then Type = Asm.GetType(Info.EntryType, False, True)
            If Type Is Nothing Then
                For Each T In Asm.GetTypes()
                    If T.IsClass AndAlso Not T.IsAbstract AndAlso GetType(IPclPlugin).IsAssignableFrom(T) Then
                        Type = T
                        Exit For
                    End If
                Next
            End If
            If Type Is Nothing OrElse Not GetType(IPclPlugin).IsAssignableFrom(Type) Then
                Info.[Error] = "DLL 中没有实现 IPclPlugin 的类型"
                Return
            End If
            Info.Host = New PluginHost(Info)
            Info.Instance = Activator.CreateInstance(Type)
            CType(Info.Instance, IPclPlugin).OnLoad(Info.Host)
            Info.Loaded = True
            Info.Host.Log("插件已加载")
            Info.AddEvent("已加载（API v" & ApiVersionUi & "）")
            CollectUi(Info)
            '注册 v4 事件接口
            If TypeOf Info.Instance Is IPclPluginLifecycle Then PluginLifecycleHandlers.Add((CType(Info.Instance, IPclPluginLifecycle), Info))
            If TypeOf Info.Instance Is IPclPluginLink Then PluginLinkHandlers.Add((CType(Info.Instance, IPclPluginLink), Info))
            If TypeOf Info.Instance Is IPclPluginDownload Then PluginDownloadHandlers.Add((CType(Info.Instance, IPclPluginDownload), Info))
            If TypeOf Info.Instance Is IPclPluginClosing Then PluginClosingHandlers.Add((CType(Info.Instance, IPclPluginClosing), Info))
            ' 加载成功说明这次是好的：清掉上次会话留下的失败计数与熔断提示，避免卡片一直挂着「重置失败计数」
            ResetPluginFailures(Info)
            If Not String.IsNullOrEmpty(Info.[Error]) AndAlso
               (Info.[Error].StartsWith("已熔断", StringComparison.Ordinal) OrElse Info.[Error].StartsWith("上次会话连续失败", StringComparison.Ordinal)) Then
                Info.[Error] = ""
            End If
        Catch ex As Exception
            Info.[Error] = ex.GetBaseException().Message
            Info.AddEvent("加载失败：" & ex.GetBaseException().Message)
            Logger.Error(ex, "加载插件失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "加载", ex) '连续失败会自动熔断
        End Try
    End Sub

    ''' <summary>插件签名状态（v6.5）。</summary>
    Public Enum PluginSignatureStatus
        Unsigned = 0
        Signed = 1
        Invalid = 2
        UnknownPublisher = 3
    End Enum

    Private PendingApplied As Boolean
    Private ReadOnly TrustedPublishers As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>把 .pending 里等待生效的插件换入正式目录（启动时调用一次，此时还没有插件被加载）。</summary>
    Private Sub ApplyPendingUpdates()
        If PendingApplied Then Return
        PendingApplied = True
        If Not IO.Directory.Exists(PathPendingRoot) Then Return
        '有插件已加载时不动目录，避免把正在使用的文件挪走
        If Plugins.Any(Function(P) P.Loaded) Then Return
        For Each PendingDir In IO.Directory.GetDirectories(PathPendingRoot)
            Dim Info = ReadManifest(PendingDir)
            If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Info.Id) Then
                Logger.Warn($"待生效插件 {IO.Path.GetFileName(PendingDir)} 的清单无效，已跳过（不会动插件目录）")
                Continue For
            End If
            Dim Target = IO.Path.Combine(PathPluginRoot, Info.Id)
            Dim Existing = IO.Directory.Exists(Target)
            Dim OldFingerprint As String = ""
            If Existing Then
                'v7.5（安全审计 3.8 R3）：先记住旧清单的权限指纹，替换后比对
                Try
                    Dim OldInfo = ReadManifest(Target)
                    If OldInfo IsNot Nothing Then OldFingerprint = PermissionFingerprint(OldInfo)
                Catch ex As Exception
                    Logger.Warn($"待生效插件 {Info.Id} 无法读取旧清单指纹：{ex.GetBaseException().Message}")
                End Try
            End If
            If Existing Then
                Dim Backup = IO.Path.Combine(PathBackupRoot, Info.Id & "-" & Date.Now.ToString("yyyyMMdd-HHmmss"))
                IO.Directory.CreateDirectory(PathBackupRoot)
                Try
                    IO.Directory.Move(Target, Backup)
                Catch ex As Exception
                    Logger.Warn($"待生效插件 {Info.Id} 无法替换旧目录：{ex.GetBaseException().Message}")
                    Continue For
                End Try
            End If
            Try
                IO.Directory.Move(PendingDir, Target)
                Logger.Info($"待生效插件已替换：{Info.DisplayName}（{Info.Id}）v{Info.Version}")
                'v7.5（安全审计 3.8 R3）：权限清单变了就不能沿用旧的启用状态，否则扩权会在重启后静默生效
                Dim NewFingerprint = PermissionFingerprint(Info)
                If OldFingerprint <> "" AndAlso Not String.Equals(OldFingerprint, NewFingerprint, StringComparison.Ordinal) Then
                    EnabledState.Remove(Info.Id)
                    WriteEnabled()
                    Logger.Warn($"待生效插件 {Info.DisplayName}（{Info.Id}）的权限清单有变化，已自动停用，需用户重新确认")
                    ModMain.Hint($"{Info.DisplayName} 更新后权限有变化，已自动停用，请在插件页重新确认", HintType.Blue)
                End If
            Catch ex As Exception
                Logger.Warn($"待生效插件 {Info.Id} 写入失败：{ex.GetBaseException().Message}")
            End Try
        Next
        Try
            If Not IO.Directory.EnumerateFileSystemEntries(PathPendingRoot).Any() Then IO.Directory.Delete(PathPendingRoot)
        Catch
        End Try
    End Sub

    ''' <summary>插件正在运行时无法直接覆盖（程序集占着 DLL），先放到 .pending，下次启动换入。</summary>
    Private Function StagePendingUpdate(TempRoot As String, Info As PluginInfo, Existing As PluginInfo) As PluginImportResult
        Try
            IO.Directory.CreateDirectory(PathPendingRoot)
            Dim StageDir = IO.Path.Combine(PathPendingRoot, Info.Id)
            If IO.Directory.Exists(StageDir) Then IO.Directory.Delete(StageDir, True)
            IO.Directory.Move(TempRoot, StageDir)
            Logger.Info($"插件 {Info.DisplayName}（{Info.Id}）v{Info.Version} 已放入待生效队列，重启后替换 v{Existing.Version}")
            RaisePluginInstalled(Info)
            Return New PluginImportResult With {
                .Success = True,
                .NeedRestart = True,
                .PluginId = Info.Id,
                .PluginName = Info.DisplayName,
                .Version = Info.Version,
                .Folder = StageDir,
                .Upgraded = True,
                .Message = "NEED_RESTART"
            }
        Catch ex As Exception
            Return ImportFail("ERR_WRITE_FAILED", "写入待生效目录失败：" & ex.GetBaseException().Message)
        End Try
    End Function

    ''' <summary>清理 .trash / .backup 中超过保留天数的目录，以及空的 .tmp。</summary>
    Public Function CleanUpOldDirectories(Optional Days As Integer = 30) As Integer
        Dim Removed = 0
        Dim Deadline = Date.Now.AddDays(-Days)
        For Each Root In {PathTrashRoot, PathBackupRoot}
            If Not IO.Directory.Exists(Root) Then Continue For
            For Each PendingDir In IO.Directory.GetDirectories(Root)
                Try
                    If IO.Directory.GetLastWriteTime(PendingDir) < Deadline Then
                        'v7.3：.trash / .backup 里可能是用户想找回的插件目录，过期清理也走回收站
                        MeloongCore.DirectoryUtils.Delete(PendingDir, True)
                        Removed += 1
                    End If
                Catch
                End Try
            Next
        Next
        Dim TempRoot = IO.Path.Combine(PathPluginRoot, ".tmp")
        Try
            If IO.Directory.Exists(TempRoot) Then
                For Each PendingDir In IO.Directory.GetDirectories(TempRoot)
                    Try
                        If IO.Directory.GetLastWriteTime(PendingDir) < Deadline Then
                            IO.Directory.Delete(PendingDir, True)
                            Removed += 1
                        End If
                    Catch
                    End Try
                Next
                If Not IO.Directory.EnumerateFileSystemEntries(TempRoot).Any() Then IO.Directory.Delete(TempRoot)
            End If
        Catch
        End Try
        If Removed > 0 Then Logger.Info($"已清理 {Removed} 个插件临时目录（.trash / .backup / .tmp，保留 {Days} 天）")
        Return Removed
    End Function

    ''' <summary>某个插件的回滚点（.backup 下的目录，按时间从新到旧）。</summary>
    Private Function BackupFoldersFor(Id As String) As List(Of String)
        Dim Result As New List(Of String)
        If String.IsNullOrWhiteSpace(Id) OrElse Not IO.Directory.Exists(PathBackupRoot) Then Return Result
        Try
            For Each PendingDir In IO.Directory.GetDirectories(PathBackupRoot)
                Dim Name = IO.Path.GetFileName(PendingDir)
                If Name.StartsWith(Id & "-", StringComparison.OrdinalIgnoreCase) Then Result.Add(PendingDir)
            Next
            Result.Sort(Function(A, B) String.CompareOrdinal(B, A))
        Catch
        End Try
        Return Result
    End Function

    ''' <summary>是否有可回滚的上一版。</summary>
    Public Function HasBackup(Id As String) As Boolean
        Return BackupFoldersFor(Id).Count > 0
    End Function

    ''' <summary>回滚到上一个版本：当前目录进 .trash，最近的备份移回原位。需要重启生效。</summary>
    Public Function RollbackPlugin(Id As String) As Boolean
        Dim Info = FindPluginById(Id)
        If Info Is Nothing Then Return False
        Dim Backups = BackupFoldersFor(Info.Id)
        If Backups.Count = 0 Then Return False
        Try
            Dim BackupDir = Backups(0)
            If Info.Loaded Then
                ' 程序集已加载、DLL 被占用，无法移动目录：排队到下次启动换入
                IO.Directory.CreateDirectory(PathPendingRoot)
                Dim StageDir = IO.Path.Combine(PathPendingRoot, Info.Id)
                If IO.Directory.Exists(StageDir) Then IO.Directory.Delete(StageDir, True)
                CopyFolder(BackupDir, StageDir)
                Logger.Info($"插件 {Info.DisplayName}（{Info.Id}）已安排回滚到 {IO.Path.GetFileName(BackupDir)}，重启启动器后生效")
                Return True
            End If
            SetEnabled(Info, False)
            Dim Trash = IO.Path.Combine(PathTrashRoot, Info.Id & "-" & Date.Now.ToString("yyyyMMdd-HHmmss"))
            IO.Directory.CreateDirectory(PathTrashRoot)
            IO.Directory.Move(Info.Folder, Trash)
            IO.Directory.Move(BackupDir, Info.Folder)
            Refresh(False)
            Logger.Info($"插件 {Info.DisplayName}（{Info.Id}）已回滚到 {IO.Path.GetFileName(BackupDir)}，重启后生效")
            Return True
        Catch ex As Exception
            Logger.Warn($"插件 {Info.Id} 回滚失败：{ex.GetBaseException().Message}")
            Return False
        End Try
    End Function

    ''' <summary>递归复制目录（回滚排队用）。</summary>
    Private Sub CopyFolder(SourceDir As String, TargetDir As String)
        IO.Directory.CreateDirectory(TargetDir)
        For Each F In IO.Directory.GetFiles(SourceDir)
            IO.File.Copy(F, IO.Path.Combine(TargetDir, IO.Path.GetFileName(F)), True)
        Next
        For Each D In IO.Directory.GetDirectories(SourceDir)
            CopyFolder(D, IO.Path.Combine(TargetDir, IO.Path.GetFileName(D)))
        Next
    End Sub
    ''' <summary>读取本地发布者公钥表（Plugins\publishers.json：{"发布者名":"base64(SPKI DER)"}）。</summary>
    Private Sub LoadTrustedPublishers()
        TrustedPublishers.Clear()
        Try
            If Not IO.File.Exists(PathPublishersFile) Then Return
            Dim Json = JObject.Parse(IO.File.ReadAllText(PathPublishersFile, Encoding.UTF8))
            For Each Prop In Json.Properties()
                Dim Value = If(Prop.Value, "").ToString().Trim()
                If Value <> "" Then TrustedPublishers(Prop.Name.Trim()) = Value
            Next
        Catch ex As Exception
            Logger.Warn("读取插件发布者公钥失败：" & ex.GetBaseException().Message)
        End Try
    End Sub

    ''' <summary>校验签名：signature = base64(ECDSA-P256-SHA256(id\nversion\ndllSha256))，公钥来自 publishers.json。</summary>
    ''' <summary>把 SPKI DER 的 P-256 公钥转成 ECDsa（.NET Framework 没有 ImportSubjectPublicKeyInfo）。</summary>
    Private Function ImportP256PublicKey(Der As Byte()) As Security.Cryptography.ECDsa
        'SPKI 末尾固定是 03 42 00 04 || X(32) || Y(32)，取末 64 字节再套 BCRYPT ECC public blob
        If Der Is Nothing OrElse Der.Length < 68 Then Return Nothing
        Dim Blob(71) As Byte
        Blob(0) = &H45 : Blob(1) = &H43 : Blob(2) = &H53 : Blob(3) = &H31 'ECS1 = ECDSA_PUBLIC_P256
        Blob(4) = 32
        Array.Copy(Der, Der.Length - 64, Blob, 8, 64)
        Dim Key = Security.Cryptography.CngKey.Import(Blob, Security.Cryptography.CngKeyBlobFormat.EccPublicBlob)
        Return New Security.Cryptography.ECDsaCng(Key)
    End Function

    Public Function VerifySignature(Info As PluginInfo) As PluginSignatureStatus
        If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Info.Signature) Then Return PluginSignatureStatus.Unsigned
        If TrustedPublishers.Count = 0 Then LoadTrustedPublishers()
        If TrustedPublishers.Count = 0 Then Return PluginSignatureStatus.UnknownPublisher
        'v7.4（安全审计 V8）：载荷必须覆盖清单（权限 + hosts + 可读写路径），
        '否则别人可以保留合法签名、只替换 plugin.json 里的权限，把签名当摆设。
        Dim Payload = $"{Info.Id}{vbLf}{Info.Version}{vbLf}{If(Info.DllSha256, "").ToLowerInvariant()}{vbLf}{Sha256OfText(PermissionFingerprint(Info))}"
        Dim Sig As Byte()
        Try
            Sig = Convert.FromBase64String(Info.Signature.Trim())
        Catch
            Return PluginSignatureStatus.Invalid
        End Try
        For Each Pair In TrustedPublishers
            Try
                Using Ecdsa = ImportP256PublicKey(Convert.FromBase64String(Pair.Value))

                    If Ecdsa.VerifyData(Encoding.UTF8.GetBytes(Payload), Sig, Security.Cryptography.HashAlgorithmName.SHA256) Then
                        Return PluginSignatureStatus.Signed
                    End If
                End Using
            Catch
            End Try
        Next
        Return PluginSignatureStatus.Invalid
    End Function

    ' ================= v7.5（安全审计 3.8 R10）：插件按字节流隔离加载 =================

    Private ReadOnly PluginResolverLock As New Object
    Private PluginResolverReady As Boolean

    ''' <summary>注册一次依赖解析器：插件自带的其他 DLL 也从字节流加载，避免按路径复用同一份程序集。</summary>
    Private Sub EnsurePluginResolver()
        SyncLock PluginResolverLock
            If PluginResolverReady Then Return
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf ResolvePluginDependency
            PluginResolverReady = True
        End SyncLock
    End Sub

    ''' <summary>插件目录里的同名 DLL 从字节流加载（v7.5 R10：同名程序集不再互相复用）。</summary>
    Private Function ResolvePluginDependency(sender As Object, Args As ResolveEventArgs) As Reflection.Assembly
        Try
            Dim Requested = If(Args Is Nothing, "", If(Args.Name, ""))
            Dim SimpleName = Requested.Split(","c)(0).Trim()
            If SimpleName = "" Then Return Nothing
            For Each Item In Plugins.ToList()
                If Item Is Nothing OrElse String.IsNullOrWhiteSpace(Item.Folder) Then Continue For
                Dim Candidate = IO.Path.Combine(Item.Folder, SimpleName & ".dll")
                If Not IO.File.Exists(Candidate) Then Continue For
                Return Reflection.Assembly.Load(IO.File.ReadAllBytes(Candidate))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析插件依赖失败：" & If(Args Is Nothing, "", Args.Name))
        End Try
        Return Nothing
    End Function

    ''' <summary>已授权主机快照（插件 id → 主机列表），供插件设置页展示与撤销。</summary>
    Public Function ApprovedHostsSnapshot() As List(Of KeyValuePair(Of PluginInfo, List(Of String)))
        Dim Result As New List(Of KeyValuePair(Of PluginInfo, List(Of String)))
        For Each Info In Plugins.ToList()
            Dim Hosts = ListApprovedHosts(Info.Id)
            If Hosts.Count > 0 Then Result.Add(New KeyValuePair(Of PluginInfo, List(Of String))(Info, Hosts))
        Next
        Return Result
    End Function
    ''' <summary>
    ''' 卸载全部插件（忽略单个插件的异常）。
    ''' </summary>
    Public Sub UnloadAll()
        StopWatcher()
        PluginPages.Clear()
        PluginNavItems.Clear()
        PluginSettings.Clear()
        PluginEventHandlers.Clear()
        PluginLifecycleHandlers.Clear()
        PluginLinkHandlers.Clear()
        PluginDownloadHandlers.Clear()
        PluginClosingHandlers.Clear()
        PluginHomeCards.Clear()
        PluginSettingsCards.Clear()
        PluginStatusWidgets.Clear()
        For Each Info In Plugins
            If Info.Loaded AndAlso Info.Instance IsNot Nothing Then
                Try
                    CType(Info.Instance, IPclPlugin).OnUnload()
                Catch ex As Exception
                    Logger.Error(ex, "卸载插件失败：" & Info.DisplayName, LogBehavior.None)
                End Try
            End If
            'API v6：插件卸载时清掉它注册的服务
            PluginServices.UnregisterByOwner(Info.Id)
            Info.Loaded = False
            Info.Instance = Nothing
        Next
    End Sub

    ''' <summary>
    ''' 启用/禁用插件并持久化。
    ''' </summary>
    Public Sub SetEnabled(Info As PluginInfo, Value As Boolean)
        If Info Is Nothing Then Return
        '热重载会重建 PluginInfo，页面可能持有旧对象；按 id 归一到当前列表里的那个
        Dim Canonical = Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, Info.Id, StringComparison.OrdinalIgnoreCase))
        If Canonical IsNot Nothing Then Info = Canonical
        Info.Enabled = Value
        If Value Then
            EnabledState.Add(Info.Id)
            ResetPluginFailures(Info) '用户重新启用就是重新计数，熔断状态解除
            LoadOne(Info)
        Else
            EnabledState.Remove(Info.Id)
            If Info.Loaded AndAlso Info.Instance IsNot Nothing Then
                Try
                    CType(Info.Instance, IPclPlugin).OnUnload()
                Catch ex As Exception
                    Logger.Error(ex, "卸载插件失败：" & Info.DisplayName, LogBehavior.None)
                End Try
            End If
            'API v6：插件卸载时清掉它注册的服务
            PluginServices.UnregisterByOwner(Info.Id)
            Info.Loaded = False
            Info.Instance = Nothing
            RemoveUi(Info)
        End If
        WriteEnabled()
    End Sub

    ''' <summary>
    ''' 在资源管理器中打开插件目录。
    ''' </summary>
    Public Sub OpenFolder()
        Directory.CreateDirectory(PathPluginRoot)
        StartProcess("explorer", """" & PathPluginRoot & """")
    End Sub

    ''' <summary>按 id 查找插件（忽略大小写），找不到返回 Nothing。</summary>
    Public Function FindPluginById(Id As String) As PluginInfo
        If String.IsNullOrWhiteSpace(Id) Then Return Nothing
        Dim Key = Id.Trim()
        SyncLock Plugins
            Return Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, Key, StringComparison.OrdinalIgnoreCase))
        End SyncLock
    End Function

    ''' <summary>
    ''' 卸载插件：先停用并卸载，再把整个目录移入 Plugins\.trash 下（数据目录跟着走，需要时可手工恢复）。
    ''' </summary>
    Public Function UninstallPlugin(Id As String) As Boolean
        Dim Info = FindPluginById(Id)
        If Info Is Nothing Then Return False
        Dim DisplayName As String = Info.DisplayName
        Dim Version As String = Info.Version
        Try
            SetEnabled(Info, False)
        Catch ex As Exception
            Logger.Error(ex, "卸载插件前停用失败：" & DisplayName, LogBehavior.None)
        End Try
        Try
            PluginServices.UnregisterByOwner(Info.Id)
            RemoveUi(Info)
            Dim TrashRoot As String = Path.Combine(PathPluginRoot, ".trash")
            Directory.CreateDirectory(TrashRoot)
            Dim Target As String = Path.Combine(TrashRoot, $"{Info.Id}-{Date.Now:yyyyMMddHHmmss}")
            If Directory.Exists(Info.Folder) Then Directory.Move(Info.Folder, Target)
            EnabledState.Remove(Info.Id)
            TrustedHosts.Remove(Info.Id)
            WriteEnabled()
            SaveTrustState()
            Refresh(True)
            Logger.Info($"插件已卸载：{DisplayName}（{Id}）→ {Target}")
            RaisePluginUninstalled(Id, Version)
            Return True
        Catch ex As Exception
            Logger.Error(ex, "卸载插件失败：" & DisplayName, LogBehavior.None)
            Return False
        End Try
    End Function

    ' ---- 清单与状态文件 ----

    Private Function ReadManifest(Folder As String) As PluginInfo
        Dim ManifestPath = Path.Combine(Folder, ManifestFileName)
        If Not File.Exists(ManifestPath) Then Return Nothing
        Dim Info As New PluginInfo With {.Folder = Folder}
        Try
            Dim Json = JObject.Parse(File.ReadAllText(ManifestPath, Encoding.UTF8))
            Info.Id = If(Json.Value(Of String)("id"), "").Trim()
            Info.Name = If(Json.Value(Of String)("name"), "").Trim()
            Info.Version = If(Json.Value(Of String)("version"), "").Trim()
            Info.Author = If(Json.Value(Of String)("author"), "").Trim()
            Info.Description = If(Json.Value(Of String)("description"), "").Trim()
            Info.EntryType = If(Json.Value(Of String)("entry"), "").Trim()
            Info.DllName = If(Json.Value(Of String)("dll"), "").Trim()
            Dim PermsToken = Json("permissions")
            If PermsToken IsNot Nothing Then
                Dim PermsArray = TryCast(PermsToken, JArray)
                If PermsArray IsNot Nothing Then
                    For Each P In PermsArray
                        Dim Name = P.ToString().Trim().ToLowerInvariant()
                        If Name <> "" Then Info.Permissions.Add(Name)
                    Next
                Else
                    Dim PermsObject = TryCast(PermsToken, JObject)
                    If PermsObject IsNot Nothing Then
                        Info.Detail.HasDetail = True
                        For Each Prop In PermsObject.Properties()
                            Dim Key = Prop.Name.Trim().ToLowerInvariant()
                            If Key = "" Then Continue For
                            Info.Permissions.Add(Key)
                            Select Case Key
                                Case "net"
                                    'v7.4（安全审计 V7）：net 写成 true / 字符串时表示允许任意主机；
                                    '写成对象时只认 hosts 里的条目，写错键名会得到空列表 → EnsureHostAllowed 直接拒绝
                                    'v7.5（安全审计 3.8 N1）：只有布尔 true 表示「任意主机」；字符串/数组按主机列表处理
                                    Dim NetScalar = TryCast(Prop.Value, Newtonsoft.Json.Linq.JValue)
                                    If NetScalar IsNot Nothing AndAlso NetScalar.Type = Newtonsoft.Json.Linq.JTokenType.Boolean AndAlso CBool(NetScalar.Value) Then Info.Detail.NetAny = True
                                    For Each H In ReadStringList(Prop.Value)
                                        Info.Detail.Hosts.Add(H)
                                    Next
                                    Dim NetObject = TryCast(Prop.Value, JObject)
                                    If NetObject IsNot Nothing Then
                                        For Each H In ReadStringList(NetObject("hosts"))
                                            If Not Info.Detail.Hosts.Contains(H) Then Info.Detail.Hosts.Add(H)
                                        Next
                                    End If
                                Case "files"
                                    Dim FilesObject = TryCast(Prop.Value, JObject)
                                    If FilesObject IsNot Nothing Then
                                        For Each R In ReadStringList(FilesObject("read"))
                                            Info.Detail.ReadPaths.Add(R)
                                        Next
                                        For Each W In ReadStringList(FilesObject("write"))
                                            Info.Detail.WritePaths.Add(W)
                                        Next
                                    Else
                                        For Each P In ReadStringList(Prop.Value)
                                            Info.Detail.ReadPaths.Add(P)
                                            Info.Detail.WritePaths.Add(P)
                                        Next
                                    End If
                            End Select
                        Next
                    End If
                End If
            End If
            Dim ShaToken = Json("sha256")
            If ShaToken IsNot Nothing Then
                Dim ShaObject = TryCast(ShaToken, JObject)
                Info.DllSha256 = If(ShaObject IsNot Nothing, If(ShaObject.Value(Of String)("dll"), ""), ShaToken.ToString()).Trim()
            End If
            If String.IsNullOrWhiteSpace(Info.DllSha256) AndAlso Not String.IsNullOrWhiteSpace(Info.DllName) Then
                Dim DllFull = IO.Path.Combine(Folder, Info.DllName)
                If IO.File.Exists(DllFull) Then
                    Try
                        Info.DllSha256 = Sha256OfFile(DllFull)
                    Catch
                    End Try
                End If
            End If
            Dim MinApiToken = Json("minApiVersion")
            If MinApiToken IsNot Nothing Then
                Dim MinApiValue As Integer
                If Integer.TryParse(MinApiToken.ToString().Trim(), MinApiValue) Then Info.MinApiVersion = MinApiValue
            End If
            Info.Homepage = If(Json.Value(Of String)("homepage"), "").Trim()
            Info.License = If(Json.Value(Of String)("license"), "").Trim()
            Info.Signature = If(Json.Value(Of String)("signature"), "").Trim()
            Info.UpdateUrl = If(Json.Value(Of String)("update"), "").Trim()
            Info.SignatureStatus = VerifySignature(Info)
            Dim Deps = Json.Value(Of JArray)("dependencies")
            If Deps IsNot Nothing Then
                For Each D In Deps
                    'API v6：依赖可以写成 "其它插件id@>=1.2"，也可以只写 id
                    Dim Name = D.ToString().Trim()
                    If Name <> "" Then Info.Dependencies.Add(Name)
                Next
            End If
            If String.IsNullOrEmpty(Info.Id) Then Info.Id = Path.GetFileName(Folder)
            Info.HasDll = Directory.GetFiles(Folder, "*.dll").Length > 0
            Return Info
        Catch ex As Exception
            Info.[Error] = "plugin.json 解析失败：" & ex.GetBaseException().Message
            Return Info
        End Try
    End Function

    Private Function ReadEnabled() As HashSet(Of String)
        Dim Result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            If Not File.Exists(PathEnabledFile) Then Return Result
            Dim Json = JObject.Parse(File.ReadAllText(PathEnabledFile, Encoding.UTF8))
            Dim Array = Json.Value(Of JArray)("enabled")
            If Array IsNot Nothing Then
                For Each Item In Array
                    Dim Id = Item.ToString().Trim()
                    If Id <> "" Then Result.Add(Id.ToLowerInvariant())
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件启用状态失败", LogBehavior.None)
        End Try
        Return Result
    End Function

    Private Sub WriteEnabled()
        Try
            Directory.CreateDirectory(PathPluginRoot)
            Dim Array As New JArray
            For Each Id In EnabledState.OrderBy(Function(S) S, StringComparer.OrdinalIgnoreCase)
                Array.Add(Id)
            Next
            Dim Json As New JObject
            Json("enabled") = Array
            File.WriteAllText(PathEnabledFile, Json.ToString(), New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "保存插件启用状态失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>
    ''' 首次运行时写一份带注释的说明文件，方便用户知道怎么放插件。
    ''' </summary>
    Private Sub WriteEnabledExample()
        Try
            Dim ReadmePath = Path.Combine(PathPluginRoot, "说明.txt")
            If File.Exists(ReadmePath) Then Return
            File.WriteAllText(ReadmePath,
"PCL Harness 插件目录" & vbCrLf & vbCrLf &
"插件放在 PCL\Plugins\<插件文件夹>\ 下，每个文件夹至少有一个 plugin.json：" & vbCrLf &
"{ ""id"": ""mydemo"", ""name"": ""我的插件"", ""version"": ""1.0.0"", ""author"": ""我"", ""description"": ""做什么"", ""entry"": ""Demo.Plugin"", ""dll"": ""Demo.Plugin.dll"" }" & vbCrLf & vbCrLf &
"· 只有 plugin.json 时：视为声明型插件，会出现在插件页但不执行代码。" & vbCrLf &
"· 带 DLL 且 DLL 里实现了 PCL.IPclPlugin 时：启用后会反射加载，并可调用宿主 API（提示、弹窗、写日志、打开网址、数据目录）。" & vbCrLf &
"· 启用状态保存在同目录的 plugins.json，可手工编辑。" & vbCrLf,
                                 New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "写入插件说明失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>
    ''' 收集插件通过 IPclPluginUi 注册的页面 / 入口 / 设置项。
    ''' </summary>
    Private Sub CollectUi(Info As PluginInfo)
        '重复启用同一插件时，先清掉它上一次注册的条目，避免标题栏入口与设置项重复
        RemoveUi(Info)
        '事件钩子：实现了 IPclPluginEvents 就登记，不需要再实现 IPclPluginUi
        'v7：events 权限真正生效，没声明就完全不登记（v7.4 起旧插件同样需要声明）
        If TypeOf Info.Instance Is IPclPluginEvents Then
            If Info.HasPermission("events") Then
                PluginEventHandlers.Add((CType(Info.Instance, IPclPluginEvents), Info))
            Else
                Info.AddEvent("没有 events 权限，事件钩子已跳过")
            End If
        End If
        If Not TypeOf Info.Instance Is IPclPluginUi Then Return
        'v7：ui 权限真正生效，没声明就不采集任何界面扩展
        If Not Info.HasPermission("ui") Then
            Info.AddEvent("没有 ui 权限，界面扩展已跳过")
            Return
        End If
        Dim Ui = CType(Info.Instance, IPclPluginUi)
        Try
            Dim Pages = Ui.GetPages()
            If Pages IsNot Nothing Then
                For Each P In Pages
                    If P Is Nothing OrElse String.IsNullOrWhiteSpace(P.Id) Then Continue For
                    If P.Factory Is Nothing Then Continue For
                    P.Owner = Info
                    If String.IsNullOrWhiteSpace(P.Title) Then P.Title = P.Id
                    PluginPages.Add(P)
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件页面失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "读取界面", ex)
        End Try
        Try
            Dim Navs = Ui.GetNavItems()
            If Navs IsNot Nothing Then
                For Each N In Navs
                    If N Is Nothing OrElse String.IsNullOrWhiteSpace(N.Text) Then Continue For
                    If N.Page Is Nothing AndAlso N.Action Is Nothing Then Continue For
                    If N.Page IsNot Nothing AndAlso Not PluginPages.Contains(N.Page) Then Continue For
                    N.Owner = Info
                    PluginNavItems.Add(N)
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件入口失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "读取界面", ex)
        End Try
        Try
            Dim Sets = Ui.GetSettings()
            If Sets IsNot Nothing Then
                For Each S In Sets
                    If S Is Nothing OrElse String.IsNullOrWhiteSpace(S.Key) Then Continue For
                    S.Owner = Info
                    If String.IsNullOrWhiteSpace(S.Title) Then S.Title = S.Key
                    PluginSettings.Add(S)
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件设置失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "读取界面", ex)
        End Try
        If Not TypeOf Info.Instance Is IPclPluginUi2 Then Return
        Dim Ui2 = CType(Info.Instance, IPclPluginUi2)
        Try
            Dim Cards = Ui2.GetHomeCards()
            If Cards IsNot Nothing Then
                For Each C In Cards
                    If C Is Nothing OrElse C.Factory Is Nothing Then Continue For
                    C.Owner = Info
                    PluginHomeCards.Add(C)
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件主页卡片失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "读取界面", ex)
        End Try
        Try
            Dim Cards = Ui2.GetSettingsCards()
            If Cards IsNot Nothing Then
                For Each C In Cards
                    If C Is Nothing OrElse C.Factory Is Nothing Then Continue For
                    C.Owner = Info
                    PluginSettingsCards.Add(C)
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件设置卡片失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "读取界面", ex)
        End Try
        Try
            Dim Widgets = Ui2.GetStatusWidgets()
            If Widgets IsNot Nothing Then
                For Each W In Widgets
                    If W Is Nothing OrElse W.TextProvider Is Nothing Then Continue For
                    W.Owner = Info
                    PluginStatusWidgets.Add(W)
                Next
            End If
        Catch ex As Exception
            Logger.Error(ex, "读取插件状态栏组件失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "读取界面", ex)
        End Try
    End Sub

    ''' <summary>
    ''' 移除某个插件注册的全部页面 / 入口 / 设置项。
    ''' </summary>
    Private Sub RemoveUi(Info As PluginInfo)
        If Info Is Nothing Then Return
        PluginPages.RemoveAll(Function(P) P IsNot Nothing AndAlso P.Owner Is Info)
        PluginNavItems.RemoveAll(Function(N) N IsNot Nothing AndAlso N.Owner Is Info)
        PluginSettings.RemoveAll(Function(S) S IsNot Nothing AndAlso S.Owner Is Info)
        PluginEventHandlers.RemoveAll(Function(H) H.Owner Is Info)
        PluginLifecycleHandlers.RemoveAll(Function(H) H.Owner Is Info)
        PluginLinkHandlers.RemoveAll(Function(H) H.Owner Is Info)
        PluginDownloadHandlers.RemoveAll(Function(H) H.Owner Is Info)
        PluginClosingHandlers.RemoveAll(Function(H) H.Owner Is Info)
        PluginHomeCards.RemoveAll(Function(C) C IsNot Nothing AndAlso C.Owner Is Info)
        PluginSettingsCards.RemoveAll(Function(C) C IsNot Nothing AndAlso C.Owner Is Info)
        PluginStatusWidgets.RemoveAll(Function(W) W IsNot Nothing AndAlso W.Owner Is Info)
    End Sub

    ' ================= 事件分发（v3） =================

    ''' <summary>启动器就绪（插件全部加载完成后调用一次）。</summary>
    Public Sub RaiseLauncherReady()
        For Each Entry In PluginEventHandlers.ToList()
            Try
                Entry.Handler.OnLauncherReady()
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnLauncherReady", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.LauncherReady)
        '主页卡片可能比页面初始化更晚就绪，补一次渲染
        Try
            RunInUi(Sub()
                        If FrmMain IsNot Nothing AndAlso FrmLaunchRight IsNot Nothing Then
                            FrmLaunchRight.RefreshPluginCards()
                        End If
                    End Sub)
        Catch ex As Exception
            Logger.Error(ex, "刷新插件主页卡片失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>主页面切换完成。</summary>
    Public Sub RaisePageChanged(Page As Integer, Optional SubType As Integer = -1)
        For Each Entry In PluginEventHandlers.ToList()
            Try
                Entry.Handler.OnPageChanged(Page, SubType)
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnPageChanged", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.PageChanged, Page, SubType)
    End Sub

    ''' <summary>构建启动参数时；插件可往 Info.ExtraJvmArgs / ExtraGameArgs 追加参数。</summary>
    Public Sub RaiseGameLaunching(Info As PluginLaunchInfo)
        For Each Entry In PluginEventHandlers.ToList()
            Try
                Entry.Handler.OnGameLaunching(Info)
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnGameLaunching", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.GameLaunching, Info)
    End Sub

    ''' <summary>游戏进程刚启动后。</summary>
    Public Sub RaiseGameLaunched(Info As PluginLaunchInfo)
        For Each Entry In PluginEventHandlers.ToList()
            Try
                Entry.Handler.OnGameLaunched(Info)
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnGameLaunched", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.GameLaunched, Info)
    End Sub

    ' ================= 事件分发（v4） =================

    ''' <summary>游戏进程退出后。</summary>
    Public Sub RaiseGameExited(Info As PluginLaunchInfo, ExitCode As Integer)
        For Each Entry In PluginLifecycleHandlers.ToList()
            Try
                Entry.Handler.OnGameExited(Info, ExitCode)
                Entry.Owner.AddEvent($"OnGameExited(退出码 {ExitCode})")
            Catch ex As Exception
                Entry.Owner.AddEvent("OnGameExited 失败：" & ex.GetBaseException().Message)
                NotePluginFailure(Entry.Owner, "事件 OnGameExited", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.GameExited, Info, ExitCode)
    End Sub

    ''' <summary>当前版本切换后。</summary>
    Public Sub RaiseInstanceChanged(InstanceName As String)
        For Each Entry In PluginLifecycleHandlers.ToList()
            Try
                Entry.Handler.OnInstanceChanged(If(InstanceName, ""))
                Entry.Owner.AddEvent("OnInstanceChanged(" & If(InstanceName, "") & ")")
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnInstanceChanged", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.InstanceChanged, If(InstanceName, ""))
    End Sub

    ''' <summary>主题在深色/浅色之间切换后。</summary>
    Public Sub RaiseThemeChanged(IsDark As Boolean)
        For Each Entry In PluginLifecycleHandlers.ToList()
            Try
                Entry.Handler.OnThemeChanged(IsDark)
                Entry.Owner.AddEvent("OnThemeChanged(" & If(IsDark, "深色", "浅色") & ")")
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnThemeChanged", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.ThemeChanged, IsDark)
    End Sub

    ''' <summary>成功创建联机房间后。</summary>
    Public Sub RaiseLinkRoomCreated(Invite As String)
        For Each Entry In PluginLinkHandlers.ToList()
            Try
                Entry.Handler.OnLinkRoomCreated(If(Invite, ""))
                Entry.Owner.AddEvent("OnLinkRoomCreated")
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnLinkRoomCreated", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.LinkRoomCreated, If(Invite, ""))
    End Sub

    ''' <summary>成功加入联机房间后。</summary>
    Public Sub RaiseLinkRoomJoined()
        For Each Entry In PluginLinkHandlers.ToList()
            Try
                Entry.Handler.OnLinkRoomJoined()
                Entry.Owner.AddEvent("OnLinkRoomJoined")
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnLinkRoomJoined", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.LinkRoomJoined)
    End Sub

    ''' <summary>离开联机房间或断开后。</summary>
    Public Sub RaiseLinkRoomClosed()
        For Each Entry In PluginLinkHandlers.ToList()
            Try
                Entry.Handler.OnLinkRoomClosed()
                Entry.Owner.AddEvent("OnLinkRoomClosed")
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnLinkRoomClosed", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.LinkRoomClosed)
    End Sub

    ''' <summary>是否为需要通知插件的下载类加载器（名称包含 下载 或 Download）。</summary>
    Public Function IsDownloadLoader(Name As String) As Boolean
        If String.IsNullOrWhiteSpace(Name) Then Return False
        Return Name.Contains("下载") OrElse Name.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>
    ''' API v6：判断插件版本是否满足依赖约束。
    ''' 支持 "1.2"（等价于 大于等于）、">=1.2"、">1.1"、"<=2.0"、"=1.2.3"；约束留空表示只要插件存在即可。
    ''' </summary>
    Public Function SatisfiesVersion(Actual As String, Constraint As String) As Boolean
        If String.IsNullOrWhiteSpace(Constraint) Then Return True
        If String.IsNullOrWhiteSpace(Actual) Then Return False
        Dim Text As String = Constraint.Trim()
        Dim Op As String = ">="
        If Text.StartsWith(">=", StringComparison.Ordinal) Then
            Op = ">=" : Text = Text.Substring(2)
        ElseIf Text.StartsWith("<=", StringComparison.Ordinal) Then
            Op = "<=" : Text = Text.Substring(2)
        ElseIf Text.StartsWith("=", StringComparison.Ordinal) Then
            Op = "=" : Text = Text.Substring(1)
        ElseIf Text.StartsWith(">", StringComparison.Ordinal) Then
            Op = ">" : Text = Text.Substring(1)
        ElseIf Text.StartsWith("<", StringComparison.Ordinal) Then
            Op = "<" : Text = Text.Substring(1)
        End If
        Text = Text.Trim()
        If Text = "" Then Return True
        Dim Result As Integer = McUtils.CompareVersion(Actual, Text)
        Select Case Op
            Case ">="
                Return Result >= 0
            Case "<="
                Return Result <= 0
            Case ">"
                Return Result > 0
            Case "<"
                Return Result < 0
            Case Else
                Return Result = 0
        End Select
    End Function

    ''' <summary>一个下载任务开始。</summary>
    Public Sub RaiseDownloadStarted(Target As String)
        For Each Entry In PluginDownloadHandlers.ToList()
            Try
                Entry.Handler.OnDownloadStarted(If(Target, ""))
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnDownloadStarted", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.DownloadStarted, If(Target, ""))
    End Sub

    ''' <summary>一个下载任务结束。</summary>
    Public Sub RaiseDownloadFinished(Target As String, Success As Boolean)
        For Each Entry In PluginDownloadHandlers.ToList()
            Try
                Entry.Handler.OnDownloadFinished(If(Target, ""), Success)
                Entry.Owner.AddEvent("OnDownloadFinished(" & If(Target, "") & ", " & Success & ")")
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnDownloadFinished", ex)
            End Try
        Next
        EmitPluginEvent(PluginEvents.DownloadFinished, If(Target, ""), Success)
    End Sub

    ''' <summary>关闭启动器前询问插件；返回 True 表示有插件要求取消关闭。</summary>
    Public Function RaiseClosing() As Boolean
        Dim Cancel As Boolean = False
        EmitPluginEvent(PluginEvents.Closing)
        For Each Entry In PluginClosingHandlers.ToList()
            Try
                If Entry.Handler.OnClosing() Then Cancel = True
            Catch ex As Exception
                NotePluginFailure(Entry.Owner, "事件 OnClosing", ex)
            End Try
        Next
        Return Cancel
    End Function

    ' ================= 插件安装 / 卸载事件（v6.4） =================

    ''' <summary>插件已安装或升级完成（在导入管线写盘、刷新之后触发）。</summary>
    Public Sub RaisePluginInstalled(Info As PluginInfo)
        If Info Is Nothing Then Return
        EmitPluginEvent(PluginEvents.PluginInstalled, If(Info.Id, ""), If(Info.Version, ""))
    End Sub

    ''' <summary>插件已被卸载（Host.Plugins.Uninstall 成功后触发）。</summary>
    Public Sub RaisePluginUninstalled(Id As String, Version As String)
        EmitPluginEvent(PluginEvents.PluginUninstalled, If(Id, ""), If(Version, ""))
    End Sub

    ' ================= 热重载（v4） =================

    Private _Watcher As FileSystemWatcher = Nothing
    Private _WatcherTimer As Timers.Timer = Nothing

    ''' <summary>开始监视插件目录，plugin.json 变化后自动重载全部插件。</summary>
    Public Sub StartWatcher()
        If _Watcher IsNot Nothing Then Return
        Try
            Directory.CreateDirectory(PathPluginRoot)
            _Watcher = New FileSystemWatcher(PathPluginRoot, "*.json")
            _Watcher.IncludeSubdirectories = True
            _Watcher.NotifyFilter = NotifyFilters.LastWrite Or NotifyFilters.FileName Or NotifyFilters.Size
            Dim Queue As FileSystemEventHandler = Sub(Sender, Args)
                                                      Dim E = TryCast(Args, FileSystemEventArgs)
                                                      If E IsNot Nothing AndAlso IsPluginStateFile(E.FullPath) Then Return
                                                      QueueReload()
                                                  End Sub
            AddHandler _Watcher.Changed, Queue
            AddHandler _Watcher.Created, Queue
            AddHandler _Watcher.Deleted, Queue
            AddHandler _Watcher.Renamed, Sub(Sender, Args)
                                             Dim R = TryCast(Args, RenamedEventArgs)
                                             If R IsNot Nothing AndAlso (IsPluginStateFile(R.FullPath) OrElse IsPluginStateFile(R.OldFullPath)) Then Return
                                             QueueReload()
                                         End Sub
            _Watcher.EnableRaisingEvents = True
            _WatcherTimer = New Timers.Timer(700) With {.AutoReset = False}
            AddHandler _WatcherTimer.Elapsed, Sub(Sender, Args) DoHotReload()
        Catch ex As Exception
            Logger.Error(ex, "启动插件热重载监视失败", LogBehavior.None)
        End Try
    End Sub

    Private Sub StopWatcher()
        Try
            If _Watcher IsNot Nothing Then
                _Watcher.EnableRaisingEvents = False
                _Watcher.Dispose()
                _Watcher = Nothing
            End If
            If _WatcherTimer IsNot Nothing Then
                _WatcherTimer.Stop()
                _WatcherTimer.Dispose()
                _WatcherTimer = Nothing
            End If
        Catch
            '忽略
        End Try
    End Sub

    Private Sub QueueReload()
        Try
            If _WatcherTimer Is Nothing Then Return
            _WatcherTimer.Stop()
            _WatcherTimer.Start()
        Catch
            '忽略
        End Try
    End Sub

    Private Sub DoHotReload()
        Try
            Dim LoadedNames = Plugins.Where(Function(P) P.Loaded).Select(Function(P) P.DisplayName).ToList()
            ModBase.RunInUi(Sub()
                                Try
                                    Refresh(True)
                                    If LoadedNames.Count > 0 Then ModMain.Hint("插件已热重载：" & String.Join("、", LoadedNames), HintType.Blue, True)
                                Catch ex As Exception
                                    Logger.Error(ex, "热重载插件失败", LogBehavior.None)
                                End Try
                            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "热重载插件失败", LogBehavior.None)
        End Try
    End Sub

    ''' <summary>
    ''' 读取某个插件的设置值（未保存过时返回 DefaultValue）。
    ''' </summary>
    Public Function GetSetting(Info As PluginInfo, Key As String, Optional DefaultValue As Object = Nothing) As Object
        Try
            If Info Is Nothing OrElse Not File.Exists(PathSettingsFile) Then Return DefaultValue
            Dim Json = JObject.Parse(File.ReadAllText(PathSettingsFile, Encoding.UTF8))
            Dim Sect = Json.Value(Of JObject)(Info.Id)
            If Sect Is Nothing Then Return DefaultValue
            Dim Token = Sect(Key)
            If Token Is Nothing Then Return DefaultValue
            Select Case Token.Type
                Case JTokenType.Boolean : Return Sect.Value(Of Boolean)(Key)
                Case JTokenType.Integer : Return Sect.Value(Of Long)(Key)
                Case JTokenType.Float : Return Sect.Value(Of Double)(Key)
                Case JTokenType.Null : Return DefaultValue
                Case Else : Return Sect.Value(Of String)(Key)
            End Select
        Catch ex As Exception
            Logger.Error(ex, "读取插件设置失败", LogBehavior.None)
            Return DefaultValue
        End Try
    End Function

    ''' <summary>
    ''' 保存某个插件的设置值。
    ''' </summary>
    Public Sub SetSetting(Info As PluginInfo, Key As String, Value As Object)
        If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Key) Then Return
        Try
            Dim Json As JObject
            If File.Exists(PathSettingsFile) Then
                Json = JObject.Parse(File.ReadAllText(PathSettingsFile, Encoding.UTF8))
            Else
                Json = New JObject
            End If
            Dim Sect = Json.Value(Of JObject)(Info.Id)
            If Sect Is Nothing Then
                Sect = New JObject
                Json(Info.Id) = Sect
            End If
            If Value Is Nothing Then
                Sect(Key) = JValue.CreateNull()
            Else
                Sect(Key) = JToken.FromObject(Value)
            End If
            Directory.CreateDirectory(PathPluginRoot)
            File.WriteAllText(PathSettingsFile, Json.ToString(), New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "保存插件设置失败", LogBehavior.None)
        End Try
    End Sub

    ' ================= 内嵌 UI（v4） =================

    ''' <summary>把插件注册的主页卡片渲染到指定面板。</summary>
    Public Sub RenderHomeCards(Panel As Object)
        RenderCards(PluginHomeCards, TryCast(Panel, System.Windows.Controls.Panel))
    End Sub

    ''' <summary>把插件注册的设置页卡片渲染到指定面板。</summary>
    Public Sub RenderSettingsCards(Panel As Object)
        RenderCards(PluginSettingsCards, TryCast(Panel, System.Windows.Controls.Panel))
    End Sub

    Private Sub RenderCards(Cards As List(Of PluginCardInfo), Target As System.Windows.Controls.Panel)
        If Target Is Nothing Then Return
        Target.Children.Clear()
        For Each C In Cards.ToList()
            Try
                Dim Content = C.Factory()
                Dim Elem = TryCast(Content, FrameworkElement)
                If Elem Is Nothing Then Continue For
                Dim Card As New MyCard With {.Title = C.DisplayTitle, .Margin = New Thickness(0, 0, 0, 15)}
                Elem.Margin = New Thickness(15, 38, 15, 15)
                Card.Children.Add(Elem)
                Target.Children.Add(Card)
            Catch ex As Exception
                Logger.Error(ex, "渲染插件卡片失败：" & C.DisplayTitle, LogBehavior.None)
            End Try
        Next
    End Sub


    ' ================= API v5 基础设施 =================

    ''' <summary>API v5：细化权限（plugin.json 的 permissions 写成对象时才有内容）。</summary>
    Public Class PermissionDetail
        Public HasDetail As Boolean
        ''' <summary>net 允许访问的主机，支持 *.example.com 与 * 。</summary>
        Public ReadOnly Hosts As New List(Of String)
        ''' <summary>net 写成 true 或字符串时表示「任意主机」（v7.4，安全审计 V7）。</summary>
        Public NetAny As Boolean
        ''' <summary>files 允许读取的路径（支持 * 通配）。</summary>
        Public ReadOnly ReadPaths As New List(Of String)
        ''' <summary>files 允许写入的路径（支持 * 通配）。</summary>
        Public ReadOnly WritePaths As New List(Of String)
    End Class

    ''' <summary>把清单里的字符串或字符串数组统一读成列表。</summary>
    Private Function ReadStringList(Token As JToken) As List(Of String)
        Dim Result As New List(Of String)
        If Token Is Nothing Then Return Result
        Dim Items = TryCast(Token, JArray)
        If Items IsNot Nothing Then
            For Each Item In Items
                Dim Text = Item.ToString().Trim()
                If Text <> "" Then Result.Add(Text)
            Next
        Else
            Dim JValueToken = TryCast(Token, JValue)
            If JValueToken Is Nothing Then Return Result
            Dim Text = JValueToken.ToString().Trim()
            If Text <> "" Then Result.Add(Text)
        End If
        Return Result
    End Function

    ''' <summary>主机名是否匹配模式（支持 *.example.com 与 *）。</summary>
    Public Function MatchHostPattern(Host As String, Pattern As String) As Boolean
        If String.IsNullOrWhiteSpace(Pattern) Then Return False
        Dim H = Host.Trim().ToLowerInvariant()
        Dim P = Pattern.Trim().ToLowerInvariant()
        If P = "*" Then Return True
        If P.StartsWith("*.") Then Return H = P.Substring(2) OrElse H.EndsWith(P.Substring(1), StringComparison.OrdinalIgnoreCase)
        Return String.Equals(H, P, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>路径是否匹配模式（相对路径按启动器目录解析，末尾反斜杠表示整个目录）。</summary>
    Public Function PathMatches(Pattern As String, FullPath As String) As Boolean
        If String.IsNullOrWhiteSpace(Pattern) Then Return False
        Dim P = Pattern.Trim().Replace("/", "\")
        Dim F = FullPath.Trim().Replace("/", "\")
        Try
            If Not IO.Path.IsPathRooted(P) Then P = IO.Path.GetFullPath(IO.Path.Combine(New IO.DirectoryInfo(Paths.BaseThenName).FullName, P))
        Catch
            Return False
        End Try
        If P.EndsWith("\") Then Return F.StartsWith(P, StringComparison.OrdinalIgnoreCase)
        If Not P.Contains("*") Then Return String.Equals(F, P, StringComparison.OrdinalIgnoreCase) OrElse F.StartsWith(P & "\", StringComparison.OrdinalIgnoreCase)
        Return F.ToLowerInvariant() Like P.ToLowerInvariant()
    End Function

    ''' <summary>计算一段文本的 SHA-256（小写十六进制），用于签名载荷里的清单指纹（v7.4，安全审计 V8）。</summary>
    Public Function Sha256OfText(Value As String) As String
        Try
            Using Sha = Security.Cryptography.SHA256.Create()
                Dim Bytes = Sha.ComputeHash(Encoding.UTF8.GetBytes(If(Value, "")))
                Dim Builder As New Text.StringBuilder()
                For Each B In Bytes
                    Builder.Append(B.ToString("x2"))
                Next
                Return Builder.ToString()
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "计算文本 sha256 失败", LogBehavior.None)
            Return ""
        End Try
    End Function

    ''' <summary>计算文件的 SHA-256（小写十六进制）；失败返回空字符串。</summary>
    Public Function Sha256OfFile(FilePath As String) As String
        Try
            Using Stream = IO.File.OpenRead(FilePath)
                Using Sha = Security.Cryptography.SHA256.Create()
                    Dim Bytes = Sha.ComputeHash(Stream)
                    Dim Builder As New Text.StringBuilder()
                    For Each B In Bytes
                        Builder.Append(B.ToString("x2"))
                    Next
                    Return Builder.ToString()
                End Using
            End Using
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>检查网址是否在插件的 net 白名单里（未声明 hosts 时按全部允许处理）。</summary>
    Public Sub EnsureHostAllowed(Info As PluginInfo, Url As String)
        If Info Is Nothing OrElse Not Info.Detail.HasDetail Then Return
        If Info.Detail.Hosts.Count = 0 AndAlso Not Info.Detail.NetAny Then
            'v7.4（安全审计 V7）：写了 net 却没给出主机（含键名拼错）→ 一律拒绝，不再 fail-open
            Throw New UnauthorizedAccessException("插件 " & Info.DisplayName & " 声明了 net 权限，但没有列出可访问的主机（plugin.json 的 permissions.net 应写成 {""hosts"": [ … ]} 或 true），已拒绝：" & Url)
        End If
        Dim Host As String = ""
        Try
            Host = New Uri(Url).Host
        Catch
        End Try
        If Host = "" Then Throw New ArgumentException("无效的网址：" & Url)
        'v7.5（安全审计 3.8 N1）：net 写成 true = 允许任意主机（此前 NetAny 只用于跳过空列表报错，导致 true 全拒）
        If Info.Detail.NetAny Then Return
        If Info.Detail.Hosts.Any(Function(P) MatchHostPattern(Host, P)) Then Return
        ' net.dynamic：清单里没列出的主机，可以在运行时逐个向用户申请
        If Info.HasPermission("net.dynamic") Then
            If IsHostApproved(Info, Host) Then Return
            Dim Allow As Boolean = False
            Try
                ModBase.RunInUiWait(Sub()
                                        Dim Choice As Integer = ModMain.MyMsgBox(
                                            $"插件「{Info.DisplayName}」想访问网络主机：{vbCrLf}{Host}{vbCrLf}{vbCrLf}" &
                                            $"它声明的固定主机：{String.Join("、", Info.Detail.Hosts)}{vbCrLf}" &
                                            "批准后这个主机对该插件长期有效，可以在「插件设置」里撤销。",
                                            "插件申请访问网络", "允许", "拒绝")
                                        Allow = (Choice = 1)
                                    End Sub)
            Catch ex As Exception
                Logger.Error(ex, "插件动态网络授权失败", LogBehavior.None)
            End Try
            If Allow Then
                ApproveHost(Info, Host)
                Logger.Info($"[插件 {Info.DisplayName}] 已授权访问主机 {Host}")
                Return
            End If
            Info.AddEvent($"用户拒绝访问主机：{Host}")
            Throw New UnauthorizedAccessException($"插件 {Info.DisplayName} 申请访问 {Host} 被拒绝")
        End If
        Info.AddEvent($"拦截未授权的网络请求：{Host}")
        Throw New UnauthorizedAccessException($"插件 {Info.DisplayName} 的 net 权限只允许 {String.Join("、", Info.Detail.Hosts)}，已拦截 {Host}")
    End Sub

    ''' <summary>
    ''' API v6：事件名常量。插件用 Host.Events.On(PluginEvents.游戏启动前, Sub(info) ...) 订阅，
    ''' 也可以直接写字符串（事件名不区分大小写）。
    ''' </summary>
    Public Class PluginEvents
        Public Const LauncherReady As String = "启动器就绪"
        Public Const PageChanged As String = "页面切换"
        Public Const GameLaunching As String = "游戏启动前"
        Public Const GameLaunched As String = "游戏启动后"
        Public Const GameExited As String = "游戏退出"
        Public Const InstanceChanged As String = "实例切换"
        Public Const ThemeChanged As String = "主题切换"
        Public Const LinkRoomCreated As String = "联机创建"
        Public Const LinkRoomJoined As String = "联机加入"
        Public Const LinkRoomClosed As String = "联机关闭"
        Public Const DownloadStarted As String = "下载开始"
        Public Const DownloadFinished As String = "下载完成"
        Public Const Closing As String = "关闭前"
        Public Const PluginInstalled As String = "插件安装"
        Public Const PluginUninstalled As String = "插件卸载"

        ''' <summary>全部事件名。</summary>
        Public Shared Function All() As String()
            Return {LauncherReady, PageChanged, GameLaunching, GameLaunched, GameExited, InstanceChanged, ThemeChanged,
                    LinkRoomCreated, LinkRoomJoined, LinkRoomClosed, DownloadStarted, DownloadFinished, Closing,
                    PluginInstalled, PluginUninstalled}
        End Function

        ''' <summary>事件的参数签名（开发模式控制台显示用）。</summary>
        Public Shared Function GetSignature(EventName As String) As String
            Select Case If(EventName, "").ToLowerInvariant()
                Case LauncherReady.ToLowerInvariant()
                    Return "()"
                Case PageChanged.ToLowerInvariant()
                    Return "(page As Integer, subType As Integer)"
                Case GameLaunching.ToLowerInvariant(), GameLaunched.ToLowerInvariant()
                    Return "(info As PluginLaunchInfo)"
                Case GameExited.ToLowerInvariant()
                    Return "(info As PluginLaunchInfo, exitCode As Integer)"
                Case InstanceChanged.ToLowerInvariant()
                    Return "(instanceName As String)"
                Case ThemeChanged.ToLowerInvariant()
                    Return "(isDark As Boolean)"
                Case LinkRoomCreated.ToLowerInvariant()
                    Return "(invite As String)"
                Case LinkRoomJoined.ToLowerInvariant(), LinkRoomClosed.ToLowerInvariant(), Closing.ToLowerInvariant()
                    Return "()"
                Case DownloadStarted.ToLowerInvariant()
                    Return "(target As String)"
                Case DownloadFinished.ToLowerInvariant()
                    Return "(target As String, success As Boolean)"
                Case PluginInstalled.ToLowerInvariant(), PluginUninstalled.ToLowerInvariant()
                    Return "(pluginId As String, version As String)"
                Case Else
                    Return "(...)"
            End Select
        End Function
    End Class

    ''' <summary>
    ''' API v6：开发模式实时日志。插件页「开发模式」分区与控制台读取这里的内容，
    ''' 记录事件触发、宿主 API 调用、被拦截的操作和插件异常。
    ''' </summary>
    Public Class PluginDevLog
        Public Const LevelTrace As Integer = 0
        Public Const LevelInfo As Integer = 1
        Public Const LevelWarn As Integer = 2
        Public Const LevelError As Integer = 3

        Public Class Entry
            Public Time As Date = Date.Now
            Public Level As Integer = LevelInfo
            Public Source As String = ""
            Public Text As String = ""
            Public ReadOnly Property Display As String
                Get
                    Dim Mark As String
                    Select Case Level
                        Case LevelError
                            Mark = "错误"
                        Case LevelWarn
                            Mark = "警告"
                        Case LevelTrace
                            Mark = "追踪"
                        Case Else
                            Mark = "信息"
                    End Select
                    Return $"{Time:HH:mm:ss.fff} [{Mark}] {Source}：{Text}"
                End Get
            End Property
        End Class

        Private Const Capacity As Integer = 4000
        Private Shared ReadOnly _Lock As New Object
        Private Shared ReadOnly _Items As New List(Of Entry)
        ''' <summary>有新日志时触发（可能在任意线程）。</summary>
        Public Shared Event Changed()

        Public Shared Sub Write(Level As Integer, Source As String, Text As String)
            Try
                SyncLock _Lock
                    _Items.Add(New Entry With {.Time = Date.Now, .Level = Level, .Source = If(Source, ""), .Text = If(Text, "")})
                    If _Items.Count > Capacity Then _Items.RemoveRange(0, _Items.Count - Capacity)
                End SyncLock
                RaiseEvent Changed()
            Catch
                '开发日志本身不允许影响主流程
            End Try
        End Sub

        Public Shared Sub Trace(Source As String, Text As String)
            Write(LevelTrace, Source, Text)
        End Sub

        Public Shared Sub Info(Source As String, Text As String)
            Write(LevelInfo, Source, Text)
        End Sub

        Public Shared Sub Warn(Source As String, Text As String)
            Write(LevelWarn, Source, Text)
        End Sub

        Public Shared Sub [Error](Source As String, Text As String)
            Write(LevelError, Source, Text)
        End Sub

        ''' <summary>取一份快照；MinLevel 为最低级别，SourceFilter 为空表示不过滤。</summary>
        Public Shared Function Snapshot(Optional MinLevel As Integer = LevelTrace, Optional SourceFilter As String = Nothing, Optional MaxCount As Integer = 800) As List(Of Entry)
            Dim Result As New List(Of Entry)
            SyncLock _Lock
                For i = _Items.Count - 1 To 0 Step -1
                    If Result.Count >= MaxCount Then Exit For
                    Dim Item = _Items(i)
                    If Item.Level < MinLevel Then Continue For
                    If Not String.IsNullOrWhiteSpace(SourceFilter) AndAlso
                       Item.Source.IndexOf(SourceFilter.Trim(), StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                       Item.Text.IndexOf(SourceFilter.Trim(), StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                    Result.Add(Item)
                Next
            End SyncLock
            Result.Reverse()
            Return Result
        End Function

        Public Shared Sub Clear()
            SyncLock _Lock
                _Items.Clear()
            End SyncLock
            RaiseEvent Changed()
        End Sub
    End Class

    ''' <summary>
    ''' API v6：事件总线。插件用 Host.Events.On("事件名", Sub(...) ...) 订阅。
    ''' 支持优先级（数值大的先执行）、一次性订阅（执行后自动移除）与异步分发。
    ''' </summary>
    Public Class PluginEventBus
        '属于哪个插件（用于把处理器异常计入该插件的熔断计数）
        Friend Property OwnerInfo As ModPlugin.PluginInfo

        Private Class Subscription
            Public Handler As [Delegate]
            Public Priority As Integer
            Public OnlyOnce As Boolean
            Public Owner As String
        End Class

        Private ReadOnly _Handlers As New Dictionary(Of String, List(Of Subscription))(StringComparer.OrdinalIgnoreCase)

        ''' <summary>订阅事件。Priority 越大越先执行；OnlyOnce 为 True 时只执行一次。</summary>
        ''' <summary>v5 兼容重载：等价于优先级 0、非一次性、无所有者。</summary>
        ''' <remarks>v6 给 On 增加了可选参数，CLR 签名随之变化；已编译的 v5 插件仍按两参数调用，必须保留这个重载，否则会抛 MissingMethodException。</remarks>
        Public Sub [On](EventName As String, Handler As [Delegate])
            [On](EventName, Handler, 0, False, "")
        End Sub

        Public Sub [On](EventName As String, Handler As [Delegate], Optional Priority As Integer = 0, Optional OnlyOnce As Boolean = False, Optional Owner As String = "")
            If String.IsNullOrWhiteSpace(EventName) OrElse Handler Is Nothing Then Return
            'v7：事件总线同样受 events 权限约束，否则「实现 IPclPluginEvents」的闸门可以被绕开
            If OwnerInfo IsNot Nothing AndAlso Not OwnerInfo.HasPermission("events") Then
                OwnerInfo.AddEvent($"没有 events 权限，订阅被拒绝：{EventName}")
                PluginDevLog.Trace("事件总线", $"拒绝订阅 {EventName}：缺少 events 权限")
                Return
            End If
            SyncLock _Handlers
                Dim Items As List(Of Subscription) = Nothing
                If Not _Handlers.TryGetValue(EventName, Items) Then
                    Items = New List(Of Subscription)
                    _Handlers(EventName) = Items
                End If
                Items.Add(New Subscription With {.Handler = Handler, .Priority = Priority, .OnlyOnce = OnlyOnce, .Owner = If(Owner, "")})
            End SyncLock
            PluginDevLog.Trace("事件总线", $"订阅 {EventName}{PluginEvents.GetSignature(EventName)}（优先级 {Priority}{If(OnlyOnce, "，一次性", "")}）")
        End Sub

        ''' <summary>订阅一次性事件。</summary>
        Public Sub Once(EventName As String, Handler As [Delegate], Optional Priority As Integer = 0)
            [On](EventName, Handler, Priority, True)
        End Sub

        ''' <summary>取消订阅。</summary>
        Public Sub Off(EventName As String, Handler As [Delegate])
            If String.IsNullOrWhiteSpace(EventName) OrElse Handler Is Nothing Then Return
            SyncLock _Handlers
                Dim Items As List(Of Subscription) = Nothing
                If _Handlers.TryGetValue(EventName, Items) Then
                    For i = Items.Count - 1 To 0 Step -1
                        If Items(i).Handler Is Handler Then Items.RemoveAt(i)
                    Next
                    If Items.Count = 0 Then _Handlers.Remove(EventName)
                End If
            End SyncLock
        End Sub

        ''' <summary>某个事件的订阅者数量。</summary>
        Public Function SubscriberCount(EventName As String) As Integer
            SyncLock _Handlers
                Dim Items As List(Of Subscription) = Nothing
                If _Handlers.TryGetValue(EventName, Items) Then Return Items.Count
            End SyncLock
            Return 0
        End Function

        ''' <summary>当前有订阅者的事件与订阅数（开发模式显示用）。</summary>
        Public Function SubscriberSummary() As List(Of String)
            Dim Result As New List(Of String)
            SyncLock _Handlers
                For Each Pair In _Handlers
                    Result.Add($"{Pair.Key}：{Pair.Value.Count} 个订阅")
                Next
            End SyncLock
            Result.Sort()
            Return Result
        End Function

        ''' <summary>触发事件，返回成功执行的处理器数量。处理器抛出的异常会被隔离并记入开发日志。</summary>
        ''' <summary>
        ''' 把事件参数适配到处理器声明的类型。
        ''' v5 插件订阅「页面切换」时按 (String, String) 声明，而 v6 内部统一传 Integer；
        ''' 直接 DynamicInvoke 会抛「Int32 无法转换为 String」，这里按目标参数类型做安全转换。
        ''' </summary>
        Private Shared Function AdaptArguments(Handler As [Delegate], Args As Object()) As Object()
            If Handler Is Nothing OrElse Args Is Nothing OrElse Args.Length = 0 Then Return Args
            Dim Params = Handler.Method.GetParameters()
            If Params.Length = 0 Then Return Args

            'ParamArray：把多余参数打包成数组
            Dim LastParam = Params(Params.Length - 1)
            Dim IsParamArray As Boolean = LastParam.GetCustomAttributes(GetType(ParamArrayAttribute), False).Length > 0
            If IsParamArray AndAlso Args.Length >= Params.Length Then
                Dim Packed(Params.Length - 1) As Object
                For i = 0 To Params.Length - 2
                    Packed(i) = ConvertValue(Args(i), Params(i).ParameterType)
                Next
                Dim Extra(Args.Length - Params.Length) As Object
                For i = Params.Length - 1 To Args.Length - 1
                    Extra(i - (Params.Length - 1)) = Args(i)
                Next
                Packed(Params.Length - 1) = Extra
                Return Packed
            End If

            Dim Result(Args.Length - 1) As Object
            For i = 0 To Args.Length - 1
                If i < Params.Length Then
                    Result(i) = ConvertValue(Args(i), Params(i).ParameterType)
                Else
                    Result(i) = Args(i)
                End If
            Next
            Return Result
        End Function

        Private Shared Function ConvertValue(Value As Object, Target As Type) As Object
            If Value Is Nothing OrElse Target Is Nothing OrElse Target.IsInstanceOfType(Value) Then Return Value
            Try
                If Target Is GetType(String) Then Return Convert.ToString(Value)
                If Target.IsEnum Then Return [Enum].ToObject(Target, Convert.ToInt32(Value))
                If Value.GetType() Is GetType(String) Then Return Convert.ChangeType(Value, Target)
            Catch
                '转换失败就原样传进去，让处理器自己报错并被隔离
            End Try
            Return Value
        End Function
        Public Function Emit(EventName As String, ParamArray Args As Object()) As Integer
            If String.IsNullOrWhiteSpace(EventName) Then Return 0
            Dim Snapshot As New List(Of Subscription)
            SyncLock _Handlers
                Dim Items As List(Of Subscription) = Nothing
                If Not _Handlers.TryGetValue(EventName, Items) Then
                    Items = Nothing
                End If
                If Items IsNot Nothing Then
                    '按优先级降序（OrderByDescending 是稳定排序，同优先级保持订阅顺序）
                    Snapshot.AddRange(Items.OrderByDescending(Function(S) S.Priority))
                End If
            End SyncLock
            Dim Executed As Integer = 0
            For Each Item In Snapshot
                Try
                    Item.Handler.DynamicInvoke(AdaptArguments(Item.Handler, Args))
                    Executed += 1
                Catch ex As Exception
                    PluginDevLog.Error("事件总线", $"处理 {EventName} 时出错：{ex.GetBaseException().Message}")
                    If OwnerInfo IsNot Nothing Then NotePluginFailure(OwnerInfo, "事件" & EventName, ex)
                    Logger.Error(ex, "插件事件处理失败：" & EventName, LogBehavior.None)
                End Try
                If Item.OnlyOnce Then
                    SyncLock _Handlers
                        Dim Items As List(Of Subscription) = Nothing
                        If _Handlers.TryGetValue(EventName, Items) Then
                            Items.Remove(Item)
                            If Items.Count = 0 Then _Handlers.Remove(EventName)
                        End If
                    End SyncLock
                End If
            Next
            Return Executed
        End Function

        ''' <summary>异步触发事件（不阻塞调用方，处理器在主线程池里执行）。</summary>
        Public Sub EmitAsync(EventName As String, ParamArray Args As Object())
            Dim EventArgs = Args
            System.Threading.Tasks.Task.Run(Sub()
                                          Try
                                              Emit(EventName, EventArgs)
                                          Catch ex As Exception
                                              Logger.Error(ex, "异步分发插件事件失败：" & EventName, LogBehavior.None)
                                          End Try
                                      End Sub)
        End Sub
    End Class

    ''' <summary>
    ''' API v6：插件间服务注册表。插件 A 把实现注册进来，插件 B 通过接口类型取用。
    ''' 卸载插件时会自动清理它注册的服务。
    ''' </summary>
    Public Class PluginServiceRegistry
        Private Class Entry
            Public Value As Object
            Public Owner As String
        End Class

        Private ReadOnly _Lock As New Object
        Private ReadOnly _Services As New Dictionary(Of Type, List(Of Entry))

        Friend Sub RegisterService(ServiceType As Type, Value As Object, Owner As String)
            SyncLock _Lock
                Dim Items As List(Of Entry) = Nothing
                If Not _Services.TryGetValue(ServiceType, Items) Then
                    Items = New List(Of Entry)
                    _Services(ServiceType) = Items
                End If
                Items.Add(New Entry With {.Value = Value, .Owner = If(Owner, "")})
            End SyncLock
            PluginDevLog.Info("服务", $"注册 {ServiceType.Name}（来自 {Owner}）")
        End Sub

        Friend Sub UnregisterService(ServiceType As Type, Owner As String)
            SyncLock _Lock
                Dim Items As List(Of Entry) = Nothing
                If _Services.TryGetValue(ServiceType, Items) Then
                    For i = Items.Count - 1 To 0 Step -1
                        If String.Equals(Items(i).Owner, If(Owner, ""), StringComparison.OrdinalIgnoreCase) Then Items.RemoveAt(i)
                    Next
                    If Items.Count = 0 Then _Services.Remove(ServiceType)
                End If
            End SyncLock
        End Sub

        Friend Function GetService(ServiceType As Type) As Object
            SyncLock _Lock
                Dim Items As List(Of Entry) = Nothing
                If _Services.TryGetValue(ServiceType, Items) AndAlso Items.Count > 0 Then Return Items(Items.Count - 1).Value
            End SyncLock
            Return Nothing
        End Function

        Friend Function HasService(ServiceType As Type) As Boolean
            SyncLock _Lock
                Dim Items As List(Of Entry) = Nothing
                Return _Services.TryGetValue(ServiceType, Items) AndAlso Items.Count > 0
            End SyncLock
        End Function

        Friend Sub UnregisterByOwner(Owner As String)
            SyncLock _Lock
                For Each Pair In _Services.ToList()
                    Pair.Value.RemoveAll(Function(E) String.Equals(E.Owner, If(Owner, ""), StringComparison.OrdinalIgnoreCase))
                    If Pair.Value.Count = 0 Then _Services.Remove(Pair.Key)
                Next
            End SyncLock
        End Sub

        ''' <summary>当前注册的服务（开发模式显示用）。</summary>
        Public Function Describe() As List(Of String)
            Dim Result As New List(Of String)
            SyncLock _Lock
                For Each Pair In _Services
                    Result.Add($"{Pair.Key.Name} ← {Pair.Value(Pair.Value.Count - 1).Owner}")
                Next
            End SyncLock
            Result.Sort()
            Return Result
        End Function
    End Class

    ''' <summary>
    ''' API v6：插件访问服务注册表的入口（Host.Services）。
    ''' 用法：Host.Services.Register(Of IMyApi)(Me) / Host.Services.Get(Of IMyApi)()
    ''' </summary>
    Public Class PluginServiceAccessor
        Friend ReadOnly OwnerId As String

        Friend Sub New(OwnerId As String)
            Me.OwnerId = OwnerId
        End Sub

        Public Sub Register(Of T As Class)(Impl As T)
            If Impl Is Nothing Then Throw New ArgumentNullException(NameOf(Impl))
            ModPlugin.PluginServices.RegisterService(GetType(T), Impl, OwnerId)
        End Sub

        Public Sub Unregister(Of T As Class)()
            ModPlugin.PluginServices.UnregisterService(GetType(T), OwnerId)
        End Sub

        Public Function [Get](Of T As Class)() As T
            Return TryCast(ModPlugin.PluginServices.GetService(GetType(T)), T)
        End Function

        Public Function TryGet(Of T As Class)(ByRef Service As T) As Boolean
            Service = [Get](Of T)()
            Return Service IsNot Nothing
        End Function

        Public Function Has(Of T As Class)() As Boolean
            Return ModPlugin.PluginServices.HasService(GetType(T))
        End Function
    End Class

    ''' <summary>全局服务注册表。</summary>
    Public ReadOnly PluginServices As New PluginServiceRegistry

    ''' <summary>API v6：任务上下文。工作委托里用它上报进度、检查取消。</summary>
    Public Class PluginTaskContext
        Friend ReadOnly Loader As LoaderBase

        Friend Sub New(Loader As LoaderBase)
            Me.Loader = Loader
        End Sub

        ''' <summary>任务是否已被取消。</summary>
        Public ReadOnly Property Cancelled As Boolean
            Get
                Return Loader.State = LoadState.Canceled
            End Get
        End Property

        ''' <summary>上报进度（0~1）；Message 为空时只更新进度条。</summary>
        Public Sub Report(Value As Double, Optional Message As String = Nothing)
            Loader.Progress = Math.Max(0, Math.Min(1, Value))
            If Not String.IsNullOrEmpty(Message) Then Loader.Name = "插件任务：" & Message
        End Sub

        ''' <summary>已取消时抛出 OperationCanceledException，用于提前退出。</summary>
        Public Sub ThrowIfCancelled()
            If Cancelled Then Throw New OperationCanceledException("插件任务已取消")
        End Sub
    End Class

    ''' <summary>API v6：后台任务句柄。</summary>
    Public Class PluginTaskHandle
        Public ReadOnly Loader As LoaderBase

        Friend Sub New(Loader As LoaderBase)
            Me.Loader = Loader
        End Sub

        Public ReadOnly Property Progress As Double
            Get
                Return Loader.Progress
            End Get
        End Property

        Public ReadOnly Property State As LoadState
            Get
                Return Loader.State
            End Get
        End Property

        ''' <summary>取消任务。</summary>
        Public Sub Cancel()
            Try
                Loader.Cancel()
            Catch ex As Exception
                Logger.Error(ex, "取消插件任务失败", LogBehavior.None)
            End Try
        End Sub

        ''' <summary>等待任务结束（会抛出让任务失败时记录过的异常）。</summary>
        Public Sub Wait()
            Loader.WaitForExit()
        End Sub
    End Class

    ''' <summary>
    ''' API v6：后台任务。插件用 Host.Tasks.Run("名字", Sub(ctx) ... End Sub) 起任务，
    ''' 任务会出现在右下角「任务管理」里，可显示进度、可取消。
    ''' </summary>
    Public Class PluginTaskManager
        Friend ReadOnly OwnerId As String

        Friend Sub New(OwnerId As String)
            Me.OwnerId = OwnerId
        End Sub

        Public Function Run(Name As String, Work As Action(Of PluginTaskContext)) As PluginTaskHandle
            If Work Is Nothing Then Throw New ArgumentNullException(NameOf(Work))
            Dim FullName As String = "插件任务：" & If(String.IsNullOrWhiteSpace(Name), "未命名", Name)
            Dim Ldr As LoaderBase = Nothing
            Ldr = New LoaderTask(Of Object, Object)(FullName,
                Sub(Task)
                    Dim Context As New PluginTaskContext(Task)
                    PluginDevLog.Info("任务", $"开始 {FullName}")
                    Try
                        Work(Context)
                        PluginDevLog.Info("任务", $"完成 {FullName}")
                    Catch ex As OperationCanceledException
                        PluginDevLog.Warn("任务", $"取消 {FullName}")
                        Task.Cancel()
                    Catch ex As Exception
                        PluginDevLog.Error("任务", $"{FullName} 失败：{ex.GetBaseException().Message}")
                        Throw
                    End Try
                End Sub)
            Ldr.Show = True
            Ldr.Start()
            '开始后再加入任务栏，避免 50ms 的刷新把 Waiting 状态的任务丢掉
            LoaderTaskbar.Add(Ldr)
            Return New PluginTaskHandle(Ldr)
        End Function
    End Class

    ''' <summary>向所有已加载插件广播事件（宿主内部使用）。</summary>
    Public Function EmitPluginEvent(EventName As String, ParamArray Args As Object()) As Integer
        Dim Count As Integer = 0
        Dim Subscribers As Integer = 0
        For Each Info In Plugins.ToList()
            If Info.Loaded AndAlso Info.Host IsNot Nothing Then
                Count += Info.Host.Events.Emit(EventName, Args)
                Subscribers += Info.Host.Events.SubscriberCount(EventName)
            End If
        Next
        PluginDevLog.Write(If(Subscribers = 0, PluginDevLog.LevelTrace, PluginDevLog.LevelInfo), "事件",
                           $"{EventName}{PluginEvents.GetSignature(EventName)} → 执行 {Count} 个处理器（共 {Subscribers} 个订阅）")
        Return Count
    End Function

    ''' <summary>API v6.2：收集所有插件为这个实例提供的右键菜单项（每个插件调用一次，异常隔离）。</summary>
    Public Function GetInstanceMenuItems(InstanceName As String, InstanceFolder As String) As List(Of PluginMenuItem)
        Dim Result As New List(Of PluginMenuItem)
        Dim Ctx As New PluginInstanceInfo With {.Name = If(InstanceName, ""), .Folder = If(InstanceFolder, "")}
        For Each Info In Plugins.ToList()
            If Not Info.Enabled OrElse Not Info.Loaded OrElse Info.Instance Is Nothing Then Continue For
            If Not TypeOf Info.Instance Is IPclPluginInstanceMenu Then Continue For
            Dim Provider = CType(Info.Instance, IPclPluginInstanceMenu)
            Dim Items As IEnumerable(Of PluginMenuItem) = GuardCall(Info, "实例右键菜单", Function() Provider.GetInstanceMenuItems(Ctx), Nothing)
            If Items Is Nothing Then Continue For
            For Each Entry In Items
                If Entry Is Nothing OrElse String.IsNullOrWhiteSpace(Entry.Text) Then Continue For
                Entry.Owner = Info
                Result.Add(Entry)
            Next
        Next
        Return Result
    End Function

    ''' <summary>API v6.2：执行插件提供的右键菜单项；异常只会记一次失败，不会打断启动器。</summary>
    Public Sub RunInstanceMenu(Item As PluginMenuItem)
        If Item Is Nothing Then Return
        GuardCall(Item.Owner, "实例右键菜单：" & Item.Text, Item.Action)
    End Sub

    ''' <summary>API v6.2：传给插件实例右键菜单的实例信息。</summary>
    Public Class PluginInstanceInfo
        ''' <summary>实例（版本）名称。</summary>
        Public Property Name As String = ""
        ''' <summary>实例所在文件夹。</summary>
        Public Property Folder As String = ""
    End Class

    ''' <summary>API v6.2：插件往「启动」页实例右键菜单里加的一项。</summary>
    Public Class PluginMenuItem
        ''' <summary>菜单文字。建议加插件名前缀，例如「导出实例（Demo 插件）」。</summary>
        Public Property Text As String = ""
        ''' <summary>点击后执行的动作；异常会被隔离并计入熔断。</summary>
        Public Property Action As Action
        ''' <summary>由启动器自动填写，插件不需要设置。</summary>
        Public Property Owner As PluginInfo
    End Class

    ''' <summary>
    ''' API v6.2：实例右键菜单扩展点。实现本接口后，插件可以往「启动」页左侧实例列表的右键菜单里添加操作。
    ''' 每次打开右键菜单时调用一次，插件应按当前实例动态返回可用的项（返回 Nothing 或空集合表示不加）。
    ''' </summary>
    Public Interface IPclPluginInstanceMenu
        Function GetInstanceMenuItems(Instance As PluginInstanceInfo) As IEnumerable(Of PluginMenuItem)
    End Interface

#Region "插件导入（拖入 / 导入按钮）"

    ''' <summary>插件包扩展名：本质是 zip，根目录或唯一一层子目录里放 plugin.json。</summary>
    Public Const PluginPackageExtension As String = "pclplugin"

    ''' <summary>导入结果：成功与否、错误码、给用户看的说明。</summary>
    Public Class PluginImportResult
        Public Success As Boolean
        Public Code As String = "OK"
        Public Message As String = ""
        Public PluginId As String = ""
        Public PluginName As String = ""
        Public Version As String = ""
        Public Folder As String = ""
        Public Upgraded As Boolean
        ''' <summary>是否需要重启启动器才生效（插件正在运行时覆盖安装）。</summary>
        Public NeedRestart As Boolean
        Public ReadOnly Property Display As String
            Get
                If Success AndAlso NeedRestart Then
                    Dim Ver2 As String = If(String.IsNullOrWhiteSpace(Version), "", " v" & Version)
                    Return $"已就绪：{PluginName}（{PluginId}）{Ver2}，重启启动器后生效"
                ElseIf Success Then
                    Dim Action As String = If(Upgraded, "已更新", "已导入")
                    Dim Ver As String = If(String.IsNullOrWhiteSpace(Version), "", " v" & Version)
                    Return $"{Action}：{PluginName}（{PluginId}）{Ver}"
                Else
                    Return $"失败：{Message}"
                End If
            End Get
        End Property
    End Class

    Private Function ImportFail(Code As String, Message As String) As PluginImportResult
        Return New PluginImportResult With {.Success = False, .Code = Code, .Message = Message}
    End Function

    Private Function IsManifestEntry(FullName As String) As Boolean
        Dim Clean = FullName.Replace("\"c, "/"c).TrimStart("/"c)
        Dim Parts = Clean.Split("/"c)
        If Parts.Length = 0 OrElse Parts.Length > 2 Then Return False
        Return String.Equals(Parts(Parts.Length - 1), ManifestFileName, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function IsValidPluginId(Id As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(Id) AndAlso
               Text.RegularExpressions.Regex.IsMatch(Id, "^[a-z0-9][a-z0-9_\-\.]{0,63}$", Text.RegularExpressions.RegexOptions.IgnoreCase)
    End Function

    Private Function GetFileSha256(Path As String) As String
        Try
            Using Stream = IO.File.OpenRead(Path)
                Using Sha = Security.Cryptography.SHA256.Create()
                    Return BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>判断一个文件是否像插件包（zip，且根目录或唯一顶层目录里有 plugin.json）。</summary>
    Public Function LooksLikePluginPackage(Path As String) As Boolean
        Try
            If Not File.Exists(Path) Then Return False
            If Not {PluginPackageExtension, "zip"}.Contains(IO.Path.GetExtension(Path).ToLowerInvariant()) Then Return False
            Using Archive = FileUtils.OpenZip(Path)
                Return Archive.Entries.Any(Function(E) IsManifestEntry(E.FullName))
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>判断一个 DLL 是否声明了 PclPluginId（单 DLL 简装用）。</summary>
    Public Function LooksLikePluginAssembly(Path As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(ReadAssemblyMetadata(Path, "PclPluginId"))
    End Function

    ''' <summary>用反射只读地读取程序集上的 AssemblyMetadataAttribute（不执行代码）。</summary>
    Private Function ReadAssemblyMetadata(DllPath As String, Key As String) As String
        Try
            If Not File.Exists(DllPath) Then Return Nothing
            Dim Asm = Reflection.Assembly.ReflectionOnlyLoadFrom(DllPath)
            For Each Attr In Asm.GetCustomAttributesData()
                If Attr.AttributeType IsNot GetType(Reflection.AssemblyMetadataAttribute) Then Continue For
                If Attr.ConstructorArguments.Count <> 2 Then Continue For
                If String.Equals(CStr(Attr.ConstructorArguments(0).Value), Key, StringComparison.OrdinalIgnoreCase) Then
                    Return CStr(Attr.ConstructorArguments(1).Value)
                End If
            Next
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>按文件类型导入插件：.pclplugin / 含 plugin.json 的 zip / 声明了 PclPluginId 的 dll。</summary>
    Public Function ImportPluginFile(Path As String, Optional Overwrite As Boolean = False) As PluginImportResult
        Try
            If String.IsNullOrWhiteSpace(Path) Then Return ImportFail("ERR_NOT_FOUND", "没有指定文件")
            If Not File.Exists(Path) Then Return ImportFail("ERR_NOT_FOUND", "文件不存在：" & Path)
            Dim Ext = IO.Path.GetExtension(Path).ToLowerInvariant()
            If Ext = ".dll" Then Return ImportPluginDll(Path, Overwrite)
            Return ImportPluginPackage(Path, Overwrite)
        Catch ex As IO.InvalidDataException
            Return ImportFail("ERR_NOT_ARCHIVE", "这不是有效的插件包：可能不是 zip，或者文件已损坏")
        Catch ex As Exception
            Logger.Error(ex, "导入插件失败：" & Path, LogBehavior.None)
            Return ImportFail("ERR_WRITE_FAILED", ex.GetBaseException().Message)
        End Try
    End Function

    ''' <summary>导入一个插件包（.pclplugin / zip）。</summary>
    Public Function ImportPluginPackage(PackagePath As String, Optional Overwrite As Boolean = False) As PluginImportResult
        Dim TempRoot As String = Nothing
        Try
            If Not File.Exists(PackagePath) Then Return ImportFail("ERR_NOT_FOUND", "文件不存在：" & PackagePath)
            Using Archive = FileUtils.OpenZip(PackagePath)
                Dim Manifests = Archive.Entries.Where(Function(E) IsManifestEntry(E.FullName)).ToList()
                If Manifests.Count = 0 Then Return ImportFail("ERR_NO_MANIFEST", "压缩包里没有找到 plugin.json（要放在根目录或唯一一层子目录里）")
                If Manifests.Count > 1 Then Return ImportFail("ERR_AMBIGUOUS_MANIFEST", "压缩包里有多个 plugin.json：" & String.Join("、", Manifests.Select(Function(M) M.FullName)))
                Dim ManifestEntry = Manifests(0)
                Dim Prefix = ManifestEntry.FullName.Replace("\"c, "/"c)
                Prefix = Prefix.Substring(0, Prefix.Length - ManifestFileName.Length)
                '解压到临时目录（只取清单所在目录下的内容）
                TempRoot = IO.Path.Combine(PathPluginRoot, ".tmp", Guid.NewGuid().ToString("N"))
                IO.Directory.CreateDirectory(TempRoot)
                Dim RootFull = IO.Path.GetFullPath(TempRoot).TrimEnd("\"c) & "\"
                For Each Entry In Archive.Entries
                    Dim Name = Entry.FullName.Replace("\"c, "/"c)
                    If String.IsNullOrEmpty(Entry.Name) Then Continue For '文件夹
                    If Not Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim Rel = Name.Substring(Prefix.Length)
                    If Rel = "" Then Continue For
                    If Rel.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Rel.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase) OrElse Rel.EndsWith("Thumbs.db", StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim Dest = IO.Path.GetFullPath(IO.Path.Combine(TempRoot, Rel.Replace("/"c, "\"c)))
                    If Not Dest.StartsWith(RootFull, StringComparison.OrdinalIgnoreCase) Then
                        Return ImportFail("ERR_UNSAFE_PATH", "压缩包里的路径越界：" & Entry.FullName)
                    End If
                    IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Dest))
                    Using Source = Entry.Open()
                        Using Target = IO.File.Create(Dest)
                            Source.CopyTo(Target)
                        End Using
                    End Using
                Next
            End Using
            Return InstallFromFolder(TempRoot, Overwrite, PackagePath)
        Finally
            If TempRoot IsNot Nothing Then
                Try
                    If IO.Directory.Exists(TempRoot) Then IO.Directory.Delete(TempRoot, True)
                    Dim TmpParent = IO.Path.GetDirectoryName(TempRoot)
                    If IO.Directory.Exists(TmpParent) AndAlso Not IO.Directory.EnumerateFileSystemEntries(TmpParent).Any() Then IO.Directory.Delete(TmpParent)
                Catch
                    '忽略
                End Try
            End If
        End Try
    End Function

    ''' <summary>导入一个单 DLL（清单由 AssemblyMetadata 生成）。</summary>
    Public Function ImportPluginDll(DllPath As String, Optional Overwrite As Boolean = False) As PluginImportResult
        Dim TempRoot As String = Nothing
        Try
            Dim Id = ReadAssemblyMetadata(DllPath, "PclPluginId")
            If String.IsNullOrWhiteSpace(Id) Then
                Return ImportFail("ERR_NO_MANIFEST", "这个 DLL 没有声明 PclPluginId，无法当作插件安装；请改用 .pclplugin 包（内含 plugin.json）")
            End If
            TempRoot = IO.Path.Combine(PathPluginRoot, ".tmp", Guid.NewGuid().ToString("N"))
            IO.Directory.CreateDirectory(TempRoot)
            Dim DllName = IO.Path.GetFileName(DllPath)
            IO.File.Copy(DllPath, IO.Path.Combine(TempRoot, DllName), True)
            Dim Json As New JObject From {
                {"id", Id.Trim()},
                {"name", If(ReadAssemblyMetadata(DllPath, "PclPluginName"), Id)},
                {"version", If(ReadAssemblyMetadata(DllPath, "PclPluginVersion"), "0.0.0")},
                {"author", If(ReadAssemblyMetadata(DllPath, "PclPluginAuthor"), "")},
                {"description", If(ReadAssemblyMetadata(DllPath, "PclPluginDescription"), "")},
                {"dll", DllName},
                {"minApiVersion", ApiVersionUi}
            }
            Dim Perms = ReadAssemblyMetadata(DllPath, "PclPluginPermissions")
            If Not String.IsNullOrWhiteSpace(Perms) Then
                Dim Arr As New JArray
                For Each Part In Perms.Split(New Char() {","c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
                    Arr.Add(Part.Trim())
                Next
                Json("permissions") = Arr
            End If
            IO.File.WriteAllText(IO.Path.Combine(TempRoot, ManifestFileName), Json.ToString(), New Text.UTF8Encoding(False))
            Return InstallFromFolder(TempRoot, Overwrite, DllPath)
        Finally
            If TempRoot IsNot Nothing Then
                Try
                    If IO.Directory.Exists(TempRoot) Then IO.Directory.Delete(TempRoot, True)
                    Dim TmpParent = IO.Path.GetDirectoryName(TempRoot)
                    If IO.Directory.Exists(TmpParent) AndAlso Not IO.Directory.EnumerateFileSystemEntries(TmpParent).Any() Then IO.Directory.Delete(TmpParent)
                Catch
                    '忽略
                End Try
            End If
        End Try
    End Function

    ''' <summary>把已经解压好的插件目录装进 Plugins，并处理校验 / 冲突 / 回滚。</summary>
    Private Function InstallFromFolder(TempRoot As String, Overwrite As Boolean, Origin As String) As PluginImportResult
        Dim Info = ReadManifest(TempRoot)
        If Info Is Nothing Then Return ImportFail("ERR_BAD_JSON", "plugin.json 无法解析")
        Info.Id = Info.Id.Trim()
        If Not IsValidPluginId(Info.Id) Then
            Return ImportFail("ERR_BAD_ID", $"插件 id「{Info.Id}」不合法：只能用字母、数字与 - _ .，并以字母或数字开头")
        End If
        If Info.MinApiVersion > ApiVersionUi Then
            Return ImportFail("ERR_MIN_API", $"这个插件要求 API v{Info.MinApiVersion}，当前启动器是 v{ApiVersionUi}，请先更新 PCL Harness")
        End If
        'DLL 校验值
        If Not String.IsNullOrWhiteSpace(Info.DllName) Then
            Dim DllPath = IO.Path.Combine(TempRoot, Info.DllName)
            If Not File.Exists(DllPath) Then
                Return ImportFail("ERR_NO_DLL", $"清单里写了 dll「{Info.DllName}」，但包里没有这个文件")
            End If
            If Not String.IsNullOrWhiteSpace(Info.DllSha256) Then
                Dim Actual = GetFileSha256(DllPath)
                If Actual Is Nothing OrElse Not String.Equals(Actual, Info.DllSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    Return ImportFail("ERR_SHA256", "DLL 校验失败：清单里的 sha256 与实际文件不一致，包可能损坏或被改动过")
                End If
            End If
        End If
        '依赖
        Dim Missing As New List(Of String)
        Dim BadVersion As New List(Of String)
        For Each DepRaw In Info.Dependencies
            Dim DepId = DepRaw.Trim()
            Dim Constraint As String = ""
            Dim AtPos = DepId.IndexOf("@"c)
            If AtPos >= 0 Then
                Constraint = DepId.Substring(AtPos + 1).Trim()
                DepId = DepId.Substring(0, AtPos).Trim()
            End If
            If DepId = "" Then Continue For
            Dim Target = Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, DepId, StringComparison.OrdinalIgnoreCase))
            If Target Is Nothing Then
                Missing.Add(DepRaw)
            ElseIf Constraint <> "" AndAlso Not SatisfiesVersion(Target.Version, Constraint) Then
                BadVersion.Add($"{DepRaw}（已装 {Target.Version}）")
            End If
        Next
        If Missing.Count > 0 Then Return ImportFail("ERR_MISSING_DEP", "缺少依赖插件：" & String.Join("、", Missing))
        If BadVersion.Count > 0 Then Return ImportFail("ERR_MISSING_DEP", "依赖插件版本不满足：" & String.Join("、", BadVersion))

        Dim Existing = Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, Info.Id, StringComparison.OrdinalIgnoreCase))
        If Existing IsNot Nothing AndAlso Not Overwrite Then
            Return ImportFail("ERR_EXISTS", $"已存在插件「{Existing.DisplayName}」v{Existing.Version}")
        End If
        ' 运行中的插件：DLL 被 Assembly.LoadFrom 占用，直接换目录会失败；先放进 .pending，下次启动换入。
        If Existing IsNot Nothing AndAlso Existing.Loaded Then
            Return StagePendingUpdate(TempRoot, Info, Existing)
        End If
        Dim WasEnabled As Boolean = Existing IsNot Nothing AndAlso Existing.Enabled
        Dim OldFingerprint As String = If(Existing IsNot Nothing, PermissionFingerprint(Existing), "")

        Dim TargetFolder = If(Existing IsNot Nothing, Existing.Folder, IO.Path.Combine(PathPluginRoot, Info.Id))
        Dim Backup As String = Nothing
        If IO.Directory.Exists(TargetFolder) Then
            If Existing IsNot Nothing AndAlso Existing.Enabled Then SetEnabled(Existing, False)
            Try
                Backup = IO.Path.Combine(PathPluginRoot, ".backup", IO.Path.GetFileName(TargetFolder) & "-" & Date.Now.ToString("yyyyMMdd-HHmmss"))
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Backup))
                IO.Directory.Move(TargetFolder, Backup)
            Catch ex As Exception
                Return ImportFail("ERR_WRITE_FAILED", "无法替换旧插件目录（可能文件被占用）：" & ex.GetBaseException().Message)
            End Try
        End If
        Try
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(TargetFolder))
            IO.Directory.Move(TempRoot, TargetFolder)
        Catch ex As Exception
            If Backup IsNot Nothing AndAlso Not IO.Directory.Exists(TargetFolder) Then
                Try
                    IO.Directory.Move(Backup, TargetFolder)
                Catch
                    '忽略
                End Try
            End If
            Return ImportFail("ERR_WRITE_FAILED", "写入插件目录失败：" & ex.GetBaseException().Message)
        End Try
        '保留这一份作为回滚点，只剪掉更旧的备份
        If Backup IsNot Nothing Then
            Try
                Dim Backups = BackupFoldersFor(Info.Id)
                For Index = 1 To Backups.Count - 1
                    Try
                        'v7.3：旧回滚点也走回收站，误删还能找回
                        MeloongCore.DirectoryUtils.Delete(Backups(Index), True)
                    Catch
                        '忽略
                    End Try
                Next
            Catch
                '忽略
            End Try
        End If
        Refresh(True)
        '原来启用过、且权限没变化，就继续启用；权限变了让它重新走一次确认
        Dim Installed = Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, Info.Id, StringComparison.OrdinalIgnoreCase))
        If WasEnabled AndAlso Installed IsNot Nothing AndAlso PermissionFingerprint(Installed) = OldFingerprint Then
            SetEnabled(Installed, True)
        End If
        Logger.Info($"插件导入成功：{Info.DisplayName}（{Info.Id}）v{Info.Version} ← {Origin}")
        If Installed IsNot Nothing Then RaisePluginInstalled(Installed)
        Return New PluginImportResult With {
            .Success = True,
            .PluginId = Info.Id,
            .PluginName = Info.DisplayName,
            .Version = Info.Version,
            .Folder = TargetFolder,
            .Upgraded = Existing IsNot Nothing,
            .Message = "OK"
        }
    End Function

    ''' <summary>导入一批插件（带界面反馈：进度提示 + 覆盖确认 + 结果汇总），可在任意线程调用。</summary>
    Public Sub ImportPluginsWithUi(Files As IEnumerable(Of String))
        Dim List = Files.Where(Function(F) Not String.IsNullOrWhiteSpace(F)).ToList()
        If List.Count = 0 Then Return
        ModBase.RunInNewThread(
        Sub()
            ModBase.RunInUi(Sub() ModMain.Hint("正在导入插件……", HintType.Blue, False))
            Dim Lines As New List(Of String)
            Dim OkCount As Integer = 0
            Dim FailCount As Integer = 0
            For Each FilePath In List
                Dim Overwrite As Boolean = False
                Dim Result As PluginImportResult = Nothing
                Do While True
                    Result = ImportPluginFile(FilePath, Overwrite)
                    If Result.Code <> "ERR_EXISTS" Then Exit Do
                    Dim Choice As Integer = 2
                    ModBase.RunInUiWait(Sub() Choice = ModMain.MyMsgBox($"{Result.Message}{vbCrLf}{vbCrLf}要覆盖安装吗？", "插件已存在", "覆盖安装", "跳过"))
                    If Choice <> 1 Then
                        Result = ImportFail("ERR_SKIPPED", "已跳过（" & Result.Message & "）")
                        Exit Do
                    End If
                    Overwrite = True
                Loop
                If Result.Success Then OkCount += 1 Else FailCount += 1
                Lines.Add(If(Result.Success, "· " & Result.Display, "· " & IO.Path.GetFileName(FilePath) & " " & Result.Display))
            Next
            ModBase.RunInUi(
            Sub()
                If ModMain.FrmPlugin IsNot Nothing Then ModMain.FrmPlugin.RefreshAfterImport()
                If FailCount = 0 Then
                    ModMain.Hint($"已导入 {OkCount} 个插件，到插件页启用后生效", HintType.Green, True)
                ElseIf OkCount = 0 Then
                    ModMain.Hint($"插件导入失败：{FailCount} 个", HintType.Red, True)
                Else
                    ModMain.Hint($"插件导入完成：成功 {OkCount} 个，失败 {FailCount} 个", HintType.Blue, True)
                End If
                ModMain.MyMsgBox(String.Join(vbCrLf, Lines), "导入插件", "确定")
            End Sub)
        End Sub, "导入插件")
    End Sub

#End Region

    ' ================= Host.Plugins（v6.4） =================

    ''' <summary>暴露给其他插件的插件摘要（只读快照，避免把内部对象暴露出去）。</summary>
    Public Class PluginSummary
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property Version As String = ""
        Public Property Author As String = ""
        Public Property Description As String = ""
        Public Property Enabled As Boolean
        Public Property Loaded As Boolean
        Public Property MinApiVersion As Integer
        Public Property UpdateUrl As String = ""
        Public Property SignatureStatus As PluginSignatureStatus = PluginSignatureStatus.Unsigned
        Public Property HasBackup As Boolean
        Public ReadOnly Property Permissions As New List(Of String)

        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace(Name), Id, Name)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 宿主提供给插件的「管理其他插件」接口，需要 plugins 权限。
    ''' </summary>
    Public Class PluginHostPlugins
        Friend ReadOnly Owner As PluginInfo

        Friend Sub New(Owner As PluginInfo)
            Me.Owner = Owner
        End Sub

        Private Sub EnsureAllowed()
            If Owner Is Nothing Then Return
            'v7.5（安全审计 3.8 R1）：不再对「空权限」放行——没声明 plugins 的插件一律不能管理其他插件
            Owner.EnsurePermission("plugins")
        End Sub

        Private Function ToSummary(Info As PluginInfo) As PluginSummary
            If Info Is Nothing Then Return Nothing
            Dim Result As New PluginSummary With {
                .Id = Info.Id,
                .Name = Info.Name,
                .Version = Info.Version,
                .Author = Info.Author,
                .Description = Info.Description,
                .Enabled = Info.Enabled,
                .Loaded = Info.Loaded,
                .MinApiVersion = Info.MinApiVersion,
                .UpdateUrl = Info.UpdateUrl,
                .SignatureStatus = Info.SignatureStatus,
                .HasBackup = HasBackup(Info.Id)
            }
            For Each Permission In Info.Permissions
                Result.Permissions.Add(Permission)
            Next
            Return Result
        End Function

        ''' <summary>列出当前已发现的所有插件。</summary>
        Public Function List() As List(Of PluginSummary)
            EnsureAllowed()
            Dim Result As New List(Of PluginSummary)
            SyncLock Plugins
                For Each Info In Plugins.ToList()
                    Dim One = ToSummary(Info)
                    If One IsNot Nothing Then Result.Add(One)
                Next
            End SyncLock
            Return Result
        End Function

        ''' <summary>按 id 查找插件，找不到返回 Nothing。</summary>
        Public Function Find(Id As String) As PluginSummary
            EnsureAllowed()
            Return ToSummary(FindPluginById(Id))
        End Function

        ''' <summary>导入一个插件包（.pclplugin / .zip / .dll），走与界面一致的安全管线。</summary>
        Public Function ImportPackage(Path As String, Optional Overwrite As Boolean = True) As PluginImportResult
            EnsureAllowed()
            Return ImportPluginFile(Path, Overwrite)
        End Function

        ''' <summary>启用或停用插件，成功返回 True。</summary>
        Public Function SetEnabled(Id As String, Value As Boolean) As Boolean
            EnsureAllowed()
            Dim Info = FindPluginById(Id)
            If Info Is Nothing Then Return False
            If Value AndAlso HighestPermissionRisk(Info) >= 1 AndAlso Not IsTrusted(Info) Then
                'v7.5（安全审计 3.8 R2）：插件不能替用户静默启用「声明了中/高风险权限」的插件，必须由用户在插件页确认
                Owner.AddEvent("拒绝启用 " & Info.Id & "：目标插件声明了中/高风险权限，需要用户在插件页亲自确认")
                Return False
            End If
            ModPlugin.SetEnabled(Info, Value)
            Return True
        End Function

        ''' <summary>卸载插件（移入 Plugins\.trash），成功返回 True。</summary>
        ''' <summary>插件声明的更新地址（清单 update 字段），没声明返回空字符串。</summary>
        Public Function UpdateUrl(Id As String) As String
            EnsureAllowed()
            Dim Info = FindPluginById(Id)
            Return If(Info Is Nothing, "", Info.UpdateUrl)
        End Function

        ''' <summary>是否有可回滚的上一版备份。</summary>
        Public Function CanRollback(Id As String) As Boolean
            EnsureAllowed()
            Return HasBackup(Id)
        End Function

        ''' <summary>回滚到上一版（重启启动器后生效），成功返回 True。</summary>
        Public Function Rollback(Id As String) As Boolean
            EnsureAllowed()
            If Owner IsNot Nothing AndAlso String.Equals(Id, Owner.Id, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("插件不能回滚自己")
            End If
            Return RollbackPlugin(Id)
        End Function

        ''' <summary>列出本机已授权给该插件的主机（net.dynamic）。</summary>
        Public Function ApprovedHosts(Id As String) As List(Of String)
            EnsureAllowed()
            Return ListApprovedHosts(Id)
        End Function

        Public Function Uninstall(Id As String) As Boolean
            EnsureAllowed()
            Dim Info = FindPluginById(Id)
            If Info Is Nothing Then Return False
            If Owner IsNot Nothing AndAlso String.Equals(Info.Id, Owner.Id, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("插件不能卸载自己")
            End If
            Return UninstallPlugin(Id)
        End Function
    End Class

End Module

''' <summary>
''' 可选的 UI 扩展接口（v2）。插件在实现 IPclPlugin 的同时实现本接口，
''' 即可向启动器注册自定义页面、标题栏入口和设置项。
''' </summary>
Public Interface IPclPluginUi
    ''' <summary>注册自定义页面；返回 Nothing 或空集合表示不注册。</summary>
    Function GetPages() As IEnumerable(Of PluginPageInfo)
    ''' <summary>注册标题栏入口（最多建议 3 个）；返回 Nothing 表示不注册。</summary>
    Function GetNavItems() As IEnumerable(Of PluginNavItem)
    ''' <summary>注册设置项；返回 Nothing 表示不注册。</summary>
    Function GetSettings() As IEnumerable(Of PluginSetting)
End Interface

''' <summary>
''' 插件实现必须继承的接口。
''' </summary>
Public Interface IPclPlugin
    ''' <summary>插件被启用时调用。</summary>
    Sub OnLoad(Host As PluginHost)
    ''' <summary>插件被禁用或程序退出时调用。</summary>
    Sub OnUnload()
End Interface

''' <summary>
''' 可选的事件接口（v3）。插件在实现 IPclPlugin 的同时实现本接口，
''' 即可收到启动器就绪、页面切换、游戏启动前后的事件。所有方法都实现了空体即可，可以只关心其中一个。
''' </summary>
Public Interface IPclPluginEvents
    ''' <summary>启动器初始化完成、插件已全部加载时调用一次。</summary>
    Sub OnLauncherReady()
    ''' <summary>主页面切换完成时调用。Page 对应 FormMain.PageType 的数值。</summary>
    Sub OnPageChanged(Page As Integer, SubType As Integer)
    ''' <summary>构建启动参数时调用；可以往 Info.ExtraJvmArgs / ExtraGameArgs 追加参数。</summary>
    Sub OnGameLaunching(Info As PluginLaunchInfo)
    ''' <summary>游戏进程刚启动后调用，此时 Info.ProcessId 可用。</summary>
    Sub OnGameLaunched(Info As PluginLaunchInfo)
End Interface

''' <summary>
''' 主页卡片（v4）：插件可以把一个自定义控件直接放到“启动”页或“设置”页的卡片流里。
''' </summary>
Public Class PluginCardInfo
    ''' <summary>唯一标识（同一插件内不重复即可）。</summary>
    Public Id As String = ""
    ''' <summary>卡片标题。</summary>
    Public Title As String = ""
    ''' <summary>一句话说明，可空。</summary>
    Public Description As String = ""
    ''' <summary>创建卡片内容的委托，返回一个 WPF 控件（Border/StackPanel 等）。启动器会缓存它，多次切换页面复用同一个控件。</summary>
    Public Factory As Func(Of Object) = Nothing
    ''' <summary>所属插件，由启动器填写。</summary>
    Public Owner As Object = Nothing
    Public ReadOnly Property DisplayTitle As String
        Get
            If String.IsNullOrWhiteSpace(Title) Then Return If(String.IsNullOrWhiteSpace(Id), "插件卡片", Id)
            Return Title
        End Get
    End Property
End Class

''' <summary>
''' 状态栏小组件（v4）：显示在窗口底部右侧的一小段文字，刷新频率由启动器控制（约 1 秒）。
''' </summary>
Public Class PluginStatusWidget
    Public Id As String = ""
    ''' <summary>返回要显示的文字；返回空字符串则不显示。</summary>
    Public TextProvider As Func(Of String) = Nothing
    ''' <summary>点击时的动作，可空。</summary>
    Public OnClick As Action = Nothing
    Public Owner As Object = Nothing
End Class

''' <summary>
''' 生命周期事件接口（v4）。所有方法都需要实现，空体即可。
''' </summary>
Public Interface IPclPluginLifecycle
    ''' <summary>游戏进程退出后调用；ExitCode 为进程退出码（无法获取时为 -1）。</summary>
    Sub OnGameExited(Info As PluginLaunchInfo, ExitCode As Integer)
    ''' <summary>启动器切换了当前版本时调用；InstanceName 为新版本名，未选择时为空字符串。</summary>
    Sub OnInstanceChanged(InstanceName As String)
    ''' <summary>主题在深色/浅色之间切换后调用。</summary>
    Sub OnThemeChanged(IsDark As Boolean)
End Interface

''' <summary>
''' 联机事件接口（v4）。所有方法都需要实现，空体即可。
''' </summary>
Public Interface IPclPluginLink
    ''' <summary>成功创建房间后调用，Invite 为邀请码文本。</summary>
    Sub OnLinkRoomCreated(Invite As String)
    ''' <summary>成功加入房间后调用。</summary>
    Sub OnLinkRoomJoined()
    ''' <summary>离开房间或连接断开后调用。</summary>
    Sub OnLinkRoomClosed()
End Interface

''' <summary>
''' 下载事件接口（v4）。所有方法都需要实现，空体即可。
''' </summary>
Public Interface IPclPluginDownload
    ''' <summary>一个下载任务开始（Target 为下载目标描述，例如工程名）。</summary>
    Sub OnDownloadStarted(Target As String)
    ''' <summary>一个下载任务结束；Success 表示是否全部成功。</summary>
    Sub OnDownloadFinished(Target As String, Success As Boolean)
End Interface

''' <summary>
''' 关闭拦截接口（v4）：返回 True 表示取消本次关闭。
''' </summary>
Public Interface IPclPluginClosing
    Function OnClosing() As Boolean
End Interface

''' <summary>
''' 主页/设置页卡片与状态栏小组件（v4）。实现本接口的插件可以在 IPclPluginUi 之外再注册内嵌 UI。
''' </summary>
Public Interface IPclPluginUi2
    ''' <summary>注册“启动”页卡片；返回 Nothing 表示不注册。</summary>
    Function GetHomeCards() As IEnumerable(Of PluginCardInfo)
    ''' <summary>注册“设置”页卡片；返回 Nothing 表示不注册。</summary>
    Function GetSettingsCards() As IEnumerable(Of PluginCardInfo)
    ''' <summary>注册状态栏小组件；返回 Nothing 表示不注册。</summary>
    Function GetStatusWidgets() As IEnumerable(Of PluginStatusWidget)
End Interface

''' <summary>
''' API v6：插件基类。继承它就不用把 7 个接口的方法全部实现一遍，
''' 只 Overrides 自己关心的事件即可（例如 Public Overrides Sub OnLauncherReady()）。
''' 基类已经实现 IPclPlugin / IPclPluginEvents / IPclPluginLifecycle / IPclPluginLink / IPclPluginDownload / IPclPluginClosing。
''' </summary>
Public MustInherit Class PclPluginBase
    Implements IPclPlugin
    Implements IPclPluginEvents
    Implements IPclPluginLifecycle
    Implements IPclPluginLink
    Implements IPclPluginDownload
    Implements IPclPluginClosing

    ''' <summary>宿主对象，OnLoad 之后可用。</summary>
    Public Property Host As PluginHost

    ''' <summary>插件自己的数据目录（会自动创建）。</summary>
    Public ReadOnly Property DataFolder As String
        Get
            Return If(Host?.DataFolder, "")
        End Get
    End Property

    ''' <summary>写一条开发日志（会显示在插件页的开发模式控制台里）。</summary>
    Public Sub DevLog(Message As String)
        PluginDevLog.Info(If(Host?.Info?.DisplayName, "插件"), If(Message, ""))
    End Sub

    ''' <summary>弹出左下角提示。</summary>
    Public Sub Hint(Message As String, Optional Type As HintType = HintType.Blue)
        Host?.Hint(Message, Type)
    End Sub

    Public Overridable Sub OnLoad(Host As PluginHost) Implements IPclPlugin.OnLoad
        Me.Host = Host
    End Sub

    Public Overridable Sub OnUnload() Implements IPclPlugin.OnUnload
    End Sub

    Public Overridable Sub OnLauncherReady() Implements IPclPluginEvents.OnLauncherReady
    End Sub

    Public Overridable Sub OnPageChanged(Page As Integer, SubType As Integer) Implements IPclPluginEvents.OnPageChanged
    End Sub

    Public Overridable Sub OnGameLaunching(Info As PluginLaunchInfo) Implements IPclPluginEvents.OnGameLaunching
    End Sub

    Public Overridable Sub OnGameLaunched(Info As PluginLaunchInfo) Implements IPclPluginEvents.OnGameLaunched
    End Sub

    Public Overridable Sub OnGameExited(Info As PluginLaunchInfo, ExitCode As Integer) Implements IPclPluginLifecycle.OnGameExited
    End Sub

    Public Overridable Sub OnInstanceChanged(InstanceName As String) Implements IPclPluginLifecycle.OnInstanceChanged
    End Sub

    Public Overridable Sub OnThemeChanged(IsDark As Boolean) Implements IPclPluginLifecycle.OnThemeChanged
    End Sub

    Public Overridable Sub OnLinkRoomCreated(Invite As String) Implements IPclPluginLink.OnLinkRoomCreated
    End Sub

    Public Overridable Sub OnLinkRoomJoined() Implements IPclPluginLink.OnLinkRoomJoined
    End Sub

    Public Overridable Sub OnLinkRoomClosed() Implements IPclPluginLink.OnLinkRoomClosed
    End Sub

    Public Overridable Sub OnDownloadStarted(Target As String) Implements IPclPluginDownload.OnDownloadStarted
    End Sub

    Public Overridable Sub OnDownloadFinished(Target As String, Success As Boolean) Implements IPclPluginDownload.OnDownloadFinished
    End Sub

    Public Overridable Function OnClosing() As Boolean Implements IPclPluginClosing.OnClosing
        Return False
    End Function
End Class

''' <summary>
''' 当前游戏实例的信息快照（API v7，需要 instance.read 权限，只读）。
''' </summary>
Public Class PluginInstanceSnapshot
    ''' <summary>是否选中了某个实例；为 False 时其它字段都不可信。</summary>
    Public Property Available As Boolean
    ''' <summary>实例名（通常是版本文件夹名）。</summary>
    Public Property Name As String = ""
    ''' <summary>版本 id，例如 1.21.5-Fabric 0.16.14。</summary>
    Public Property VersionId As String = ""
    ''' <summary>继承的父版本名（没有则为空）。</summary>
    Public Property InheritName As String = ""
    ''' <summary>版本目录（以 \ 结尾）。</summary>
    Public Property PathVersion As String = ""
    ''' <summary>版本隔离目录（以 \ 结尾）。</summary>
    Public Property PathIndie As String = ""
    ''' <summary>该实例是否支持安装 Mod。</summary>
    Public Property Modable As Boolean
    ''' <summary>实例的发布日期。</summary>
    Public Property ReleaseTime As Date = Date.MinValue
End Class

''' <summary>
''' 一个已加载的游戏实例（API v7.3，需要 instance.read 权限，只读）。只给名字，不提供绝对路径。
''' </summary>
Public Class PluginInstanceEntry
    ''' <summary>实例名（通常是版本文件夹名）。</summary>
    Public Property Name As String = ""
    ''' <summary>所属 .minecraft 文件夹的名字（不是完整路径）。</summary>
    Public Property Folder As String = ""
    ''' <summary>版本 id，例如 1.21.5-Fabric 0.16.14。</summary>
    Public Property VersionId As String = ""
    ''' <summary>继承的父版本名（没有则为空）。</summary>
    Public Property InheritName As String = ""
    ''' <summary>该实例是否支持安装 Mod。</summary>
    Public Property Modable As Boolean
    ''' <summary>是否是当前选中的实例。</summary>
    Public Property IsSelected As Boolean
    ''' <summary>实例的发布日期。</summary>
    Public Property ReleaseTime As Date = Date.MinValue
End Class

''' <summary>
''' 当前实例里的一个内容文件（模组 / 资源包 / 光影，API v7.3，需要 mods.read 权限，只读）。
''' </summary>
Public Class PluginContentEntry
    ''' <summary>文件名（不含 .disabled / .old 后缀），可以直接传给 SetModEnabled。</summary>
    Public Property FileName As String = ""
    ''' <summary>显示名（去掉了 .jar / .zip 扩展名）。</summary>
    Public Property Name As String = ""
    ''' <summary>类型：mods / resourcepacks / shaderpacks。</summary>
    Public Property Kind As String = "mods"
    ''' <summary>当前是否启用。</summary>
    Public Property Enabled As Boolean
    ''' <summary>文件大小（字节）。</summary>
    Public Property Size As Long
End Class
''' <summary>
''' 当前账号的信息快照（API v7，需要 account 权限，只读）。
''' </summary>
Public Class PluginAccountSnapshot
    ''' <summary>是否已登录。</summary>
    Public Property Available As Boolean
    ''' <summary>玩家名。</summary>
    Public Property Name As String = ""
    ''' <summary>UUID（只有微软登录才有，其它登录方式为空）。</summary>
    Public Property Uuid As String = ""
    ''' <summary>登录方式：Ms / Legacy / Nide / Auth / None。</summary>
    Public Property LoginType As String = "None"
End Class

''' <summary>
''' 一个游戏日志文件（API v7，需要 logs.read 权限，只读）。
''' </summary>
Public Class PluginLogFile
    ''' <summary>相对实例目录的路径，例如 logs\latest.log。</summary>
    Public Property Path As String = ""
    ''' <summary>文件名。</summary>
    Public Property Name As String = ""
    ''' <summary>最后写入时间。</summary>
    Public Property ModifiedAt As Date = Date.MinValue
    ''' <summary>字节数。</summary>
    Public Property Size As Long
End Class

''' <summary>
''' 提供给插件的宿主对象。
''' </summary>
Public Class PluginHost

    Public ReadOnly Info As ModPlugin.PluginInfo

    ''' <summary>API v6：插件间服务注册表（也可以直接用模块级的 ModPlugin.PluginServices）。</summary>
    Public ReadOnly Services As PluginServiceAccessor

    ''' <summary>API v6：后台任务（会出现在「任务管理」里，可显示进度、可取消）。</summary>
    Public ReadOnly Tasks As PluginTaskManager

    ''' <summary>API v6.4：管理其他插件（列出/导入/启停/卸载），需要 plugins 权限。</summary>
    Public ReadOnly Plugins As ModPlugin.PluginHostPlugins

    Friend Sub New(Info As ModPlugin.PluginInfo)
        Me.Info = Info
        Me.Services = New PluginServiceAccessor(If(Info.Id, Info.DisplayName))
        Me.Tasks = New PluginTaskManager(If(Info.Id, Info.DisplayName))
        Me.Plugins = New ModPlugin.PluginHostPlugins(Info)
        Me.Events.OwnerInfo = Info
    End Sub

    ''' <summary>API v6：在开发日志里记一条信息（会显示在插件页的开发模式控制台）。</summary>
    Public Sub DevLog(Message As String)
        PluginDevLog.Info(Info.DisplayName, If(Message, ""))
    End Sub

    ''' <summary>插件自己的数据目录（会自动创建）。</summary>
    Public ReadOnly Property DataFolder As String
        Get
            Dim Folder = Path.Combine(Info.Folder, "data")
            Directory.CreateDirectory(Folder)
            Return Folder
        End Get
    End Property

    ''' <summary>写入启动器日志。</summary>
    Public Sub Log(Message As String)
        Logger.Info("[插件 " & Info.DisplayName & "] " & If(Message, ""))
    End Sub

    ''' <summary>左下角弹出提示。</summary>
    Public Sub Hint(Message As String, Optional Type As HintType = HintType.Blue)
        ModMain.Hint("[" & Info.DisplayName & "] " & If(Message, ""), Type, True)
    End Sub

    ''' <summary>弹出对话框。</summary>
    Public Sub Message(Title As String, Text As String)
        MyMsgBox(Text, Title)
    End Sub

    ''' <summary>用默认浏览器打开网址。</summary>
    Public Sub OpenUrl(Url As String)
        ModBase.OpenWebsite(Url)
    End Sub

    ''' <summary>在资源管理器中打开插件目录。</summary>
    Public Sub OpenFolder()
        ModPlugin.OpenFolder()
    End Sub

    ''' <summary>当前选中的游戏版本名（未选择时返回空字符串）。</summary>
    Public ReadOnly Property InstanceName As String
        Get
            If McInstanceSelected Is Nothing Then Return ""
            Return If(McInstanceSelected.Name, "")
        End Get
    End Property

    ''' <summary>当前版本隔离目录（未选择版本时返回空字符串）。</summary>
    Public ReadOnly Property InstanceFolder As String
        Get
            If McInstanceSelected Is Nothing Then Return ""
            Return If(McInstanceSelected.PathIndie, "")
        End Get
    End Property

    ''' <summary>当前登录的玩家名（未登录时返回空字符串）。</summary>
    Public ReadOnly Property PlayerName As String
        Get
            Return If(McLoginName(), "")
        End Get
    End Property

    ''' <summary>跳到启动器的主页面。Page 对应 FormMain.PageType 的数值，例如 1 = PageType.Launch。</summary>
    Public Sub Navigate(Page As Integer)
        ModBase.RunInUi(Sub()
                            If ModMain.FrmMain Is Nothing Then Return
                            ModMain.FrmMain.PageChange(New FormMain.PageStackData With {.Page = CType(Page, FormMain.PageType)})
                        End Sub)
    End Sub

    ' ================= v7 新增能力（都需要在清单里显式声明对应权限） =================

    ''' <summary>API v7：当前实例的信息快照（需要 instance.read，只读）。没有权限会抛异常。</summary>
    Public ReadOnly Property InstanceInfo As PluginInstanceSnapshot
        Get
            Info.EnsurePermission("instance.read")
            Dim Snap As New PluginInstanceSnapshot
            Try
                Dim Inst = McInstanceSelected
                If Inst Is Nothing Then Return Snap
                Snap.Available = True
                Snap.Name = If(Inst.Name, "")
                Snap.PathVersion = If(Inst.PathVersion, "")
                Snap.PathIndie = If(Inst.PathIndie, "")
                Snap.InheritName = If(Inst.InheritName, "")
                Snap.Modable = Inst.Modable
                Snap.ReleaseTime = Inst.ReleaseTime
                Try
                    Dim Json = Inst.JsonObject
                    If Json IsNot Nothing AndAlso Json("id") IsNot Nothing Then Snap.VersionId = Json("id").ToString()
                Catch ex As Exception
                    Logger.Warn(ex, "读取实例版本 id 失败")
                End Try
            Catch ex As Exception
                Info.AddEvent("读取实例信息失败：" & ex.Message)
            End Try
            Return Snap
        End Get
    End Property

    ''' <summary>
    ''' API v7.3：列出所有 .minecraft 文件夹里已加载的游戏实例（需要 instance.read，只读）。
    ''' 只返回名字与版本信息，不含绝对路径。
    ''' </summary>
    Public Function ListInstances() As List(Of PluginInstanceEntry)
        Info.EnsurePermission("instance.read")
        Dim Result As New List(Of PluginInstanceEntry)
        Try
            Dim Selected = McInstanceSelected
            Dim SelectedPath As String = If(Selected Is Nothing, "", If(Selected.PathVersion, ""))
            For Each Folder In McFolderList.ToArray()
                Try
                    Dim VersionsDir = IO.Path.Combine(Folder.Location, "versions")
                    If Not IO.Directory.Exists(VersionsDir) Then Continue For
                    For Each VersionDir In IO.Directory.GetDirectories(VersionsDir)
                        Dim FolderName = IO.Path.GetFileName(VersionDir)
                        If FolderName.StartsWithF(".") Then Continue For
                        Try
                            Dim Inst As New McInstance(VersionDir)
                            If Inst Is Nothing Then Continue For
                            Result.Add(New PluginInstanceEntry With {
                                .Name = If(Inst.Name, FolderName),
                                .Folder = If(Folder.Name, ""),
                                .VersionId = TryGetVersionId(Inst),
                                .InheritName = If(Inst.InheritName, ""),
                                .Modable = Inst.Modable,
                                .IsSelected = SelectedPath <> "" AndAlso String.Equals(If(Inst.PathVersion, ""), SelectedPath, StringComparison.OrdinalIgnoreCase),
                                .ReleaseTime = Inst.ReleaseTime})
                        Catch ex As Exception
                            Logger.Warn(ex, "枚举实例失败：" & VersionDir)
                        End Try
                    Next
                Catch ex As Exception
                    Logger.Warn(ex, "枚举实例文件夹失败：" & If(Folder.Location, ""))
                End Try
            Next
        Catch ex As Exception
            Info.AddEvent("枚举实例失败：" & ex.Message)
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' API v7.3：当前选中的实例（需要 instance.read，只读）。没有选中时返回 Nothing。
    ''' </summary>
    Public Function SelectedInstance() As PluginInstanceEntry
        Info.EnsurePermission("instance.read")
        Dim Inst = McInstanceSelected
        If Inst Is Nothing Then Return Nothing
        Dim FolderName As String = ""
        Try
            Dim VersionPath As String = If(Inst.PathVersion, "")
            For Each Folder In McFolderList
                If String.IsNullOrEmpty(Folder.Location) Then Continue For
                Dim Prefix = IO.Path.Combine(Folder.Location, "versions") & "\"
                If VersionPath.StartsWithF(Prefix, True) Then
                    FolderName = If(Folder.Name, "")
                    Exit For
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "匹配实例所属文件夹失败")
        End Try
        Return New PluginInstanceEntry With {
            .Name = If(Inst.Name, ""),
            .Folder = FolderName,
            .VersionId = TryGetVersionId(Inst),
            .InheritName = If(Inst.InheritName, ""),
            .Modable = Inst.Modable,
            .IsSelected = True,
            .ReleaseTime = Inst.ReleaseTime}
    End Function

    ''' <summary>
    ''' API v7.3：把当前选中的实例切换成指定名字的实例（需要 instance.select）。
    ''' 找不到、正在被修改或校验未通过时返回 False。
    ''' </summary>
    Public Function SelectInstance(InstanceName As String) As Boolean
        Info.EnsurePermission("instance.select")
        Dim Key = If(InstanceName, "").Trim()
        If Key = "" Then Return False
        Dim Target As McInstance = Nothing
        Try
            For Each Folder In McFolderList.ToArray()
                Dim VersionDir = IO.Path.Combine(Folder.Location, "versions", Key)
                If Not IO.Directory.Exists(VersionDir) Then Continue For
                Target = New McInstance(VersionDir)
                Exit For
            Next
        Catch ex As Exception
            Info.AddEvent("查找实例失败：" & ex.Message)
            Return False
        End Try
        If Target Is Nothing Then
            Info.AddEvent($"切换实例失败：找不到 {Key}")
            Return False
        End If
        Try
            If McInstanceLock.IsLocked(Target) Then
                Info.AddEvent($"切换实例失败：{Key} 正在被修改")
                Return False
            End If
            If Not Target.Check() Then
                Info.AddEvent($"切换实例失败：{Key} 校验未通过")
                Return False
            End If
        Catch ex As Exception
            Info.AddEvent("切换实例失败：" & ex.Message)
            Return False
        End Try
        Try
            Dim Ok As Boolean = False
            ModBase.RunInUiWait(Sub()
                                    McInstanceSelected = Target
                                    Ok = True
                                End Sub)
            If Ok Then
                ModMain.Hint($"插件已切换实例：{If(Target.Name, Key)}", HintType.Blue)
                Info.AddEvent($"已切换实例：{If(Target.Name, Key)}")
            End If
            Return Ok
        Catch ex As Exception
            Info.AddEvent("切换实例失败：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' API v7.3：列出当前实例里的一类内容文件（需要 mods.read，只读）。
    ''' Kind 支持 mods / resourcepacks / shaderpacks。
    ''' </summary>
    Public Function ListContents(Optional Kind As String = "mods", Optional InstanceName As String = "") As List(Of PluginContentEntry)
        Info.EnsurePermission("mods.read")
        Dim Result As New List(Of PluginContentEntry)
        Dim Normalized = NormalizeContentKind(Kind, "mods")
        If Normalized = "" Then
            Info.AddEvent("读取内容列表失败：未知类型 " & If(Kind, ""))
            Return Result
        End If
        Dim Dir = ResolveContentDir(Normalized, InstanceName)
        If String.IsNullOrEmpty(Dir) Then Return Result
        Dim Suffix = ContentFileSuffix(Normalized)
        Try
            For Each FilePath In IO.Directory.GetFiles(Dir)
                Dim FileName = IO.Path.GetFileName(FilePath)
                Dim Base = FileName
                Dim Enabled As Boolean = True
                If Base.EndsWithF(".disabled", True) Then
                    Base = Base.Substring(0, Base.Length - 9)
                    Enabled = False
                ElseIf Base.EndsWithF(".old", True) Then
                    Base = Base.Substring(0, Base.Length - 4)
                    Enabled = False
                End If
                If Not Base.EndsWithF(Suffix, True) Then Continue For
                Try
                    Result.Add(New PluginContentEntry With {
                        .FileName = Base,
                        .Name = IO.Path.GetFileNameWithoutExtension(Base),
                        .Kind = Normalized,
                        .Enabled = Enabled,
                        .Size = New IO.FileInfo(FilePath).Length})
                Catch ex As Exception
                    Logger.Warn(ex, "读取内容文件失败：" & FilePath)
                End Try
            Next
        Catch ex As Exception
            Info.AddEvent("读取内容列表失败：" & ex.Message)
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' API v7.3：启用或禁用当前实例里的一类内容文件（需要 mods.write）。
    ''' 禁用即把文件改名成 .disabled；已存在同名禁用副本时不覆盖，直接返回 False。
    ''' </summary>
    Public Function SetContentEnabled(FileName As String, Enabled As Boolean, Optional Kind As String = "mods", Optional InstanceName As String = "") As Boolean
        Info.EnsurePermission("mods.write")
        Dim Normalized = NormalizeContentKind(Kind, "mods")
        If Normalized = "" Then Return False
        Dim Base = IO.Path.GetFileName(If(FileName, "").Trim())
        If Base = "" Then Return False
        If Base.EndsWithF(".disabled", True) Then Base = Base.Substring(0, Base.Length - 9)
        If Base.EndsWithF(".old", True) Then Base = Base.Substring(0, Base.Length - 4)
        Dim Dir = ResolveContentDir(Normalized, InstanceName)
        If String.IsNullOrEmpty(Dir) Then Return False
        Dim Active = IO.Path.Combine(Dir, Base)
        Dim Disabled = Active & ".disabled"
        Dim Backup = Active & ".old"
        Try
            If Enabled Then
                If IO.File.Exists(Active) Then Return True
                Dim Source = If(IO.File.Exists(Disabled), Disabled, If(IO.File.Exists(Backup), Backup, ""))
                If Source = "" Then
                    Info.AddEvent($"启用失败：找不到 {Base}")
                    Return False
                End If
                IO.File.Move(Source, Active)
            Else
                If Not IO.File.Exists(Active) Then Return True
                If IO.File.Exists(Disabled) OrElse IO.File.Exists(Backup) Then
                    Info.AddEvent($"禁用失败：{Base} 已存在禁用副本，请先在启动器里处理冲突")
                    Return False
                End If
                IO.File.Move(Active, Disabled)
            End If
            Info.AddEvent($"{If(Enabled, "已启用", "已禁用")}：{Normalized}/{Base}")
            Return True
        Catch ex As Exception
            Info.AddEvent("修改内容启用状态失败：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>API v7.3：列出模组（mods.read）。</summary>
    Public Function ListMods(Optional InstanceName As String = "") As List(Of PluginContentEntry)
        Return ListContents("mods", InstanceName)
    End Function

    ''' <summary>API v7.3：启用或禁用模组（mods.write）。</summary>
    Public Function SetModEnabled(FileName As String, Enabled As Boolean, Optional InstanceName As String = "") As Boolean
        Return SetContentEnabled(FileName, Enabled, "mods", InstanceName)
    End Function

    ''' <summary>API v7.3：列出资源包（mods.read）。</summary>
    Public Function ListResourcePacks(Optional InstanceName As String = "") As List(Of PluginContentEntry)
        Return ListContents("resourcepacks", InstanceName)
    End Function

    ''' <summary>API v7.3：启用或禁用资源包（mods.write）。</summary>
    Public Function SetResourcePackEnabled(FileName As String, Enabled As Boolean, Optional InstanceName As String = "") As Boolean
        Return SetContentEnabled(FileName, Enabled, "resourcepacks", InstanceName)
    End Function

    ''' <summary>API v7.3：列出光影包（mods.read）。</summary>
    Public Function ListShaderPacks(Optional InstanceName As String = "") As List(Of PluginContentEntry)
        Return ListContents("shaderpacks", InstanceName)
    End Function

    ''' <summary>API v7.3：启用或禁用光影包（mods.write）。</summary>
    Public Function SetShaderPackEnabled(FileName As String, Enabled As Boolean, Optional InstanceName As String = "") As Boolean
        Return SetContentEnabled(FileName, Enabled, "shaderpacks", InstanceName)
    End Function

    Private Function TryGetVersionId(Inst As McInstance) As String
        Try
            Dim Json = Inst.JsonObject
            If Json IsNot Nothing AndAlso Json("id") IsNot Nothing Then Return Json("id").ToString()
        Catch ex As Exception
            Logger.Warn(ex, "读取实例版本 id 失败")
        End Try
        Return ""
    End Function

    Private Function NormalizeContentKind(Kind As String, DefaultKind As String) As String
        Dim Name = If(Kind, "").Trim().ToLowerInvariant()
        If Name = "" Then Name = DefaultKind
        Select Case Name
            Case "mods", "resourcepacks", "shaderpacks"
                Return Name
            Case Else
                Return ""
        End Select
    End Function

    Private Function ContentFileSuffix(Kind As String) As String
        If Kind = "mods" Then Return ".jar"
        Return ".zip"
    End Function

    Private Function ResolveInstanceRoot(InstanceName As String) As String
        Dim Key = If(InstanceName, "").Trim()
        If Key = "" Then
            Dim Inst = McInstanceSelected
            If Inst Is Nothing Then Return ""
            Return If(Inst.PathIndie, "")
        End If
        Try
            For Each Folder In McFolderList.ToArray()
                Dim VersionDir = IO.Path.Combine(Folder.Location, "versions", Key)
                If Not IO.Directory.Exists(VersionDir) Then Continue For
                Dim Inst As New McInstance(VersionDir)
                If Inst IsNot Nothing Then Return If(Inst.PathIndie, "")
            Next
        Catch ex As Exception
            Logger.Warn(ex, "查找实例目录失败：" & Key)
        End Try
        Return ""
    End Function

    Private Function ResolveContentDir(Kind As String, InstanceName As String) As String
        Dim Root = ResolveInstanceRoot(InstanceName)
        If String.IsNullOrEmpty(Root) Then
            Info.AddEvent("读取内容目录失败：没有可用的实例")
            Return ""
        End If
        Dim Dir = IO.Path.Combine(Root, Kind)
        Try
            If Not IO.Directory.Exists(Dir) Then IO.Directory.CreateDirectory(Dir)
        Catch ex As Exception
            Logger.Warn(ex, "创建内容目录失败：" & Dir)
            Return ""
        End Try
        Return Dir
    End Function
    ''' <summary>API v7：当前账号的信息快照（需要 account，只读）。没有权限会抛异常。</summary>
    Public ReadOnly Property AccountInfo As PluginAccountSnapshot
        Get
            Info.EnsurePermission("account")
            Dim Snap As New PluginAccountSnapshot
            Try
                Dim Name As String = If(McLoginName(), "")
                If String.IsNullOrWhiteSpace(Name) Then Return Snap
                Snap.Available = True
                Snap.Name = Name
                Dim TypeName As String = "None"
                Try
                    TypeName = Settings.GetSafe("LoginType").ToString()
                Catch ex As Exception
                    Logger.Warn(ex, "读取登录方式失败")
                End Try
                Snap.LoginType = TypeName
                If String.Equals(TypeName, "5", StringComparison.OrdinalIgnoreCase) Then
                    Try
                        Snap.Uuid = If(Settings.GetSafe("CacheMsV2Uuid").ToString(), "")
                    Catch ex As Exception
                        Logger.Warn(ex, "读取账号 UUID 失败")
                    End Try
                End If
            Catch ex As Exception
                Info.AddEvent("读取账号信息失败：" & ex.Message)
            End Try
            Return Snap
        End Get
    End Property

    ''' <summary>
    ''' API v7：列出当前实例的日志与崩溃报告（需要 logs.read，只读）。
    ''' 返回 values 相对实例目录，可直接交给 ReadLog。
    ''' </summary>
    Public Function ListLogs() As List(Of PluginLogFile)
        Info.EnsurePermission("logs.read")
        Dim Result As New List(Of PluginLogFile)
        Try
            Dim Inst = McInstanceSelected
            If Inst Is Nothing OrElse String.IsNullOrEmpty(Inst.PathIndie) Then Return Result
            For Each Rel In New String() {"logs", "crash-reports"}
                Dim Dir = IO.Path.Combine(Inst.PathIndie, Rel)
                If Not IO.Directory.Exists(Dir) Then Continue For
                For Each File In IO.Directory.GetFiles(Dir)
                    Dim Ext = IO.Path.GetExtension(File).ToLowerInvariant()
                    If Ext <> ".log" AndAlso Ext <> ".txt" Then Continue For
                    Dim Fi As New IO.FileInfo(File)
                    Result.Add(New PluginLogFile With {
                        .Path = Rel & "\" & Fi.Name,
                        .Name = Fi.Name,
                        .ModifiedAt = Fi.LastWriteTime,
                        .Size = Fi.Length})
                Next
            Next
            Result.Sort(Function(A, B) B.ModifiedAt.CompareTo(A.ModifiedAt))
        Catch ex As Exception
            Info.AddEvent("读取日志列表失败：" & ex.Message)
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' API v7：读取某个日志的尾部若干行（需要 logs.read，只读）。
    ''' Path 请使用 ListLogs 返回的相对路径；越界或不存在都会返回空字符串。
    ''' </summary>
    Public Function ReadLog(Path As String, Optional MaxLines As Integer = 200) As String
        Info.EnsurePermission("logs.read")
        If String.IsNullOrWhiteSpace(Path) Then Return ""
        If MaxLines <= 0 Then MaxLines = 200
        If MaxLines > 2000 Then MaxLines = 2000
        Try
            Dim Inst = McInstanceSelected
            If Inst Is Nothing OrElse String.IsNullOrEmpty(Inst.PathIndie) Then Return ""
            Dim Rel = Path.Replace("/", "\").TrimStart("\"c)
            If Rel.Contains("..") Then Return ""
            Dim Root = IO.Path.GetFullPath(Inst.PathIndie)
            Dim Full = IO.Path.GetFullPath(IO.Path.Combine(Root, Rel))
            If Not Full.StartsWith(Root, StringComparison.OrdinalIgnoreCase) Then Return ""
            If Not IO.File.Exists(Full) Then Return ""
            Dim All As String
            Using Fs As New IO.FileStream(Full, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                Using Reader As New IO.StreamReader(Fs, Text.Encoding.UTF8)
                    All = Reader.ReadToEnd()
                End Using
            End Using
            Dim Lines = All.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            If Lines.Length <= MaxLines Then Return String.Join(vbCrLf, Lines)
            Return String.Join(vbCrLf, Lines.Skip(Lines.Length - MaxLines))
        Catch ex As Exception
            Info.AddEvent("读取日志失败：" & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>API v7：切换内置皮肤（0=正版 / 1=Steve / 2=Alex，需要 skin）。</summary>
    Public Function SetBuiltInSkin(Kind As Integer) As Boolean
        Info.EnsurePermission("skin")
        Try
            If Kind < 0 OrElse Kind > 2 Then Return False
            Settings.Set("LaunchSkinType", Kind)
            Info.AddEvent("已切换内置皮肤：" & Kind.ToString())
            Return True
        Catch ex As Exception
            Info.AddEvent("切换皮肤失败：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>API v7：把一张 PNG 设为本地皮肤（需要 skin）。源文件还必须过 files 权限检查。</summary>
    Public Function SetLocalSkin(PngPath As String, Optional Slim As Boolean = False) As Boolean
        Info.EnsurePermission("skin")
        Try
            If String.IsNullOrWhiteSpace(PngPath) OrElse Not IO.File.Exists(PngPath) Then Return False
            If Not String.Equals(IO.Path.GetExtension(PngPath), ".png", StringComparison.OrdinalIgnoreCase) Then Return False
            CheckFileAccess(PngPath, False)
            Dim Target = Paths.AppDataThenName & "CustomSkin.png"
            IO.Directory.CreateDirectory(Paths.AppDataThenName)
            IO.File.Copy(PngPath, Target, True)
            Settings.Set("LaunchSkinType", 4)
            Settings.Set("LaunchSkinSlim", Slim)
            Info.AddEvent("已设置本地皮肤：" & IO.Path.GetFileName(PngPath))
            Return True
        Catch ex As Exception
            Info.AddEvent("设置本地皮肤失败：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' API v7：把本地文件安装进当前实例（需要 instance.install）。
    ''' Folder 只接受 mods / resourcepacks / shaderpacks / datapacks。
    ''' </summary>
    Public Function InstallContent(LocalFile As String, Optional Folder As String = "mods") As Boolean
        Info.EnsurePermission("instance.install")
        Try
            If String.IsNullOrWhiteSpace(LocalFile) OrElse Not IO.File.Exists(LocalFile) Then Return False
            CheckFileAccess(LocalFile, False)
            Dim Name = If(Folder, "mods").Trim().ToLowerInvariant()
            If Name <> "mods" AndAlso Name <> "resourcepacks" AndAlso Name <> "shaderpacks" AndAlso Name <> "datapacks" Then Return False
            Dim Inst = McInstanceSelected
            If Inst Is Nothing OrElse String.IsNullOrEmpty(Inst.PathIndie) Then Return False
            Dim TargetDir = IO.Path.Combine(Inst.PathIndie, Name)
            IO.Directory.CreateDirectory(TargetDir)
            Dim Target = IO.Path.Combine(TargetDir, IO.Path.GetFileName(LocalFile))
            IO.File.Copy(LocalFile, Target, True)
            Info.AddEvent($"已安装到 {Name}：{IO.Path.GetFileName(LocalFile)}")
            Return True
        Catch ex As Exception
            Info.AddEvent("安装内容失败：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>API v7：启动当前实例（需要 launch）。已经在启动或没有选中实例时返回 False。</summary>
    Public Function LaunchGame() As Boolean
        Info.EnsurePermission("launch")
        Try
            If String.IsNullOrEmpty(InstanceName) Then Return False
            Dim Ok As Boolean = False
            ModBase.RunInUiWait(Sub() Ok = McLaunchStart())
            If Ok Then Info.AddEvent("已请求启动游戏：" & InstanceName) Else Info.AddEvent("启动游戏被拒绝：" & InstanceName)
            Return Ok
        Catch ex As Exception
            Info.AddEvent("启动游戏失败：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>API v7：读取启动器的全局设置（需要 settings.global，只读；读不到返回空字符串）。</summary>
    Public Function GetGlobalSetting(Key As String) As String
        Info.EnsurePermission("settings.global")
        If String.IsNullOrWhiteSpace(Key) Then Return ""
        Try
            Dim Value = Settings.GetSafe(Key.Trim())
            If Value Is Nothing Then Return ""
            Return Value.ToString()
        Catch ex As Exception
            Info.AddEvent("读取全局设置失败：" & Key.Trim() & "，" & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>在 UI 线程执行（插件自己的线程里操作界面时用）。</summary>
    Public Sub RunInUi(Action As Action)
        ModBase.RunInUi(Sub() Action.Invoke())
    End Sub

    ' ================= v4 新增能力 =================

    ''' <summary>启动器当前是否深色主题。</summary>
    Public ReadOnly Property ThemeIsDark As Boolean
        Get
            Return ModSecret.IsDarkMode
        End Get
    End Property

    ''' <summary>启动器当前主题的色相（0-360）。</summary>
    Public ReadOnly Property ThemeHue As Integer
        Get
            Return ModSecret.ColorHue
        End Get
    End Property

    ''' <summary>弹出一条更醒目的通知（默认蓝色提示）。</summary>
    Public Sub ShowNotification(Text As String, Optional Type As HintType = HintType.Blue)
        ModMain.Hint(If(Text, ""), Type, True)
    End Sub

    ''' <summary>检查权限。未在 plugin.json 声明 permissions 的旧插件默认放行，保持兼容。</summary>
    Public Function HasPermission(Name As String) As Boolean
        Return Info.HasPermission(Name)
    End Function

    ''' <summary>读写任意路径需要 files 权限；未声明时只允许访问插件自己的 data 目录与当前版本目录。</summary>
    Private Sub CheckFileAccess(Path As String, Optional ForWrite As Boolean = False)
        If Info.HasPermission("files") Then
            If Not Info.Detail.HasDetail Then Return
            Dim FullPath = IO.Path.GetFullPath(Path)
            Dim Patterns = If(ForWrite, Info.Detail.WritePaths, Info.Detail.ReadPaths)
            Dim OwnData = IO.Path.GetFullPath(DataFolder).TrimEnd("\"c) & "\"
            If Patterns.Count = 0 Then
                'v7.4（安全审计 V2）：声明了 files 却没给这个方向的路径时，该方向一律拒绝
                If FullPath.StartsWith(OwnData, StringComparison.OrdinalIgnoreCase) Then Return
                Throw New UnauthorizedAccessException($"插件 {Info.DisplayName} 声明了 files 权限，但没有列出{If(ForWrite, "可写", "可读")}路径，此方向一律拒绝：{FullPath}")
            End If
            If FullPath.StartsWith(OwnData, StringComparison.OrdinalIgnoreCase) Then Return
            If Patterns.Any(Function(P) PathMatches(P, FullPath)) Then Return
            Throw New UnauthorizedAccessException($"插件 {Info.DisplayName} 的 files 权限未包含该路径：{FullPath}（plugin.json 中 permissions.files 只允许 {String.Join("、", Patterns)}）")
        End If
        Dim Full = IO.Path.GetFullPath(Path)
        Dim Allowed As New List(Of String) From {IO.Path.GetFullPath(DataFolder)}
        Try
            Dim Indie = InstanceFolder
            If Not String.IsNullOrWhiteSpace(Indie) Then Allowed.Add(IO.Path.GetFullPath(Indie))
        Catch
            '忽略
        End Try
        'v7.4（安全审计 V6）：围栏必须按目录边界比较，否则 versions\1.20 会放行 versions\1.20.1
        If Allowed.Any(Function(A) Full.StartsWith(If(A.EndsWith("\"c), A, A & "\"), StringComparison.OrdinalIgnoreCase)) Then Return
        Throw New UnauthorizedAccessException($"插件 {Info.DisplayName} 没有 files 权限，只能访问自己的 data 目录：" & Full)
    End Sub

    ''' <summary>读取文本文件（默认 UTF-8）。</summary>
    Public Function ReadTextFile(Path As String) As String
        CheckFileAccess(Path)
        Return IO.File.ReadAllText(Path, Encoding.UTF8)
    End Function

    ''' <summary>写入文本文件（默认 UTF-8，自动创建目录）。</summary>
    Public Sub WriteTextFile(Path As String, Text As String)
        CheckFileAccess(Path, True)
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Path))
        IO.File.WriteAllText(Path, If(Text, ""), New UTF8Encoding(False))
    End Sub

    Public Function FileExists(Path As String) As Boolean
        CheckFileAccess(Path)
        Return IO.File.Exists(Path)
    End Function

    ''' <summary>
    ''' 按允许列表逐跳校验重定向（v7.4，安全审计 V4）。最多 5 跳，每一跳都重新校验主机。
    ''' </summary>
    Private Function OpenHttp(Url As String) As Net.HttpWebResponse
        Dim Current = Url
        For Hop As Integer = 1 To 5
            EnsureHostAllowed(Info, Current)
            Dim Request = CType(Net.WebRequest.Create(Current), Net.HttpWebRequest)
            Request.AllowAutoRedirect = False
            Request.Timeout = 15000
            Request.ReadWriteTimeout = 15000
            Request.UserAgent = "PCLHarness-Plugin/" & ApiVersionUi
            Dim Response = CType(Request.GetResponse(), Net.HttpWebResponse)
            Dim Code = CInt(Response.StatusCode)
            If Code = 301 OrElse Code = 302 OrElse Code = 303 OrElse Code = 307 OrElse Code = 308 Then
                Dim Location = Response.Headers("Location")
                Response.Dispose()
                If String.IsNullOrWhiteSpace(Location) Then Throw New IO.IOException("重定向响应缺少 Location：" & Current)
                Current = New Uri(New Uri(Current), Location).AbsoluteUri
                Continue For
            End If
            Return Response
        Next
        Throw New IO.IOException("重定向次数过多（超过 5 次）：" & Url)
    End Function

    ''' <summary>发起一个 GET 请求并返回文本（需要 net 权限，最多读取 8 MiB）。</summary>
    Public Function HttpGetString(Url As String) As String
        If Not Info.HasPermission("net") Then Throw New UnauthorizedAccessException("插件 " & Info.DisplayName & " 没有 net 权限")
        Using Response = OpenHttp(Url)
            Using Reader As New IO.StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                Dim Buffer(8191) As Char
                Dim Builder As New Text.StringBuilder()
                Dim Total As Long = 0
                While True
                    Dim Read = Reader.Read(Buffer, 0, Buffer.Length)
                    If Read <= 0 Then Exit While
                    Builder.Append(Buffer, 0, Read)
                    Total += Read
                    'v7.4（安全审计 V4）：没有上限的 ReadToEnd 会被超大响应拖垮内存
                    If Total > 8L * 1024 * 1024 Then Throw New IO.IOException("响应超过 8 MB 上限，已中止")
                End While
                Return Builder.ToString()
            End Using
        End Using
    End Function

    ''' <summary>下载到本地文件（需要 net + files 权限），成功返回 True。</summary>
    ''' <summary>HttpDownload 的结果分类（v7.5，安全审计 3.8 R9）；调用方可以区分未授权、主机被拒、超限和一般失败。</summary>
    Public Enum PluginDownloadResult
        Ok = 0
        ''' <summary>没有 net 权限。</summary>
        NotPermitted = 1
        ''' <summary>目标主机不在白名单里（或重定向跳到了白名单外）。</summary>
        HostDenied = 2
        ''' <summary>目标路径没有 files 写权限。</summary>
        PathDenied = 3
        ''' <summary>超过 512 MB 上限。</summary>
        TooLarge = 4
        ''' <summary>其他失败（网络、磁盘等）。</summary>
        Failed = 5
    End Enum

    ''' <summary>「超限」的识别前缀（v7.5）；放在异常消息里，调用方无需解析中文文案。</summary>
    Private Const TooLargeMarker As String = "[TOO_LARGE] "

    ''' <summary>下载到本地文件（需要 net + files 权限），成功返回 True。失败会删除半截文件。</summary>
    Public Function HttpDownload(Url As String, LocalPath As String) As Boolean
        Return HttpDownloadResult(Url, LocalPath) = PluginDownloadResult.Ok
    End Function

    ''' <summary>下载并返回结果分类（需要 net + files 权限）；失败会删除半截文件（v7.5，安全审计 3.8 R9）。</summary>
    ''' <summary>下载并返回结果分类（需要 net + files 权限）；失败会删除半截文件（v7.5，安全审计 3.8 R9）。</summary>
    Public Function HttpDownloadResult(Url As String, LocalPath As String) As PluginDownloadResult
        'v7.5（安全审计 3.8 R9）：按「授权 → 主机 → 路径」分段判定，异常类型就能区分原因，不必猜消息
        If Not Info.HasPermission("net") Then Return PluginDownloadResult.NotPermitted
        Try
            EnsureHostAllowed(Info, Url)
        Catch ex As UnauthorizedAccessException
            Info.AddEvent("HttpDownload 被拒绝（主机）：" & ex.Message)
            Return PluginDownloadResult.HostDenied
        End Try
        Try
            CheckFileAccess(LocalPath, True)
        Catch ex As UnauthorizedAccessException
            Info.AddEvent("HttpDownload 被拒绝（路径）：" & ex.Message)
            Return PluginDownloadResult.PathDenied
        End Try
        Try
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(IO.Path.GetFullPath(LocalPath)))
            Using Response = OpenHttp(Url)
                Using Input = Response.GetResponseStream()
                    Using Output As New IO.FileStream(LocalPath, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None)
                        Dim Buffer(81919) As Byte
                        Dim Total As Long = 0
                        While True
                            Dim Read = Input.Read(Buffer, 0, Buffer.Length)
                            If Read <= 0 Then Exit While
                            Output.Write(Buffer, 0, Read)
                            Total += Read
                            If Total > 512L * 1024 * 1024 Then Throw New IO.IOException(TooLargeMarker & "下载超过 512 MB 上限，已中止")
                        End While
                    End Using
                End Using
            End Using
            Return PluginDownloadResult.Ok
        Catch ex As Exception
            Info.AddEvent("HttpDownload 失败：" & ex.GetBaseException().Message)
            'v7.5（安全审计 3.8 R9）：失败时清掉半截文件，避免留下一个看起来「下载完成」的残缺文件
            Try
                If IO.File.Exists(LocalPath) Then IO.File.Delete(LocalPath)
            Catch DeleteEx As Exception
                Info.AddEvent("清理未完成的下载失败：" & DeleteEx.GetBaseException().Message)
            End Try
            Dim Raw = ex.GetBaseException()
            If TypeOf Raw Is IO.IOException AndAlso Raw.Message.StartsWith(TooLargeMarker, StringComparison.Ordinal) Then Return PluginDownloadResult.TooLarge
            If TypeOf Raw Is UnauthorizedAccessException Then Return PluginDownloadResult.NotPermitted
            Return PluginDownloadResult.Failed
        End Try
    End Function

    Public Sub AppendJvmArgs(Text As String)
        If Not Info.HasPermission("args") Then Throw New UnauthorizedAccessException("插件 " & Info.DisplayName & " 没有 args 权限")
        If String.IsNullOrWhiteSpace(Text) Then Return
        ExtraJvmArgs.Add(Text.Trim())
        Info.AddEvent("追加 JVM 参数：" & Text.Trim())
    End Sub

    ''' <summary>追加游戏参数（例如 "--demo"）；需要 args 权限。</summary>
    Public Sub AppendGameArgs(Text As String)
        If Not Info.HasPermission("args") Then Throw New UnauthorizedAccessException("插件 " & Info.DisplayName & " 没有 args 权限")
        If String.IsNullOrWhiteSpace(Text) Then Return
        ExtraGameArgs.Add(Text.Trim())
        Info.AddEvent("追加游戏参数：" & Text.Trim())
    End Sub

    ''' <summary>插件追加的 JVM 参数（启动器在构建参数时会读取，按加载顺序拼接）。</summary>
    Public ReadOnly ExtraJvmArgs As New List(Of String)

    ''' <summary>插件追加的游戏参数。</summary>
    Public ReadOnly ExtraGameArgs As New List(Of String)

    ''' <summary>延迟若干毫秒后在 UI 线程执行。</summary>
    Public Sub RunLater(Milliseconds As Integer, Action As Action)
        If Milliseconds < 0 Then Milliseconds = 0
        Dim T As New System.Threading.Thread(
            Sub()
                System.Threading.Thread.Sleep(Milliseconds)
                RunInUi(Action)
            End Sub) With {.IsBackground = True}
        T.Start()
    End Sub


    ' ================= v5 新增能力 =================

    ''' <summary>API v5：事件总线，例如 Host.Events.On("页面切换", Sub(Name) Host.LogInfo("进入 " & Name) End Sub)。</summary>
    Public ReadOnly Events As New PluginEventBus()

    ''' <summary>API v5：读取强类型设置（自动做类型转换，失败时用默认值）。</summary>
    Public Function GetSetting(Of T)(Key As String, DefaultValue As T) As T
        Try
            Dim Raw = ModPlugin.GetSetting(Info, Key, DefaultValue)
            If Raw Is Nothing Then Return DefaultValue
            If TypeOf Raw Is T Then Return CType(Raw, T)
            Return CType(Convert.ChangeType(Raw, GetType(T), Globalization.CultureInfo.InvariantCulture), T)
        Catch
            Return DefaultValue
        End Try
    End Function

    ''' <summary>API v5：写入强类型设置（值会自动保存到 plugins.settings.json）。</summary>
    Public Sub SetSetting(Of T)(Key As String, Value As T)
        ModPlugin.SetSetting(Info, Key, Value)
    End Sub

    ''' <summary>API v5：调试日志（只写启动器日志，不打扰用户）。</summary>
    Public Sub LogDebug(Text As String)
        Logger.Info($"[插件 {Info.DisplayName}][调试] {Text}")
    End Sub

    ''' <summary>API v5：普通日志。</summary>
    Public Sub LogInfo(Text As String)
        Logger.Info($"[插件 {Info.DisplayName}] {Text}")
    End Sub

    ''' <summary>API v5：警告日志（同时记到插件事件）。</summary>
    Public Sub LogWarn(Text As String)
        Logger.Info($"[插件 {Info.DisplayName}][警告] {Text}")
        Info.AddEvent("警告：" & Text)
    End Sub

    ''' <summary>API v5：错误日志（同时记到插件事件）。</summary>
    Public Sub LogError(Text As String)
        Logger.Error(New Exception(Text), $"插件 {Info.DisplayName} 报错", LogBehavior.None)
        Info.AddEvent("错误：" & Text)
    End Sub

    ''' <summary>API v5：弹出带按钮的对话框，返回点下的按钮文字（阻塞到用户点击）。</summary>
    Public Function Ask(Text As String, Optional Title As String = "插件提示", Optional Button1 As String = "确定", Optional Button2 As String = "取消") As String
        Dim Result As Integer = 1
        RunInUi(Sub()
                    Result = ModMain.MyMsgBox(If(Text, ""), Title, Button1, If(Button2, ""))
                End Sub)
        If Result = 2 AndAlso Not String.IsNullOrEmpty(Button2) Then Return Button2
        Return Button1
    End Function
End Class