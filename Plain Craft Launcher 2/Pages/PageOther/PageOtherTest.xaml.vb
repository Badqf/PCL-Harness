Imports System.IO
Imports System.Diagnostics
Imports System.Runtime.InteropServices

Public Class PageOtherTest

    ' ==== 内存优化 ====

    <DllImport("psapi.dll")>
    Private Shared Function EmptyWorkingSet(hProcess As IntPtr) As Boolean
    End Function

    ''' <summary>
    ''' 把启动器占用的物理内存交还给系统。只处理当前进程，不影响游戏。
    ''' </summary>
    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        Try
            Dim Proc = Process.GetCurrentProcess()
            Dim Before = Proc.WorkingSet64
            EmptyWorkingSet(Proc.Handle)
            GC.Collect()
            GC.WaitForPendingFinalizers()
            Proc.Refresh()
            Dim Freed = Math.Max(0, Before - Proc.WorkingSet64)
            If ShowHint Then
                If Freed >= 1024 * 1024 Then
                    Hint($"已释放 {Freed / 1024 / 1024:0.0} MB 内存", HintType.Green)
                Else
                    Hint("内存已经比较干净了，没什么可释放的", HintType.Blue)
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, "内存优化失败")
            If ShowHint Then Hint("内存优化失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        MemoryOptimize(ShowHint)
    End Sub

    ' ==== 清理日志与临时文件 ====

    ''' <summary>
    ''' 清理启动器日志与缓存目录中的旧临时文件。
    ''' </summary>
    Public Shared Sub RubbishClear()
        Dim DeletedCount As Integer = 0
        Dim DeletedSize As Long = 0
        Try
            '启动器日志
            For Each FilePath In Directory.GetFiles(Paths.BaseThenName, "Log*.txt")
                Try
                    DeletedSize += New FileInfo(FilePath).Length
                    File.Delete(FilePath)
                    DeletedCount += 1
                Catch
                    '正在写入的日志跳过
                End Try
            Next
            '缓存目录中超过 1 小时的临时文件
            If Directory.Exists(PathTemp) Then
                For Each FilePath In Directory.GetFiles(PathTemp, "*", SearchOption.AllDirectories)
                    Try
                        Dim Info As New FileInfo(FilePath)
                        If Info.LastWriteTime > Date.Now.AddHours(-1) Then Continue For
                        DeletedSize += Info.Length
                        File.Delete(FilePath)
                        DeletedCount += 1
                    Catch
                        '被占用的文件跳过
                    End Try
                Next
            End If
            Hint($"已清理 {DeletedCount} 个文件，释放 {DeletedSize / 1024 / 1024:0.0} MB 空间", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "清理日志失败")
            Hint("清理失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    ' ==== 自定义下载 ====

    ''' <summary>
    ''' 从指定网址下载文件。Folder 为空时下载到 PCLHarness\Download。
    ''' </summary>
    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        RunInThread(
        Sub()
            Try
                If String.IsNullOrWhiteSpace(Url) Then Throw New Exception("下载地址不能为空")
                Dim TargetFolder As String = If(String.IsNullOrWhiteSpace(Folder), Paths.BaseThenName & "Download", Folder)
                Directory.CreateDirectory(TargetFolder)
                If String.IsNullOrWhiteSpace(FileName) Then FileName = Url.AfterLast("/").BeforeFirst("?")
                If String.IsNullOrWhiteSpace(FileName) Then FileName = "download.bin"
                Dim Target As String = Path.Combine(TargetFolder, FileName)
                Hint($"开始下载：{FileName}")
                NetDownloadByLoader(Url, Target)
                Hint($"下载完成：{FileName}", HintType.Green)
            Catch ex As Exception
                Logger.Error(ex, "自定义下载失败：" & Url)
                Hint("下载失败：" & ex.Message, HintType.Red)
            End Try
        End Sub)
    End Sub

    ' ==== 今日人品 ====

    Public Shared Sub Jrrp()
        Dim Seed As Integer = 17
        For Each Ch In (Date.Now.ToString("yyyyMMdd") & "PCLHarness")
            Seed = (Seed * 131 + AscW(Ch)) Mod 1000003
        Next
        Dim Value As Integer = Seed Mod 101
        Dim Comment As String
        Select Case Value
            Case 90 To 100
                Comment = "宜开新档、宜下整合包，忌手滑删存档。"
            Case 70 To 89
                Comment = "状态不错，适合折腾 Mod，记得先备份。"
            Case 40 To 69
                Comment = "普普通通的一天，不如去把整合包更新一下。"
            Case 15 To 39
                Comment = "建议今天只玩原版，装 Mod 容易出事。"
            Case Else
                Comment = "今天不宜启动游戏，宜整理文件夹。"
        End Select
        Hint($"今日人品 {Value}%：{Comment}", If(Value >= 70, HintType.Green, HintType.Blue))
    End Sub

    ' ==== 随机文本 ====

    Private Shared ReadOnly Rand As New Random()

    Private Shared ReadOnly Hints As String() = {
        "按 F11 可以快速切换深色 / 浅色外观。",
        "在「设置 → 个性化 → 预设」里可以一键换主色调。",
        "插件不只是装饰：扩展点里能调每个插件的设置，还能搜索。",
        "联机直连模式下，房主把「地址|邀请码」整段发给朋友就行。",
        "启动器数据都在 PCLHarness 文件夹里，备份就是复制这个文件夹。",
        "第一次使用可以从别的 PCL 一键迁移旧设置，不用重新配。",
        "更多 → 百宝箱 里的自定义下载支持任意 http 直链。",
        "日志太大占地方时，可以在百宝箱里一键清理。"
    }

    Private Shared ReadOnly Caves As String() = {
        "你听见洞穴深处传来一声叹息。",
        "有人在矿洞尽头放了一支火把，然后就没有然后了。",
        "这一层的刷怪笼已经被拆了，但声音还在。",
        "墙上的告示写着：「别往下挖了」。",
        "你好像听到了水流声，可周围全是干的。"
    }

    Private Shared ReadOnly PresetHints As String() = {
        "蓝色是默认配色，青色偏清爽，紫色适合夜间。",
        "饱和度调低会更接近原版 PCL 的观感。",
        "亮度别拉太满，留一点对比度图标才清楚。",
        "渐变值影响标题栏与卡片的明暗过渡。"
    }

    Public Shared Function GetRandomHint() As String
        Return Hints(Rand.Next(Hints.Length))
    End Function

    Public Shared Function GetRandomCave() As String
        Return Caves(Rand.Next(Caves.Length))
    End Function

    Public Shared Function GetRandomPresetHint() As String
        Return PresetHints(Rand.Next(PresetHints.Length))
    End Function

    ' ==== 页面事件 ====

    Private Sub PageOtherTest_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        LabCleanPath.Text = $"清理范围：{Paths.BaseThenName} 下的日志文件，以及缓存目录中使用超过 1 小时的临时文件。"
        LabTip.Text = GetRandomHint()
    End Sub

    Private Sub BtnMemory_Click(sender As Object, e As MouseButtonEventArgs)
        RunInThread(Sub() MemoryOptimize(True))
    End Sub

    Private Sub BtnClean_Click(sender As Object, e As MouseButtonEventArgs)
        RunInThread(Sub() RubbishClear())
    End Sub

    Private Sub BtnTip_Click(sender As Object, e As MouseButtonEventArgs)
        LabTip.Text = GetRandomHint()
    End Sub

    Private Sub BtnDownload_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Url As String = TxtUrl.Text.Trim()
        If Not (Url.StartsWithF("http://", True) OrElse Url.StartsWithF("https://", True)) Then
            Hint("下载地址需要以 http:// 或 https:// 开头", HintType.Red)
            Return
        End If
        StartCustomDownload(Url, TxtName.Text.Trim())
    End Sub

End Class
