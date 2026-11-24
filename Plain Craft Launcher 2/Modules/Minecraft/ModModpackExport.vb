'本文件负责把当前实例导出成 .mrpack（Modrinth 格式）。
'所有文件都放进 overrides/，files 留空：不联网匹配 Modrinth / CurseForge，
'因此导出结果只保证"能装回一个同样内容的实例"，不带原平台的项目 ID。

Imports System.IO
Imports Newtonsoft.Json.Linq

Public Module ModModpackExport

    ''' <summary>默认导出哪些目录（相对实例根）。</summary>
    Public ReadOnly DefaultExportFolders As String() = {"mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks", "scripts"}

    ''' <summary>
    ''' 把实例导出为 .mrpack（overrides-only）。返回实际生成的文件路径。
    ''' </summary>
    Public Function ExportInstance(Instance As McInstance, PackName As String, PackVersion As String,
                                   TargetPath As String, Optional Folders As List(Of String) = Nothing) As String
        If Instance Is Nothing Then Throw New ArgumentNullException(NameOf(Instance))
        If String.IsNullOrWhiteSpace(TargetPath) Then Throw New ArgumentException("导出路径不能为空。", NameOf(TargetPath))

        Dim UseFolders As List(Of String) = If(Folders Is Nothing OrElse Folders.Count = 0,
                                                DefaultExportFolders.ToList(), Folders)
        Dim WorkRoot As String = RequestTaskTempFolder() & "mrpack\"
        Dim OverridesRoot As String = WorkRoot & "overrides\"
        DirectoryUtils.Create(OverridesRoot)

        '复制目录
        For Each Folder As String In UseFolders
            Dim Name As String = If(String.IsNullOrWhiteSpace(Folder), "", Folder.Trim().Replace("/"c, "\"c))
            If Name = "" OrElse Name.Contains("..") Then Continue For
            Dim Source As String = Instance.PathIndie & Name & "\"
            If Not DirectoryUtils.Exists(Source) Then Continue For
            DirectoryUtils.Create(OverridesRoot & Name & "\")
            DirectoryUtils.Copy(Source, OverridesRoot & Name & "\")
        Next

        '依赖信息
        Dim Dependencies As New JObject From {{"minecraft", Instance.Version.VanillaName}}
        If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then
            Dependencies.Add("forge", Instance.Version.ModLoaderVersions(ModLoaders.Forge))
        End If
        If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then
            Dependencies.Add("fabric-loader", Instance.Version.ModLoaderVersions(ModLoaders.Fabric))
        End If
        If Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then
            Dependencies.Add("neoforge", Instance.Version.ModLoaderVersions(ModLoaders.NeoForge))
        End If

        '索引文件
        Dim Index As New JObject From {
            {"game", "minecraft"},
            {"formatVersion", 1},
            {"versionId", PackVersion},
            {"name", PackName},
            {"summary", Instance.Info},
            {"files", New JArray},
            {"dependencies", Dependencies}
        }
        FileUtils.Write(WorkRoot & "modrinth.index.json", Index.ToString(Newtonsoft.Json.Formatting.Indented))

        '打包
        Dim Parent As String = Path.GetDirectoryName(TargetPath)
        If Not String.IsNullOrWhiteSpace(Parent) Then DirectoryUtils.Create(Parent & "\")
        If FileUtils.Exists(TargetPath) Then FileUtils.Delete(TargetPath)
        FileUtils.CreateZipFromDirectory(TargetPath, WorkRoot)
        Try
            DirectoryUtils.Delete(WorkRoot)
        Catch ex As Exception
            Logger.Warn(ex, "清理导出临时目录失败")
        End Try
        Logger.Info($"已导出整合包：{TargetPath}（{UseFolders.Join("/")}）")
        Return TargetPath
    End Function

End Module