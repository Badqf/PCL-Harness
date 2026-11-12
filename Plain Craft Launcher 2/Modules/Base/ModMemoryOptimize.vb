Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text

''' <summary>
''' 内存优化。
''' 默认只清理启动器自身；系统级优化通过一次性的管理员子进程完成（会弹出一次 UAC），
''' 用户取消授权或子进程失败时自动降级为只清理自身，主进程全程不提权。
''' </summary>
Public Module ModMemoryOptimize

    '==== 常量 ====

    ''' <summary>启动系统级优化子进程时使用的参数名。</summary>
    Public Const ChildSwitch As String = "--memory"
    ''' <summary>父进程等待子进程退出的最长时间（毫秒）。</summary>
    Private Const ParentWaitMs As Integer = 20000
    ''' <summary>子进程自身的时间预算（毫秒），超时则带着已完成的部分退出。</summary>
    Private Const ChildBudgetMs As Integer = 10000
    ''' <summary>参与优化的工作集下限，太小的进程收益低、失败多。</summary>
    Private Const TrimThreshold As Long = 32L * 1024L * 1024L
    ''' <summary>单次最多处理的进程数。</summary>
    Private Const MaxCandidates As Integer = 300
    ''' <summary>结果文件名前缀，子进程用它做路径白名单校验。</summary>
    Private Const ResultPrefix As String = "pcl-memopt-"

    '子进程退出码，刻意避开 ProcessReturnValues 的 0~5
    Private Const ChildExitOk As Integer = 0
    Private Const ChildExitBadArgs As Integer = 10
    Private Const ChildExitRejected As Integer = 11
    Private Const ChildExitException As Integer = 12
    Private Const ChildExitTimeout As Integer = 13

    'Win32 常量
    Private Const TokenAdjustPrivileges As Integer = &H20
    Private Const TokenQuery As Integer = &H8
    Private Const SePrivilegeEnabled As Integer = &H2
    ''' <summary>PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA</summary>
    Private Const ProcessAccess As Integer = &H1500
    Private Const ErrorCancelled As Integer = 1223

    '==== 对外入口 ====

    ''' <summary>
    ''' 优化内存：优先尝试一次性的管理员子进程；失败则自动降级为只清理启动器自身。
    ''' </summary>
    Public Sub Optimize(ShowHint As Boolean)
        If ShowHint Then AnnounceElevation()
        Dim Result As MemoryOptimizeResult = Nothing
        Dim Reason As String = Nothing
        Try
            Result = RunElevated()
        Catch ex As Win32Exception When ex.NativeErrorCode = ErrorCancelled
            Reason = "已取消管理员授权"
        Catch ex As TimeoutException
            Reason = "系统级优化超时"
        Catch ex As Exception
            Reason = $"系统级优化未完成（{ex.Message}）"
        End Try
        If Result IsNot Nothing Then
            Dim Freed = Math.Max(Result.FreedPhysicalBytes, Result.FreedWorkSetBytes)
            Logger.Info($"内存优化：系统级，处理 {Result.Trimmed} 个进程，释放 {Freed \ 1024 \ 1024} MB，用时 {Result.ElapsedMs} ms")
            '系统级清理不会动启动器自身，顺手把自己也交还一次
            PageOtherTest.MemoryOptimize(False)
            If ShowHint Then
                Hint($"已释放 {FormatSize(Freed)} 内存（系统级，处理 {Result.Trimmed} 个进程）", HintType.Green)
            End If
            Return
        End If
        Logger.Info($"内存优化：{Reason}，降级为只清理启动器自身")
        Dim SelfFreed = TrimSelf()
        If ShowHint Then
            Hint($"{Reason}，仅释放启动器自身 {FormatSize(SelfFreed)} 内存。系统级优化需要管理员权限。", HintType.Blue)
        End If
    End Sub

    ''' <summary>
    ''' 提权前先给一条提示，让用户在 UAC 弹窗出现前就知道要发生什么；
    ''' 取消授权时也能明白为什么系统级优化没有发生。
    ''' </summary>
    Private Sub AnnounceElevation()
        If WindowsUtils.HasAdminRole() Then
            Hint("即将进行系统级内存优化（当前已以管理员权限运行，不会再弹 UAC）", HintType.Blue)
            Return
        End If
        Hint("即将进行系统级内存优化，需要管理员权限：在接下来的弹窗中点「是」继续，点「否」则只清理启动器自身", HintType.Blue)
        '等提示条渲染出来再弹 UAC，否则用户先看到的是安全桌面
        System.Threading.Thread.Sleep(600)
    End Sub

    ''' <summary>
    ''' 只清理启动器自身，返回可用物理内存的下降量（字节）。
    ''' </summary>
    Public Function TrimSelf() As Long
        Dim Before = My.Computer.Info.AvailablePhysicalMemory
        PageOtherTest.MemoryOptimize(False)
        Dim After = My.Computer.Info.AvailablePhysicalMemory
        If After > Before Then Return CLng(After - Before)
        Return 0
    End Function

    '==== 父进程：拉起提权子进程 ====

    Private Function RunElevated() As MemoryOptimizeResult
        Dim ResultPath = Path.Combine(PathTemp, ResultPrefix & Guid.NewGuid().ToString("N") & ".json")
        Try
            Dim ParentId = Process.GetCurrentProcess().Id
            Dim Args = $"{ChildSwitch} ""{ResultPath}"" {ParentId}"
            Dim Child = StartProcess(New ProcessStartInfo(PathExe) With {
                .UseShellExecute = True,
                .Verb = "runas",
                .Arguments = Args
            })
            If Not Child.WaitForExit(ParentWaitMs) Then
                '低权限进程无法结束提权进程，只能不再等待，让子进程按自己的预算收尾
                Logger.Info($"内存优化：子进程未在 {ParentWaitMs \ 1000} 秒内退出，已放弃等待")
                Throw New TimeoutException("等待子进程退出超时")
            End If
            If Not File.Exists(ResultPath) Then Throw New Exception($"子进程退出码 {Child.ExitCode}，没有返回结果")
            Dim Json = File.ReadAllText(ResultPath, Encoding.UTF8)
            Dim Result = JsonConvert.DeserializeObject(Of MemoryOptimizeResult)(Json)
            If Result Is Nothing Then Throw New Exception("结果文件内容为空")
            Result.ExitCode = Child.ExitCode
            If Not String.IsNullOrWhiteSpace(Result.ErrorMessage) Then Throw New Exception(Result.ErrorMessage)
            Return Result
        Finally
            Try
                If File.Exists(ResultPath) Then File.Delete(ResultPath)
            Catch ex As Exception
                Logger.Info($"内存优化：删除结果文件失败（{ex.Message}）")
            End Try
        End Try
    End Function

    '==== 子进程：枚举、过滤、清理 ====

    ''' <summary>
    ''' 系统级优化子进程入口。返回退出码，任何情况下都不抛出异常。
    ''' </summary>
    Public Function RunChild(Args As String()) As Integer
        Dim ResultPath As String = Nothing
        Try
            If Args Is Nothing OrElse Args.Length < 2 OrElse Args(0) <> ChildSwitch Then Return ChildExitBadArgs
            ResultPath = Args(1)
            If Not IsValidResultPath(ResultPath) Then Return ChildExitBadArgs
            If Args.Length >= 3 Then
                Dim ParentId As Integer
                If Not Integer.TryParse(Args(2).Trim(""""c), ParentId) Then Return ChildExitBadArgs
                If Not IsLauncherProcess(ParentId) Then Return ChildExitRejected
            End If
            Dim Result As New MemoryOptimizeResult With {.Elevated = True}
            Dim Watch = Stopwatch.StartNew()
            Dim Before = My.Computer.Info.AvailablePhysicalMemory
            TryEnableDebugPrivilege()
            TrimProcesses(Result, Watch)
            Dim After = My.Computer.Info.AvailablePhysicalMemory
            If After > Before Then Result.FreedPhysicalBytes = CLng(After - Before)
            Result.ElapsedMs = Watch.ElapsedMilliseconds
            WriteResult(ResultPath, Result)
            If Result.Truncated Then Return ChildExitTimeout
            Return ChildExitOk
        Catch ex As Exception
            Try
                If ResultPath IsNot Nothing AndAlso IsValidResultPath(ResultPath) Then
                    WriteResult(ResultPath, New MemoryOptimizeResult With {.Elevated = True, .ErrorMessage = ex.Message})
                End If
            Catch
                '写不进去就只剩退出码
            End Try
            Return ChildExitException
        End Try
    End Function

    ''' <summary>
    ''' 结果文件白名单：必须是绝对路径、文件名以 pcl-memopt- 开头、以 .json 结尾、不含上跳目录。
    ''' </summary>
    Private Function IsValidResultPath(PathFull As String) As Boolean
        If String.IsNullOrWhiteSpace(PathFull) Then Return False
        If Not Path.IsPathRooted(PathFull) Then Return False
        If PathFull.Contains("..") Then Return False
        Dim Name = Path.GetFileName(PathFull).ToLowerInvariant()
        If Not Name.StartsWith(ResultPrefix) Then Return False
        If Not Name.EndsWith(".json") Then Return False
        Return True
    End Function

    ''' <summary>
    ''' 只接受同名启动器作为父进程，避免被其它程序借壳用来提权写文件。
    ''' </summary>
    Private Function IsLauncherProcess(ParentId As Integer) As Boolean
        Try
            Using Item = Process.GetProcessById(ParentId)
                Dim ParentPath = Item.MainModule?.FileName
                If String.IsNullOrWhiteSpace(ParentPath) Then Return False
                Return String.Equals(Path.GetFileName(ParentPath), Path.GetFileName(PathExe), StringComparison.OrdinalIgnoreCase)
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub TrimProcesses(Result As MemoryOptimizeResult, Watch As Stopwatch)
        Dim SelfId = Process.GetCurrentProcess().Id
        Dim SelfName = Path.GetFileNameWithoutExtension(PathExe)
        Dim SessionId = Process.GetCurrentProcess().SessionId
        Dim ForegroundId = GetForegroundProcessId()
        Dim Candidates As New List(Of Process)
        Dim Skipped As Integer = 0
        For Each Item As Process In Process.GetProcesses()
            Try
                Dim Name = Item.ProcessName
                If Item.Id = SelfId OrElse Item.Id = ForegroundId OrElse
                   String.Equals(Name, SelfName, StringComparison.OrdinalIgnoreCase) OrElse
                   IsCriticalName(Name) OrElse IsJavaName(Name) Then
                    Skipped += 1
                    Continue For
                End If
                If Item.SessionId <> SessionId Then
                    Skipped += 1
                    Continue For
                End If
                If Item.WorkingSet64 < TrimThreshold Then
                    Skipped += 1
                    Continue For
                End If
                Candidates.Add(Item)
            Catch ex As Exception
                Skipped += 1
            End Try
        Next
        Result.Skipped = Skipped
        Result.Candidates = Candidates.Count
        Candidates.Sort(Function(A, B) B.WorkingSet64.CompareTo(A.WorkingSet64))
        Dim Count = Math.Min(MaxCandidates, Candidates.Count)
        For Index As Integer = 0 To Count - 1
            If Watch.ElapsedMilliseconds > ChildBudgetMs Then
                Result.Truncated = True
                Exit For
            End If
            Dim Item = Candidates(Index)
            Dim Handle As IntPtr = IntPtr.Zero
            Try
                Handle = OpenProcess(ProcessAccess, False, Item.Id)
                If Handle = IntPtr.Zero Then
                    Result.Denied += 1
                    Continue For
                End If
                Dim Before = Item.WorkingSet64
                If EmptyWorkingSet(Handle) Then
                    Item.Refresh()
                    Result.Trimmed += 1
                    Dim Freed = Before - Item.WorkingSet64
                    If Freed > 0 Then Result.FreedWorkSetBytes += Freed
                Else
                    Result.Denied += 1
                End If
            Catch ex As Exception
                If Result.Failures.Count < 20 Then
                    Result.Failures.Add(New MemoryOptimizeFailure With {.Pid = Item.Id, .Name = SafeProcessName(Item), .Reason = ex.Message})
                End If
            Finally
                If Handle <> IntPtr.Zero Then CloseHandle(Handle)
            End Try
        Next
    End Sub

    Private Function SafeProcessName(Item As Process) As String
        Try
            Return Item.ProcessName
        Catch ex As Exception
            Return "?"
        End Try
    End Function

    ''' <summary>
    ''' 清这些进程会蓝屏、掉登录、闪任务栏或触发安全软件，一律跳过。
    ''' </summary>
    Private Function IsCriticalName(Name As String) As Boolean
        Select Case Name.ToLowerInvariant()
            Case "system", "idle", "registry", "memory compression", "memcompression", "secure system",
                 "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "fontdrvhost",
                 "explorer", "dwm", "audiodg", "shellexperiencehost", "startmenuexperiencehost",
                 "searchhost", "searchapp", "textinputhost", "runtimebroker",
                 "msmpeng", "nissrv", "mpdefendercoreservice", "securityhealthservice", "sense"
                Return True
        End Select
        Return False
    End Function

    Private Function IsJavaName(Name As String) As Boolean
        Dim Lower = Name.ToLowerInvariant()
        Return Lower = "java" OrElse Lower = "javaw" OrElse Lower = "javaws"
    End Function

    Private Function GetForegroundProcessId() As Integer
        Try
            Dim Handle = GetForegroundWindow()
            If Handle = IntPtr.Zero Then Return -1
            Dim Id As Integer
            GetWindowThreadProcessId(Handle, Id)
            Return Id
        Catch ex As Exception
            Return -1
        End Try
    End Function

    Private Sub TryEnableDebugPrivilege()
        Dim Token As IntPtr = IntPtr.Zero
        Try
            If Not OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges Or TokenQuery, Token) Then Return
            Dim Privilege As New LuidValue
            If Not LookupPrivilegeValue(Nothing, "SeDebugPrivilege", Privilege) Then Return
            Dim State As New TokenPrivileges With {
                .PrivilegeCount = 1,
                .Luid = Privilege,
                .Attributes = SePrivilegeEnabled
            }
            AdjustTokenPrivileges(Token, False, State, 0, IntPtr.Zero, IntPtr.Zero)
        Catch ex As Exception
            '没有调试权限也能处理同用户的进程，忽略
        Finally
            If Token <> IntPtr.Zero Then CloseHandle(Token)
        End Try
    End Sub

    Private Sub WriteResult(PathFull As String, Result As MemoryOptimizeResult)
        Dim Json = JsonConvert.SerializeObject(Result, Formatting.None)
        Dim TempPath = PathFull & ".tmp"
        File.WriteAllText(TempPath, Json, New UTF8Encoding(False))
        If File.Exists(PathFull) Then File.Delete(PathFull)
        File.Move(TempPath, PathFull)
    End Sub

    Private Function FormatSize(Bytes As Long) As String
        If Bytes >= 1024L * 1024L * 1024L Then Return $"{Bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB"
        Return $"{Bytes / 1024.0 / 1024.0:0.0} MB"
    End Function

    '==== P/Invoke ====

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function OpenProcess(DesiredAccess As Integer, InheritHandle As Boolean, ProcessId As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function CloseHandle(Handle As IntPtr) As Boolean
    End Function

    <DllImport("psapi.dll", SetLastError:=True)>
    Private Function EmptyWorkingSet(hProcess As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function OpenProcessToken(ProcessHandle As IntPtr, DesiredAccess As Integer, ByRef TokenHandle As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function LookupPrivilegeValue(SystemName As String, PrivilegeName As String, ByRef Luid As LuidValue) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function AdjustTokenPrivileges(TokenHandle As IntPtr, DisableAllPrivileges As Boolean, ByRef NewState As TokenPrivileges,
                                           BufferLength As Integer, PreviousState As IntPtr, ReturnLength As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef ProcessId As Integer) As Integer
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure LuidValue
        Public LowPart As Integer
        Public HighPart As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure TokenPrivileges
        Public PrivilegeCount As Integer
        Public Luid As LuidValue
        Public Attributes As Integer
    End Structure

End Module

''' <summary>
''' 内存优化的结果，同时用作子进程与父进程之间的 JSON 交换格式。
''' </summary>
Public Class MemoryOptimizeResult
    Public Property Schema As Integer = 1
    Public Property Elevated As Boolean = True
    Public Property Candidates As Integer
    Public Property Trimmed As Integer
    Public Property Skipped As Integer
    Public Property Denied As Integer
    Public Property FreedWorkSetBytes As Long
    Public Property FreedPhysicalBytes As Long
    Public Property ElapsedMs As Long
    Public Property Truncated As Boolean
    Public Property ErrorMessage As String
    Public Property ExitCode As Integer
    Public Property Failures As New List(Of MemoryOptimizeFailure)
End Class

Public Class MemoryOptimizeFailure
    Public Property Pid As Integer
    Public Property Name As String
    Public Property Reason As String
End Class
