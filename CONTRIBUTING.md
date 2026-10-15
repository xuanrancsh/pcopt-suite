# 贡献指南（Contributing）

感谢你愿意为 **pcopt-suite** 出一份力！本项目面向"游戏党 PC 优化"，最看重三件事：
**说人话、可还原、可审计**。请确保你的贡献也符合这三点。

提交贡献即表示你同意以本项目的 **MIT** 许可证发布你的贡献（见 [LICENSE](LICENSE)）。

---

## 1. 可以提供哪些贡献

| 类型 | 说明 | 入口 |
|---|---|---|
| 🐞 Bug 报告 | 崩溃、还原不彻底、UI 异常、文档错误 | [Bug 模板](.github/ISSUE_TEMPLATE/bug_report.yml) |
| 💡 功能建议 | 新功能、体验改进 | [功能建议模板](.github/ISSUE_TEMPLATE/feature_request.yml) |
| 📜 规则包提案 | 新增/修改一条 tweak（YAML） | [规则提案模板](.github/ISSUE_TEMPLATE/rule_proposal.yml) |
| 🧩 代码 / 文档 PR | 修 bug、加功能、补文档 | [PR 模板](.github/PULL_REQUEST_TEMPLATE.md) |
| 🎮 游戏数据库 | 增补 `rules/games.yaml` 的 executable 特征 | PR |
| 💬 讨论 | 使用问题、优化心得、硬件兼容报告 | GitHub Discussions |

> 🔒 **安全漏洞请勿开公开 Issue**，请按 [SECURITY.md](SECURITY.md) 私下上报。

---

## 2. 不被接受的贡献（合规红线）

以下内容**一律拒绝**（与套件承诺冲突）：

- ❌ 引入 **GPL / AGPL / SSPL / PolyForm** 等不利于本项目的许可证代码或依赖（见 `NOTICE` 借鉴边界铁律）；
- ❌ 任何形式的**游戏进程注入**、DLL 注入、远程线程、驱动级 hook、游戏内存读写、游戏文件修改；
- ❌ **显卡型号伪装**或伪造硬件信息；
- ❌ 任何**遥测 / 数据回传 / 广告**，或白名单（更新检查）之外的网络请求；
- ❌ 把**用户数据、硬件序列号、含用户名的路径**写入日志；
- ❌ 提交**密钥、证书、私钥、个人信息**或编译产物（CI 会拦截）。

---

## 3. 开发环境

- **操作系统**：Windows 10 22H2 / Windows 11 23H2 及以上（x64）
- **SDK**：.NET **9.0 SDK**（`dotnet --version` 应为 `9.0.x`）
- **IDE**：Visual Studio 2022 / Rider / VS Code + C# Dev Kit 均可
- 建议以**管理员**身份调试涉及系统改动的功能（否则相关项会被跳过并提示）

```bash
git clone <本仓库地址>
cd pcopt-suite
dotnet build pcopt-suite.sln -c Release
```

---

## 4. 构建、测试与本地校验

```bash
# 构建（要求 0 警告 0 错误；Directory.Build.props 已开启 TreatWarningsAsErrors）
dotnet build pcopt-suite.sln -c Release

# 全量测试（内核 + 三产品）
dotnet test pcopt-suite.sln -c Release

# 运行某个产品
dotnet run --project src/Products/GameOptBox -c Release

# 官方规则包验签（无需联网）
dotnet run --project src/Tools/PcOpt.RuleSigner -c Release -- rules/tweaks --verify

# 本地合规自检（与 CI 一致）
pwsh -File scripts/check-licenses.ps1
pwsh -File scripts/check-no-secrets.ps1
```

**提 PR 前请确保**：`dotnet build` 0 警告、`dotnet test` 全绿、上述两条合规自检通过。
CI（`.github/workflows/ci.yml`）会在 `windows-latest` 上重跑同样的门禁。

---

## 5. 代码规范（遵循 [docs/04-架构设计.md](docs/04-架构设计.md) §8 跨文件约定）

1. **命名**：规则 Id 为 `分类.对象.动作`（如 `svc.sysmain.disable`）；配置键 camelCase；私有字段 `_camelCase`；异步方法以 `Async` 结尾。
2. **错误处理**：内核（`PcOpt.Core`）公开 API 返回 `Result` / `Result<T>`，**不跨模块抛异常**，失败时 `Error` 必须是**面向用户可读的中文**；UI 层用 Toast 展示。
3. **还原优先**：任何 Apply 之前 Journal 必须已落盘（引擎已保证，勿绕过）。
4. **日志隐私**：用户数据 / 硬件序列号 / 含用户名的路径**绝不进日志**。
5. **UI 线程**：ViewModel 命令全部 async；超过 2 秒的操作必须报 `IProgress<T>`。
6. **文案**：面向用户的字符串遵守 PRD §5.3"说人话"，避免术语堆砌与夸大承诺。
7. **许可证头**：每个 `.cs` 文件首行必须有 MIT 短版权头（IDE0073 会强制）。
8. **只读硬件**：GPU 类注册表键**只读**，不得新增写操作（有断言测试守护）。
9. **目录归属**：内核能力放 `src/PcOpt.Core`；通用 UI 组件放 `src/PcOpt.UI`；产品专有逻辑放 `src/Products/<产品>`。

> 代码风格由 `.editorconfig` + `Directory.Build.props`（`Nullable=enable`、`ImplicitUsings=enable`、
> `TreatWarningsAsErrors=true`、`AnalysisLevel=latest`）统一约束，请勿在 PR 中降低这些标准。

---

## 6. 提交与 PR 规范

- **提交信息**：建议 `类型: 简述`（如 `feat: 新增 USB 轮询率调整`、`fix: 还原中心偶发漏还原`）；中文或英文均可，**一次提交只做一件事**。
- **PR**：请填写 [PR 模板](.github/PULL_REQUEST_TEMPLATE.md)，说明**动机 / 改动 / 验证方式**；关联相关 Issue。
- **测试**：行为变更需附单元测试；修复 Bug 需附**可复现的失败用例**（修复后转绿）。
- **不要**在 PR 中包含：私钥/证书/`.pfx`/`secrets.json`、`bin/`、`obj/`、个人路径、与本次改动无关的格式化噪音。
- **签名**：官方规则包由维护者离线签名（见下节），PR 中**不要**提交 `.sig`（除非你作为维护者按流程签署）。

---

## 7. 规则包（tweak）贡献流程 ⭐

这是最适合社区参与的部分。规则为 YAML，一文件一条。

### 7.1 规则文件格式

放在 `rules/tweaks/` 下，文件名建议 `<分类>.<对象>.<动作>.yaml`（与 `id` 一致）：

```yaml
id: game.power.plan.high.perf          # 必填，分类.对象.动作
category: 游戏                          # 必填，中文分类
risk: Notice                            # 必填，Safe / Notice / Danger
displayName: 切换到高性能电源计划          # 必填，一句话人话标题
description: 让 CPU 在游戏时更少降频。笔记本电量消耗会快一些。  # 必填，效果 + 代价
consequenceHints:                       # 建议填写：白话后果提示
  - 笔记本续航会短一些
  - 退出后会自动切回原来的电源计划
conditions:                             # 可选：适用条件
  - minBuild: 19041
actions:                                # 必填：一或多个动作
  - type: PowerCfg
    kind: SetActive
    argument: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
```

**可用的动作类型**（`actions[].type`）：`RegistrySet`、`ServiceStartType`、`ScheduledTask`、`PowerCfg`、
`ProcessPriority`、`TimerResolution`、`Shell`。每种动作都必须由**可还原的 handler** 支持（"有 Apply 必有 Revert"）。

### 7.2 规则评审要点

- ✅ **说人话**：`displayName` / `description` / `consequenceHints` 让非专业玩家也能看懂，**不夸大收益**；
- ✅ **风险分级准确**：会影响安全、隐私或稳定性的必须标 `Notice` / `Danger`；
- ✅ **可还原**：动作必须能按快照还原（社区只需正确声明动作，还原由内核保证）；
- ✅ **合规**：不得触碰游戏文件/进程、不得伪造硬件信息、不得引入遥测；
- ✅ **有据可依**：在 PR 中说明该调整的作用原理与参考来源（可引用官方文档或上游项目思路）。

### 7.3 流程

```
提出规则提案（Issue 模板）
   → 编写 rules/tweaks/<id>.yaml（+ 必要时补充测试/游戏数据库）
   → 本地通过：dotnet test、check-licenses、check-no-secrets
   → 提 PR（附"预期解决什么问题"与证据）
   → 维护者评审（正确性 / 风险 / 文案 / 合规）
   → 合入主干
   → 维护者离线用私钥签名 → 生成 rules/tweaks/<id>.sig → 随下个 Release 发布
```

> **私钥安全**：签名私钥由维护者**离线保管**，绝不入库、绝不进 CI 日志（详见 [SECURITY.md](SECURITY.md)
> 与 [docs/03-开源计划.md](docs/03-开源计划.md) 第 5 章）。贡献者**无需**自备签名即可提规则 PR。

### 7.4 自建规则包

你也可自建规则包并附带自己的公钥；用户导入非官方签名包时会看到"非官方签名，风险自担"提示。
导入流程与签名工具用法见 [rules/README.md](rules/README.md)。

---

## 8. 游戏数据库贡献

`rules/games.yaml` 收录游戏的可执行文件特征（用于产品 A 的启动识别）。新增条目请提供：
游戏名、平台（Steam / Epic / Battle.net / WeGame 等）、主可执行文件名、以及（可选）反作弊类型。

---

## 9. 交流

- **Issue**：Bug / 功能建议 / 规则提案（请选用对应模板）
- **Discussions**：使用问题、优化心得、硬件兼容报告
- **安全**：私下上报（[SECURITY.md](SECURITY.md)）

再次感谢你的贡献 ❤️
