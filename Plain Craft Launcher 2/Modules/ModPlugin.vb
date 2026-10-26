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

    ''' <summary>
    ''' 扩展点版本：
    ''' v2 自定义页面 / 标题栏入口 / 设置项；
    ''' v3 事件钩子；
    ''' v4 生命周期与联机/下载事件、内嵌卡片与状态栏小组件、权限与依赖、热重载与事件日志。
    ''' </summary>
    Public Const ApiVersionUi As Integer = 4

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
            For Each Folder In Directory.GetDirectories(PathPluginRoot)
                Dim Info = ReadManifest(Folder)
                If Info Is Nothing Then Continue For
                Info.Enabled = EnabledIds.Contains(Info.Id.ToLowerInvariant())
                If String.IsNullOrEmpty(Info.Id) Then
                    Info.[Error] = "plugin.json 缺少 id"
                    Info.Enabled = False
                End If
                Plugins.Add(Info)
            Next
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
                        For Each DepId In Info.Dependencies
                            Dim Dep = Plugins.FirstOrDefault(Function(P) String.Equals(P.Id, DepId, StringComparison.OrdinalIgnoreCase))
                            If Dep Is Nothing Then
                                Missing = DepId
                                Exit For
                            End If
                            If Dep.Enabled AndAlso Not Dep.Loaded AndAlso Pending.Contains(Dep) Then
                                Blocked = True
                                Exit For
                            End If
                            If Dep.Enabled AndAlso Not String.IsNullOrEmpty(Dep.[Error]) Then
                                Missing = DepId
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
            Info.Loaded = False
            Info.Instance = Nothing
        Next
    End Sub

    ''' <summary>
    ''' 启用/禁用插件并持久化。
    ''' </summary>
    Public Sub SetEnabled(Info As PluginInfo, Value As Boolean)
        If Info Is Nothing Then Return
        Info.Enabled = Value
        If Value Then
            LoadOne(Info)
        Else
            If Info.Loaded AndAlso Info.Instance IsNot Nothing Then
                Try
                    CType(Info.Instance, IPclPlugin).OnUnload()
                Catch ex As Exception
                    Logger.Error(ex, "卸载插件失败：" & Info.DisplayName, LogBehavior.None)
                End Try
            End If
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
            Dim Perms = Json.Value(Of JArray)("permissions")
            If Perms IsNot Nothing Then
                For Each P In Perms
                    Dim Name = P.ToString().Trim().ToLowerInvariant()
                    If Name <> "" Then Info.Permissions.Add(Name)
                Next
            End If
            Dim Deps = Json.Value(Of JArray)("dependencies")
            If Deps IsNot Nothing Then
                For Each D In Deps
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
            For Each Info In Plugins
                If Info.Enabled Then Array.Add(Info.Id)
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
                Logger.Error(ex, "插件事件 OnLauncherReady 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
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
                Logger.Error(ex, "插件事件 OnPageChanged 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>构建启动参数时；插件可往 Info.ExtraJvmArgs / ExtraGameArgs 追加参数。</summary>
    Public Sub RaiseGameLaunching(Info As PluginLaunchInfo)
        For Each Entry In PluginEventHandlers.ToList()
            Try
                Entry.Handler.OnGameLaunching(Info)
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnGameLaunching 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>游戏进程刚启动后。</summary>
    Public Sub RaiseGameLaunched(Info As PluginLaunchInfo)
        For Each Entry In PluginEventHandlers.ToList()
            Try
                Entry.Handler.OnGameLaunched(Info)
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnGameLaunched 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
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
                Logger.Error(ex, "插件事件 OnGameExited 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>当前版本切换后。</summary>
    Public Sub RaiseInstanceChanged(InstanceName As String)
        For Each Entry In PluginLifecycleHandlers.ToList()
            Try
                Entry.Handler.OnInstanceChanged(If(InstanceName, ""))
                Entry.Owner.AddEvent("OnInstanceChanged(" & If(InstanceName, "") & ")")
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnInstanceChanged 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>主题在深色/浅色之间切换后。</summary>
    Public Sub RaiseThemeChanged(IsDark As Boolean)
        For Each Entry In PluginLifecycleHandlers.ToList()
            Try
                Entry.Handler.OnThemeChanged(IsDark)
                Entry.Owner.AddEvent("OnThemeChanged(" & If(IsDark, "深色", "浅色") & ")")
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnThemeChanged 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>成功创建联机房间后。</summary>
    Public Sub RaiseLinkRoomCreated(Invite As String)
        For Each Entry In PluginLinkHandlers.ToList()
            Try
                Entry.Handler.OnLinkRoomCreated(If(Invite, ""))
                Entry.Owner.AddEvent("OnLinkRoomCreated")
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnLinkRoomCreated 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>成功加入联机房间后。</summary>
    Public Sub RaiseLinkRoomJoined()
        For Each Entry In PluginLinkHandlers.ToList()
            Try
                Entry.Handler.OnLinkRoomJoined()
                Entry.Owner.AddEvent("OnLinkRoomJoined")
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnLinkRoomJoined 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>离开联机房间或断开后。</summary>
    Public Sub RaiseLinkRoomClosed()
        For Each Entry In PluginLinkHandlers.ToList()
            Try
                Entry.Handler.OnLinkRoomClosed()
                Entry.Owner.AddEvent("OnLinkRoomClosed")
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnLinkRoomClosed 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>是否为需要通知插件的下载类加载器（名称包含 下载 或 Download）。</summary>
    Public Function IsDownloadLoader(Name As String) As Boolean
        If String.IsNullOrWhiteSpace(Name) Then Return False
        Return Name.Contains("下载") OrElse Name.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>一个下载任务开始。</summary>
    Public Sub RaiseDownloadStarted(Target As String)
        For Each Entry In PluginDownloadHandlers.ToList()
            Try
                Entry.Handler.OnDownloadStarted(If(Target, ""))
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnDownloadStarted 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>一个下载任务结束。</summary>
    Public Sub RaiseDownloadFinished(Target As String, Success As Boolean)
        For Each Entry In PluginDownloadHandlers.ToList()
            Try
                Entry.Handler.OnDownloadFinished(If(Target, ""), Success)
                Entry.Owner.AddEvent("OnDownloadFinished(" & If(Target, "") & ", " & Success & ")")
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnDownloadFinished 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
            End Try
        Next
    End Sub

    ''' <summary>关闭启动器前询问插件；返回 True 表示有插件要求取消关闭。</summary>
    Public Function RaiseClosing() As Boolean
        Dim Cancel As Boolean = False
        For Each Entry In PluginClosingHandlers.ToList()
            Try
                If Entry.Handler.OnClosing() Then Cancel = True
            Catch ex As Exception
                Logger.Error(ex, "插件事件 OnClosing 失败：" & Entry.Owner.DisplayName, LogBehavior.None)
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
            Dim Queue As FileSystemEventHandler = Sub(Sender, Args) QueueReload()
            AddHandler _Watcher.Changed, Queue
            AddHandler _Watcher.Created, Queue
            AddHandler _Watcher.Deleted, Queue
            AddHandler _Watcher.Renamed, Sub(Sender, Args) QueueReload()
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

    ''' <summary>刷新窗口右下角的插件状态栏。</summary>
    Public Sub RefreshStatusWidgets()
        Try
            If ModMain.FrmMain Is Nothing Then Return
            Dim Parts As New List(Of String)
            For Each W In PluginStatusWidgets.ToList()
                Try
                    Dim T = If(W.TextProvider(), "").Trim()
                    If T <> "" Then Parts.Add(T)
                Catch ex As Exception
                    W.Owner.AddEvent("状态组件读取失败：" & ex.GetBaseException().Message)
                End Try
            Next
            Dim Text = String.Join("   ", Parts)
            If Text = ModMain.FrmMain.LabPluginStatus.Text Then Return
            ModMain.FrmMain.LabPluginStatus.Text = Text
            ModMain.FrmMain.LabPluginStatus.Visibility = If(Text = "", Visibility.Collapsed, Visibility.Visible)
        Catch ex As Exception
            Logger.Error(ex, "刷新插件状态栏失败", LogBehavior.None)
        End Try
    End Sub

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
''' 提供给插件的宿主对象。
''' </summary>
Public Class PluginHost

    Public ReadOnly Info As ModPlugin.PluginInfo

    Friend Sub New(Info As ModPlugin.PluginInfo)
        Me.Info = Info
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
    Private Sub CheckFileAccess(Path As String)
        If Info.HasPermission("files") Then Return
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
        CheckFileAccess(Path)
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
            CheckFileAccess(LocalPath)
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


End Class
