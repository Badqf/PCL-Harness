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
                   Name.Equals("fuse.json", StringComparison.OrdinalIgnoreCase)
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
    ''' v6 事件总线全覆盖（含优先级/一次性/异步）、插件服务注册与依赖版本约束、后台任务与进度、开发模式控制台。
    ''' </summary>
    Public Const ApiVersionUi As Integer = 6

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
            If Permissions.Count = 0 Then Return True '未声明权限的旧插件不限制，保持兼容
            Return Permissions.Contains(Name)
        End Function
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
            For Each Folder In Directory.GetDirectories(PathPluginRoot)
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
            Dim DllPath = Path.Combine(Info.Folder, If(String.IsNullOrEmpty(Info.DllName), Info.Id & ".dll", Info.DllName))
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
                If Not String.Equals(ActualHash, Info.DllSha256, StringComparison.OrdinalIgnoreCase) Then
                    Dim Want = Info.DllSha256.Substring(0, Math.Min(12, Info.DllSha256.Length))
                    Dim Got = ActualHash.Substring(0, Math.Min(12, ActualHash.Length))
                    Info.[Error] = "DLL 校验失败：sha256 与 plugin.json 不一致，已拒绝加载（文件可能损坏或被替换）"
                    Info.AddEvent($"校验失败：清单 {Want}…，实际 {Got}…")
                    Return
                End If
            End If
            Dim Asm = Assembly.LoadFrom(DllPath)
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
        Catch ex As Exception
            Info.[Error] = ex.GetBaseException().Message
            Info.AddEvent("加载失败：" & ex.GetBaseException().Message)
            Logger.Error(ex, "加载插件失败：" & Info.DisplayName, LogBehavior.None)
            NotePluginFailure(Info, "加载", ex) '连续失败会自动熔断
        End Try
    End Sub

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
            Dim MinApiToken = Json("minApiVersion")
            If MinApiToken IsNot Nothing Then
                Dim MinApiValue As Integer
                If Integer.TryParse(MinApiToken.ToString().Trim(), MinApiValue) Then Info.MinApiVersion = MinApiValue
            End If
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
        If TypeOf Info.Instance Is IPclPluginEvents Then
            PluginEventHandlers.Add((CType(Info.Instance, IPclPluginEvents), Info))
        End If
        If Not TypeOf Info.Instance Is IPclPluginUi Then Return
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
        If Info.Detail.Hosts.Count = 0 Then Return
        Dim Host As String = ""
        Try
            Host = New Uri(Url).Host
        Catch
        End Try
        If Host = "" Then Throw New ArgumentException("无效的网址：" & Url)
        If Info.Detail.Hosts.Any(Function(P) MatchHostPattern(Host, P)) Then Return
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

        ''' <summary>全部事件名。</summary>
        Public Shared Function All() As String()
            Return {LauncherReady, PageChanged, GameLaunching, GameLaunched, GameExited, InstanceChanged, ThemeChanged,
                    LinkRoomCreated, LinkRoomJoined, LinkRoomClosed, DownloadStarted, DownloadFinished, Closing}
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
        Public Sub [On](EventName As String, Handler As [Delegate], Optional Priority As Integer = 0, Optional OnlyOnce As Boolean = False, Optional Owner As String = "")
            If String.IsNullOrWhiteSpace(EventName) OrElse Handler Is Nothing Then Return
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
                    Item.Handler.DynamicInvoke(Args)
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
''' 提供给插件的宿主对象。
''' </summary>
Public Class PluginHost

    Public ReadOnly Info As ModPlugin.PluginInfo

    ''' <summary>API v6：插件间服务注册表（也可以直接用模块级的 ModPlugin.PluginServices）。</summary>
    Public ReadOnly Services As PluginServiceAccessor

    ''' <summary>API v6：后台任务（会出现在「任务管理」里，可显示进度、可取消）。</summary>
    Public ReadOnly Tasks As PluginTaskManager

    Friend Sub New(Info As ModPlugin.PluginInfo)
        Me.Info = Info
        Me.Services = New PluginServiceAccessor(If(Info.Id, Info.DisplayName))
        Me.Tasks = New PluginTaskManager(If(Info.Id, Info.DisplayName))
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
            If Patterns.Count = 0 Then Return
            Dim OwnData = IO.Path.GetFullPath(DataFolder).TrimEnd("\"c) & "\"
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
        If Allowed.Any(Function(A) Full.StartsWith(A, StringComparison.OrdinalIgnoreCase)) Then Return
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

    ''' <summary>发起一个 GET 请求并返回文本（需要 net 权限；未声明权限时按默认允许处理）。</summary>
    Public Function HttpGetString(Url As String) As String
        If Not Info.HasPermission("net") Then Throw New UnauthorizedAccessException("插件 " & Info.DisplayName & " 没有 net 权限")
        EnsureHostAllowed(Info, Url)
        Dim Request = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Request.Timeout = 15000
        Request.ReadWriteTimeout = 15000
        Request.UserAgent = "PCLHarness-Plugin/" & ApiVersionUi
        Using Response = Request.GetResponse()
            Using Reader As New IO.StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function

    ''' <summary>下载到本地文件（需要 net + files 权限），成功返回 True。</summary>
    Public Function HttpDownload(Url As String, LocalPath As String) As Boolean
        Try
            EnsureHostAllowed(Info, Url)
            CheckFileAccess(LocalPath, True)
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(LocalPath))
            Using Client As New Net.WebClient()
                Client.Headers.Add("User-Agent", "PCLHarness-Plugin/" & ApiVersionUi)
                Client.DownloadFile(Url, LocalPath)
            End Using
            Return True
        Catch ex As Exception
            Info.AddEvent("HttpDownload 失败：" & ex.GetBaseException().Message)
            Return False
        End Try
    End Function

    ''' <summary>追加 JVM 参数（例如 "-Xmx4G"）；需要 args 权限。</summary>
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
