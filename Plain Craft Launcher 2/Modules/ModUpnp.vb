Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Xml.Linq

''' <summary>
''' 极简 UPnP IGD 客户端：只做「查询公网 IP」和「添加端口映射」两件事。
''' 不依赖任何外部服务，全部在局域网内向路由器询问。
''' </summary>
Public Module ModUpnp

    Private Const SsdpAddress As String = "239.255.255.250"
    Private Const SsdpPort As Integer = 1900

    Private Structure IgdService
        Public ControlUrl As String
        Public ServiceType As String
    End Structure

    ''' <summary>
    ''' 尝试在路由器上建立 TCP + UDP 端口映射，成功返回 True 并给出公网端点（IP:端口）。
    ''' 失败不抛异常，返回 False 并给出原因。
    ''' </summary>
    Public Function TryMapPort(InternalPort As Integer, ByRef PublicEndpoint As String, ByRef FailReason As String) As Boolean
        Try
            Dim LocalIp As String = GetLanIPv4()
            If String.IsNullOrEmpty(LocalIp) Then
                FailReason = "没有找到局域网 IPv4 地址"
                Return False
            End If
            Dim Igd = DiscoverIgd()
            If Igd.ControlUrl Is Nothing Then
                FailReason = "路由器未开启 UPnP 或未响应"
                Return False
            End If
            Dim ExtIp = GetExternalIp(Igd)
            For Each Proto In {"TCP", "UDP"}
                Dim Ok As Boolean, Msg As String = ""
                Ok = AddPortMapping(Igd, Proto, InternalPort, LocalIp, Msg)
                If Not Ok Then
                    FailReason = $"{Proto} 端口映射失败：{Msg}"
                    Return False
                End If
            Next
            If String.IsNullOrEmpty(ExtIp) Then ExtIp = GetExternalIp(Igd)
            If String.IsNullOrEmpty(ExtIp) Then
                FailReason = "无法读取路由器公网 IP"
                Return False
            End If
            PublicEndpoint = $"{ExtIp}:{InternalPort}"
            Logger.Info($"UPnP 映射成功：{LocalIp}:{InternalPort} → {PublicEndpoint}")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "UPnP 端口映射出现异常")
            FailReason = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>当前机器在局域网中的 IPv4（取默认路由网卡）。</summary>
    Public Function GetLanIPv4() As String
        Try
            Using Client As New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                Client.Connect("223.5.5.5", 53) ' 不会真的发包，只是让系统选路由
                Return CType(Client.LocalEndPoint, IPEndPoint).Address.ToString()
            End Using
        Catch
            Try
                For Each Nic In NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    If Nic.OperationalStatus <> NetworkInformation.OperationalStatus.Up Then Continue For
                    For Each Addr In Nic.GetIPProperties().UnicastAddresses
                        If Addr.Address.AddressFamily = AddressFamily.InterNetwork AndAlso Not IPAddress.IsLoopback(Addr.Address) Then
                            Return Addr.Address.ToString()
                        End If
                    Next
                Next
            Catch
            End Try
            Return Nothing
        End Try
    End Function

    ''' <summary>通过 SSDP 找到家里路由器的 IGD 控制地址。</summary>
    Private Function DiscoverIgd() As IgdService
        Dim Result As New IgdService
        Try
            Dim Request As String =
                "M-SEARCH * HTTP/1.1" & vbCrLf &
                $"HOST: {SsdpAddress}:{SsdpPort}" & vbCrLf &
                "MAN: ""ssdp:discover""" & vbCrLf &
                "MX: 2" & vbCrLf &
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1" & vbCrLf & vbCrLf
            Dim Bytes = Encoding.ASCII.GetBytes(Request)
            Using Udp As New UdpClient()
                Udp.Client.ReceiveTimeout = 2500
                Udp.Send(Bytes, Bytes.Length, New IPEndPoint(IPAddress.Parse(SsdpAddress), SsdpPort))
                Dim Deadline = Date.Now.AddMilliseconds(2500)
                While Date.Now < Deadline
                    Dim Remote As New IPEndPoint(IPAddress.Any, 0)
                    Dim Data As Byte()
                    Try
                        Data = Udp.Receive(Remote)
                    Catch ex As SocketException
                        Exit While
                    End Try
                    Dim Text = Encoding.ASCII.GetString(Data)
                    Dim Location As String = Nothing
                    For Each Line In Text.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                        If Line.StartsWithF("LOCATION:", True) Then Location = Line.Substring(9).Trim()
                    Next
                    If String.IsNullOrEmpty(Location) Then Continue While
                    Dim Desc = LoadXml(Location)
                    If Desc Is Nothing Then Continue While
                    Dim Service = FindWanService(Desc, Location)
                    If Service.ControlUrl Is Nothing Then Continue While
                    Return Service
                End While
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "SSDP 发现失败")
        End Try
        Return Result
    End Function

    Private Function LoadXml(Url As String) As XDocument
        Try
            Using Wc As New Net.WebClient()
                Wc.Proxy = Nothing
                Wc.Headers(HttpRequestHeader.UserAgent) = "PCL-Harness"
                Dim Text = Wc.DownloadString(Url)
                Return XDocument.Parse(Text)
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"读取路由器描述失败：{Url}")
            Return Nothing
        End Try
    End Function

    ''' <summary>在设备描述里找 WANIPConnection / WANPPPConnection 服务。</summary>
    Private Function FindWanService(Desc As XDocument, BaseUrl As String) As IgdService
        Dim Result As New IgdService
        Dim ns = Desc.Root.Name.Namespace
        For Each Svc In Desc.Descendants(ns + "service")
            Dim Type = Svc.Element(ns + "serviceType")?.Value
            Dim Ctrl = Svc.Element(ns + "controlURL")?.Value
            If String.IsNullOrEmpty(Type) OrElse String.IsNullOrEmpty(Ctrl) Then Continue For
            If Not (Type.Contains("WANIPConnection") OrElse Type.Contains("WANPPPConnection")) Then Continue For
            Result.ServiceType = Type
            Result.ControlUrl = ResolveUrl(BaseUrl, Ctrl)
            Return Result
        Next
        Return Result
    End Function

    Private Function ResolveUrl(BaseUrl As String, Relative As String) As String
        If Relative.StartsWithF("http", True) Then Return Relative
        Dim Base As New Uri(BaseUrl)
        Return New Uri(Base, Relative).ToString()
    End Function

    Private Function SoapRequest(Igd As IgdService, Action As String, Body As String) As String
        Dim Envelope =
            "<?xml version=""1.0""?>" &
            "<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">" &
            $"<s:Body><u:{Action} xmlns:u=""{Igd.ServiceType}"">{Body}</u:{Action}></s:Body></s:Envelope>"
        Using Client As New Net.WebClient()
            Client.Proxy = Nothing
            Client.Headers(HttpRequestHeader.ContentType) = "text/xml; charset=""utf-8"""
            Client.Headers.Add("SOAPAction", $"""{Igd.ServiceType}#{Action}""")
            Dim Bytes = Encoding.UTF8.GetBytes(Envelope)
            Dim Reply = Client.UploadData(Igd.ControlUrl, "POST", Bytes)
            Return Encoding.UTF8.GetString(Reply)
        End Using
    End Function

    Private Function AddPortMapping(Igd As IgdService, Proto As String, Port As Integer, LocalIp As String, ByRef ErrMsg As String) As Boolean
        Dim Body =
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{Port}</NewExternalPort><NewProtocol>{Proto}</NewProtocol>" &
            $"<NewInternalPort>{Port}</NewInternalPort><NewInternalClient>{LocalIp}</NewInternalClient>" &
            "<NewEnabled>1</NewEnabled><NewPortMappingDescription>PCL Harness Link</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration>"
        Try
            SoapRequest(Igd, "AddPortMapping", Body)
            Return True
        Catch ex As Exception
            ErrMsg = ex.Message
            Return False
        End Try
    End Function

    Private Function GetExternalIp(Igd As IgdService) As String
        Try
            Dim Reply = SoapRequest(Igd, "GetExternalIPAddress", "<NewRemoteHost></NewRemoteHost>")
            Dim Doc = XDocument.Parse(Reply)
            Return Doc.Descendants().FirstOrDefault(Function(e) e.Name.LocalName = "NewExternalIPAddress")?.Value
        Catch ex As Exception
            Logger.Warn(ex, "读取路由器公网 IP 失败")
            Return Nothing
        End Try
    End Function

End Module
