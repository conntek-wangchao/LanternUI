# 发布到 GitHub（含联网更新通道）

## 这台机器的现实情况（2026-10-29 实测）

| 项 | 结果 |
|---|---|
| `https://api.github.com` | 直连 **200** 可用 |
| `https://github.com` | 直连超时；**必须走代理** `http://127.0.0.1:7897`（经代理 200） |
| 账号 `conntek-wangchao` | 存在，1 个公开仓库 `deepseek-harness` |
| 已保存的 GitHub 凭据 | 只有 `pureskywalk` 的 OAuth 令牌（Git Credential Manager），**不能**在 `conntek-wangchao` 下建仓库 |
| `gh` CLI | 未安装 |

所以要发布到 `conntek-wangchao`，需要该账号的一个 **classic PAT（scope: `repo`）**。
令牌放在环境变量里，不要写进文件或命令行参数：

```powershell
# 你自己的终端里执行一次（不影响聊天记录）
setx GH_TOKEN "<粘贴 PAT>"
```

新开的进程会继承它；之后我或你运行下面这条即可。

## 一条命令发布

```powershell
tools\publish-github.ps1 -Owner conntek-wangchao -Repo LanternUI -Version 1.0.1 -CreateRepo -PushSource
```

它按顺序做：

1. 校验令牌并打印它属于谁（防止用错账号）
2. `-CreateRepo`：`POST /user/repos` 建仓库（已存在会告警后继续）
3. `-PushSource`：`git init` / commit / push（经代理，令牌只放在本次请求头里，不写进 `.git/config`）
4. `tools\pack.ps1` 打出 self-contained 单文件包
5. 建 Release，上传 zip 与 `release.json` 两个资产
6. 生成 `channel\release.json`（`package` 用**资产的绝对 URL**），提交并推送
7. 打印更新通道地址

## 更新通道就是那个文件

发布完成后通道是：

```
https://raw.githubusercontent.com/conntek-wangchao/LanternUI/main/channel/release.json
```

应用里「关于 → 更新通道」填这个地址，或在命令行：

```powershell
LanternUI.Demo.exe --update-source https://raw.githubusercontent.com/conntek-wangchao/LanternUI/main/channel/release.json --check-updates
```

注意：`raw.githubusercontent.com` 有缓存，发新版后如果客户端还读到旧的清单，把通道改成
`.../channel/release.json?v=1.0.1`（查询串一变就绕过缓存）。

## 仓库里有什么

| 路径 | 作用 |
|---|---|
| `src/\`、`demos/\`、`tests/\` | 框架、示例、测试 |
| `tools/pack.ps1` | 打包（三种模式） |
| `tools/publish-github.ps1` | 本文件描述的一键发布 |
| `tools/try-update.ps1` | 本机验证更新链路 |
| `channel/release.json` | 更新通道（发布脚本自动维护） |
| `check.ps1` / `check-i18n.ps1` | 质量门禁（构建 + 测试 + 本地化） |

`release-*/\` 与 `sandbox/\` 已在 `.gitignore` 里，不会被推上去。

## 之后可以让 CI 接管

`.github/workflows/release.yml`（还没写）可以在打 tag `v*` 时自动：
构建 → 测试 → `pack.ps1` → 建 Release 上传资产 → 更新 `channel/release.json`。
用仓库自带的 `GITHUB_TOKEN` 即可，不需要个人令牌。要我加就说一声。
