# PCL Harness

基于 [Plain Craft Launcher 2 开源版](https://github.com/Meloong-Git/PCL)（上游版本 2.14.0.0）二次开发的自编版启动器，在保留原版功能的基础上加入独立配置目录、主题系统、插件系统与联机直连等扩展。

- 当前版本：**3.6.2**
- 上游基线：PCL 2.14.0.0（2026-10-16 发布）
- 反馈 / 问题：<https://github.com/Badqf/PCL-Harness/issues>
- 插件开发文档：[PLUGIN-DEV.md](PLUGIN-DEV.md)

## Harness 新增功能

### 启动与配置
- **独立配置目录**：配置与缓存放在 `PCLHarness\`，用户数据放在 `%AppData%\Roaming\PCLHarness\`，与原版 PCL 互不覆盖。
- **首次启动指引**：首次运行时**自动**把旧 PCL 的数据迁移到独立目录（启动器设置、图片与音乐、启动器数据、凭据、自定义主页等，旧文件不删除也不修改），迁移在读取设置之前完成，老配置本次启动即生效；迁移完只弹一次结果页列出搬了什么，旧目录没有数据时不弹窗。老版本遗留的「跳过」标记仍会手动询问一次。

### 界面与主题
- **主题系统**：深色 / 浅色，色相、饱和度、亮度三个滑块 + 预设配色，顶部标题栏、控件、文字与“内存分配”里的动态数字全部跟随。
- **切换即重启**：改动明暗或配色后自动重启启动器，不再需要手动重启才能整体生效。

### 插件系统（API v5）
- 清单 `plugin.json`（`id`/`name`/`version`/`author`/`description`/`entry`/`dll`/`permissions`/`dependencies`）。
- 接口：`IPclPlugin`、`IPclPluginUi`、`IPclPluginUi2`（主页卡片 / 设置页卡片 / 状态栏组件）、`IPclPluginEvents`、`IPclPluginLifecycle`、`IPclPluginLink`、`IPclPluginDownload`、`IPclPluginClosing`。
- 宿主能力：日志与提示、消息框、打开网页/文件夹、主题信息、文件读写（受限目录）、HTTP 请求与下载、追加 JVM / 游戏参数、跨页导航。
- 权限（`net`/`files`/`args`/`ui`/`process`）与依赖顺序加载、循环依赖与缺失依赖提示。
- **v5 新增**：事件总线（`Host.Events.On`）、强类型设置（`GetSetting/SetSetting`）、日志分级、`net.hosts` 与 `files.read/write` 白名单、DLL `sha256` 校验、`minApiVersion` 闸门。
- **热重载**：改动插件目录里的 JSON 会自动重新加载并提示。
- 插件管理页支持搜索、启用/禁用，并显示版本、来源、说明与权限。
- 示例插件与开发包：`samples/DemoPlugin`（发布版目录里另有 `plugin-dev-kit.zip`）。

### 联机直连（实验性）
- 免登录直连模式：房主创建房间后得到 `内网IP:端口|房间码` 邀请串，朋友填入即可加入。
- 内置 [EasyTier](https://github.com/EasyTier/EasyTier)，自动申请 UPnP 端口映射，房主无需手动改路由器。
- **虚拟局域网（TUN）模式**（3.4.0，状态显示见 3.4.1）：启动时自动创建虚拟网卡，房主固定 `10.114.114.1`、加入者自动分配 `10.114.114.2~254`，任意游戏、任意端口都能连，路由器只需放行 EasyTier 监听端口；需要管理员权限（首次会弹 UAC 与防火墙授权）并安装 wintun 虚拟网卡。完成页只显示该连的虚拟地址，状态条会区分「等待朋友加入 / 游戏端口还没开 / 已就绪」，并显示直连或中继、RTT 与 NAT 类型；完成页的「联机诊断」能一键导出这些信息与内核日志，方便排查连不上的原因（3.4.5）；直连模式的监听端口改为「真实绑定测试 + 占用时自动换端口」，不再被 Hyper-V / WSL / Docker 预留的端口段坑到（3.4.6）；直连邀请同时带外网与局域网地址，跨网络也能直接加入（3.4.7）。
- TUN 不改变打洞能力：仍需一方网络可达（UPnP 或手动端口映射），对称 NAT / CGNAT 依旧连不上。
- 注意：未开 TUN 时（`--no-tun`）不能连 `127.0.0.1`，同一台机器双开测试请用局域网 IP。

### 上游 2.14.0.0 的新特性（已合并）
- 版本设置新增「修改」页：可修改、添加、移除、更新已安装版本的 MC 版本与组件。
- 启动游戏时按 Esc 取消、主页启动日志可复制、新增主页预设、整合包另存为后打开文件夹等。

## 构建

环境要求：

- Windows 10/11，.NET Framework 4.8 开发工具
- .NET SDK（用于编译 `PCLCS` 等 C# 子项目）
- Visual Studio Build Tools 或 MSBuild

```powershell
# 在仓库根目录
.\build.ps1
```

产物：`Plain Craft Launcher 2\bin\PCL Harness.exe`。

> 本仓库不含 `bin/`、`obj/`、上游发布补丁包 `patches/` 与密钥文件；插件密钥放在 `PCLHarness\Secrets.json` 或环境变量里，不入库。

## 目录结构

```
Plain Craft Launcher 2\   VB.NET / WPF 主程序
PCLCS\                    C# 子项目（Minecraft 版本处理、版本修改页等）
MeloongCore\              上游核心库
samples\DemoPlugin\       插件示例与一键构建脚本
PLUGIN-DEV.md             插件开发指南（API v5）
```

## 许可

本项目是**第三方基于 PCL 独立进行的二次创作**，不隶属于龙腾猫跃 / PCL 官方。

上游 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) **不使用 GPL**，而是《PCL 分发有限许可》+《PCL 存储库合理使用指南》（原文见 [LICENSE](LICENSE)）。本项目按「重度使用」的要求执行：

- 在关于页首位给出龙腾猫跃的署名与赞助链接；
- 公开源代码（本仓库）；
- 继续使用上游《PCL 分发有限许可》作为指引（本仓库 [LICENSE](LICENSE)）；
- 保留上游 `McLaunchPrecheck` 的正版购买劝导与赞助劝导；
- Windows 下不提供与 PCL 赞助解锁功能相似的功能。

名称、署名与分发方式如需调整，先看 [LICENSE](LICENSE) 或到上游 Discussions 确认。


## 致谢

- [龙腾猫跃 / Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL)：本项目的基础
- [EasyTier](https://github.com/EasyTier/EasyTier)：联机模块
- [bangbang93 / BMCLAPI](https://bmclapi.bangbang93.com)、[MC 百科](https://www.mcmod.cn)、[MCIM](https://github.com/mcmod-info-mirror)、[重庆大学开源软件镜像站](https://mirrors.cqu.edu.cn/) 等镜像与数据来源
