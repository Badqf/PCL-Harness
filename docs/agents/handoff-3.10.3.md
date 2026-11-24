# 交接：3.10.3（C：上限收紧 + 多镜像回退）

> 写于 2026-11-24 19:35，目的是让**新会话**（Lead 已切回 DeepSeek）能直接接管，不必回溯聊天记录。

## 当前状态

- 分支 `main`，远端与本地一致（`455dc9a`，之后只有文档提交）。
- 已发布：3.10.2（Release 456319176，构建 479）——含 API v10 的批量下载/写实例/导出、逐文件进度回调、启动前后同步钩子、自动更新失败提示。
- issue #1「无法正常更新」已关闭（根因是用户机器禁止写 `%TEMP%` 与桌面，属环境；3.10.2 起改为明确报错不再假成功）。
- **3.10.3 尚未开工**，规格已定稿在 `docs/decisions/ADR-001-3.10.3范围.md`（含两轮评审与修订）。

## 3.10.3 要做的（按 ADR-001 修订版）

1. `DownloadFiles` 单次调用**宿主硬上限 16 GiB**；插件只能调低；超限在联网前直接 `TooLarge`（不静默夹住）；实际接收字节持续计量。
2. `PluginDownloadRequest` 增加**有序 `AltUrls`**：顺序 `Url → AltUrls`，共享同一目标与 SHA-1；**无 SHA-1 时禁用 `AltUrls`**；每文件总尝试 **3 次**（含首次）；404/410/确定性 4xx/`HashMismatch` 只换源不重试同一源；`TooLarge`/`Denied`/`Canceled` 立即终止。
3. 全源失败仍报 `Failed`，消息拼接逐源诊断；不新增状态枚举。
4. 进度用**高水位**，仅在启用 `AltUrls` 时生效；仅在校验通过且文件提交完成后到 100%。
5. 不升 `ApiVersionUi`；单 URL 旧插件行为与 3.10.2 一致（唯一例外：超过 16 GiB 会新增 `TooLarge`）。

## 实现落点

- `Plain Craft Launcher 2\Modules\ModPlugin.vb`：`PluginDownloadOptions`（`TotalLimitBytes` 与封顶）、`PluginDownloadRequest`（`AltUrls`）、`DownloadFiles` 的换源循环与进度高水位。
- 探针：`C:\Temp\pcl-api-probe`（`plugin.json` + `Probe.vb` + `ApiProbe10.Plugin.dll`，v0.0.4 已实现三个回调接口）。
- 三个必测用例：首源 404 次源成功 → `Ok`；首源错内容次源正确 → `Ok`；全失败 → `Failed` 且带逐源诊断；给 5 个源总尝试不超过 3 次。

## 本机约定（踩过的坑）

- 编码：`ModPlugin.vb` / `PLUGIN-DEV.md` / `docs\更新记录.md` 必须 **UTF-8 无 BOM + 纯 LF**；`tools\*.ps1` 必须 **BOM + CRLF**；新建 `.vb` 模块要按邻近文件（`ModModpack.vb`）的 CRLF+BOM 规范化，并登记到 `Plain Craft Launcher 2.vbproj`。
- 写 VB 文件保 BOM/CRLF：用 `[IO.File]::WriteAllBytes` + 手动 `EF BB BF` 前缀，改完 `git diff --numstat` 应是纯新增、`bareLF=0`。
- 发版顺序：`build.ps1` → `pack-plugin-sdk.ps1 -Version X`（产物落在 `F:\` 根，需移入 `F:\PCL2-Release\`）→ `tools\preflight.ps1 -AllowDirty -ExpectVersion X` → 提交 → `git tag -a vX` → `tools\git-push.ps1 -Tag vX`（走 SSH）→ 建 Release（target 用**完整** SHA）→ 上传两资产 → `ghfast.top` 校验 206。
- 推送：默认走 SSH（`ssh://git@ssh.github.com:443/Badqf/PCL-Harness.git`），失败再走 Steamcommunity302 中继；**不要**再传 `curloptResolve`/`http.version=HTTP/1.1`。
- UI 交互测试后必须扫 `F:\PCL2-Release\PCLHarness\Log1.txt` 的 `E [Application]`（UTF-8 读取），截图不能代替。

## 模型与协作（本轮新增）

- Lead 路由已切回 **DeepSeek**：`C:\Users\XYM_3\.dsh\profiles\web\cordis.patch.yml` 的 `agent-default-model` = `deepseek-official / deepseek-flash / low`（备份 `cordis.patch.yml.bak-20261124-1932`）。
- 交叉评审用 `workflow` 显式指定另一家模型，实测可用：
  - Kimi：`provider: moonshotai-cn`，`model: kimi-k3.1-pro`
  - 智谱：`provider: codearts`，`model: glm-5.3`
- 队友花名册挂在 Lead 会话日志上（append-only），**无法删除**；新会话自然重置。本会话保留 `advisor-kimi` 与 `qa-verify` 两个真正会用到的。
- 顾问协作协议：`docs/agents/README.md`；评审问答存 `docs/agents/inbox/`。