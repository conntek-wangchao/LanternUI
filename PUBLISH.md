raw.githubusercontent.com 走 CDN，发布后**大约 5 分钟**才更新，而且**查询串不能绕开**
（实测 `?v=1.0.3` 与不带参数返回的都是旧版本）。想立刻确认，用 API：
`https://api.github.com/repos/<owner>/<repo>/contents/channel/release.json?ref=main`。
要彻底避免延迟，就把通道放到自家静态服务器并加 `Cache-Control: no-store`。

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