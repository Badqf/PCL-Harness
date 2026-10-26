''' <summary>
''' 承载插件自定义页面的容器：把插件返回的任意用户控件包成启动器认可的右侧页面。
''' 页面切换动画、返回顶部等行为由 MyPageRight 统一提供。
''' </summary>
Public Class PagePluginHost
    Inherits MyPageRight

    Public Sub New(ContentElement As FrameworkElement)
        If ContentElement Is Nothing Then Return
        If TypeOf ContentElement Is MyScrollViewer Then
            Me.Child = ContentElement
        Else
            '默认套一层滚动容器，方便插件页面直接放长内容
            Dim Scroll As New MyScrollViewer With {
                .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                .HorizontalContentAlignment = HorizontalAlignment.Stretch
            }
            Scroll.Content = ContentElement
            Me.Child = Scroll
        End If
    End Sub

End Class
