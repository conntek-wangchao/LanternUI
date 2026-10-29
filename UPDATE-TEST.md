# 试一次升级（已经准备好的两份发布）

| 目录 | 版本 | 说明 |
|---|---|---|
| `release-1.0.0\` | 1.0.0 | 起点。zip + `release.json`（sha256 / size / package） |
| `release-1.0.1\` | 1.0.1 | 目标。同上，`notes: "test build for the update flow"` |
| `sandbox\` | — | 测试用地：`install\` 装 1.0.0，`settings.json` 是它的配置，`updates\` / `logs\` 由它自己生成 |

两份都是 **self-contained single-file**（约 63.5 MB，不要求目标机器装 .NET）。

## 先在窗口里试（About 页驱动）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\try-update.ps1 -Fresh -UiTest
```

它只做两件事：把 1.0.0 解到 `sandbox\install`，并把 `sandbox\settings.json` 的
`ui_update_source` 指向 `release-1.0.1`。然后你自己开：

```
sandbox\install\LanternUI.Demo.exe
```

关于页 → **更新通道**（现在是一个可编辑的输入框，回车或失焦即保存）→ **立即检查**
→ 显示 “Update 1.0.1 is available.” → **安装 1.0.1** → 确认重启 → 版本行变成 1.0.1。

## 一条命令（命令行把整条链路跑完）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\try-update.ps1 -Fresh -Tamper
```

它会：解压 1.0.0 到 `sandbox\install` → 检查更新 → **证明篡改过的包被拒** → 下载并暂存 →
正常启动一次（更新在窗口出现前落地，`--shot` 截一张图后退出）→ 打印版本变化、残留、日志。

上一次运行的结果：

```
installed version: 1.0.0.0
    staged after the corrupted try: False
    pending marker: True
version: 1.0.0.0 -> 1.0.1.0
pending marker now: False
staged copies left: 0
window after the update: True
log tail:
    11:03:09.681  info   update: checking ...\release-1.0.1
    11:03:09.745  good   update: 1.0.1 is available
    11:03:09.747  info   update: downloading LanternUI-1.0.1-win-x64-self-contained-single.zip
    11:03:10.014  good   update: 1.0.1 is ready to install
    11:03:10.265  good   update: LanternUI.Demo.exe
    11:03:10.277  good   Update installed 1.0.1
the whole path works: checked, verified, installed, and the new version is in place
```

## 手动一步步来（想自己看每步退出码时）

```powershell
$sandbox = "$PWD\sandbox"
$exe = "$sandbox\install\LanternUI.Demo.exe"
$cfg = "$sandbox\settings.json"

# 0) 装一个 1.0.0
Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$sandbox\install" -Force | Out-Null
Expand-Archive release-1.0.0\LanternUI-1.0.0-win-x64-self-contained-single.zip "$sandbox\install" -Force
'{}' | Set-Content $cfg -Encoding UTF8

# 1) 有新版本吗？       0 = 有，2 = 没有，1 = 没配频道
& $exe --config $cfg --update-source "$PWD\release-1.0.1" --check-updates; $LASTEXITCODE

# 2) 下载 + 校验 + 暂存   0 = 已暂存，1 = 拒绝
& $exe --config $cfg --update-source "$PWD\release-1.0.1" --download-update; $LASTEXITCODE
Get-Content "$sandbox\updates\pending.json"

# 3) 正常启动一次，更新在窗口出现前落地（--shot 截完图就退出）
& $exe --config $cfg --lang zh-CN --shot "$sandbox\after-update.png"

# 4) 看结果
(Get-Item $exe).VersionInfo.FileVersion          # 1.0.1.0
Test-Path "$sandbox\updates\pending.json"        # False
Get-ChildItem "$sandbox\updates" -Force           # 空：暂存副本已清掉
Get-Content "$sandbox\logs\lantern-*.log" -Encoding UTF8 | Select-String update | Select-Object -Last 6
```

## 反向用例（故意弄坏）

| 做法 | 期望 |
|---|---|
| 往 zip 末尾追加一个字节 | `--download-update` 退出码 **1**，日志 `size mismatch`（先查大小再查哈希），`pending.json` 不生成 |
| 改 zip 但把 `size` 一起改对 | 退出码 **1**，日志 `sha256 mismatch` |
| 删掉 `release.json` 的 `sha256` | 拒绝安装（清单缺哈希） |
| 把 zip 改名 | 检查/下载失败并说明缺包 |
| 版本与当前相同 | `--check-updates` 退出码 **2**，什么都不下载 |
| 程序正在运行时再起一个实例去装 | 落地被拒（exe 被占用），`pending.json` 保留，关掉实例后下次启动自动装好 |

`tools\try-update.ps1 -Tamper` 会自动跑第一条。

## 相关文件

| 位置 | 作用 |
|---|---|
| `sandbox\settings.json` | 测试实例的配置（`updates\`、`logs\` 都在它旁边） |
| `sandbox\updates\pending.json` | "下载好了，下次启动装"的标记 |
| `sandbox\logs\lantern-*.log` | 完整日志（UTF-8，读的时候加 `-Encoding UTF8`） |
| `release-1.0.x\release.json` | 频道清单：version / beta / notes / package / sha256 / size / published |

## 准备这两份发布时发现并修掉的 3 个真问题

1. **打包出来的单文件根本起不来**：WPF 会按名字去框架目录找 `UIAutomationTypes`，单文件捆绑满足不了它，
   一建窗口就 `FileNotFoundException`。`pack.ps1` 现在带 `-p:IncludeAllContentForSelfExtract=true`。
2. **更新装到了临时解压目录**：单文件运行时 `AppContext.BaseDirectory` 指向 `%TEMP%\.net\...`，
   于是"更新成功"了但安装目录还是旧版本。新增 `UpdateApplier.InstallRoot`（取可执行文件所在目录），
   并把"收尾"逻辑收进 `UpdateApplier.ApplyPending`。
3. **每次升级后留下 63 MB 的暂存副本**：`ClearPending` 只删标记文件。现在连 `pending.Staged`
   和 `download`/`staging` 临时目录一起删。

这三条都有测试或脚本证据：`Finishing_a_pending_update_replaces_files_and_leaves_nothing_behind`、
`UpdateTests` 里原有的哈希/大小/端到端用例，以及上面这条 `try-update.ps1` 的输出。

## 仍然没做的

发布清单**没有签名**：`sha256` 只能证明包没被损坏，不能证明是谁发的。要防"频道被换掉"，
需要客户端内置公钥 + 清单签名（我列在待办里，你说先不做）。用 GitHub Releases 这类 HTTPS 渠道
时风险可接受，自建 HTTP 源建议补上。