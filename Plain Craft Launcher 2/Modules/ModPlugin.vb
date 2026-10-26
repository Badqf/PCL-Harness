Imports System.IO
Imports System.Reflection
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' PCL Harness 插件框架（v1）。
''' <para>插件目录：启动器目录\PCL\Plugins\插件文件夹\，其中必须包含 plugin.json；实现 DLL 可选。</para>
''' <para>plugin.json 示例：{ "id":"demo", "name":"示例插件", "version":"1.0.0", "author":"你", "description":"说明", "entry":"Demo.Plugin", "dll":"Demo.Plugin.dll" }</para>
''' </summary>
Public Module ModPlugin

    Public Const ApiVersion As Integer = 1
    Public Const ManifestFileName As String = "plugin.json"

    ''' <summary>
    ''' 插件根目录（%启动器目录%\PCL\Plugins）。
    ''' </summary>
    Public ReadOnly Property PathPluginRoot As String = Path.Combine(Paths.Base.TrimEnd("\"c), "PCL", "Plugins")
    Private ReadOnly Property PathEnabledFile As String = Path.Combine(PathPluginRoot, "plugins.json")
    Public ReadOnly Property PathSettingsFile As String = Path.Combine(PathPluginRoot, "plugins.settings.json")

    ''' <summary>UI 扩展点版本：v2 起支持 自定义页面 / 标题栏入口 / 设置项。</summary>
    Public Const ApiVersionUi As Integer = 2

    ''' <summary>插件注册的全部自定义页面。</summary>
    Public ReadOnly PluginPages As New List(Of PluginPageInfo)
    ''' <summary>插件注册的全部标题栏入口。</summary>
    Public ReadOnly PluginNavItems As New List(Of PluginNavItem)
    ''' <summary>插件注册的全部设置项。</summary>
    Public ReadOnly PluginSettings As New List(Of PluginSetting)

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
        ''' <summary>值变化回调（新值）。</summary>
        Public Handler As Action(Of Object)
        Public Owner As PluginInfo
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
                For Each Info In Plugins
                    If Info.Enabled AndAlso String.IsNullOrEmpty(Info.[Error]) Then LoadOne(Info)
                Next
            End If
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
            CollectUi(Info)
        Catch ex As Exception
            Info.[Error] = ex.GetBaseException().Message
            Logger.Error(ex, "加载插件失败：" & Info.DisplayName, LogBehavior.None)
        End Try
    End Sub

    ''' <summary>
    ''' 卸载全部插件（忽略单个插件的异常）。
    ''' </summary>
    Public Sub UnloadAll()
        PluginPages.Clear()
        PluginNavItems.Clear()
        PluginSettings.Clear()
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
    End Sub

    ''' <summary>
    ''' 移除某个插件注册的全部页面 / 入口 / 设置项。
    ''' </summary>
    Private Sub RemoveUi(Info As PluginInfo)
        If Info Is Nothing Then Return
        PluginPages.RemoveAll(Function(P) P IsNot Nothing AndAlso P.Owner Is Info)
        PluginNavItems.RemoveAll(Function(N) N IsNot Nothing AndAlso N.Owner Is Info)
        PluginSettings.RemoveAll(Function(S) S IsNot Nothing AndAlso S.Owner Is Info)
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

End Class
