''' <summary>
''' 插件页左侧栏：插件管理 / 扩展点 / 开发说明。
''' 结构与 PageSetupLeft 完全一致，直接挂在主窗口的 PanMainLeft 上。
''' </summary>
Public Class PagePluginLeft

    Public Property PageID As Integer = 0

    ''' <summary>
    ''' 勾选事件切换右侧分区。
    ''' </summary>
    Private Sub PageCheck(sender As MyListItem, e As EventArgs) Handles ItemManage.Check, ItemExtend.Check, ItemDoc.Check
        '尚未初始化控件属性时 sender.Tag 为 Nothing，直接跳过
        If sender.Tag Is Nothing Then Return
        PageID = CInt(Val(sender.Tag.ToString()))
        If FrmPlugin IsNot Nothing Then FrmPlugin.SwitchSection(PageID)
    End Sub

    ''' <summary>
    ''' 由右侧页面反向同步左栏选中项（例如从插件列表点「扩展点」跳转）。
    ''' </summary>
    Public Sub SyncSection(Index As Integer)
        PageID = Index
        CType(PanItem.Children(Index), MyListItem).SetChecked(True, False, False)
    End Sub

    Public Sub ResetCheck()
        CType(PanItem.Children(PageID), MyListItem).SetChecked(True, True, False)
    End Sub

End Class
