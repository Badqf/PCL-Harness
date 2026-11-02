# 插件市场所需 API 合同（v6.4 / 本体 3.7.3）

> 这份文档是**给「插件市场插件」那个独立对话的交接材料**。
> 市场不内置进本体，而是一个普通插件，放在另一个仓库；本体只负责提供下面这些能力，不再为市场改代码。

- 目标本体版本：**3.7.3**（`VersionCode 452`）
- 市场插件要求：`"minApiVersion": 6`，权限至少 `["ui", "events", "files", "net.dynamic", "plugins"]`
- 相关源码：`Plain Craft Launcher 2\Modules\ModPlugin.vb`（`Host.Plugins` 在模块内 `PluginHostPlugins` 类，`net.dynamic` 在 `EnsureHostAllowed`）

---

## 1. 新增权限

| 权限名 | 宿主给用户的说明 | 触发点 |
| --- | --- | --- |
| `plugins` | 查看、安装、启用、禁用或卸载其他插件（插件市场类插件需要） | 调用 `Host.Plugins` 下任何成员时校验一次 |
| `net.dynamic` | 在运行时申请访问新的主机（每次申请都会弹窗询问，可随时在「插件设置」里撤销） | 请求的主机不在清单 `net.hosts` 里时 |

两条权限都参与启用前的权限确认弹窗与 `trust.json` 的 `permissionsHash`；权限文本变化会让插件在下次启动时重新确认。

`plugins` 的校验规则与 `HasPermission` 一致：

- `permissions` 完全没写（旧插件）：视为不受限，直接放行；
- 写了但没包含 `plugins`：抛 `UnauthorizedAccessException`，并往该插件的「事件记录」里写一条「调用了未授权的插件管理接口」。

`net.dynamic` 的校验只在 `net` 权限的固定主机全部不匹配之后才发生，见 §3。

---

## 2. `Host.Plugins`（管理其他插件）

`Host.Plugins` 是一个 `PluginHostPlugins` 对象，只读，随 `Host` 一起创建。所有成员都要求 `plugins` 权限。

### 2.1 返回类型

```vb
Public Class PluginSummary
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Version As String = ""
    Public Property Author As String = ""
    Public Property Description As String = ""
    Public Property Enabled As Boolean      ' 是否在 plugins.json 的启用列表里
    Public Property Loaded As Boolean       ' 本次会话是否已加载 DLL
    Public Property MinApiVersion As Integer
    Public ReadOnly Property Permissions As New List(Of String)
    Public ReadOnly Property DisplayName As String   ' Name 为空时回落 Id
End Class
```

这是**快照**，不是活对象：每次 `List()` / `Find()` 都会新建，改了也不会影响宿主。

### 2.2 成员

```vb
' 列出所有已发现的插件（含未启用、仅清单、加载失败的）
Public Function List() As List(Of PluginSummary)

' 按 id 查找，忽略大小写；找不到返回 Nothing
Public Function Find(Id As String) As PluginSummary

' 导入插件包（.pclplugin / .zip / .dll），走与「导入插件…」按钮完全一致的管线
' 返回 PluginImportResult（Success / Code / Message / PluginId / PluginName / Version / Upgraded / Display）
Public Function ImportPackage(Path As String, Optional Overwrite As Boolean = True) As PluginImportResult

' 启用 / 停用，插件不存在返回 False
Public Function SetEnabled(Id As String, Value As Boolean) As Boolean

' 卸载：先停用并 OnUnload，再把整个目录移入 Plugins\.trash\<id>-<yyyyMMddHHmmss>
' 插件不存在返回 False；试图卸载自己抛 InvalidOperationException
Public Function Uninstall(Id As String) As Boolean
```

### 2.3 语义细节

- **`ImportPackage` 的安全检查由宿主完成**：唯一 `plugin.json`、id 合法性 `^[a-z0-9][a-z0-9_\-\.]{0,63}$`、`minApiVersion`、DLL `sha256`、依赖版本、路径逃逸、体积；失败返回 `PluginImportResult.Success = False` 且 `Code` 为 `ERR_*`（`ERR_NOT_ARCHIVE` / `ERR_NO_MANIFEST` / `ERR_AMBIGUOUS_MANIFEST` / `ERR_BAD_JSON` / `ERR_BAD_ID` / `ERR_MIN_API` / `ERR_SHA256` / `ERR_MISSING_DEP` / `ERR_EXISTS` / `ERR_UNSAFE_PATH` / `ERR_WRITE_FAILED`）。
- **覆盖安装**：旧目录整体移到 `Plugins\.backup\`，`data\` 目录、设置项、启用状态保留；权限指纹变了不会自动重新启用，而是等用户下次手动启用时重新确认。
- **导入成功后触发 `插件安装` 事件**；`Uninstall` 成功后触发 `插件卸载` 事件。
- `SetEnabled`/`Uninstall` 内部会 `Refresh(True)` 重建插件列表，**市场的 UI 不能缓存 `PluginSummary` 或依赖旧对象**，每次重新 `List()`。
- 市场插件**不要**给自己 `SetEnabled(False)`：停用会在 DLL 仍加载时卸载界面，后续调用行为未定义。

---

## 3. `net.dynamic`（运行时申请主机）

`Host.HttpGetString` / `Host.HttpDownload` 内部会调用宿主的 `EnsureHostAllowed`，流程：

1. 清单里没有 `permissions.net`（或没有 `hosts` 明细）→ 不限制，直接放行；
2. 主机命中 `net.hosts` 里的任一模式（支持 `*`、`*.example.com`）→ 放行；
3. 清单里**没有** `net.dynamic` → 拦截并抛 `UnauthorizedAccessException`（老行为）；
4. 有 `net.dynamic`：
   - 该主机已在 `trust.json` 的 `hosts` 列表里 → 放行；
   - 否则弹一次模态框：「插件「X」想访问网络主机：`api.example.com`……允许 / 拒绝」；
   - 允许 → 写入 `trust.json`，本次及以后放行；拒绝 → 抛 `UnauthorizedAccessException("插件 X 申请访问 api.example.com 被拒绝")`，并在事件记录里留一条。

`trust.json` 结构（`schema` 仍为 2，`hosts` 是可选字段，旧文件缺省即无授权）：

```json
{
  "schema": 2,
  "plugins": {
    "my-market": {
      "permissionsHash": "files,net.dynamic,plugins,ui",
      "hosts": ["api.github.com", "raw.githubusercontent.com"]
    }
  }
}
```

**撤销**：宿主提供 `ModPlugin.RevokeHosts(id)` 与 `ModPlugin.ListApprovedHosts(id)`（模块内公开函数），插件页「插件设置」后续会接一个「已授权主机」列表 + 撤销按钮；市场插件自己也可以在自己的设置页里调这两个函数。

---

## 4. 新增事件

| 事件名 | 参数签名 | 触发时机 |
| --- | --- | --- |
| `插件安装` | `(pluginId As String, version As String)` | 导入/升级写盘、刷新完成之后 |
| `插件卸载` | `(pluginId As String, version As String)` | `Host.Plugins.Uninstall` 移入 `.trash` 之后 |

```vb
Host.Events.On(ModPlugin.PluginEvents.PluginInstalled,
    Sub(pluginId As String, version As String)
        Host.Log($"检测到插件变动：{pluginId} v{version}")
    End Sub)
```

事件参数用字符串而不是 `PluginInfo`，避免把内部类型暴露给插件；`All()` 现在返回 15 个事件名，开发模式控制台的 `events` 命令会自动带上签名。

---

## 5. 线程与调用约束

- `List` / `Find` 内部有 `SyncLock`，任意线程安全。
- `ImportPackage` / `SetEnabled` / `Uninstall` 会做文件 IO 与程序集加载，**必须在后台线程调用**；`Host.Tasks.Run` 是最省事的做法：

  ```vb
  Host.Tasks.Run("安装插件", Sub(task)
      Dim result = Host.Plugins.ImportPackage(pkgPath)
      task.Report(1, 1)   ' 或者用循环汇报进度
      Host.Hint(result.Display)
  End Sub)
  ```

- `net.dynamic` 的弹窗走 `ModBase.RunInUiWait`：**不要在 UI 线程做网络请求**（否则模态框会把 UI 卡住直到用户点按钮）。后台线程不受影响。
- `MyMsgBox` 的返回值：第一个按钮为 `1`。

---

## 6. 市场插件清单示例

```json
{
  "id": "pcl-harness-market",
  "name": "插件市场",
  "version": "0.1.0",
  "author": "PCL Harness",
  "description": "在启动器里浏览、安装、更新 PCL Harness 插件。",
  "entry": "PCLHarness.Market.MarketPlugin",
  "dll": "PCLHarness.Market.dll",
  "minApiVersion": 6,
  "homepage": "https://github.com/Badqf/PCL-Harness-Market",
  "license": "PCL 分发有限许可",
  "permissions": ["ui", "events", "files", "net.dynamic", "plugins"],
  "net": { "hosts": ["raw.githubusercontent.com", "github.com", "objects.githubusercontent.com", "ghfast.top", "gh-proxy.com"] }
}
```

索引里的下载地址建议直接用 GitHub Release 的原始地址，市场插件自己按 `ghfast.top` → `gh-proxy.com` → 原址 回退。

---

## 7. 明确**没有**提供的能力（市场别指望）

- 没有「插件商店页」这种一等公民 UI，市场只能用 `IPclPluginUi` 注册自己的页面 / 顶栏入口。
- 没有宿主侧的回滚：安装失败只保证不留半成品目录，不回滚到旧版本（旧版本在 `Plugins\.backup\`，可手工恢复）。
- 没有远程索引的抓取/校验助手：分片、`sha256`、commit 锚定、缓存都由市场插件自己做（可直接参考 `docs\插件导入与分发方案.md`）。
- 没有签名验证：`plugin.json` 的 `signature` 仍只是「发布者声明（未验证）」。
- 没有跨插件的服务发现之外的通信（`Host.Services` 只能按接口类型找，不能点名找插件）。

---

## 8. 验收清单（本体侧）

- [x] `Host.Plugins` 四个成员在样例插件里可用（`samples\V6Demo` 增加一条自检调用）
- [x] `net.dynamic` 首次访问弹窗、允许后写入 `trust.json`、重启后不再弹
- [x] 拒绝后抛 `UnauthorizedAccessException`，事件记录里有「用户拒绝访问主机」
- [x] `插件安装` / `插件卸载` 事件在控制台可见
- [x] 权限确认弹窗里能看到 `plugins`、`net.dynamic` 的中文说明
- [ ] 插件设置页的「已授权主机」列表 + 撤销按钮（后续小版本）
