# PCL Harness

基于 [Plain Craft Launcher 2 开源版](https://github.com/Meloong-Git/PCL)（上游 2.14.0.0）二次开发的自编版启动器：保留原版全部功能，另外加入独立配置目录、主题系统与插件框架。

- 下载（镜像）：<https://ghfast.top/https://github.com/Badqf/PCL-Harness/releases/latest>
- 插件市场：<https://github.com/Badqf/PCL-Harness-Market>
- 插件开发：[PLUGIN-DEV.md](PLUGIN-DEV.md)（API v10）；发版规范：[docs/发布流程.md](docs/发布流程.md)
- 反馈问题：<https://github.com/Badqf/PCL-Harness/issues>

## 下载与联机由插件提供

本体不再内置「下载」「联机」两个页面（3.9.0 起），改为从 [插件市场](https://github.com/Badqf/PCL-Harness-Market) 按需安装。安装并在「插件」页启用后，顶部出现对应入口；禁用或卸载后入口立即消失。

| 市场条目 | 能力 | 要求 |
| --- | --- | --- |
| `download-page`「下载页面」 | 安装游戏、Mod、整合包、资源包、光影包与数据包 | PCL Harness 3.9.0+（API v8），仅申请 `ui` 权限 |
| `link-page`「联机页面」 | 房间码 P2P 直连，支持虚拟局域网（TUN）模式 | 同上 |

- 已经下载到本地的游戏、Mod、整合包与联机配置**全部保留**，卸载插件也不会删除。
- 市场索引与包地址见仓库内 `index.json`；镜像不可用时可手动下载 `.pclplugin`，用「插件 → 导入插件…」安装。

## Harness 新增功能

### 启动与配置
- **独立配置目录**：配置与缓存放在 `PCLHarness\`，用户数据放在 `%AppData%\Roaming\PCLHarness\`，与原版 PCL 互不覆盖。
- **首次启动指引**：首次运行自动把旧 PCL 的数据迁移到独立目录（启动器设置、图片与音乐、启动器数据、凭据、自定义主页等，旧文件不删除也不修改），迁移在读取设置之前完成；迁移完弹一次结果页列出搬了什么，旧目录没有数据时不弹窗。

### 界面与主题
- **主题系统**：深色 / 浅色，色相、饱和度、亮度三个滑块 + 预设配色，标题栏、控件、文字与内存分配里的动态数字全部跟随。
- **切换即重启**：改动明暗或配色后自动重启启动器，不需要手动重启才生效。
- 色相按上游许可限制在蓝色系（200°–230°），提供标准蓝 / 冰蓝 / 海蓝 / 靛青四档预设。

### 插件框架
- 插件可扩展页面、标题栏入口、设置项与实例右键菜单，并调用实例与内容读写、批量下载、整合包识别与安装、文件与网络等宿主能力。
- 权限按低 / 中 / 高分级，启用前逐条确认并记住指纹；未声明权限即零权限。高风险权限在启动时会单独提示，取消即自动禁用。
- **可靠性**：热重载、单份备份与「回滚到上一版」、`.trash`/`.backup` 30 天清理、卸载时目录移入 Windows 回收站、按字节流隔离加载（同名 DLL 的多个插件互不干扰）。
- 示例插件见 `samples/DemoPlugin`；SDK 随 Release 发布（`PCL-Harness-Plugin-SDK-*.zip`），构建脚本在 `tools/sdk/`。

### 自动更新
- 启动器通过 GitHub Releases 自检更新，镜像优先、官方源兜底；默认静默下载、退出时替换，也可以在设置里手动检查。

## Windows 提示「未知发布者」怎么办

PCL Harness 没有购买代码签名证书，从网上下载的 exe 会被 Windows SmartScreen 拦一次，弹窗写着「Windows 已保护你的电脑 / 未知发布者」。这不是杀毒报警，而是「文件来自网络、发布者身份未验证」。

- **正常放行**：弹窗里点「更多信息」→「仍要运行」。
- **提前解除**（推荐）：右键 exe →「属性」→ 勾选「解除锁定」→「确定」；或执行 `Unblock-File .\PCL Harness.exe`。
- **自动更新不受影响**：更新时会自动清掉新文件的网络标记，更新后的启动不会再被拦。

签名流程见 [docs/发布流程.md](docs/发布流程.md)。

## 构建

需要 Windows 10/11、.NET Framework 4.8 开发工具、.NET SDK 与 MSBuild（或 Visual Studio Build Tools）。

```powershell
.\build.ps1                    # 产物：Plain Craft Launcher 2\bin\PCL Harness.exe
.\tools\pack-plugin-sdk.ps1 -Version <版本>   # 打包插件 SDK
```

本仓库不含 `bin/`、`obj/`、上游补丁包 `patches/` 与密钥文件；插件密钥放在 `PCLHarness\Secrets.json` 或环境变量里，不入库。

## 目录结构

```
Plain Craft Launcher 2\   VB.NET / WPF 主程序
PCLCS\                    C# 子项目（Minecraft 版本处理、版本修改页等）
MeloongCore\              上游核心库
samples\DemoPlugin\       插件示例
tools\sdk\                插件 SDK 与构建脚本
PLUGIN-DEV.md             插件开发指南
```

运行期目录（与仓库分离）：`PCLHarness\`（配置、缓存、`Plugins\`、`Secrets.json`）、`%AppData%\Roaming\PCLHarness\`（用户数据）。

## 许可

本项目是**第三方基于 PCL 独立进行的二次创作**，不隶属于龙腾猫跃 / PCL 官方。

上游 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 不使用 GPL，而是《PCL 分发有限许可》+《PCL 存储库合理使用指南》（原文见 [LICENSE](LICENSE)）。本项目按「重度使用」的要求执行：关于页首位给出龙腾猫跃的署名与赞助链接、公开源代码、继续使用上游许可、保留正版购买与赞助劝导、不提供与赞助解锁相似的功能。

名称、署名与分发方式如需调整，先看 [LICENSE](LICENSE) 或到上游 Discussions 确认。

## 致谢

- [龙腾猫跃 / Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL)：本项目的基础
- [EasyTier](https://github.com/EasyTier/EasyTier)：联机插件使用的组网内核
- [BMCLAPI](https://bmclapi.bangbang93.com)、[MC 百科](https://www.mcmod.cn)、[MCIM](https://github.com/mcmod-info-mirror)、[重庆大学开源软件镜像站](https://mirrors.cqu.edu.cn/) 等镜像与数据来源
