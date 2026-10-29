# 已发布：conntek-wangchao/LanternUI

## 现在的事实（2026-10-29 实测通过）

| 项 | 地址 / 结果 |
|---|---|
| 仓库 | https://github.com/conntek-wangchao/LanternUI （public，默认分支 main） |
| 首个提交 | e3912e8 — 框架 + 示例 + 工具，106 个文件 / 1.4 MB（bin、obj、shots、release-*、sandbox、tmp 全部忽略） |
| 发布 | https://github.com/conntek-wangchao/LanternUI/releases/tag/v1.0.1 |
| 资产 | LanternUI-1.0.1-win-x64-self-contained-single.zip（63.5 MB）与 release.json |
| 更新通道 | https://raw.githubusercontent.com/conntek-wangchao/LanternUI/main/channel/release.json |
| 通道提交 | 5b899a5 — channel/release.json（package 指向 Release 资产） |

## 联网更新的实测结果

把一份 1.0.0 装在沙箱里，通道指向上面那个 raw 地址：

~~~
check exit=0                      <- 读到清单：1.0.1 is available
download exit=0                   <- 从 Release 资产下载 63.5 MB，size + sha256 校验通过，已暂存
start  exit=0                     <- 落地
installed: 1.0.0.0 -> 1.0.1.0
log: update: downloading https://github.com/.../releases/download/v1.0.1/....zip
     update: 1.0.1 is ready to install
     update: LanternUI.Demo.exe
~~~

## 这台机器的网络注意事项（重要）

| 目标 | 直连 | 走代理 127.0.0.1:7897 |
|---|---|---|
| api.github.com | 200 | 200 |
| github.com | 超时 | 200 |
| raw.githubusercontent.com | 200 | 200 |
| uploads.github.com（传资产） | 201，必须直连 | 000 |

所以：

- git push 要带 -c http.proxy=http://127.0.0.1:7897
- 上传 Release 资产要直连（tools/publish-github.ps1 的 -UploadProxy 默认留空即直连）
- 应用自己联网更新时，进程需要 HTTPS_PROXY（.NET 的 HttpClient 认这个变量）：

~~~
$env:HTTPS_PROXY = 'http://127.0.0.1:7897'
$env:HTTP_PROXY  = 'http://127.0.0.1:7897'
sandbox\install\LanternUI.Demo.exe      # 关于 -> 更新通道填 raw 地址 -> 立即检查
~~~

不加这两个变量时，检查清单（raw）仍能通过，但下载资产会失败（github.com 直连不通），
提示行会显示 下载被拒绝：...

## 怎么发下一个版本

~~~
$env:GH_TOKEN = '<classic PAT, repo scope>'
tools\publish-github.ps1 -Owner conntek-wangchao -Repo LanternUI -Version 1.0.2 -Notes "what changed"
~~~

它会：打包 -> 建 v1.0.2 Release -> 上传 zip 与 release.json -> 更新并推送 channel/release.json。
重复发布会复用已存在的资产，不会报错。

## 安全

- 这次使用的 classic PAT 出现在聊天记录里，请立刻到 GitHub -> Settings -> Developer settings ->
  Tokens (classic) 把它 Revoke 掉，需要时再生成新的（只勾 repo）。
- 令牌只存在于环境变量与本次请求头里，没有写进 .git/config，也没有提交进仓库。

## 还没做

| 项 | 说明 |
|---|---|
| 清单签名 | 现在信任链是 HTTPS + 你的账号；频道被接管仍可推恶意包 |
| CI 自动发布 | 加 .github/workflows/release.yml，打 tag 时自动打包上传（用仓库自带 GITHUB_TOKEN，不需要个人令牌） |
| 资产上传走代理 | 若以后网络必须代理才能上传，需要给 uploads.github.com 单独配隧道 |
| 下载进度 / 断点续传 | 63.5 MB 全量包，慢网只有一行"正在下载…" |
