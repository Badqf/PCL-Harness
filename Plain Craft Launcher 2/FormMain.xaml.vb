Imports System.ComponentModel
Imports System.Windows.Interop

Public Class FormMain

#Region "基础"

    '更新日志
    ''' <summary>待展示的「已更新至…」弹窗对应的旧版本号；-1 表示没有。</summary>
    Private Shared PendingUpdateLogVersion As Integer = -1

    ''' <summary>展示待处理的更新日志（需在主线程调用）。返回是否展示过。</summary>
    Private Shared Function ShowPendingUpdateLog() As Boolean
        If PendingUpdateLogVersion < 0 Then Return False
        Dim FromVersion As Integer = PendingUpdateLogVersion
        PendingUpdateLogVersion = -1
        If FrmMain IsNot Nothing Then FrmMain.ShowUpdateLog(FromVersion)
        Return True
    End Function

    Private Sub ShowUpdateLog(LastVersion As Integer)
        Dim FeatureCount As Integer = 0, BugCount As Integer = 0
        Dim FeatureList As New List(Of KeyValuePair(Of Integer, String))
        '统计更新日志条目
        If BuildType = BuildTypes.Release Then
        If LastVersion < 447 Then 'PCL Harness 3.6.8
            BugCount += 2
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：API v5 插件调用 PluginEventBus.On 时报「找不到方法」——v6 给 On 加了可选参数改变了 CLR 签名，现已补回两参数重载"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：API v5 插件订阅「页面切换」时收到整数而声明的是字符串，导致类型转换异常；事件总线现在按处理器参数类型自动适配"))
        End If
        If LastVersion < 446 Then 'PCL Harness 3.6.7
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：插件卡片与权限确认框把「签名」改称「发布者声明（未验证）」，避免被误认为已验签"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：trust.json 升级为 schema 2，权限记录改名为 permissionsHash（旧文件可继续读取并自动升级）"))
            FeatureCount += 2
            BugCount += 0
        End If
        If LastVersion < 445 Then 'PCL Harness 3.6.6
            ' 插件信任：启用前确认权限
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：插件启用前会先列出它申请的权限，确认后才加载；权限有变化会再次询问"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：插件卡片会标注「未签名」并显示来源提示，确认记录保存在 Plugins\trust.json"))
            FeatureCount += 2
        End If
        If LastVersion < 444 Then 'PCL Harness 3.6.5
            Logger.Info($"[Update] 版本号 {LastVersion} → {VersionCode}，稍后展示更新日志")
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：插件页勾选「启用」后状态没保存（热重载把 plugins.json 当成插件内容，自己写文件又触发重载，来回覆盖成空）"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：插件启用状态改由内存里的权威集合写入，热重载不再重建页面正在用的对象"))
            FeatureCount += 0
            BugCount += 1
        End If
        If LastVersion < 443 Then 'PCL Harness 3.6.4
            Logger.Info($"[Update] 版本号 {LastVersion} → {VersionCode}，稍后展示更新日志")
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：实例右键菜单挂到了文件夹上，现在真正出现在「版本选择」里的实例上（含插件菜单项与「实例设置」）"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：去掉窗口右下角那行插件状态灰字，插件状态统一显示在插件页的卡片里"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：开发版 SDK 附带「实例菜单示例」插件源码，演示 API v6.2 的菜单、后台任务与事件"))
            FeatureCount += 2
            BugCount += 1
        End If
        If LastVersion < 442 Then 'PCL Harness 3.6.3
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：插件 API v6.2——实例右键菜单扩展点，插件可以往「启动」页实例列表的右键菜单里加自己的操作（实现 IPclPluginInstanceMenu 即可）"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：插件提供的菜单项在点击时统一隔离异常，出错只记一次失败、不会打断启动器"))
            FeatureCount += 2
            BugCount += 0
        End If
        If LastVersion < 441 Then 'PCL Harness 3.6.2
            'PCL Harness 3.6.2
            FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：插件健康状态——插件管理卡片会显示最近失败次数、最近异常，并可一键「重置失败计数」"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：插件通过接口回调（页面切换、游戏启动/退出、联机、下载、退出前）抛出的异常之前只写日志、不计入熔断；现在统一隔离并计入失败次数"))
            FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：失败次数写入 Plugins\fuse.json，重启后仍能看到「上次会话连续失败 N 次，已自动禁用」"))
            FeatureCount += 2
            BugCount += 1
        End If
            If LastVersion < 440 Then 'PCL Harness 3.6.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：欢迎页去掉过期的「需要填 Secrets.json」提示，以及「不依赖官方服务」「凭据留在本地」两条；插件框架说明更新到 API v6"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：插件页左侧「开发说明」不再额外留白，按正常顺序排在最后"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：开发说明里的示例位置改为「开发版 SDK」，并补充 build.ps1 用法"))
                FeatureCount += 3
                BugCount += 0
            End If
            If LastVersion < 439 Then 'PCL Harness 3.6.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：插件连续出错 3 次会自动禁用，并在列表里标记「已熔断」，重新启用即清零重试"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：插件管理改成卡片式布局，每个插件一块，显示作者、状态、权限、依赖与最近事件，并提供「打开文件夹」「事件记录」"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：插件页左侧分区改名为「插件管理 / 插件设置 / 控制台 / 开发说明」，开发说明固定在最底部"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：控制台日志框缩短，命令按钮不再被挤出窗口；开发说明补充 v6 事件、服务、任务、熔断与示例说明"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：插件的事件总线收不到「页面切换」事件（之前只在代码里广播，真实导航没有触发）"))
                FeatureCount += 4
                BugCount += 1
            End If
            If LastVersion < 438 Then 'PCL Harness 3.5.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：开发模式改成插件页左侧独立的分区，不再和插件列表挤在一起"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：开发模式的控制台日志从顶部开始向下填充，并始终滚动到最新一行"))
                FeatureCount += 2
                BugCount += 0
            End If
            If LastVersion < 437 Then 'PCL Harness 3.5.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：插件 API v6——事件总线覆盖启动器就绪、页面切换、游戏启动前后与退出、版本切换、主题切换、联机、下载、关闭前，共 13 类事件，支持优先级和一次性订阅"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：插件之间可以注册和取用服务（Host.Services），插件卸载时自动清理它注册的内容"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：插件可以起后台任务（Host.Tasks.Run），任务会出现在「任务管理」里，可以显示进度、可以取消"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：插件页的「开发模式」——实时日志窗口和命令回显（help / events / plugins / services / tasks / emit / clear）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：插件基类 PclPluginBase，只需 Overrides 自己关心的事件，不用把 7 个接口全部实现一遍"))
                FeatureCount += 5
                BugCount += 0
            End If
            If LastVersion < 436 Then 'PCL Harness 3.4.14
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：联机页 TUN 勾选框与旁边文字不同步的进入动画（现在方框和文字一起淡入）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：「用上次端口直接开房」按钮与 TUN 勾选框的间距，不再挤在一起"))
                FeatureCount += 1
                BugCount += 1
            End If
            If LastVersion < 435 Then 'PCL Harness 3.4.13
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：正版登录页未登录时不再显示一长串说明，具体步骤由登录弹窗给出"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：首次启动时会尝试从旧版开源构建导入正版登录信息（官方 PCL 的加密数据无法解密，需要重新登录一次）"))
                FeatureCount += 2
                BugCount += 0
            End If
            If LastVersion < 434 Then 'PCL Harness 3.4.12
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：微软正版登录正式可用——AppID 已通过 Mojang 审核，登录页不再提示「审核中」"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：正版登录失败时的提示改为按真实原因说明（账号没买 Java 版 / 代码输错了账号等）"))
                FeatureCount += 2
                BugCount += 0
            End If
            If LastVersion < 433 Then 'PCL Harness 3.4.11
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：TUN 提权重启后现在会自动跳回联机页，并且「用上次端口直接开房」按钮能正常显示和使用（重启前就会记住端口）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：设置页去掉「启动时自动打开联机页」开关，改成只有提权重启后自动跳转一次"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：TUN 开关的确认框不再卡住勾选动画"))
                FeatureCount += 2
                BugCount += 1
            End If
            If LastVersion < 432 Then 'PCL Harness 3.4.10
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：联机主界面加了「虚拟局域网（TUN）模式」开关，勾选后会提示将自动以管理员身份重启，并在重启前告诉你要在 UAC 窗口点「是」"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：启动时会自动进入联机页（可在设置里关闭），并把上次用的端口做成「用上次端口直接开房」按钮"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：联机页按钮大小统一，「联机诊断」按钮改小；联机设置页精简，去掉贡献节点等无用入口，只保留 P2P 直连"))
                FeatureCount += 3
                BugCount += 0
            End If
            If LastVersion < 431 Then 'PCL Harness 3.4.9
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：开启 TUN 模式后创建/加入房间时，启动器会自动以管理员身份重启，联机模块直接继承管理员权限——不再出现「只有安装虚拟网卡时弹了一次 UAC」这种半吊子状态"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：联机诊断里的启动器权限说明同步更新"))
                FeatureCount += 1
                BugCount += 1
            End If

            If LastVersion < 430 Then 'PCL Harness 3.4.8
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：联机诊断里的「自己的 NAT 类型」不再永远显示「未知」——直连模式关掉了 EasyTier 的 STUN，现在启动器自己做 STUN 探测，能判断是锥形 NAT 还是对称型 NAT"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：每次开房间重新探测 NAT，换网络后不会沿用旧结果"))
                FeatureCount += 1
                BugCount += 1
            End If

            If LastVersion < 429 Then 'PCL Harness 3.4.7
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：直连邀请现在会同时带上外网地址和局域网地址，外网的朋友也能直接加入，不再只给一条连不上的内网地址"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：跨网络加入失败时，会明确提示「邀请里只有内网地址」，并告诉房主该怎么开 UPnP 或端口映射"))
                FeatureCount += 1
                BugCount += 1
            End If

            If LastVersion < 428 Then 'PCL Harness 3.4.6
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：在装有 Hyper-V、WSL、Docker 的电脑上创建房间时，联机模块可能因为端口被系统预留而报「监听端口失败」并崩溃"))
                FeatureCount += 0
                BugCount += 1
            End If

            If LastVersion < 427 Then 'PCL Harness 3.4.5
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：联机页的完成界面加了「联机诊断」按钮，一键生成虚拟地址、监听端口、NAT 类型、与房主的连通性和最近内核日志，可整份复制"))
                FeatureCount += 1
                BugCount += 0
            End If

            If LastVersion < 426 Then 'PCL Harness 3.4.4
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：升级到新版本后启动一定会弹「PCL Harness 已更新至 …」，不再被历史最高版本号记录跳过"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：启动检测更新后如果已是最新版，会提示「当前为最新版 x.x.x」"))
                FeatureCount += 2
                BugCount += 1
            End If

            If LastVersion < 425 Then 'PCL Harness 3.4.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：启动时检测更新会弹出「正在检测更新……」提示，且开始检测的等待时间明显缩短"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：静默更新后启动不再闪出黑色命令行窗口；「PCL Harness 已更新至 …」弹窗恢复显示"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "调整：更新提示里的「稍后」只本次运行内生效；对外版本名统一显示为 PCL Harness（关于页保留「自编版」）"))
                FeatureCount += 3
                BugCount += 0
            End If

            If LastVersion < 424 Then 'PCL Harness 3.4.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "变更：首次启动改为自动迁移旧 PCL 数据，不再需要逐项勾选"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "变更：迁移完只弹一次结果页，列出本次复制了哪些内容；旧文件不删除、不修改"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：旧 PCL 目录里没有数据时不再弹迁移窗口"))
                FeatureCount += 3
                BugCount += 0
            End If

            If LastVersion < 423 Then 'PCL Harness 3.4.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：TUN 模式完成页直接显示「虚拟地址 10.114.114.1:端口」，LAN/公网地址自动收起"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：TUN 模式状态条显示直连/中继、RTT、双方 NAT 类型，鼠标悬停延迟图标可看详情"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：区分「网络没通」和「游戏端口没开」——房主没开房间、朋友连不上时给出具体提示"))
                FeatureCount += 3
                BugCount += 0
            End If

            If LastVersion < 422 Then 'PCL Harness 3.4.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：联机「虚拟局域网（TUN）模式」，任意游戏、任意端口都能直接连（需要管理员权限）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：TUN 模式下自动创建虚拟网卡，房主固定 10.114.114.1，加入者自动分配 10.114.114.2~254"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：TUN 模式不再需要端口映射，路由器只需放行 EasyTier 监听端口，游戏内直接填虚拟 IP"))
                FeatureCount += 3
                BugCount += 0
            End If

            If LastVersion < 421 Then 'PCL Harness 3.3.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "修复：微软登录的 Client ID 改为内置，陌生电脑无需再手动配置 Secrets.json"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：更新下载时左下角提示只出现一次，不再常驻（进度看右下角「任务管理」）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "调整：更新包下载完成后静默替换，退出启动器后下次打开即为新版本，不再自动重启"))
                FeatureCount += 2
                BugCount += 1
            End If
                        If LastVersion < 420 Then 'PCL Harness 3.3.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "调整：个性化配色预设精简为 4 个（标准蓝 / 冰蓝 / 海蓝 / 靛青），按钮不再拥挤"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "移除：配色「渐变」滑条，主题固定为纯色（上游许可要求不得改变其他色相）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "调整：首次启动数据迁移不再复制 Plugins 文件夹（原版 PCL 本身没有插件功能）"))
                FeatureCount += 3
                BugCount += 0
            End If

            If LastVersion < 419 Then 'PCL Harness 3.2.9
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：新版本下载改用左下角 Hint 提示，不再固定占用一行文字"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "        提示会自动消失；进度仍可在右下角「任务管理」查看与取消"))
                FeatureCount += 1
                BugCount += 0
            End If

        If LastVersion < 418 Then 'PCL Harness 3.2.8
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：下载新版本时会显示在任务管理里，右下角显示进度圆环，可随时手动取消"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：左下角显示新版本下载进度，下载完成后提示「退出启动器时自动更新」"))
                FeatureCount += 2
                BugCount += 0
            End If

            If LastVersion < 417 Then 'PCL Harness 3.2.7
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：更新设置选「在有新版本时自动下载」时，后台静默下载并在退出启动器时替换、重启"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "调整：关于与鸣谢页移除「赞助者」卡片（含赞助作者按钮）"))
                FeatureCount += 1
                BugCount += 0
            End If

            If LastVersion < 416 Then 'PCL Harness 3.2.6
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：启动器可检查 GitHub Release 并在线更新（优先加速源，官方源备用）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：更新设置里的「检查更新」按钮可手动检查并下载"))
                FeatureCount += 2
                BugCount += 0
            End If
            If LastVersion < 415 Then 'PCL Harness 3.2.5
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：插件未启用或缺少清单时，日志会明确提示（扫描完成：共 N 个插件，已启用 M 个）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：插件文档与 SDK 中的路径、权限、编码说明与宿主实际行为不一致"))
                FeatureCount += 1
                BugCount += 3
            End If
            If LastVersion < 414 Then 'PCL Harness 3.2.4
                FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：插件 API v5（事件总线、强类型设置、权限白名单、DLL 校验）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(4, "新增：微软正版登录（设备代码流）"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：色调限定蓝色系，内置 8 套预设"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：登录 403 被误报为 IP 异常；登录页提示与配置状态不一致"))
                FeatureCount += 6
                BugCount += 3
            End If
            If LastVersion < 411 Then 'Release 2.14.0.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：允许修改、添加、移除、更新已安装版本的 MC 版本以及各个组件，可从版本设置中进入"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：启动游戏时允许按 Esc 取消"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：允许复制主页的启动日志中的内容"))
                FeatureCount += 19
                BugCount += 26
            End If
            If LastVersion < 408 Then 'Release 2.13.1.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：版本设置中，添加打开截图文件夹的快捷方式"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：导出整合包时，可以勾选导出光影设置文件"))
                FeatureCount += 23
                BugCount += 28
            End If
            If LastVersion < 406 Then 'Release 2.13.0.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(4, "新增：重做 Java 管理与相关设置，允许调整 Java 优先级、指定 Java 版本范围等"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：导出整合包时允许自动导出版本文件夹中的 Java"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：重做弹出提示的样式以及动画，以更符合现代 UI 审美"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：可能无法安装 OptiFine 26.1.2"))
                FeatureCount += 35
                BugCount += 17
            End If
            If LastVersion < 404 Then 'Release 2.12.8.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：Windows 7 无法正常联网"))
                FeatureCount += 1
                BugCount += 2
            End If
            If LastVersion < 402 Then 'Release 2.12.8.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：升级框架到 .NET Framework 4.8"))
                FeatureCount += 35
                BugCount += 21
            End If
            If LastVersion < 398 Then 'Release 2.12.7.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：下载游戏、整合包的稳定性"))
                FeatureCount += 1
                BugCount += 5
            End If
            If LastVersion < 391 Then 'Release 2.12.6.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：会为部分 Forge / NeoForge 选择不支持的 Java"))
                BugCount += 4
            End If
            If LastVersion < 389 Then 'Release 2.12.6.1
                If LastVersion = 387 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法使用 Java 25+ 启动 Forge"))
                FeatureCount += 4
                BugCount += 3
            End If
            If LastVersion < 387 Then 'Release 2.12.6
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：降低 Minecraft 的内存占用"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：Minecraft 使用 Java 25+ 时的一个性能问题"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法下载 NeoForge 26.1"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：老版本 Windows 无法启动游戏"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：连接 Mojang 的服务可能失败，提示错误码 421"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法访问爱发电"))
                FeatureCount += 24
                BugCount += 19
            End If
            If LastVersion < 383 Then 'Release 2.12.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：下载可能完全卡住，或是下载进度反复回退"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：内存管理设置，可以选择 G1GC、ZGC 或分代 ZGC"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：来自 Modrinth 的文件下载速度"))
                FeatureCount += 15
                BugCount += 16
            End If
            If LastVersion < 381 Then 'Release 2.12.2
                FeatureCount += 3
                BugCount += 13
            End If
            If LastVersion < 379 Then 'Release 2.12.1
                If LastVersion >= 376 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "删除：暂时隐藏联机入口……不过只是暂时关闭，它还会回来的！"))
                End If
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：适配新的 Minecraft 版本号系统与 Unobfuscated 版本"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：巨幅优化各个下载页面和 Mod 管理页面的性能"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：更换披风时会显示当前使用的披风"))
                FeatureCount += 33
                BugCount += 29
            End If
            If LastVersion < 376 Then 'Release 2.11.2
                'FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：联机功能！"))
                FeatureCount += 32
                BugCount += 21
            End If
        Else
            '5：          FEAT+
            '4：     IMP+ FEAT*
            '3：BUG+ IMP* FEAT-
            '2：BUG* IMP-
            '1：BUG-
            If LastVersion < 412 Then 'Snapshot 2.14.0.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：允许修改、添加、移除、更新已安装版本的 MC 版本以及各个组件，可从版本设置中进入"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：启动游戏时允许按 Esc 取消"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：允许复制主页的启动日志中的内容"))
                FeatureCount += 19
                BugCount += 26
            End If
            If LastVersion < 410 Then 'Snapshot 2.13.1.1
                If LastVersion = 409 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：更新 Mod 时可能更新成另一个 Mod 加载器的版本"))
                FeatureCount += 2
                BugCount += 6
            End If
            If LastVersion < 409 Then 'Snapshot 2.13.1.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：版本设置中，添加打开截图文件夹的快捷方式"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：导出整合包时，可以勾选导出光影设置文件"))
                FeatureCount += 21
                BugCount += 22
            End If
            If LastVersion < 407 Then 'Snapshot 2.13.0.1
                If LastVersion = 405 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：使用部分主页预设时崩溃"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法删除部分文件夹"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：版本设置中指定的 Java 无法被保存"))
                    FeatureCount += 1
                    BugCount += 3
                End If
                FeatureCount += 2
            End If
            If LastVersion < 405 Then 'Snapshot 2.13.0.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(4, "新增：重做 Java 管理与相关设置，允许调整 Java 优先级、指定 Java 版本范围等"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：导出整合包时允许自动导出版本文件夹中的 Java"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：重做弹出提示的样式以及动画，以更符合现代 UI 审美"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：可能无法安装 OptiFine 26.1.2"))
                FeatureCount += 35
                BugCount += 17
            End If
            If LastVersion < 403 Then 'Snapshot 2.12.8.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：Windows 7 无法正常联网"))
                FeatureCount += 1
                BugCount += 2
            End If
            If LastVersion < 401 Then 'Snapshot 2.12.8.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：可能无法更换披风"))
                FeatureCount += 8
                BugCount += 6
            End If
            If LastVersion < 399 Then 'Snapshot 2.12.8.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：升级框架到 .NET Framework 4.8"))
                FeatureCount += 28
                BugCount += 19
            End If
            If LastVersion < 397 Then 'Snapshot 2.12.7.3
                If LastVersion >= 393 AndAlso LastVersion < 397 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法使用自定义隐藏主题"))
            End If
            If LastVersion < 395 Then 'Snapshot 2.12.7.2
                If LastVersion = 393 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分个性化功能失效"))
            End If
            If LastVersion < 393 Then 'Snapshot 2.12.7.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：允许永久关闭启动页面的快照版提示"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：下载游戏、整合包的稳定性"))
                FeatureCount += 1
                BugCount += 5
            End If
            If LastVersion < 390 Then 'Snapshot 2.12.6.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：会为部分 Forge / NeoForge 选择不支持的 Java"))
                BugCount += 4
            End If
            If LastVersion < 388 Then 'Snapshot 2.12.6.1
                If LastVersion = 386 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法使用 Java 25+ 启动 Forge"))
                FeatureCount += 4
                BugCount += 3
            End If
            If LastVersion < 386 Then 'Snapshot 2.12.6
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：Minecraft 使用 Java 25+ 时的一个性能问题"))
                If LastVersion = 384 OrElse LastVersion = 385 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法正常重命名游戏版本"))
                If LastVersion = 385 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法显示 Modrinth 整合包的版本列表"))
                BugCount += 1
            End If
            If LastVersion < 385 Then 'Snapshot 2.12.5
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法下载 NeoForge 26.1"))
                If LastVersion = 384 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：下载可能失败，提示任务管理刷新线程出错"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法保存选择的 Minecraft 文件夹"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：使用中文搜索 Mod 时，部分结果会忽略筛选条件"))
                End If
                FeatureCount += 3
                BugCount += 6
            End If
            If LastVersion < 384 Then 'Snapshot 2.12.4
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：降低 Minecraft 的内存占用"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：老版本 Windows 无法启动游戏"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：连接 Mojang 的服务可能失败，提示错误码 421"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法访问爱发电"))
                FeatureCount += 21
                BugCount += 14
            End If
            If LastVersion < 382 Then 'Snapshot 2.12.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：下载可能完全卡住，或是下载进度反复回退"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：内存管理设置，可以选择 G1GC、ZGC 或分代 ZGC"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：来自 Modrinth 的文件下载速度"))
                FeatureCount += 15
                BugCount += 16
            End If
            If LastVersion < 380 Then 'Snapshot 2.12.2
                FeatureCount += 3
                BugCount += 13
            End If
            If LastVersion < 378 Then 'Snapshot 2.12.1
                If LastVersion >= 377 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：在版本列表中存在 OptiFine 时可能无法加载版本列表"))
                    BugCount += 2
                End If
            End If
            If LastVersion < 377 Then 'Snapshot 2.12.0
                If LastVersion >= 373 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "删除：暂时隐藏联机入口……不过只是暂时关闭，它还会回来的！"))
                End If
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：适配新的 Minecraft 版本号系统与 Unobfuscated 版本"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：巨幅优化各个下载页面和 Mod 管理页面的性能"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：更换披风时会显示当前使用的披风"))
                FeatureCount += 33
                BugCount += 29
            End If
            If LastVersion < 375 Then 'Snapshot 2.11.2
                'If LastVersion >= 373 Then
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：对联机进行了各种各样的优化，以改善稳定性"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：若有加入者的网络环境比房主更好，会提示可以让那位加入者担任房主"))
                'End If
                FeatureCount += 16
                BugCount += 4
            End If
            If LastVersion < 374 Then 'Snapshot 2.11.1
                'If LastVersion >= 373 Then
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：使用离线登录也可以直接加入联机房间了"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：会从所有共享节点中自动选择负载最低的进行中继连接"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：若复制了邀请码，则可以直接快速加入房间"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：关闭 PCL 时总是会提示是否退出联机，防止在关闭 PCL 时无意地关闭或退出了房间"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：允许自定义要连接的节点"))
                'End If
                FeatureCount += 9
                BugCount += 7
            End If
            If LastVersion < 373 Then 'Snapshot 2.11.0
                'FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：联机功能！"))
                FeatureCount += 7
                BugCount += 10
            End If
        End If
        '整理更新日志文本
        Dim ContentList As New List(Of String)
        Dim SortedFeatures = FeatureList.OrderByDescending(Function(f) f.Key).ToList
        If Not SortedFeatures.Any() AndAlso FeatureCount = 0 AndAlso BugCount = 0 Then ContentList.Add("没有更新日志……")
        For i = 0 To Math.Min(9, SortedFeatures.Count - 1) '最多取 10 项
            ContentList.Add(SortedFeatures(i).Value)
        Next
        If SortedFeatures.Count > 10 Then FeatureCount += SortedFeatures.Count - 10
        If FeatureCount > 0 OrElse BugCount > 0 Then
            ContentList.Add(If(FeatureCount > 0, FeatureCount & " 项小调整与修改", "") &
                If(FeatureCount > 0 AndAlso BugCount > 0, "，", "") &
                If(BugCount > 0, "修复了 " & BugCount & " 个 Bug", "") &
                "，详见完整更新日志")
        End If
        Dim Content As String = "· " & ContentList.Join(vbCrLf & "· ")
        '输出更新日志
        RunInNewThread(
        Sub()
            If MyMsgBox(Content, "PCL Harness 已更新至 " & VersionBaseName, "确定", "完整更新日志") = 2 Then
                OpenWebsite("https://github.com/Badqf/PCL-Harness/releases/latest")
            End If
        End Sub, "UpdateLog Output")
    End Sub

    '窗口加载
    Private IsWindowLoadFinished As Boolean = False
    Public Sub New()
        ApplicationStartTick = GetTimeMs()
        '窗体参数初始化
        FrmMain = Me
        FrmLaunchLeft = New PageLaunchLeft
        FrmLaunchRight = New PageLaunchRight
        '版本号改变
        Dim LastVersion As Integer = Settings.Get(Of Integer)("SystemLastVersionReg")
        If LastVersion < VersionCode Then
            '触发升级
            UpgradeSub(LastVersion)
        ElseIf LastVersion > VersionCode Then
            '触发降级
            DowngradeSub(LastVersion)
        End If
        '版本隔离设置迁移
        If Not Settings.HasSaved("LaunchArgumentIndieV2") Then
            If Settings.HasSaved("LaunchArgumentIndie") Then
                Logger.Info("从老 PCL 迁移版本隔离")
                Settings.Set("LaunchArgumentIndieV2", Settings.Get(Of Integer)("LaunchArgumentIndie"))
            ElseIf HasIniKey("Setup", "LaunchVersionSelect") Then
                Logger.Info("从老 PCL 升级，但此前未调整版本隔离，使用老的版本隔离默认值")
                Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndie"))
            Else
                Logger.Info("全新的 PCL，使用新的版本隔离默认值")
                Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndieV2"))
            End If
        End If
        '刷新主题
        ThemeCheckAll(False)
        '应用外观模式与自定义配色
        ModSecret.IsDarkMode = Settings.Get(Of Integer)("UiColorMode") <> 1
        '色调限定蓝色区间（上游许可：主要色相必须为蓝色），老用户的越界配色在此收敛一次
        Dim RawHue As Integer = Settings.Get(Of Integer)("UiLauncherHue")
        Dim FixedHue As Integer = RawHue.Clamp(200, 230)
        If FixedHue <> RawHue Then
            Settings.Set("UiLauncherHue", FixedHue)
            Logger.Info($"配色色调 {RawHue}° 超出蓝色区间，已收敛为 {FixedHue}°（上游许可要求主要色相为蓝色）")
        End If
        ModSecret.ColorHue = FixedHue
        ModSecret.ColorSat = Settings.Get(Of Integer)("UiLauncherSat").Clamp(0, 100)
        ModSecret.ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight")
        ModSecret.ColorHueTopbarDelta = 0 '3.3.0 起移除渐变滑条，固定为纯色
        ThemeRefresh(Settings.Get(Of Integer)("UiLauncherTheme"))
        '注册拖拽事件（不能直接加 Handles，否则没用；#6340）
        [AddHandler](DragDrop.DragEnterEvent, New DragEventHandler(AddressOf HandleDrag), handledEventsToo:=True)
        [AddHandler](DragDrop.DragOverEvent, New DragEventHandler(AddressOf HandleDrag), handledEventsToo:=True)
        '加载 UI
        InitializeComponent()
        Opacity = 0
        '开启管理员权限下的文件拖拽
        If WindowsUtils.HasAdminRole() Then
            Static Helper As New DragHelper
            AddHandler SourceInitialized,
            Sub()
                Dim WpfHelper As New WindowInteropHelper(Me)
                Helper.HwndIntPtrSource = HwndSource.FromHwnd(WpfHelper.Handle)
                Helper.AddHook()
            End Sub
            AddHandler Closing, Sub() Helper.RemoveDragHook()
            AddHandler Helper.DragDrop, Sub() FileDrag(Helper.DropFilePaths)
        End If
        '切换到首页
        If Not IsNothing(FrmLaunchLeft.Parent) Then FrmLaunchLeft.SetValue(ContentPresenter.ContentProperty, Nothing)
        If Not IsNothing(FrmLaunchRight.Parent) Then FrmLaunchRight.SetValue(ContentPresenter.ContentProperty, Nothing)
        PanMainLeft.Child = FrmLaunchLeft
        PageLeft = FrmLaunchLeft
        PanMainRight.Child = FrmLaunchRight
        PageRight = FrmLaunchRight
        FrmLaunchRight.PageState = MyPageRight.PageStates.ContentStay
        '模式提醒
        If BuildType = BuildTypes.Debug Then Hint("[开发者模式] PCL 正以开发者模式运行，这可能会造成严重的性能下降，请务必立即向开发者反馈此问题！", HintType.Red)
        If ModeDebug Then Hint("[调试模式] PCL 正以调试模式运行，这可能会导致性能下降，若无必要请不要开启！")
        '尽早执行的加载池
        McFolderListLoader.Start(0) '为了让下载已存在文件检测可以正常运行，必须跑一次；为了让启动按钮尽快可用，需要尽早执行；为了与 PageLaunchLeft 联动，需要为 0 而不是 GetUuid

        Logger.Info($"第二阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")
    End Sub
    Private Sub FormMain_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ApplicationStartTick = GetTimeMs()
        AddHandler PanTitle.SizeChanged, AddressOf PanTitle_SizeChanged
        Handle = New WindowInteropHelper(Me).Handle
        '读取设置
        UpdateBackgroundAndTitleBar()
        PageSetupUI.HiddenRefresh()
        PageSetupUI.BackgroundRefresh(False, True)
        MusicRefreshPlay(False, True)
        '扩展按钮
        BtnExtraDownload.ShowCheck = AddressOf BtnExtraDownload_ShowCheck
        BtnExtraBack.ShowCheck = AddressOf BtnExtraBack_ShowCheck
        BtnExtraApril.ShowCheck = AddressOf BtnExtraApril_ShowCheck
        BtnExtraShutdown.ShowCheck = AddressOf BtnExtraShutdown_ShowCheck
        BtnExtraApril.ShowRefresh()
        '初始化尺寸改变
        Dim Resizer As New MyResizer(Me)
        Resizer.addResizerDown(ResizerB)
        Resizer.addResizerLeft(ResizerL)
        Resizer.addResizerLeftDown(ResizerLB)
        Resizer.addResizerLeftUp(ResizerLT)
        Resizer.addResizerRight(ResizerR)
        Resizer.addResizerRightDown(ResizerRB)
        Resizer.addResizerRightUp(ResizerRT)
        Resizer.addResizerUp(ResizerT)
        'PLC 彩蛋
        If RandomInteger(1, 1000) = 233 Then
            ShapeTitleLogo.Data = New GeometryConverter().ConvertFromString("M26,29 v-25 h6 a7,7 180 0 1 0,14 h-6 M83,6.5 a10,11.5 180 1 0 0,18 M48,2.5 v24.5 h13.5")
        End If
        '加载窗口
        ThemeRefreshMain()
        Try
            Height = Settings.Get(Of Integer)("WindowHeight")
            Width = Settings.Get(Of Integer)("WindowWidth")
        Catch ex As Exception '修复 #2019
            Logger.Error(ex, "读取窗口默认大小失败", LogBehavior.Toast)
            Height = MinHeight + 100
            Width = MinWidth + 100
        End Try
        'MinHeight = 50
        'MinWidth = 50
        Topmost = False
        If FrmStart IsNot Nothing Then FrmStart.Close(New TimeSpan(0, 0, 0, 0, 400 / AniSpeed))
        '更改窗口
        Top = (GetWPFSize(My.Computer.Screen.WorkingArea.Height) - Height) / 2
        Left = (GetWPFSize(My.Computer.Screen.WorkingArea.Width) - Width) / 2
        IsSizeSaveable = True
        ShowWindowToTop()
        Dim HwndSource As Interop.HwndSource = PresentationSource.FromVisual(Me)
        HwndSource.AddHook(New Interop.HwndSourceHook(AddressOf WndProc))
        AniStart({
            AaCode(Sub() AniControlEnabled -= 1, 50),
            AaOpacity(Me, Settings.Get(Of Integer)("UiLauncherTransparent") / 1000 + 0.4, 250, 100),
            AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, 600, 100, New AniEaseOutBack(AniEasePower.Weak)),
            AaDouble(Sub(i) TransformRotate.Angle += i, -TransformRotate.Angle, 500, 100, New AniEaseOutBack(AniEasePower.Weak)),
            AaCode(
            Sub()
                PanBack.RenderTransform = Nothing
                IsWindowLoadFinished = True
                Logger.Info($"DPI：{DPI}，系统版本：{Environment.OSVersion.VersionString}，PCL 位置：{PathExe}")
            End Sub, , True)
        }, "Form Show")
        'Timer 启动
        AniStart()
        TimerMainStart()
        '首次启动指引（PCLHarness 独立配置目录 + 旧 PCL 数据迁移）；上次跳过且未改设置时再给一次机会
        '关掉指引后再弹介绍页：两个弹窗不再同时冒出来（介绍页可在 设置 → 启动 里关掉）
        RunInNewThread(
            Sub()
                Threading.Thread.Sleep(900)
                RunInUi(
                    Sub()
                        ModFirstRun.ShowWizardIfNeeded()
                        '更新日志放到介绍页之前：介绍页是独立窗口，会把弹窗盖住（3.4.3 反馈「看不到更新了什么」）
                        If ShowPendingUpdateLog() Then
                            '已经弹过更新日志，介绍页留到下次启动，避免又被盖住
                        Else
                            ModIntro.ShowIfNeeded()
                        End If
                        '只有刚才因为 TUN 需要管理员权限而重启过，才自动跳回联机页（一次性）
                        If Settings.Get(Of Boolean)("LinkJumpOnNextStart") Then
                            Settings.Set("LinkJumpOnNextStart", False)
                            If PageCurrent = PageType.Launch Then PageChange(PageType.Link)
                            Logger.Info("[Link] 提权重启完成，自动跳转到联机页")
                        End If
                    End Sub)
            End Sub, "Startup Dialogs", ThreadPriority.BelowNormal)
        '加载池
        RunInNewThread(
        Sub()
            'EULA 提示
            Const EulaVersion As Integer = 2
            If Settings.Get(Of Integer)("SystemEulaVersion") < EulaVersion Then
                Select Case MyMsgBox(
                    If(Settings.Get(Of Integer)("SystemEulaVersion") = 0,
                        "在使用 PCL 前，请先阅读用户协议与免责声明。",
                        $"PCL 的用户协议与免责声明已更新。{vbCrLf}请阅读更新后的用户协议与免责声明。"),
                        "协议授权", "同意", "拒绝", "查看用户协议与免责声明",
                        Button3Action:=Sub() OpenWebsite("https://shimo.im/docs/rGrd8pY8xWkt6ryW"))
                    Case 1
                        Settings.Set("SystemEulaVersion", EulaVersion)
                    Case 2
                        EndProgram(False)
                End Select
            End If
            '启动加载器池
            Try
                JavaInit() '延后到同意协议后再执行，避免在初次启动时进行进程操作
                Threading.Thread.Sleep(100)
                DlClientListMojangLoader.Start(1) 'PCL 会同时根据这里的加载结果决定是否使用官方源进行下载
                RunCountSub()
                ServerLoader.Start()
                RunInNewThread(AddressOf TryClearTaskTemp, "TryClearTaskTemp", ThreadPriority.BelowNormal)
            Catch ex As Exception
                Logger.Error(ex, "初始化加载池运行失败")
            End Try
            '清理自动更新文件
            Try
                FileUtils.Delete(Paths.BaseThenName & "Plain Craft Launcher 2.exe")
            Catch ex As Exception
                Logger.Warn(ex, "清理自动更新文件失败")
            End Try
            '上报
            Telemetry("启动")
            '加载已启用的插件（PCL Harness 插件框架）
            RunInNewThread(
                Sub()
                    Try
                        ModPlugin.Refresh(True)
                        RunInUi(Sub() RefreshPluginNav())
                    Catch ex As Exception
                        Logger.Error(ex, "加载插件失败", LogBehavior.None)
                    End Try
                End Sub, "PluginLoad", ThreadPriority.BelowNormal)
            '插件加载失败时才提示（等插件线程扫完再报，避免被弹窗挡住）
            For i = 1 To 50
                If ModPlugin.LoadDone Then Exit For
                Threading.Thread.Sleep(100)
            Next
            If ModPlugin.LoadDone Then RunInUi(Sub() ModPlugin.WarnFailed())
            '初始化结束后补刷一次插件入口，避免早期刷新被布局重建覆盖
            RunInUi(Sub() RefreshPluginNav())
            '自编版自动更新检查（后台延迟执行，无新版本时不打扰）
            Try
                StartAutoUpdateCheck()
            Catch ex As Exception
                Logger.Warn(ex, "启动自动更新检查失败")
            End Try
            '插件就绪事件（v3）：此时插件已全部加载、标题栏入口也已建好
            If ModPlugin.LoadDone Then RunInUi(Sub() ModPlugin.RaiseLauncherReady())
        End Sub, "初始化", ThreadPriority.Lowest)

        Logger.Info($"第三阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")
    End Sub
    '根据打开次数触发的事件
    Private Sub RunCountSub()
        Settings.Set("SystemCount", Settings.Get(Of Integer)("SystemCount") + 1)
        If False Then '快照版赞助相关弹窗已按需求停用
            Select Case Settings.Get(Of Integer)("SystemCount")
                Case 1
                    MyMsgBox("欢迎使用 PCL 快照版！" & vbCrLf &
                             "快照版包含尚未在正式版发布的测试性功能，仅用于赞助者本人尝鲜。所以请不要发给其他人或者用于制作整合包哦！" & vbCrLf &
                             "如果你并非通过赞助或赞助者本人邀请进群获得的本程序，那么可能是有人在违规传播，记得提醒他一下啦。", "快照版使用说明")
            End Select
            If Settings.Get(Of Integer)("SystemCount") >= 99 Then
                If ThemeUnlock(6, False) Then
                    MyMsgBox("你已经使用了 99 次 PCL 啦，感谢你长期以来的支持！" & vbCrLf &
                             "隐藏主题 铁杆粉 已解锁！", "提示")
                End If
            End If
        End If
    End Sub
    '升级与降级事件
    Private Sub UpgradeSub(LastVersionCode As Integer)
        Logger.Info($"版本号从 {LastVersionCode} 升高到 {VersionCode}")
        Settings.Set("SystemLastVersionReg", VersionCode)
        '检查有记录的最高版本号
        Dim HighestVersionCode As Integer
        Const SettingKey = If(BuildType = BuildTypes.Snapshot, "SystemHighestAlphaVersionReg", "SystemHighestBetaVersionReg")
        HighestVersionCode = Settings.Get(Of Integer)(SettingKey)
        If HighestVersionCode < VersionCode Then
            Settings.Set(SettingKey, VersionCode)
            Logger.Info($"最高版本号从 {HighestVersionCode} 升高到 {VersionCode}")
        End If
        '被移除的窗口设置选项
        If Settings.Get(Of Integer)("LaunchArgumentWindowType") = 5 Then Settings.Set("LaunchArgumentWindowType", 1)
        '修改主题设置项名称
        If HighestVersionCode <= 207 Then
            Dim UnlockedTheme As New List(Of String) From {"2"}
            UnlockedTheme.AddRange(New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide").ToString.Split("|")))
            UnlockedTheme.AddRange(New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|")))
            Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Distinct.ToList.Join("|"c))
        End If
        '重置欧皇彩
        If LastVersionCode <= 115 AndAlso Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|").Contains("13") Then
            Dim UnlockedTheme As New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"))
            UnlockedTheme.Remove("13")
            Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Join("|"c))
            MyMsgBox("由于新版 PCL 修改了欧皇彩的解锁方式，你需要重新解锁欧皇彩。" & vbCrLf &
                     "多谢各位的理解啦！", "重新解锁提醒")
        End If
        '重置滑稽彩
        If LastVersionCode <= 152 AndAlso Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|").Contains("12") Then
            Dim UnlockedTheme As New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"))
            UnlockedTheme.Remove("12")
            Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Join("|"c))
            MyMsgBox("由于新版 PCL 修改了滑稽彩的解锁方式，你需要重新解锁滑稽彩。" & vbCrLf &
                     "多谢各位的理解啦！", "重新解锁提醒")
        End If
        '移动自定义皮肤
        If LastVersionCode <= 161 AndAlso FileUtils.Exists(Paths.BaseThenName & "CustomSkin.png") AndAlso Not FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
            FileUtils.Copy(Paths.BaseThenName & "CustomSkin.png", Paths.AppDataThenName & "CustomSkin.png")
            Logger.Info("已移动离线自定义皮肤 (162)")
        End If
        If LastVersionCode <= 263 AndAlso FileUtils.Exists(PathTemp & "CustomSkin.png") AndAlso Not FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
            FileUtils.Copy(PathTemp & "CustomSkin.png", Paths.AppDataThenName & "CustomSkin.png")
            Logger.Info("已移动离线自定义皮肤 (264)")
        End If
        '解除帮助页面的隐藏
        If LastVersionCode <= 205 Then
            Settings.Set("UiHiddenOtherHelp", False)
            Logger.Info("已解除帮助页面的隐藏")
        End If
        '单向迁移微软登录结果（#4836）
        If Not Settings.Get(Of Boolean)("CacheMsV2Migrated") Then
            Settings.Set("CacheMsV2Migrated", True)
            Settings.Set("CacheMsV2OAuthRefresh", Settings.Get(Of String)("CacheMsOAuthRefresh"))
            Settings.Set("CacheMsV2Access", Settings.Get(Of String)("CacheMsAccess"))
            Settings.Set("CacheMsV2ProfileJson", Settings.Get(Of String)("CacheMsProfileJson"))
            Settings.Set("CacheMsV2Uuid", Settings.Get(Of String)("CacheMsUuid"))
            Settings.Set("CacheMsV2Name", Settings.Get(Of String)("CacheMsName"))
            Logger.Info("已从老版本迁移微软登录结果")
        End If
        'Mod 命名设置迁移
        If Settings.HasSaved("ToolDownloadTranslate") AndAlso Not Settings.HasSaved("ToolDownloadTranslateV2") Then
            Settings.Set("ToolDownloadTranslateV2", Settings.Get(Of Integer)("ToolDownloadTranslate") + 1)
            Logger.Info("已从老版本迁移 Mod 命名设置")
        End If
        '重置 JVM 参数设置
        If LastVersionCode <= 381 AndAlso Settings.HasSaved("LaunchAdvanceJvm") AndAlso
           Settings.Get(Of String)("LaunchAdvanceJvm").ToString.Replace("-XX:+UseG1GC ", "").Replace("-XX:-UseAdaptiveSizePolicy ", "").Trim = Settings.GetDefault("LaunchAdvanceJvm") Then
            Settings.Reset("LaunchAdvanceJvm")
            Logger.Info("已重置 JVM 参数设置")
        End If
        '输出更新日志
        '只要版本号真的变了就弹一次（不再看「最高版本号」——那个记录会被同版本的其它运行提前拉高，
        '导致静默更新后反而不弹）；更新说明按「旧版本号 → 当前版本号」这段来取。
        If LastVersionCode <= 0 Then Return
        Dim AppliedTag As String = PathTemp & "Update\Applied.txt"
        If IO.File.Exists(AppliedTag) Then
            Try
                IO.File.Delete(AppliedTag)
            Catch
            End Try
        End If
        Logger.Info($"[Update] 版本号 {LastVersionCode} → {VersionCode}，稍后展示更新日志")
        PendingUpdateLogVersion = LastVersionCode
    End Sub
    Private Sub DowngradeSub(LastVersionCode As Integer)
        Logger.Info($"版本号从 {LastVersionCode} 降低到 {VersionCode}")
        Settings.Set("SystemLastVersionReg", VersionCode)
    End Sub

#End Region

#Region "自定义窗口"

    '关闭
    Private Sub FormMain_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        EndProgram(True)
        e.Cancel = True
    End Sub
    ''' <summary>
    ''' 正常关闭程序。程序将在执行此方法后约 0.3s 退出。
    ''' </summary>
    ''' <param name="SendWarning">是否在还有下载任务未完成时发出警告。</param>
    Public Sub EndProgram(SendWarning As Boolean)
        '插件事件（v4）：允许插件拦截关闭
        If SendWarning AndAlso ModPlugin.RaiseClosing() Then Return
        '强行结束下载任务？
        If HasDownloadingTask() Then
            If SendWarning AndAlso MyMsgBox("还有下载任务尚未完成，是否确定退出？", "提示", "确定", "取消") = 2 Then Return
            RunInNewThread(
            Sub()
                Logger.Info("正在强行停止任务")
                For Each Task As LoaderBase In LoaderTaskbar.ToList()
                    Task.Cancel()
                Next
            End Sub, "强行停止下载任务")
        End If
        '关闭联机？
        If FrmLinkMain?.TryExit(Not SendWarning, True) Then Return
        '关闭
        RunInUiWait(
        Sub()
            IsHitTestVisible = False
            If PanBack.RenderTransform Is Nothing Then
                Dim TransformPos As New TranslateTransform(0, 0)
                Dim TransformRotate As New RotateTransform(0)
                Dim TransformScale As New ScaleTransform(1, 1)
                PanBack.RenderTransform = New TransformGroup() With {.Children = New TransformCollection({TransformRotate, TransformPos, TransformScale})}
                AniStart({
                    AaOpacity(Me, -Opacity, 140, 40, New AniEaseOutFluent(AniEasePower.Weak)),
                    AaDouble(
                    Sub(i)
                        TransformScale.ScaleX += i
                        TransformScale.ScaleY += i
                    End Sub, 0.88 - TransformScale.ScaleX, 180),
                    AaDouble(Sub(i) TransformPos.Y += i, 20 - TransformPos.Y, 180, 0, New AniEaseOutFluent(AniEasePower.Weak)),
                    AaDouble(Sub(i) TransformRotate.Angle += i, 0.6 - TransformRotate.Angle, 180, 0, New AniEaseInoutFluent(AniEasePower.Weak)),
                    AaCode(
                    Sub()
                        IsHitTestVisible = False
                        Top = -10000
                        ShowInTaskbar = False
                    End Sub, 210),
                    AaCode(AddressOf EndProgramForce, 230)
                }, "Form Close")
            Else
                EndProgramForce()
            End If
            Logger.Info("收到关闭指令")
        End Sub)
    End Sub
    Private Shared IsLogShown As Boolean = False
    Public Shared Sub EndProgramForce(Optional ReturnCode As ProcessReturnValues = ProcessReturnValues.Success)
        On Error Resume Next
        IsProgramEnding = True
        AniControlEnabled += 1
        If IsUpdateWaitingRestart Then UpdateRestart(False)
        ModSecret.ApplyPendingUpdateOnExit()
        If ReturnCode = ProcessReturnValues.Exception Then
            If Not IsLogShown Then
                FeedbackInfo()
                Logger.Info("请在 https://github.com/Badqf/PCL-Harness/issues 提交错误报告，以便于作者解决此问题！")
                IsLogShown = True
                StartProcess(Paths.BaseThenName & "Log1.txt")
            End If
            Thread.Sleep(500) '防止 PCL 在记事本打开前就被掐掉
        End If
        Logger.Info($"程序已退出，返回值：{ReturnCode}")
        ConfigUtils.SaveAll()
        Logger.Instance.Flush()
        If ReturnCode <> ProcessReturnValues.Success Then Environment.Exit(ReturnCode)
        Process.GetCurrentProcess.Kill()
    End Sub
    Private Sub BtnTitleClose_Click(sender As Object, e As RoutedEventArgs) Handles BtnTitleClose.Click
        EndProgram(True)
    End Sub

    '移动
    Private Sub FormDragMove(sender As Object, e As MouseButtonEventArgs) Handles PanTitle.MouseLeftButtonDown, PanMsg.MouseLeftButtonDown
        On Error Resume Next
        If sender.IsMouseDirectlyOver Then DragMove()
    End Sub

    '改变大小
    ''' <summary>
    ''' 是否可以向注册表储存尺寸改变信息。以此避免初始化时误储存。
    ''' </summary>
    Public IsSizeSaveable As Boolean = False
    Private Sub FormMain_SizeChanged() Handles Me.SizeChanged, Me.Loaded
        If IsSizeSaveable Then
            Settings.Set("WindowHeight", Height)
            Settings.Set("WindowWidth", Width)
        End If
        RectForm.Rect = New Rect(0, 0, BorderForm.ActualWidth, BorderForm.ActualHeight)
        PanForm.Width = BorderForm.ActualWidth + 0.001
        PanForm.Height = BorderForm.ActualHeight + 0.001
        PanMain.Width = PanForm.Width
        PanMain.Height = Math.Max(0, PanForm.Height - PanTitle.ActualHeight)
        If WindowState = WindowState.Maximized Then WindowState = WindowState.Normal '修复 #1938
        RefreshTitleSuffixFit()
    End Sub

    ''' <summary>
    ''' 窗口变窄时收缩标题栏的 Harness 徽标，避免徽标被挤压裁切。
    ''' </summary>
    Private Sub PanTitle_SizeChanged(sender As Object, e As SizeChangedEventArgs)
        RefreshTitleSuffixFit()
    End Sub

    Public Sub RefreshTitleSuffixFit()
        If BorTitleSuffix Is Nothing Then Return
        Dim W As Double = If(Double.IsNaN(Width) OrElse Width <= 0, ActualWidth, Width)
        Dim LogoTypeNow As Integer = Settings.Get(Of Integer)("UiLogoType")
        Dim ShowSuffix As Boolean = (W >= 860) AndAlso (LogoTypeNow = 1 OrElse LogoTypeNow = 2)
        BorTitleSuffix.Visibility = If(ShowSuffix, Visibility.Visible, Visibility.Collapsed)
        BorTitleSuffixDiv.Visibility = If(ShowSuffix, Visibility.Visible, Visibility.Collapsed)
    End Sub

    '最小化
    Private Sub BtnTitleMin_Click() Handles BtnTitleMin.Click
        WindowState = WindowState.Minimized
    End Sub

    '背景图片与标题栏
    Public Shared Sub UpdateBackgroundAndTitleBar(Value) '从设置更新
        If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
        FrmMain.UpdateBackgroundAndTitleBar()
    End Sub
    Public Sub UpdateBackgroundAndTitleBar()
        Logger.Info("从设置更新背景图片与标题栏样式")
        '背景图片透明度
        ImgBack.Opacity = Settings.Get(Of Integer)("UiBackgroundOpacity") / 1000
        '背景图片模糊
        Dim BlurRadius As Double = Settings.Get(Of Integer)("UiBackgroundBlur") + 1
        If BlurRadius = 1 Then
            ImgBack.Effect = Nothing
        Else
            ImgBack.Effect = New Effects.BlurEffect With {.Radius = BlurRadius}
        End If
        ImgBack.Margin = New Thickness(-BlurRadius / 1.8)
        '背景图片适应方式
        Dim BackgroundType As Integer = Settings.Get(Of Integer)("UiBackgroundSuit")
        If ImgBack.Background IsNot Nothing Then
            Dim Brush As ImageBrush = CType(ImgBack.Background, ImageBrush)
            If BackgroundType = 0 Then
                '智能：当图片较小时平铺，较大时适应
                If Brush.ImageSource.Width < PanMain.ActualWidth / 2 AndAlso Brush.ImageSource.Height < PanMain.ActualHeight / 2 Then
                    BackgroundType = 4 '平铺
                Else
                    BackgroundType = 2 '适应
                End If
            End If
            Brush.Stretch = Stretch.UniformToFill
            Brush.TileMode = TileMode.None
            Brush.Viewport = New Rect(0, 0, 1, 1)
            Brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox
            Brush.AlignmentX = AlignmentX.Center
            Brush.AlignmentY = AlignmentY.Center
            Select Case BackgroundType
                Case 1 '中
                    Brush.Stretch = Stretch.None
                Case 3 '拉伸
                    Brush.Stretch = Stretch.Fill
                Case 4 '平铺
                    Brush.Stretch = Stretch.None
                    Brush.TileMode = TileMode.Tile
                    Brush.Viewport = New Rect(0, 0, Brush.ImageSource.Width, Brush.ImageSource.Height)
                    Brush.ViewportUnits = BrushMappingMode.Absolute

                Case 2 '适应
                Case 5 '左上
                    Brush.AlignmentX = AlignmentX.Left
                    Brush.AlignmentY = AlignmentY.Top
                Case 6 '右上
                    Brush.AlignmentX = AlignmentX.Right
                    Brush.AlignmentY = AlignmentY.Top
                Case 7 '左下
                    Brush.AlignmentX = AlignmentX.Left
                    Brush.AlignmentY = AlignmentY.Bottom
                Case 8 '右下
                    Brush.AlignmentX = AlignmentX.Right
                    Brush.AlignmentY = AlignmentY.Bottom
                Case 9 '左
                    Brush.AlignmentX = AlignmentX.Left
                    Brush.AlignmentY = AlignmentY.Center
                Case 10 '右
                    Brush.AlignmentX = AlignmentX.Right
                    Brush.AlignmentY = AlignmentY.Center
                Case 11 '上
                    Brush.AlignmentX = AlignmentX.Center
                    Brush.AlignmentY = AlignmentY.Top
                Case 12 '下
                    Brush.AlignmentX = AlignmentX.Center
                    Brush.AlignmentY = AlignmentY.Bottom
            End Select
        End If
        '标题栏显示类型
        Select Case Settings.Get(Of Integer)("UiLogoType")
            Case 0 '无
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Collapsed
                If Not IsNothing(FrmSetupUI) Then 'TODO: 和 FrmSetupUI 解耦
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Visible
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed
                End If
            Case 1 '默认
                ShapeTitleLogo.Visibility = Visibility.Visible
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Collapsed
                If Not IsNothing(FrmSetupUI) Then
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed
                End If
            Case 2 '文本
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Visible
                ImageTitleLogo.Visibility = Visibility.Collapsed
                If Not IsNothing(FrmSetupUI) Then
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Visible
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed
                End If
            Case 3 '图片
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Visible
                If Not IsNothing(FrmSetupUI) Then
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Visible
                End If
                Try
                    ImageTitleLogo.Source = Paths.BaseThenName & "Logo.png"
                Catch ex As Exception
                    ImageTitleLogo.Source = Nothing
                    Logger.Error(ex, "显示标题栏图片失败", LogBehavior.Alert)
                End Try
        End Select
        FrmSetupUI?.CardLogo.TriggerForceResize()
        '标题栏文本
        LabTitleLogo.Text = Settings.Get(Of String)("UiLogoText")
        '标题栏文本是否居左
        PanTitleMain.ColumnDefinitions(0).Width = New GridLength(If(Settings.Get(Of Boolean)("UiLogoLeft") AndAlso Settings.Get(Of Integer)("UiLogoType") = 0, 0, 1), GridUnitType.Star)
        'Logo 后缀（自编版标识）
        Dim LogoTypeNow As Integer = Settings.Get(Of Integer)("UiLogoType")
        Dim SuffixVisible As Boolean = (LogoTypeNow = 1 OrElse LogoTypeNow = 2)
        BorTitleSuffix.Visibility = If(SuffixVisible, Visibility.Visible, Visibility.Collapsed)
        BorTitleSuffixDiv.Visibility = If(SuffixVisible, Visibility.Visible, Visibility.Collapsed)
        If SuffixVisible Then
            Dim SuffixLeft As Double
            If LogoTypeNow = 2 Then
                LabTitleLogo.UpdateLayout()
                Dim LogoW As Double = If(LabTitleLogo.ActualWidth > 0, LabTitleLogo.ActualWidth, If(LabTitleLogo.Text?.Length, 0) * 9.5)
                SuffixLeft = 18 + LogoW + 11
            Else
                SuffixLeft = 68
            End If
            BorTitleSuffix.Margin = New Thickness(SuffixLeft, 0, 0, 0)
            BorTitleSuffixDiv.Margin = New Thickness(SuffixLeft - 8, 0, 0, 0)
        End If
        RefreshTitleSuffixFit()
    End Sub

#End Region

#Region "窗体事件"

    '按键事件
    Private Sub FormMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.IsRepeat Then Return
        '修复按下 Alt 后误认为弹出系统菜单导致的冻结
        If e.SystemKey = Key.LeftAlt OrElse e.SystemKey = Key.RightAlt Then e.Handled = True
        '在有弹窗时：回车选择第一个，Esc 选择最后一个
        If PanMsg.Children.Any Then
            If e.Key = Key.Enter Then
                CType(PanMsg.Children(0), Object).Btn1_Click()
            ElseIf e.Key = Key.Escape Then
                Dim Msg As Object = PanMsg.Children(0)
                If TypeOf Msg IsNot MyMsgInput AndAlso TypeOf Msg IsNot MyMsgSelect AndAlso Msg.Btn3.Visibility = Visibility.Visible Then
                    Msg.Btn3_Click()
                ElseIf Msg.Btn2.Visibility = Visibility.Visible Then
                    Msg.Btn2_Click()
                Else
                    Msg.Btn1_Click()
                End If
            End If
            Return
        End If

        '==========================
        ' 在没有弹窗时：继续检查……
        '==========================

        '按 ESC 取消启动，或返回上一级
        If e.Key = Key.Escape Then
            If PageCurrent = FormMain.PageType.Launch AndAlso McLaunchLoader.State = LoadState.Loading Then
                FrmLaunchLeft.BtnCancel_Click()
                Return
            End If
            TriggerPageBack()
        End If
        '更改隐藏版本可见性
        If e.Key = Key.F11 AndAlso PageCurrent = FormMain.PageType.InstanceSelect Then
            FrmSelectRight.ShowHidden = Not FrmSelectRight.ShowHidden
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Return
        End If
        '更改功能隐藏可见性
        If e.Key = Key.F12 Then
            PageSetupUI.HiddenForceShow = Not PageSetupUI.HiddenForceShow
            If PageSetupUI.HiddenForceShow Then
                Hint("功能隐藏设置已暂时关闭！", HintType.Green)
            Else
                Hint("功能隐藏设置已重新开启！", HintType.Green)
            End If
            PageSetupUI.HiddenRefresh()
            Return
        End If
        '按 F5 刷新页面
        If e.Key = Key.F5 Then
            If TypeOf PageLeft Is IRefreshable Then CType(PageLeft, IRefreshable).Refresh()
            If TypeOf PageRight Is IRefreshable Then CType(PageRight, IRefreshable).Refresh()
            Return
        End If
        '调用启动游戏
        If e.Key = Key.Enter AndAlso PageCurrent = FormMain.PageType.Launch Then
            If IsAprilEnabled AndAlso Not IsAprilGiveup Then
                Hint("木大！")
            Else
                FrmLaunchLeft.LaunchButtonClick()
            End If
        End If
    End Sub
    Private Sub FormMain_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseDown
        '鼠标侧键返回上一级
        If FrmMain.PanMsg.Children.Count > 0 OrElse WaitingMyMsgBox.Any Then Return '弹窗中（#5513）
        If e.ChangedButton = MouseButton.XButton1 OrElse e.ChangedButton = MouseButton.XButton2 Then TriggerPageBack()
    End Sub
    Private Sub TriggerPageBack()
        If PageCurrent = PageType.Download AndAlso PageCurrentSub = PageSubType.DownloadInstall AndAlso FrmDownloadInstall.IsInSelectPage Then
            FrmDownloadInstall.ExitSelectPage()
        Else
            PageBack()
        End If
    End Sub

    '切回窗口
    Private Sub FormMain_Activated() Handles Me.Activated
        '切回窗口时自动刷新
        Try
            If PageCurrent = PageType.InstanceSetup AndAlso PageCurrentSub = PageSubType.InstanceMod Then
                'Mod 管理
                FrmInstanceMod.ReloadModList()
            ElseIf PageCurrent = PageType.InstanceSetup AndAlso (PageCurrentSub = PageSubType.InstanceSetup OrElse PageCurrentSub = PageSubType.InstanceExport) Then
                '更新当前选用的 Java
                PageInstanceLeft.ReloadCurrentJava()
            ElseIf PageCurrent = PageType.InstanceSelect Then
                '版本选择
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.RunOnUpdated, MaxDepth:=1, ExtraPath:="versions\")
            End If
        Catch ex As Exception
            Logger.Error(ex, "切回窗口时出错")
        End Try
        '读取剪贴板，自动加入联机房间
        Return 'TODO: 联机复活赛
        If PageLinkMain.LinkState <> PageLinkMain.LinkStates.Waiting Then Return '已启动联机
        If PageCurrent = PageType.Link Then Return '已在联机界面
        Dim Code = ClipboardGetText() : If Code Is Nothing Then Return '剪贴板无文本
        If Settings.Get(Of String)("LinkLastAutoJoinInviteCode") = Code Then Return
        If PageLinkMain.ValidateCodeFormat(Code) IsNot Nothing Then Return '不是邀请码
        Settings.Set("LinkLastAutoJoinInviteCode", Code)
        RunInThread(
        Sub()
            If MyMsgBox("嘿，是否使用复制的邀请码加入房间？", "加入联机房间", "加入", "取消") = 2 Then Return '防止弹窗阻碍主线程，所以必须放在工作线程
            RunInUi(
            Sub()
                PageLinkMain.Join(Code)
                ClipboardSet(Nothing, False)
            End Sub)
        End Sub)
    End Sub

    '文件拖放
    Private Sub HandleDrag(sender As Object, e As DragEventArgs)
        Try
            If e.Handled AndAlso (e.Effects <> DragDropEffects.None) Then Return
            e.Handled = True
            '缓存
            Static PrevData As IDataObject, PrevEffects As DragDropEffects
            If e.Data Is PrevData Then
                e.Effects = PrevEffects
                Return
            End If
            '确定拖放效果
            e.Effects = DragDropEffects.None
            If e.Data.GetDataPresent(DataFormats.Text) Then
                Dim Str As String = e.Data.GetData(DataFormats.Text)
                If Str.StartsWithF("authlib-injector:yggdrasil-server:") Then
                    e.Effects = DragDropEffects.Copy
                ElseIf Str.StartsWithF("file:///") Then
                    e.Effects = DragDropEffects.Copy
                End If
            ElseIf e.Data.GetDataPresent(DataFormats.FileDrop) Then
                Dim Files As String() = e.Data.GetData(DataFormats.FileDrop)
                If Files IsNot Nothing AndAlso Files.Length > 0 Then
                    e.Effects = DragDropEffects.Link
                End If
            End If
            PrevData = e.Data
            PrevEffects = e.Effects
            Logger.Info($"设置拖放类型：{e.Effects}")
        Catch ex As Exception
            Logger.Error(ex, "处理拖放时出错")
        End Try
    End Sub
    Private Sub FrmMain_Drop(sender As Object, e As DragEventArgs) Handles Me.Drop
        Try
            ShowWindowToTop()
            If e.Data.GetDataPresent(DataFormats.Text) Then
                '获取文本
                Try
                    Dim Str As String = e.Data.GetData(DataFormats.Text)
                    Logger.Info($"接受文本拖拽：{Str}")
                    If Str.StartsWithF("authlib-injector:yggdrasil-server:") Then
                        'Authlib 拖拽
                        e.Handled = True
                        e.Effects = DragDropEffects.Copy
                        Dim AuthlibServer As String = StringUtils.FormUrlUnescape(Str.Substring("authlib-injector:yggdrasil-server:".Length))
                        Logger.Info($"Authlib 拖拽：{AuthlibServer}")
                        If Not String.IsNullOrEmpty(New ValidateHttp().Validate(AuthlibServer)) Then
                            Hint($"输入的 Authlib 验证服务器不符合网址格式（{AuthlibServer}）！", HintType.Red)
                            Return
                        End If
                        Dim Target = If(PageCurrent = PageType.InstanceSetup, PageInstanceLeft.Instance, McInstanceSelected)
                        If Target Is Nothing Then
                            Hint("请先下载游戏，再设置第三方登录！", HintType.Red)
                            Return
                        End If
                        If AuthlibServer = "https://littleskin.cn/api/yggdrasil" Then
                            'LittleSkin
                            If MyMsgBox($"是否要在版本 {Target.Name} 中开启 LittleSkin 登录？" & vbCrLf &
                                        "你可以在 版本设置 → 设置 → 服务器选项 中修改登录方式。", "第三方登录开启确认", "确定", "取消") = 2 Then
                                Return
                            End If
                            Settings.Set("VersionServerLogin", 4, Instance:=Target)
                            Settings.Set("VersionServerAuthServer", "https://littleskin.cn/api/yggdrasil", Instance:=Target)
                            Settings.Set("VersionServerAuthRegister", "https://littleskin.cn/auth/register", Instance:=Target)
                            Settings.Set("VersionServerAuthName", "LittleSkin 登录", Instance:=Target)
                        Else
                            '第三方 Authlib 服务器
                            If MyMsgBox($"是否要在版本 {Target.Name} 中开启第三方登录？" & vbCrLf &
                                        $"登录服务器：{AuthlibServer}" & vbCrLf & vbCrLf &
                                        "你可以在 版本设置 → 设置 → 服务器选项 中修改登录方式。", "第三方登录开启确认", "确定", "取消") = 2 Then
                                Return
                            End If
                            Settings.Set("VersionServerLogin", 4, Instance:=Target)
                            Settings.Set("VersionServerAuthServer", AuthlibServer, Instance:=Target)
                            Settings.Set("VersionServerAuthRegister", AuthlibServer.Replace("api/yggdrasil", "auth/register"), Instance:=Target)
                            Settings.Set("VersionServerAuthName", "", Instance:=Target)
                        End If
                        If PageCurrent = PageType.InstanceSetup AndAlso PageCurrentSub = PageSubType.InstanceSetup Then
                            '正在服务器选项页，需要刷新设置项显示
                            FrmInstanceSetup.Reload()
                        ElseIf PageCurrent = PageType.Launch Then
                            '正在主页，需要刷新左边栏
                            FrmLaunchLeft.RefreshPage(True, False)
                        End If
                    ElseIf Str.StartsWithF("file:///") Then
                        '文件拖拽（例如从浏览器下载窗口拖入）
                        Dim FilePath = Net.WebUtility.UrlDecode(Str).Substring("file:///".Length).Replace("/", "\")
                        e.Handled = True
                        e.Effects = DragDropEffects.Copy
                        FileDrag(New List(Of String) From {FilePath})
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "无法接取文本拖拽事件")
                    Return
                End Try
            ElseIf e.Data.GetDataPresent(DataFormats.FileDrop) Then
                '获取文件并检查
                Dim FilePathRaw = e.Data.GetData(DataFormats.FileDrop)
                If FilePathRaw Is Nothing Then '#2690
                    Hint("请将文件解压后再拖入！", HintType.Red)
                    Return
                End If
                e.Handled = True
                e.Effects = DragDropEffects.Link
                FileDrag(CType(FilePathRaw, IEnumerable(Of String)))
            End If
        Catch ex As Exception
            Logger.Error(ex, "接取拖拽事件失败")
        End Try
    End Sub
    Private Sub FileDrag(FilePathList As IEnumerable(Of String))
        RunInNewThread(
        Sub()
            Dim FilePath As String = FilePathList.First
            Logger.Warn($"接受文件拖拽：{FilePath}{If(FilePathList.Any, $" 等 {FilePathList.Count} 个文件", "")}")
            '基础检查
            If DirectoryUtils.Exists(FilePathList.First) AndAlso Not FileUtils.Exists(FilePathList.First) Then
                Hint("请拖入一个文件，而非文件夹！", HintType.Red)
                Return
            ElseIf Not FileUtils.Exists(FilePathList.First) Then
                Hint("拖入的文件不存在：" & FilePathList.First, HintType.Red)
                Return
            End If
            '多文件拖拽
            Dim PathList As List(Of String) = FilePathList.ToList()
            If FilePathList.Count > 1 Then
                '必须要求全部为 jar 文件
                For Each File In PathList
                    If Not {"jar", "litemod", "disabled", "old"}.Contains(PathUtils.GetExtension(File)) Then
                        Hint("一次请只拖入一个文件！", HintType.Red)
                        Return
                    End If
                Next
            End If
            '主页
            Dim Extension As String = PathUtils.GetExtension(FilePath)
            If Extension = "xaml" Then
                Logger.Info("文件后缀为 XAML，作为主页加载")
                If FileUtils.Exists(Paths.BaseThenName & "Custom.xaml") Then
                    If MyMsgBox("已存在一个主页文件，是否要将它覆盖？", "覆盖确认", "覆盖", "取消") = 2 Then
                        Return
                    End If
                End If
                FileUtils.Copy(FilePath, Paths.BaseThenName & "Custom.xaml")
                RunInUi(
                Sub()
                    Settings.Set("UiCustomType", 1)
                    FrmLaunchRight.ForceRefresh()
                    Hint("已加载主页自定义文件！", HintType.Green)
                End Sub)
                Return
            End If
            '安装 Mod
            If PageInstanceMod.InstallMods(PathList) Then Return
            '安装整合包
            If {"zip", "rar", "mrpack"}.Any(Function(t) t = Extension) Then '部分压缩包是 zip 格式但后缀为 rar，总之试一试
                Hint("正在读取压缩包……")
                Try
                    Logger.Info("文件为压缩包，尝试作为整合包安装")
                    ModpackInstall(FilePath)
                    RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))
                    Return
                Catch ex As Exception
                    If ex.IsCanceled Then Return
                    '安装失败，继续往后尝试
                End Try
            End If
            'RAR 处理
            If Extension = "rar" Then
                Hint("PCL 无法处理 rar 格式的压缩包，请在解压后重新压缩为 zip 格式再试！", HintType.Red)
                Return
            End If
            '错误报告分析
            Try
                Logger.Info("尝试进行错误报告分析")
                Dim Analyzer As New CrashAnalyzer
                Analyzer.Import(FilePath)
                If Not Analyzer.Prepare() Then Exit Try
                Analyzer.Analyze()
                Analyzer.Output(True, New List(Of String))
                Return
            Catch ex As Exception
                Logger.Error(ex, "自主错误报告分析失败")
            End Try
            '未知操作
            Hint("PCL 无法确定适合的操作……", HintType.Red)
        End Sub, "文件拖拽")
    End Sub

    '接受到 Windows 窗体事件
    Public IsSystemTimeChanged As Boolean = False
    Private Function WndProc(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr, ByRef handled As Boolean) As IntPtr
        If msg = 30 Then
            Dim NowDate = Date.Now
            If NowDate.Date = ApplicationOpenTime.Date Then
                Logger.Info($"系统时间微调为：{NowDate.ToLongDateString} {NowDate.ToLongTimeString}")
                IsSystemTimeChanged = False
            Else
                Logger.Info($"系统时间修改为：{NowDate.ToLongDateString} {NowDate.ToLongTimeString}")
                IsSystemTimeChanged = True
            End If
        ElseIf msg = 400 * 16 + 2 Then
            Logger.Info($"收到置顶信息：{hwnd.ToInt64}")
            If Not IsWindowLoadFinished Then
                Logger.Info("窗口尚未加载完成，忽略置顶请求")
                Return IntPtr.Zero
            End If
            ShowWindowToTop()
            handled = True
        End If
        Return IntPtr.Zero
    End Function

    '窗口隐藏与置顶
    Private _Hidden As Boolean = False
    Public Property Hidden As Boolean
        Get
            Return _Hidden
        End Get
        Set(value As Boolean)
            If _Hidden = value Then Return
            _Hidden = value
            If value Then
                '隐藏
                Left -= 10000
                ShowInTaskbar = False
                Visibility = Visibility.Hidden
                Logger.Info($"窗口已隐藏，位置：({Left},{Top})")
            Else
                '取消隐藏
                If Left < -2000 Then Left += 10000
                ShowWindowToTop()
            End If
        End Set
    End Property
    ''' <summary>
    ''' 把当前窗口拖到最前面。
    ''' </summary>
    Public Sub ShowWindowToTop()
        RunInUi(
        Sub()
            '这一坨乱七八糟的，别改，改了指不定就炸了，自己电脑还复现不出来
            Visibility = Visibility.Visible
            ShowInTaskbar = True
            WindowState = WindowState.Normal
            Hidden = False
            Topmost = True '偶尔 SetForegroundWindow 失效
            Topmost = False
            SetForegroundWindow(Handle)
            Focus()
            Logger.Info($"窗口已置顶，位置：({Left}, {Top}), {Width} x {Height}")
        End Sub)
    End Sub

#End Region

#Region "切换页面"

    '页面种类与属性
    ''' <summary>
    ''' 页面种类。
    ''' 该枚举在自定义事件中使用，是公开 API 的一部分。
    ''' </summary>
    Public Enum PageType
        ''' <summary>
        ''' 启动。
        ''' </summary>
        Launch = 0
        ''' <summary>
        ''' 下载。
        ''' </summary>
        Download = 1
        ''' <summary>
        ''' 联机。
        ''' </summary>
        Link = 2
        ''' <summary>
        ''' 设置。
        ''' </summary>
        Setup = 3
        ''' <summary>
        ''' 更多。
        ''' </summary>
        Other = 4
        ''' <summary>
        ''' 版本选择。这是一个副页面。
        ''' </summary>
        InstanceSelect = 5
        ''' <summary>
        ''' 任务管理。这是一个副页面。
        ''' </summary>
        TaskManager = 6
        ''' <summary>
        ''' 版本设置。这是一个副页面。
        ''' </summary>
        InstanceSetup = 7
        ''' <summary>
        ''' 社区资源详情。这是一个副页面。
        ''' </summary>
        ResourceDetail = 8
        ''' <summary>
        ''' 帮助详情。这是一个副页面。
        ''' </summary>
        HelpDetail = 9
        ''' <summary>
        ''' 插件。
        ''' </summary>
        Plugin = 10
    End Enum
    ''' <summary>
    ''' 次要页面种类。其数值必须与 StackPanel 中的下标一致。
    ''' 该枚举在自定义事件中使用，是公开 API 的一部分。
    ''' </summary>
    Public Enum PageSubType
        [Default] = 0
        DownloadInstall = 0
        DownloadMod = 2
        DownloadPack = 3
        DownloadDataPack = 4
        DownloadResourcePack = 5
        DownloadShader = 6
        SetupLaunch = 0
        SetupLink = 1
        SetupUI = 2
        SetupSystem = 3
        LinkMain = 0
        OtherHelp = 0
        OtherAbout = 1
        OtherTest = 2
        InstanceOverall = 0
        InstanceSetup = 1
        InstanceModify = 2
        InstanceMod = 3
        InstanceModDisabled = 4
        InstanceExport = 5
    End Enum
    ''' <summary>
    ''' 获取次级页面的名称。若并非次级页面则返回空字符串，故可以以此判断是否为次级页面。
    ''' </summary>
    Private Function PageNameGet(Stack As PageStackData) As String
        Select Case Stack.Page
            Case PageType.InstanceSelect
                Return "版本选择"
            Case PageType.TaskManager
                Return "任务管理"
            Case PageType.InstanceSetup
                Return "版本设置 - " & If(PageInstanceLeft.Instance Is Nothing, "未知版本", PageInstanceLeft.Instance.Name)
            Case PageType.ResourceDetail
                Return CType(Stack.Additional(0), ResourceProject).TranslatedName
            Case PageType.HelpDetail
                Return CType(Stack.Additional(0), HelpEntry).Title
            Case Else
                Return ""
        End Select
    End Function
    ''' <summary>
    ''' 刷新次级页面的名称。
    ''' </summary>
    Public Sub PageNameRefresh(Type As PageStackData)
        LabTitleInner.Text = PageNameGet(Type)
    End Sub
    ''' <summary>
    ''' 刷新次级页面的名称。
    ''' </summary>
    Public Sub PageNameRefresh()
        PageNameRefresh(PageCurrent)
    End Sub

    '页面状态存储
    ''' <summary>
    ''' 当前的主页面。
    ''' </summary>
    Public PageCurrent As PageStackData = PageType.Launch
    ''' <summary>
    ''' 上一个主页面。
    ''' </summary>
    Public PageLast As PageStackData = PageType.Launch
    ''' <summary>
    ''' 当前的子页面。
    ''' </summary>
    Public ReadOnly Property PageCurrentSub As PageSubType
        Get
            Select Case PageCurrent
                Case PageType.Download
                    If FrmDownloadLeft Is Nothing Then FrmDownloadLeft = New PageDownloadLeft
                    Return FrmDownloadLeft.PageID
                Case PageType.Setup
                    If FrmSetupLeft Is Nothing Then FrmSetupLeft = New PageSetupLeft
                    Return FrmSetupLeft.PageID
                Case PageType.Other
                    If FrmOtherLeft Is Nothing Then FrmOtherLeft = New PageOtherLeft
                    Return FrmOtherLeft.PageID
                Case PageType.InstanceSetup
                    If FrmInstanceLeft Is Nothing Then FrmInstanceLeft = New PageInstanceLeft
                    Return FrmInstanceLeft.PageID
                Case Else
                    Return 0 '没有子页面
            End Select
        End Get
    End Property
    ''' <summary>
    ''' 上层页面的编号堆栈，用于返回。
    ''' </summary>
    Public PageStack As New List(Of PageStackData)
    Public Class PageStackData

        Public Page As PageType
        Public Additional As Object

        Public Overrides Function Equals(other As Object) As Boolean
            If other Is Nothing Then Return False
            If TypeOf other Is PageStackData Then
                Dim PageOther As PageStackData = other
                If Page <> PageOther.Page Then Return False
                If Additional Is Nothing Then
                    Return PageOther.Additional Is Nothing
                Else
                    Return PageOther.Additional IsNot Nothing AndAlso Additional.Equals(PageOther.Additional)
                End If
            ElseIf TypeOf other Is Integer Then
                If Page <> other Then Return False
                Return Additional Is Nothing
            Else
                Return False
            End If
        End Function
        Public Shared Operator =(left As PageStackData, right As PageStackData) As Boolean
            Return EqualityComparer(Of PageStackData).Default.Equals(left, right)
        End Operator
        Public Shared Operator <>(left As PageStackData, right As PageStackData) As Boolean
            Return Not left = right
        End Operator
        Public Shared Widening Operator CType(Value As PageType) As PageStackData
            Return New PageStackData With {.Page = Value}
        End Operator
        Public Shared Widening Operator CType(Value As PageStackData) As PageType
            Return Value.Page
        End Operator
    End Class
    Public PageLeft As MyPageLeft, PageRight As MyPageRight

    '引发实际页面切换的入口
    Private IsChangingPage As Boolean = False
    ''' <summary>
    ''' 切换页面，并引起对应选择 UI 的改变。
    ''' </summary>
    Public Sub PageChange(Stack As PageStackData, Optional SubType As PageSubType = PageSubType.Default)
        If PageNameGet(Stack) = "" Then
            '切换到主页面
            PageChangeExit()
            IsChangingPage = True '防止下面的勾选直接触发了 PageChangeActual
            For Each Child As Object In PanTitleSelect.Children
                If TypeOf Child Is MyRadioButton AndAlso Val(CType(Child, MyRadioButton).Tag) = Stack.Page Then
                    CType(Child, MyRadioButton).SetChecked(True, True, PageNameGet(PageCurrent) = "")
                    Exit For
                End If
            Next
            IsChangingPage = False
            Select Case Stack.Page
                Case PageType.Download
                    If FrmDownloadLeft Is Nothing Then FrmDownloadLeft = New PageDownloadLeft
                    CType(FrmDownloadLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
                Case PageType.Setup
                    If FrmSetupLeft Is Nothing Then FrmSetupLeft = New PageSetupLeft
                    CType(FrmSetupLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
                Case PageType.Other
                    If FrmOtherLeft Is Nothing Then FrmOtherLeft = New PageOtherLeft
                    CType(FrmOtherLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
            End Select
            PageChangeActual(Stack, SubType)
        Else
            '切换到次页面
            Select Case Stack.Page
                Case PageType.InstanceSetup
                    If FrmInstanceLeft Is Nothing Then FrmInstanceLeft = New PageInstanceLeft
                    CType(FrmInstanceLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
            End Select
            PageChangeActual(Stack, SubType)
        End If
    End Sub
    ''' <summary>
    ''' 通过点击导航栏改变页面。
    ''' </summary>
    Private Sub BtnTitleSelect_Click(sender As MyRadioButton, raiseByMouse As Boolean) Handles BtnTitleSelect0.Check, BtnTitleSelect1.Check, BtnTitleSelect2.Check, BtnTitleSelect3.Check, BtnTitleSelect4.Check, BtnTitleSelect5.Check
        If IsChangingPage Then Return
        Dim TagValue As Integer = 0
        Try
            TagValue = CInt(Val(If(sender.Tag, 0)))
        Catch
            TagValue = 0
        End Try
        If TagValue >= 100 Then
            '插件注入的入口
            Dim Index As Integer = TagValue - 100
            If Index < 0 OrElse Index >= ModPlugin.PluginNavItems.Count Then Return
            Dim Item = ModPlugin.PluginNavItems(Index)
            If Item.Page IsNot Nothing Then
                PageChangeActual(TagValue)
            Else
                Try
                    Item.Action.Invoke()
                Catch ex As Exception
                    Logger.Error(ex, "执行插件入口失败：" & Item.Text, LogBehavior.None)
                    Hint("插件入口执行失败：" & ex.Message, HintType.Red)
                End Try
                RefreshPluginNavCheck()
            End If
            Return
        End If
        PageChangeActual(Val(sender.Tag))
    End Sub

    ''' <summary>
    ''' 把标题栏的勾选状态同步回当前页面（插件入口只执行动作、不切页时用）。
    ''' </summary>
    Public Sub RefreshPluginNavCheck()
        IsChangingPage = True
        Try
            For Each Child As Object In PanTitleSelect.Children
                If TypeOf Child Is MyRadioButton Then
                    Dim Btn = CType(Child, MyRadioButton)
                    Dim BtnTag As Integer = 0
                    Try
                        BtnTag = CInt(Val(If(Btn.Tag, 0)))
                    Catch
                        BtnTag = 0
                    End Try
                    If BtnTag < 100 Then
                        Btn.SetChecked(BtnTag = CInt(PageCurrent.Page), True, False)
                    Else
                        Btn.SetChecked(False, True, False)
                    End If
                End If
            Next
        Finally
            IsChangingPage = False
        End Try
    End Sub

    ''' <summary>
    ''' 按插件注册的入口重建标题栏按钮（插件刷新后调用）。
    ''' </summary>
    Public Sub RefreshPluginNav()
        '移除旧插件按钮
        Dim Old As New List(Of MyRadioButton)
        For Each Child As Object In PanTitleSelect.Children
            If TypeOf Child Is MyRadioButton Then
                Dim Btn = CType(Child, MyRadioButton)
                Dim BtnTag As Integer = 0
                Try
                    BtnTag = CInt(Val(If(Btn.Tag, 0)))
                Catch
                    BtnTag = 0
                End Try
                If BtnTag >= 100 Then Old.Add(Btn)
            End If
        Next
        For Each Btn In Old
            PanTitleSelect.Children.Remove(Btn)
        Next
        '加入新插件按钮
        Dim Index As Integer = 0
        For Each Item As ModPlugin.PluginNavItem In ModPlugin.PluginNavItems
            Dim Btn As New MyRadioButton With {
                .Text = Item.Text,
                .Tag = (100 + Index).ToString(),
                .Margin = New Thickness(5, 0, 5, 0),
                .LogoScale = 0.9
            }
            If Not String.IsNullOrWhiteSpace(Item.Icon) Then Btn.Logo = Item.Icon
            AddHandler Btn.Check, AddressOf BtnTitleSelect_Click
            PanTitleSelect.Children.Add(Btn)
            Index += 1
        Next
        Logger.Info($"插件入口已刷新，共 {Index} 个")
        RefreshPluginNavCheck()
    End Sub
    ''' <summary>
    ''' 通过点击返回按钮或手动触发返回来改变页面。
    ''' </summary>
    Public Sub PageBack() Handles BtnTitleInner.Click
        If PageStack.Any() Then
            PageChangeActual(PageStack(0))
        Else
            PageChange(PageType.Launch)
        End If
    End Sub

    '实际处理页面切换
    ''' <summary>
    ''' 切换现有页面的实际方法。
    ''' </summary>
    Private Sub PageChangeActual(Stack As PageStackData, Optional SubType As PageSubType = -1)
        If PageCurrent = Stack AndAlso (PageCurrentSub = SubType OrElse SubType = -1) Then Return
        If Stack.Page = PageType.InstanceSetup AndAlso PageInstanceLeft.Instance IsNot Nothing AndAlso McInstanceLock.IsLocked(PageInstanceLeft.Instance) Then
            Hint("该版本正在被修改，暂时无法进入版本设置！", HintType.Red)
            Return
        End If
        AniControlEnabled += 1
        Try

#Region "子页面处理"
            Dim PageName As String = PageNameGet(Stack)
            If PageName = "" Then
                '即将切换到一个顶级页面
                PageChangeExit()
            Else
                '即将切换到一个子页面
                If PageStack.Any Then
                    '子页面 → 另一个子页面，更新
                    AniStart({
                        AaOpacity(LabTitleInner, -LabTitleInner.Opacity, 130),
                        AaCode(Sub() LabTitleInner.Text = PageName,, True),
                        AaOpacity(LabTitleInner, 1, 150, 30)
                    }, "FrmMain Titlebar SubLayer")
                    If PageStack.Contains(Stack) Then
                        '返回到更上层的子页面
                        Do While PageStack.Contains(Stack)
                            PageStack.RemoveAt(0)
                        Loop
                    Else
                        '进入更深层的子页面
                        PageStack.Insert(0, PageCurrent)
                    End If
                Else
                    '主页面 → 子页面，进入
                    PanTitleInner.Visibility = Visibility.Visible
                    PanTitleMain.IsHitTestVisible = False
                    PanTitleInner.IsHitTestVisible = True
                    PageNameRefresh(Stack)
                    AniStart({
                        AaOpacity(PanTitleMain, -PanTitleMain.Opacity, 150),
                        AaX(PanTitleMain, 12 - PanTitleMain.Margin.Left, 150,, New AniEaseInFluent(AniEasePower.Weak)),
                        AaOpacity(PanTitleInner, 1 - PanTitleInner.Opacity, 150, 200),
                        AaX(PanTitleInner, -PanTitleInner.Margin.Left, 350, 200, New AniEaseOutBack),
                        AaCode(Sub() PanTitleMain.Visibility = Visibility.Collapsed,, True)
                    }, "FrmMain Titlebar FirstLayer")
                    PageStack.Insert(0, PageCurrent)
                End If
            End If
#End Region

#Region "实际更改页面框架 UI"
            PageLast = PageCurrent
            PageCurrent = Stack
            Select Case Stack.Page
                Case PageType.Launch '启动
                    PageChangeAnim(FrmLaunchLeft, FrmLaunchRight)
                Case PageType.Download '下载
                    If FrmDownloadLeft Is Nothing Then FrmDownloadLeft = New PageDownloadLeft
                    'PageGet 方法会在未设置 SubType 时指定默认值，并建立相关页面的实例
                    PageChangeAnim(FrmDownloadLeft, FrmDownloadLeft.PageGet(SubType))
                Case PageType.Link '联机
                    If FrmLinkMain Is Nothing Then FrmLinkMain = New PageLinkMain
                    PageChangeAnim(New MyPageLeft, FrmLinkMain)
                Case PageType.Setup '设置
                    If FrmSetupLeft Is Nothing Then FrmSetupLeft = New PageSetupLeft
                    PageChangeAnim(FrmSetupLeft, FrmSetupLeft.PageGet(SubType))
                Case PageType.Other '更多
                    If FrmOtherLeft Is Nothing Then FrmOtherLeft = New PageOtherLeft
                    PageChangeAnim(FrmOtherLeft, FrmOtherLeft.PageGet(SubType))
                Case PageType.Plugin '插件
                    If FrmPlugin Is Nothing Then FrmPlugin = New PagePlugin
                    If FrmPluginLeft Is Nothing Then FrmPluginLeft = New PagePluginLeft
                    PageChangeAnim(FrmPluginLeft, FrmPlugin)
                Case CType(100, PageType) To CType(199, PageType) '插件自定义页面
                    Dim PluginIndex As Integer = CInt(Stack.Page) - 100
                    Dim PluginPage As FrameworkElement = Nothing
                    If PluginIndex >= 0 AndAlso PluginIndex < ModPlugin.PluginNavItems.Count Then
                        PluginPage = CreatePluginPage(ModPlugin.PluginNavItems(PluginIndex).Page)
                    End If
                    If PluginPage Is Nothing Then PluginPage = New PagePlugin
                    PageChangeAnim(New MyPageLeft, PluginPage)
                Case PageType.InstanceSelect '版本选择
                    If FrmSelectLeft Is Nothing Then FrmSelectLeft = New PageSelectLeft
                    If FrmSelectRight Is Nothing Then FrmSelectRight = New PageSelectRight
                    PageChangeAnim(FrmSelectLeft, FrmSelectRight)
                Case PageType.TaskManager '任务管理
                    If FrmSpeedLeft Is Nothing Then FrmSpeedLeft = New PageSpeedLeft
                    If FrmSpeedRight Is Nothing Then FrmSpeedRight = New PageSpeedRight
                    PageChangeAnim(FrmSpeedLeft, FrmSpeedRight)
                Case PageType.InstanceSetup '版本设置
                    If FrmInstanceLeft Is Nothing Then FrmInstanceLeft = New PageInstanceLeft
                    PageChangeAnim(FrmInstanceLeft, FrmInstanceLeft.PageGet(SubType))
                Case PageType.ResourceDetail '社区资源详情
                    If FrmDownloadResourceDetail Is Nothing Then FrmDownloadResourceDetail = New PageDownloadResourceDetail
                    PageChangeAnim(New MyPageLeft, FrmDownloadResourceDetail)
                Case PageType.HelpDetail '帮助详情
                    PageChangeAnim(New MyPageLeft, Stack.Additional(1))
            End Select
#End Region

#Region "设置为最新状态"
            BtnExtraDownload.ShowRefresh()
            BtnExtraApril.ShowRefresh()
#End Region

            Logger.Info($"切换主要页面：{Stack.Page}, {SubType}")
            ModPlugin.RaisePageChanged(CInt(Stack.Page), CInt(SubType)) 'API v6：任何入口切页都广播一次'
            '页面切换事件由 ModPlugin.RaisePageChanged 统一广播（API v6 起事件总线也走同一条路径）
        Catch ex As Exception
            Logger.Error(ex, $"切换主要页面失败（ID {PageCurrent.Page}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
    ''' <summary>
    ''' 创建一个插件页面实例（失败时返回 Nothing）。
    ''' </summary>
    Private Function CreatePluginPage(Info As ModPlugin.PluginPageInfo) As FrameworkElement
        If Info Is Nothing OrElse Info.Factory Is Nothing Then Return Nothing
        Try
            Dim Element = Info.Factory.Invoke()
            If Element Is Nothing Then Return Nothing
            Return New PagePluginHost(Element)
        Catch ex As Exception
            Logger.Error(ex, "创建插件页面失败：" & Info.Title, LogBehavior.None)
            Hint("插件页面创建失败：" & ex.Message, HintType.Red)
            Return Nothing
        End Try
    End Function

    Private Sub PageChangeAnim(TargetLeft As FrameworkElement, TargetRight As FrameworkElement)
        AniStop("FrmMain LeftChange")
        AniStop("PageLeft PageChange") '停止左边栏变更导致的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        AniControlEnabled += 1
        '清除新页面关联性
        If TargetLeft.Parent IsNot Nothing Then TargetLeft.SetValue(ContentPresenter.ContentProperty, Nothing)
        If TargetRight IsNot Nothing AndAlso TargetRight.Parent IsNot Nothing Then TargetRight.SetValue(ContentPresenter.ContentProperty, Nothing)
        PageLeft = TargetLeft
        PageRight = TargetRight
        '触发页面通用动画
        CType(PanMainLeft.Child, MyPageLeft).TriggerHideAnimation()
        CType(PanMainRight.Child, MyPageRight).PageOnExit()
        AniControlEnabled -= 1
        '执行动画
        AniStart({
            AaCode(
            Sub()
                AniControlEnabled += 1
                '把新页面添加进容器
                PanMainLeft.Child = PageLeft
                PageLeft.Opacity = 0
                PanMainLeft.Background = Nothing
                AniControlEnabled -= 1
                RunInUi(Sub() PanMainLeft_Resize(PanMainLeft.ActualWidth), True)
            End Sub, 110),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                PageLeft.Opacity = 1
                PageLeft.TriggerShowAnimation()
            End Sub, 30, True)
        }, "FrmMain PageChangeLeft")
        AniStart({
            AaCode(
            Sub()
                AniControlEnabled += 1
                CType(PanMainRight.Child, MyPageRight).PageOnForceExit()
                '把新页面添加进容器
                PanMainRight.Child = PageRight
                PageRight.Opacity = 0
                PanMainRight.Background = Nothing
                AniControlEnabled -= 1
                RunInUi(Sub() BtnExtraBack.ShowRefresh(), True)
            End Sub, 110),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                PageRight.Opacity = 1
                PageRight.PageOnEnter()
            End Sub, 30, True)
        }, "FrmMain PageChangeRight")
    End Sub
    ''' <summary>
    ''' 退出子界面。
    ''' </summary>
    Private Sub PageChangeExit()
        If PageStack.Any Then
            '子页面 → 主页面，退出
            PanTitleMain.Visibility = Visibility.Visible
            PanTitleMain.IsHitTestVisible = True
            PanTitleInner.IsHitTestVisible = False
            AniStart({
                AaOpacity(PanTitleInner, -PanTitleInner.Opacity, 150),
                AaX(PanTitleInner, -18 - PanTitleInner.Margin.Left, 150,, New AniEaseInFluent),
                AaOpacity(PanTitleMain, 1 - PanTitleMain.Opacity, 150, 200),
                AaX(PanTitleMain, -PanTitleMain.Margin.Left, 350, 200, New AniEaseOutBack(AniEasePower.Weak)),
                AaCode(Sub() PanTitleInner.Visibility = Visibility.Collapsed,, True)
            }, "FrmMain Titlebar FirstLayer")
            PageStack.Clear()
        Else
            '主页面 → 主页面，无事发生
        End If
    End Sub

    '左边栏改变
    Private Sub PanMainLeft_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles PanMainLeft.SizeChanged
        If Not e.WidthChanged Then Return
        PanMainLeft_Resize(e.NewSize.Width)
    End Sub
    Private Sub PanMainLeft_Resize(NewWidth As Double)
        Dim Delta As Double = NewWidth - RectLeftBackground.Width
        If Math.Abs(Delta) > 0.1 AndAlso AniControlEnabled = 0 Then
            If PanMain.Opacity < 0.1 Then PanMainLeft.IsHitTestVisible = False '避免左边栏指向背景未能完美覆盖左边栏
            If NewWidth > 0 Then
                '宽度足够，显示
                AniStart({
                    AaWidth(RectLeftBackground, NewWidth - RectLeftBackground.Width, 180,, New AniEaseOutFluent(AniEasePower.ExtraStrong)),
                    AaOpacity(RectLeftShadow, 1 - RectLeftShadow.Opacity, 180),
                    AaCode(Sub() PanMainLeft.IsHitTestVisible = True, 150)
                }, "FrmMain LeftChange", True)
            Else
                '宽度不足，隐藏
                AniStart({
                    AaWidth(RectLeftBackground, -RectLeftBackground.Width, 180,, New AniEaseOutFluent),
                    AaOpacity(RectLeftShadow, -RectLeftShadow.Opacity, 180),
                    AaCode(Sub() PanMainLeft.IsHitTestVisible = True, 150)
                }, "FrmMain LeftChange", True)
            End If
        Else
            RectLeftBackground.Width = NewWidth
            PanMainLeft.IsHitTestVisible = True
            AniStop("FrmMain LeftChange")
        End If
    End Sub

#End Region

#Region "控件拖动"

    '在时钟中调用，使得即使鼠标在窗口外松开，也可以释放控件
    Public Sub DragTick()
        If DragControl Is Nothing Then Return
        If Not Mouse.LeftButton = MouseButtonState.Pressed Then
            DragStop()
        End If
    End Sub
    '在鼠标移动时调用，以改变 Slider 位置
    Public Sub DragDoing() Handles PanBack.MouseMove
        If DragControl Is Nothing Then Return
        If Mouse.LeftButton = MouseButtonState.Pressed Then
            DragControl.DragDoing()
        Else
            DragStop()
        End If
    End Sub
    Public Sub DragStop()
        '存在其他线程调用的可能性，因此需要确保在 UI 线程运行
        RunInUi(Sub()
                    If DragControl Is Nothing Then Return
                    Dim Control = DragControl
                    DragControl = Nothing
                    Control.DragStop() '控件会在该事件中判断 DragControl，所以得放在后面
                End Sub)
    End Sub

#End Region

#Region "附加按钮"

    '音乐
    Private Sub BtnExtraMusic_Click(sender As Object, e As EventArgs) Handles BtnExtraMusic.Click
        MusicControlPause()
    End Sub
    Private Sub BtnExtraMusic_RightClick(sender As Object, e As EventArgs) Handles BtnExtraMusic.RightClick
        MusicControlNext()
    End Sub

    '任务管理
    Private Sub BtnExtraDownload_Click(sender As Object, e As EventArgs) Handles BtnExtraDownload.Click
        PageChange(PageType.TaskManager)
    End Sub
    Private Function BtnExtraDownload_ShowCheck() As Boolean
        Return HasDownloadingTask() AndAlso Not PageCurrent = PageType.TaskManager
    End Function

    '投降
    Public Sub AprilGiveup() Handles BtnExtraApril.Click
        If IsAprilEnabled AndAlso Not IsAprilGiveup Then
            Hint("=D", HintType.Green)
            IsAprilGiveup = True
            FrmLaunchLeft.AprilScaleTrans.ScaleX = 1
            FrmLaunchLeft.AprilScaleTrans.ScaleY = 1
            BtnExtraApril.ShowRefresh()
        End If
    End Sub
    Public Function BtnExtraApril_ShowCheck() As Boolean
        Return IsAprilEnabled AndAlso Not IsAprilGiveup AndAlso PageCurrent = PageType.Launch
    End Function

    '关闭 Minecraft
    Public Sub BtnExtraShutdown_Click() Handles BtnExtraShutdown.Click
        Try
            If McLaunchLoaderReal IsNot Nothing Then McLaunchLoaderReal.Cancel()
            For Each Watcher In McWatcherList
                Watcher.Kill()
            Next
            Hint("已关闭运行中的 Minecraft！", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "强制关闭所有 Minecraft 失败")
        End Try
    End Sub
    Public Function BtnExtraShutdown_ShowCheck() As Boolean
        Return HasRunningMinecraft
    End Function

    ''' <summary>
    ''' 返回顶部。
    ''' </summary>
    Public Sub BackToTop() Handles BtnExtraBack.Click
        Dim RealScroll As MyScrollViewer = BtnExtraBack_GetRealChild()
        If RealScroll IsNot Nothing Then
            RealScroll.PerformVerticalOffsetDelta(-RealScroll.VerticalOffset)
        Else
            Logger.Error("无法返回顶部，未找到合适的 RealScroll", LogBehavior.Toast)
        End If
    End Sub
    Private Function BtnExtraBack_ShowCheck() As Boolean
        Dim RealScroll As MyScrollViewer = BtnExtraBack_GetRealChild()
        Return RealScroll IsNot Nothing AndAlso RealScroll.Visibility = Visibility.Visible AndAlso RealScroll.VerticalOffset > Height + If(BtnExtraBack.Show, 0, 700)
    End Function
    Private Function BtnExtraBack_GetRealChild() As MyScrollViewer
        If PanMainRight.Child Is Nothing OrElse TypeOf PanMainRight.Child IsNot MyPageRight Then Return Nothing
        Dim Page As MyPageRight = PanMainRight.Child
        Return Page.FindName(Page.PanScroll)
    End Function

#End Region

    '愚人节鼠标位置
    Public lastMouseArg As MouseEventArgs = Nothing
    Private Sub FormMain_MouseMove(sender As Object, e As MouseEventArgs) Handles Me.MouseMove
        lastMouseArg = e
    End Sub

End Class
