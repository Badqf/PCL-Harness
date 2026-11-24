'本文件负责把当前实例导出成整合包。
'支持三种格式：Modrinth（modrinth.index.json）、CurseForge（manifest.json）、纯压缩包（不带清单）。
'都走 overrides-only：不联网匹配平台项目 ID，只保证"能装回一个同样内容的实例"。

Imports Newtonsoft.Json.Linq

Public Module ModModpackExport

    ''' <summary>导出格式。</summary>
    Public Enum ModpackExportFormat
        ''' <summary>Modrinth 的 .mrpack（modrinth.index.json）。</summary>
        Modrinth = 0
        ''' <summary>CurseForge 的 manifest.json 结构。</summary>
        CurseForge = 1
        ''' <summary>纯压缩包：直接把目录打进 zip，不带任何清单。</summary>
        PlainZip = 2
    End Enum

    ''' <summary>默认导出哪些目录（相对实例根）。</summary>
    Public ReadOnly DefaultExportFolders As String() = {"mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks", "scripts"}

    ''' <summary>把实例导出成整合包，返回实际生成的文件路径。</summary>
    Public Function ExportInstance(Instance As McInstance, PackName As String, PackVersion As String,
                                   TargetPath As String, Optional Folders As List(Of String) = Nothing,
                                   Optional Format As ModpackExportFormat = ModpackExportFormat.Modrinth) As String
        If Instance Is Nothing Then Throw New ArgumentNullException(NameOf(Instance))
        If String.IsNullOrWhiteSpace(TargetPath) Then Throw New ArgumentException("导出路径不能为空。", NameOf(TargetPath))

        Dim UseFolders As List(Of String) = If(Folders Is Nothing OrElse Folders.Count = 0,
                                                DefaultExportFolders.ToList(), Folders)
        Dim WorkRoot As String = RequestTaskTempFolder() & "modpack\"
        Dim ContentRoot As String = If(Format = ModpackExportFormat.PlainZip, WorkRoot, WorkRoot & "overrides\")
        DirectoryUtils.Create(ContentRoot)

        '复制目录
        For Each Folder As String In UseFolders
            Dim Name As String = If(String.IsNullOrWhiteSpace(Folder), "", Folder.Trim().Replace("/"c, "\"c))
            If Name = "" OrElse Name.Contains("..") Then Continue For
            Dim Source As String = Instance.PathIndie & Name & "\"
            If Not DirectoryUtils.Exists(Source) Then Continue For
            DirectoryUtils.Create(ContentRoot & Name & "\")
            DirectoryUtils.Copy(Source, ContentRoot & Name & "\")
        Next

        '清单文件
        Select Case Format
            Case ModpackExportFormat.Modrinth
                Dim Dependencies As New JObject From {{"minecraft", Instance.Version.VanillaName}}
                If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then Dependencies.Add("forge", Instance.Version.ModLoaderVersions(ModLoaders.Forge))
                If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then Dependencies.Add("fabric-loader", Instance.Version.ModLoaderVersions(ModLoaders.Fabric))
                If Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then Dependencies.Add("neoforge", Instance.Version.ModLoaderVersions(ModLoaders.NeoForge))
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
            Case ModpackExportFormat.CurseForge
                Dim Loaders As New JArray
                If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then
                    Loaders.Add(New JObject From {{"id", "forge-" & Instance.Version.ModLoaderVersions(ModLoaders.Forge)}, {"primary", True}})
                End If
                If Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then
                    Loaders.Add(New JObject From {{"id", "neoforge-" & Instance.Version.ModLoaderVersions(ModLoaders.NeoForge)}, {"primary", True}})
                End If
                If Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then
                    Loaders.Add(New JObject From {{"id", "fabric-" & Instance.Version.ModLoaderVersions(ModLoaders.Fabric)}, {"primary", True}})
                End If
                Dim Manifest As New JObject From {
                    {"minecraft", New JObject From {{"version", Instance.Version.VanillaName}, {"modLoaders", Loaders}}},
                    {"manifestType", "minecraftModpack"},
                    {"manifestVersion", 1},
                    {"name", PackName},
                    {"version", PackVersion},
                    {"author", "PCL Harness"},
                    {"files", New JArray},
                    {"overrides", "overrides"}
                }
                FileUtils.Write(WorkRoot & "manifest.json", Manifest.ToString(Newtonsoft.Json.Formatting.Indented))
        End Select

        '打包
        Dim Parent As String = IO.Path.GetDirectoryName(TargetPath)
        If Not String.IsNullOrWhiteSpace(Parent) Then DirectoryUtils.Create(Parent & "\")
        If FileUtils.Exists(TargetPath) Then FileUtils.Delete(TargetPath)
        FileUtils.CreateZipFromDirectory(TargetPath, WorkRoot)
        Try
            DirectoryUtils.Delete(WorkRoot)
        Catch ex As Exception
            Logger.Warn(ex, "清理导出临时目录失败")
        End Try
        Logger.Info($"已导出整合包（{Format}）：{TargetPath}（{UseFolders.Join("/")}）")
        Return TargetPath
    End Function

End Module