# PCL2 2.13.1.1 源码构建说明（免管理员）

## 结论

本机**不需要 Visual Studio，也不需要管理员权限**即可构建 PCL2 主工程。已实测通过：

```
Plain Craft Launcher 2 -> F:\PCL2-Source\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe
```

产物运行正常（主窗口标题 `Plain Craft Launcher　`，常驻内存约 228 MB）。

## 原理

PCL2 主工程是老式（非 SDK 风格）VB WPF 项目，`.NET Framework 4.8`。原生 VS 构建链的关键是两件东西：

1. **WPF 标记编译**：`Microsoft.WinFX.targets` + `PresentationBuildTasks.dll`（net48 版，在 GAC 里）。
2. **VB 编译器**：需要能识别 `$"..."`、`NameOf`、`?.` 等现代语法的 Roslyn。

`dotnet msbuild`（.NET Core 版 MSBuild）**跑不了第 1 步**——它的标记编译任务只能定位 net8.0 的 `PresentationBuildTasks`，对 net48 目标会报
`error MC2000: Could not find assembly 'Microsoft.VisualBasic, Version=10.0.0.0'`。

而 Windows 自带一个 **.NET Framework 版 MSBuild 4.0**（`C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe`，版本 4.8.9221.0），它加载的正是 net48 的 WPF 构建任务。它自带的 `vbc.exe` 太老（VB 10），但只要把编译器的 `VbcToolPath` / `VbcToolExe` 指向 Roslyn 的 net472 版 `vbc.exe`，即可两全。

## 依赖的三样东西（均已就位）

| 组件 | 位置 | 来源 |
|---|---|---|
| net48 引用程序集 | `C:\Users\XYM_3\dotnet-fwreal\` | NuGet `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 |
| Roslyn net472 编译器 | `C:\Users\XYM_3\roslyn\tasks\` | NuGet `Microsoft.Net.Compilers.Toolset` 4.14.0 |
| MSBuild 4.0 | `C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe` | Windows 自带 |

## 构建

```powershell
pwsh -File F:\PCL2-Source\build.ps1
# 或指定配置
pwsh -File F:\PCL2-Source\build.ps1 -Configuration Debug
```

脚本内部等价于：

```powershell
& C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe `
    "F:\PCL2-Source\Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" `
    /p:Configuration=Release /p:Platform=AnyCPU `
    /p:TargetFrameworkRootPath="C:\Users\XYM_3\dotnet-fwreal\" `
    /p:VbcToolPath="C:\Users\XYM_3\roslyn\tasks" /p:VbcToolExe=vbc.exe `
    /m /v:m
```

## 源码侧做的四处改动（都在主工程 vbproj 里）

1. **三个 `ProjectReference` 改成 `Reference` + `HintPath`**
   原因：被引用的 `MeloongCore.csproj` / `MeloongCore.Wpf.csproj` / `PCLCS.csproj` 是 SDK 风格工程，MSBuild 4.0 解析不了（`MSB4041 项目的默认 XML 命名空间必须为 MSBuild XML 命名空间`）。
   做法：先 `dotnet build` 三个依赖工程，再让主工程直接引用其输出 DLL（`<Private>False</Private>`，运行时 DLL 已嵌在 `Resources.resx` 资源里）。
2. **新增 `<Reference Include="netstandard" />`**：`MeloongCore.dll` 是 netstandard2.0，主工程（net48）需要 netstandard 门面。
3. **新增 `<Reference Include="System.ValueTuple" />`**：`ModLoader.ProgressChanged` 事件签名为 `Action(Of ValueTuple(Of Double, Double))`。
4. 备份文件：`Plain Craft Launcher 2.vbproj.orig`（改动前）、`.legacy`（上游原始）、`.sdk`（我早前试的 SDK 风格版本，未采用）。

## 与官方 exe 的元数据差异（属正常）

| 指标 | 官方 exe | 本次构建 |
|---|---|---|
| 程序集版本 | 2.13.1.1 | 2.13.1.1 ✅ |
| 目标框架 | .NETFramework,Version=v4.8 | 同 ✅ |
| 架构 / 标志 | anycpu / IL-only | 同 ✅ |
| AssemblyRef | 23 | 23 ✅ |
| ManifestResource | 2 | 2 ✅ |
| TypeDef | 643 | 635 |
| MethodDef | 6116 | 6023 |
| Field | 3503 | 3446 |

差的那 8 个类型几乎全是 Roslyn 为匿名委托生成的 `VB$AnonymousDelegate_N`（编译器版本不同，生成数量不同），不影响源码语义。

## 运行

```
F:\PCL2-Source\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe
```

首次运行会在 exe 同级目录创建 `PCL\` 与 `.minecraft\`。冒烟测试目录：`F:\PCL2-Build\`。

## 注意

仓库根的 `LICENCE` 是《PCL 分发有限许可》。自己改着用没问题；对外分发属于「重度使用」，需满足：标注第三方二次创作、名称以 `PCL` 开头加后缀、关于页首位署名龙腾猫跃并附赞助链接、公开源码、保留许可文件、保留正版购买劝导、界面主色相为蓝色。
