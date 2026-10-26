# PCL Harness

基于 [Plain Craft Launcher 2 开源版](https://github.com/Meloong-Git/PCL)（上游版本 2.14.0.0）二次开发的自编版启动器，在保留原版功能的基础上加入独立配置目录、主题系统、百宝箱、插件系统与联机直连等扩展。

- 当前版本：**3.1.7**（自编版）
- 上游基线：PCL 2.14.0.0（2026-10-16 发布）
- 反馈 / 问题：<https://github.com/Badqf/PCL-Harness/issues>
- 插件开发文档：[PLUGIN-DEV.md](PLUGIN-DEV.md)

## Harness 新增功能

### 启动与配置
- **独立配置目录**：配置与缓存放在 `PCLHarness\`，用户数据放在 `%AppData%\Roaming\PCLHarness\`，与原版 PCL 互不覆盖。
- **首次启动指引**：首次运行弹出迁移向导，可勾选把旧 PCL 的启动器设置、插件、图片与音乐、启动器数据复制过来；也可以直接跳过（只询问一次）。

### 界面与主题
- **主题系统**：深色 / 浅色，色相、饱和度、亮度三个滑块 + 预设配色，顶部标题栏、控件、文字与“内存分配”里的动态数字全部跟随。
- **切换即重启**：改动明暗或配色后自动重启启动器，不再需要手动重启才能整体生效。

### 百宝箱（更多 → 百宝箱）
- **今日人品**：按日期生成当天固定的人品值并弹窗显示评语。
- **内存优化**：调用 `EmptyWorkingSet` 回收启动器自身占用的物理内存。
- **清理游戏垃圾**：清理当前版本目录下的 `logs\`、`crash-reports\` 与 `versions\` 里的 `*.log`，不动存档与 Mod。
- **下载自定义文件**：用 PCL 的多线程下载引擎下载任意文件到指定目录。

### 插件系统（API v4）
- 清单 `plugin.json`（`id`/`name`/`version`/`author`/`description`/`entry`/`dll`/`permissions`/`dependencies`）。
- 接口：`IPclPlugin`、`IPclPluginUi`、`IPclPluginUi2`（主页卡片 / 设置页卡片 / 状态栏组件）、`IPclPluginEvents`、`IPclPluginLifecycle`、`IPclPluginLink`、`IPclPluginDownload`、`IPclPluginClosing`。
- 宿主能力：日志与提示、消息框、打开网页/文件夹、主题信息、文件读写（受限目录）、HTTP 请求与下载、追加 JVM / 游戏参数、跨页导航。
- 权限（`net`/`files`/`args`/`ui`/`process`）与依赖顺序加载、循环依赖与缺失依赖提示。
- **热重载**：改动插件目录里的 JSON 会自动重新加载并提示。
- 插件管理页支持搜索、启用/禁用，并显示版本、来源、说明与权限。
- 示例插件与开发包：`samples/DemoPlugin`（发布版目录里另有 `plugin-dev-kit.zip`）。

### 联机直连（实验性）
- 免登录直连模式：房主创建房间后得到 `内网IP:端口|房间码` 邀请串，朋友填入即可加入。
- 内置 [EasyTier](https://github.com/EasyTier/EasyTier)，自动申请 UPnP 端口映射，房主无需手动改路由器。
- 注意：`--no-tun` 模式下不能连 `127.0.0.1`，同一台机器测试请用局域网 IP。

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
PLUGIN-DEV.md             插件开发指南（API v4）
```

## 许可

继承上游，使用 **GPLv3**（见 [LICENSE](LICENSE)）。

## 致谢

- [龙腾猫跃 / Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL)：本项目的基础
- [EasyTier](https://github.com/EasyTier/EasyTier)：联机模块
- [bangbang93 / BMCLAPI](https://bmclapi.bangbang93.com)、[MC 百科](https://www.mcmod.cn)、[MCIM](https://github.com/mcmod-info-mirror)、[重庆大学开源软件镜像站](https://mirrors.cqu.edu.cn/) 等镜像与数据来源
