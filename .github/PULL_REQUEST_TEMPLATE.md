<!--
  PR 模板：请如实填写，便于评审。
  提 PR 前请确认本地已通过：dotnet build（0 警告）/ dotnet test（全绿）/
  scripts/check-licenses.ps1 / scripts/check-no-secrets.ps1。
-->

## 变更类型

- [ ] 🐞 Bug 修复
- [ ] ✨ 新功能
- [ ] 📜 规则包（tweak / 游戏数据库）
- [ ] 📝 文档
- [ ] ♻️ 重构 / 工程化
- [ ] 🔒 安全相关

## 动机

<!-- 这个 PR 解决什么问题？关联的 Issue：#___ -->

## 改动说明

<!-- 简述改了什么、为什么这样改；涉及系统级改动请特别说明还原方式 -->

## 验证方式

<!-- 你如何验证改动有效？列出执行的命令与观察到的结果 -->

- [ ] `dotnet build pcopt-suite.sln -c Release`（**0 警告 0 错误**）
- [ ] `dotnet test pcopt-suite.sln -c Release`（**全绿**）
- [ ] `pwsh -File scripts/check-licenses.ps1`（通过）
- [ ] `pwsh -File scripts/check-no-secrets.ps1`（通过）
- [ ] 涉及规则包时：`dotnet run --project src/Tools/PcOpt.RuleSigner -c Release -- rules/tweaks --verify`

## 测试与回归

- [ ] 新增/更新了单元测试（行为变更需测试；Bug 修复需附可复现失败用例）
- [ ] 若改动内核公开 API，已确认三产品**零回归**
- [ ] 若涉及系统级改动，已确认"先写 Journal 后执行"且**可完整还原**

## 合规自检（红线）

- [ ] 未引入 GPL / AGPL / SSPL / PolyForm 等不利许可证代码或依赖（新依赖已登记 `NOTICE`）
- [ ] 无游戏进程注入 / 内存读写 / 游戏文件修改 / 硬件信息伪装
- [ ] 无遥测、无白名单（更新检查）之外的网络请求
- [ ] 日志中不含用户数据 / 硬件序列号 / 含用户名的路径
- [ ] 未包含私钥、证书、`.pfx`、`secrets.json`、`bin/`、`obj/`、个人路径

## 截图 / 录屏（UI 改动建议附上）

<!-- 深浅色两态更佳 -->

## 备注

<!-- 给评审者的补充说明、已知限制、后续计划等 -->
