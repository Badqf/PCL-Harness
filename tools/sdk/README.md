# PCL Harness 插件开发包（SDK）{{VERSION}}

对应启动器版本：**PCL Harness {{VERSION}}**（插件 API v4）。

## 包里有什么

- `PLUGIN-DEV.md`：完整开发指南（接口、权限、宿主 API、内嵌 UI、热重载、FAQ）
- `build.ps1`：通用编译脚本（自动找 vbc 与 .NET Framework 4.8 参考程序集）
- `samples\HelloPlugin\`：最小插件（十行代码）
- `samples\DemoPlugin\`：功能演示插件（事件钩子、设置项、卡片、文件/网络 API）
- `LICENSE`：GPL-3.0

## 三步上手

1. 准备启动器：把本 Release 里的 `PCL Harness.exe` 放到本 SDK 目录，或者设一个环境变量：

   ```powershell
   setx PCL_HARNESS_EXE "C:\你的路径\PCL Harness.exe"
   ```

2. 编译示例插件：

   ```powershell
   powershell -ExecutionPolicy Bypass -File build.ps1 -Project samples\HelloPlugin
   ```

3. 把 `samples\HelloPlugin` 整个文件夹复制到 `PCLHarness\Plugins\`，重启启动器。插件页里应出现「你好插件」，并弹出一条提示。

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

`plugin.json` 的 `permissions` 可选：`net`、`files`、`args`、`ui`、`process`。

- 不写 = 全部放行（兼容旧插件）
- 写了之后超范围调用会抛 `UnauthorizedAccessException`

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

## 许可

示例与指南随启动器一起以 GPL-3.0 发布（见 `LICENSE`）；你自己写的插件可以另选许可。
