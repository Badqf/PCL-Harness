# PCL Harness 插件开发包（SDK）{{VERSION}}

对应启动器版本：**PCL Harness {{VERSION}}**（插件 API v5）。

## 包里有什么

- `PLUGIN-DEV.md`：完整开发指南（接口、权限、宿主 API、内嵌 UI、热重载、FAQ）
- API v5：细粒度权限（net.hosts / files.read / files.write）、DLL sha256 校验、事件总线、强类型设置、日志分级
- `build.ps1`：通用编译脚本（自动找 vbc 与 .NET Framework 4.8 参考程序集）
- `samples\HelloPlugin\`：最小插件（十行代码，API v5：事件、强类型设置、域名白名单）
- `samples\DemoPlugin\`：功能演示插件（事件钩子、设置项、卡片、文件/网络 API）
- `LICENSE`：上游《PCL 分发有限许可》+《PCL 存储库合理使用指南》

## 三步上手

1. 准备启动器：本 SDK **不内置** `PCL Harness.exe`（避免与启动器版本错配）。从同一个 Release 下载 `PCL Harness.exe` 放到本 SDK 目录，或者设一个环境变量：

   ```powershell
   setx PCL_HARNESS_EXE "C:\你的路径\PCL Harness.exe"
   ```

2. 编译示例插件：

   ```powershell
   powershell -ExecutionPolicy Bypass -File build.ps1 -Project samples\HelloPlugin
   ```

3. 把 `samples\HelloPlugin` 整个文件夹复制到 `PCLHarness\Plugins\`，重启启动器。插件页里应出现「你好插件」，并弹出一条提示。
   **注意：新插件默认是停用的**（`plugins.json` 里没有记录就不会加载），要在插件页点一下「启用」——启用会立即热重载，不用重启。`@ 'step3'

$t=Rep $t '全部手动指定：' @'
编译脚本已处理两件事：`/codepage:65001`（源码即使不带 BOM，中文也不会乱码）和引用 `Microsoft.VisualBasic.dll`（可以直接用 `vbCrLf`；不想引用就改用 `Environment.NewLine`）。自己拼 vbc 命令行时缺这两项会分别得到「中文乱码」和 `BC30451: 未声明名称`。

全部手动指定：'@ 'compile-notes'

$t=Rep $t '两处都会被扫描。每个插件目录里放 `plugin.json` + 编译好的 dll；`data\` 是插件自己的数据目录，由启动器创建。' @'
两处都会被扫描。每个插件目录里放 `plugin.json` + 编译好的 dll；`data\` 是插件自己的数据目录，由启动器创建，运行时对应 `Host.DataFolder`，真实路径是 `PCLHarness\Plugins\<插件id>\data\`（不是 `PluginData\`）。

   开发阶段可以直接把输出目录指到 `PCLHarness\Plugins\<你的插件>\`，编译完在插件页点「重新扫描」，或改一下 `plugin.json` 的时间戳即可热重载。

## 编译环境要求

| 组件 | 说明 |
| --- | --- |
| 启动器 exe | 作为编译引用（`vbc /r:"PCL Harness.exe"`），需与 SDK 版本一致 |
| Roslyn `vbc.exe` | 脚本自动搜索 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319` 与 Visual Studio 安装目录 |
| .NET Framework 4.8 参考程序集 | 装 .NET Framework 4.8 Developer Pack，或用 `-ReferencePath` 指定 |
| 目标框架 | .NET Framework 4.8 类库（VB.NET / C# 都可以，只要实现 `PCL.IPclPlugin`） |

全部手动指定：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Project samples\DemoPlugin `
    -Launcher "C:\PCL\PCL Harness.exe" `
    -VbcPath "C:\...\Roslyn\vbc.exe" `
    -ReferencePath "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
```

## 插件放哪里

- 新版目录：`PCLHarness\Plugins\<插件id>\`（与启动器 exe 同级）
- 兼容旧目录：`PCL\Plugins\<插件id>\`

两处都会被扫描。每个插件目录里放 `plugin.json` + 编译好的 dll；`data\` 是插件自己的数据目录，由启动器创建。

## 权限

`plugin.json` 的 `permissions` 只认三种：`net`、`files`、`args`（宿主实际校验的字符串；`file` 单数、`ui`、`process`、`events` 都不会生效）。

- 不写 = 全部放行（兼容旧插件）
- 写了之后超范围调用会抛 `UnauthorizedAccessException` 
- v5 还可写成对象进一步收缩范围：`{"net":{"hosts":[...]},"files":{"read":[...],"write":[...]}}`

详见 `PLUGIN-DEV.md` 第 2 节。

## 版本对应

插件通过引用 `PCL Harness.exe` 编译，宿主 API 变化后需要重新编译。SDK 文件名里的版本号就是对应的启动器版本。

## 问题排查

| 现象 | 处理 |
| --- | --- |
| 找不到启动器 exe | `-Launcher` 指定，或设 `PCL_HARNESS_EXE`，或把 exe 放在脚本同目录 |
| 找不到 `vbc.exe` | 装 Visual Studio Build Tools 的「.NET 桌面开发」，或 .NET SDK |
| 找不到 `mscorlib.dll` | 装 .NET Framework 4.8 Developer Pack |
| 插件页显示「加载失败」 | 点开该插件看完整异常；常见是 dll 名与 `plugin.json` 的 `dll` 字段不一致，或类型没实现 `IPclPlugin` |
| 热重载不生效 | 先点「重新扫描」；仍不行就重启启动器（已加载的 dll 会被锁定） |
| 插件页看不到 / 日志里没有插件行 | 先确认 `plugin.json` 在 `PCLHarness\Plugins\<id>\` 这一层；新插件默认停用，要在插件页点「启用」。日志会写「插件 X 默认未启用…」和「扫描完成：共 N 个插件，已启用 M 个」 |
| 中文变成乱码 | 源码存成 UTF-8；自拼 vbc 要加 `/codepage:65001`（`build.ps1` 已加） |
| `BC30451: 未声明名称 vbCrLf` | 给 vbc 加 `/r:Microsoft.VisualBasic.dll`（`build.ps1` 已加），或改用 `Environment.NewLine` |

## 许可

示例与指南随启动器一起分发，沿用上游《PCL 分发有限许可》+《PCL 存储库合理使用指南》（见 `LICENSE`）。启动器本身是**第三方基于 PCL 的二次创作**，不隶属官方。

插件通过引用启动器 exe 编译并在其进程内加载，属于同一程序的组成部分；按上游「公开源代码」的要求，**建议你的插件也公开源代码**，并同样给出龙腾猫跃的署名。若希望插件闭源分发，请先到上游 Discussions 确认，或改用进程外插件（v5 规划中）。
