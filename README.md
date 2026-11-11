# PCL Harness

基于 [Plain Craft Launcher 2 开源版](https://github.com/Meloong-Git/PCL)（上游版本 2.14.0.0）二次开发的自编版启动器：保留原版全部功能，另外加入独立配置目录、主题系统、插件框架等扩展。下载与联机功能自 3.9.0 起改为插件提供。

- 当前版本：**3.9.4**
- 插件市场：<https://github.com/Badqf/PCL-Harness-Market>（启动器「插件市场」页可直接浏览、安装与更新）
- 发布流程与版本规范：[docs/发布流程.md](docs/发布流程.md)
- 上游基线：PCL 2.14.0.0（2026-10-16 发布）
- 反馈 / 问题：<https://github.com/Badqf/PCL-Harness/issues>
- 插件开发文档：[PLUGIN-DEV.md](PLUGIN-DEV.md)（API v8）

## 下载与联机由插件提供（3.9.0 起）

本体不再内置「下载」「联机」两个页面，改为从 [插件市场](https://github.com/Badqf/PCL-Harness-Market) 按需安装。安装并在「插件」页启用后，顶部标签栏出现对应入口、设置页出现对应卡片；禁用或卸载后入口立即消失。

| 市场条目 | 提供的能力 | 要求 |
| --- | --- | --- |
| `download-page`「下载页面」 | 安装游戏、Mod、整合包、资源包、光影包与数据包 | PCL Harness 3.9.0+（API v8），仅申请 `ui` 权限 |
| `link-page`「联机页面」 | 房间码 P2P 直连，支持虚拟局域网（TUN）模式 | 同上 |

- 已经下载到本地的游戏、Mod、整合包、资源包、光影包与联机配置**全部保留**，卸载插件也不会删除。
- 插件卡片上的说明来自插件包内的 `plugin.json`，与市场条目文案一致；以市场条目为准可以拿到最新说明。
- 市场索引与包地址见仓库内 `index.json`；离线或镜像不可用时可手动下载 `.pclplugin`，用「插件 → 导入插件…」安装。

## Harness 新增功能

### 启动与配置
- **独立配置目录**：配置与缓存放在 `PCLHarness\`，用户数据放在 `%AppData%\Roaming\PCLHarness\`，与原版 PCL 互不覆盖。
- **首次启动指引**：首次运行时**自动**把旧 PCL 的数据迁移到独立目录（启动器设置、图片与音乐、启动器数据、凭据、自定义主页等，旧文件不删除也不修改），迁移在读取设置之前完成，老配置本次启动即生效；迁移完只弹一次结果页列出搬了什么，旧目录没有数据时不弹窗。老版本遗留的「跳过」标记仍会手动询问一次。

### 界面与主题
- **主题系统**：深色 / 浅色，色相、饱和度、亮度三个滑块 + 预设配色，顶部标题栏、控件、文字与「内存分配」里的动态数字全部跟随。
- **切换即重启**：改动明暗或配色后自动重启启动器，不再需要手动重启才能整体生效。
- 色相按上游许可限制在蓝色系（200°–230°），提供标准蓝 / 冰蓝 / 海蓝 / 靛青四档预设。

### 插件框架（API v8）
- 清单 `plugin.json`：`id`/`name`/`version`/`author`/`description`/`entry`/`dll`/`permissions`/`dependencies`/`minApiVersion`/`update`。
- 接口：`IPclPlugin`、`PclPluginBase`、`IPclPluginUi`/`IPclPluginUi2`/`IPclPluginUi3`（主页卡片 / 设置页卡片 / 状态栏 / 自定义页面）、`IPclPluginEvents`、`IPclPluginLifecycle`、`IPclPluginLink`、`IPclPluginDownload`、`IPclPluginClosing`、`IPclPluginInstanceMenu`。
- 宿主能力：日志与提示、消息框、打开网页/文件夹、主题信息、受控文件读写、HTTP 请求与下载（含 5 跳重定向校验）、追加 JVM / 游戏参数、跨页导航、插件间服务与后台任务、实例与内容（Mod / 资源包 / 光影包）查询与启停。
- 权限分级：`0` 只读提示、`1` 需要留意、`2` 高风险（当前为 `process`）。启用前逐条确认并记住指纹；权限或授权主机有变化会重新询问，卡片上的「重新确认权限」可撤销；高风险在启动时会单独弹一次醒目提示，取消即自动禁用。
- 版本演进：v5 事件总线 / 强类型设置 / 白名单 / `sha256`；v6 界面扩展点 / 服务 / 后台任务 / 崩溃熔断；v7 实例与内容读写、权限分级与风险提示；v8 内置页面开关（`Host.EnableBuiltInPage`）与插件原生页面。
- **可靠性**：热重载、单份备份与「回滚到上一版」、`.trash`/`.backup` 30 天清理、卸载时目录移入 Windows 回收站（3.9.1，可从回收站还原）、按字节流隔离加载（同名 DLL 的多个插件互不干扰）。
- 示例插件与开发包：`samples/DemoPlugin`；SDK 随 Release 发布（`PCL-Harness-Plugin-SDK-*.zip`），构建脚本在 `tools/sdk/`。

### 联机直连（插件提供，实验性）
- 免登录直连：房主创建房间后得到 `内网IP:端口|房间码` 邀请串，朋友填入即可加入；邀请同时带外网与局域网地址，跨网络也能直接加入。
- **虚拟局域网（TUN）模式**：启动时自动创建虚拟网卡，房主固定 `10.114.114.1`、加入者自动分配 `10.114.114.2~254`，任意游戏、任意端口都能连，路由器只需放行监听端口；需要管理员权限（首次弹 UAC 与防火墙授权）并安装 wintun 虚拟网卡。
- 完成页显示该连的虚拟地址，状态条区分「等待朋友加入 / 游戏端口还没开 / 已就绪」，并显示直连或中继、RTT 与 NAT 类型；「联机诊断」可一键导出这些信息与内核日志。
- TUN 不改变打洞能力：仍需一方网络可达（UPnP 或手动端口映射），对称 NAT / CGNAT 依旧连不上。

### 上游 2.14.0.0 的新特性（已合并）
- 版本设置新增「修改」页：可修改、添加、移除、更新已安装版本的 MC 版本与组件。
- 启动游戏时按 Esc 取消、主页启动日志可复制、新增主页预设、整合包另存为后打开文件夹等。

## Windows 提示「未知发布者」怎么办

PCL Harness 目前**没有购买代码签名证书**，所以从网上下载的 exe 会被 Windows SmartScreen 拦一次，弹窗写着「Windows 已保护你的电脑 / 未知发布者」。这不是杀毒报警，而是「这个文件来自网络，且发布者身份没验证过」。

- **正常放行**：弹窗里点「更多信息」→「仍要运行」。
- **提前解除**（推荐，省得每次问）：下载后右键 exe →「属性」→ 勾选「解除锁定」→「确定」；或者用 PowerShell 执行 `Unblock-File .\PCL Harness.exe`。
- **自动更新不受影响**：启动器更新时会自己清掉新文件的「网络标记」（3.7.1 起），所以更新后的启动不会再被拦。

要彻底消除这个提示只能做**代码签名**：买证书（EV 证书立即可信；OV / Azure Trusted Signing 需要积累一点信誉），然后：

```powershell
$env:PCL_SIGN_CERT = "C:\path\codesign.pfx"    # 证书
$env:PCL_SIGN_PASSWORD = "******"              # 口令，别写进脚本
powershell -File build.ps1 -Sign
```

`build.ps1` 会调用 `signtool` 签名并校验；证书和 signtool 都到位后，发布出来的 exe 在 SmartScreen 里会显示证书主体而不是「未知发布者」。

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

插件 SDK 与打包：

```powershell
# 开发期编译单个插件（产物落在插件目录的父目录）
.\tools\sdk\build.ps1 -Project <插件目录> -Launcher '路径\PCL Harness.exe' -Pack

# 打一个完整的 SDK 分发包
.\tools\pack-plugin-sdk.ps1 -Version <版本>
```

> 本仓库不含 `bin/`、`obj/`、上游发布补丁包 `patches/` 与密钥文件；插件密钥放在 `PCLHarness\Secrets.json` 或环境变量里，不入库。

## 目录结构

```
Plain Craft Launcher 2\   VB.NET / WPF 主程序
PCLCS\                    C# 子项目（Minecraft 版本处理、版本修改页等）
MeloongCore\              上游核心库
samples\DemoPlugin\       插件示例
tools\sdk\                插件 SDK 与构建脚本
PLUGIN-DEV.md             插件开发指南（API v8）
```

运行期目录（与仓库分离）：`PCLHarness\`（配置、缓存、`Plugins\`、`Secrets.json`）、`%AppData%\Roaming\PCLHarness\`（用户数据）。

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
- [EasyTier](https://github.com/EasyTier/EasyTier)：联机插件使用的组网内核
- [bangbang93 / BMCLAPI](https://bmclapi.bangbang93.com)、[MC 百科](https://www.mcmod.cn)、[MCIM](https://github.com/mcmod-info-mirror)、[重庆大学开源软件镜像站](https://mirrors.cqu.edu.cn/) 等镜像与数据来源
