# pcopt-suite · 游戏党 PC 优化套件

> **先测量、再优化、可还原**：一个开源、可审计的 Windows 游戏优化套件，包含"自动调度 + 手动工具箱 + 帧时间诊断"三款互补产品，共享同一套内核与还原机制。

[![CI](https://github.com/xuanrancsh/pcopt-suite/actions/workflows/ci.yml/badge.svg)](https://github.com/xuanrancsh/pcopt-suite/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)

> ⚠️ 仓库地址、组织名与徽章链接为**占位**，正式发布前需替换为真实地址（见 [docs/03-开源计划.md](docs/03-开源计划.md) 第 8 章 Checklist）。

---

## English Summary

`pcopt-suite` is an open-source, auditable Windows PC optimization suite for gamers, made of three complementary
WPF applications sharing one kernel (`PcOpt.Core`) with a mandatory restore-journal: **GameModePilot** (automatic
game-mode scheduling), **GameOptBox** (a human-readable, risk-labeled manual tweak toolbox) and **FrameLab**
(frame-time measurement + diagnosis + before/after comparison). Every tweak is journaled before it is applied and
fully reversible (including crash/power-loss recovery). No game injection, no game-file modification, no GPU
spoofing, no telemetry. Licensed under MIT.

---

## 它解决什么问题

网上"优化教程"满天飞，但玩家普遍面临三个痛点：

1. **不知道有没有用** —— 改了一堆设置，帧率到底涨没涨、卡顿到底少没少，全凭感觉；
2. **不知道改了啥、怎么撤销** —— 手动改注册表/服务/计划任务，改坏了自己都找不回来；
3. **不敢用** —— 怕被反作弊误判，怕夹带私货、偷偷联网、装全家桶。

本套件用三款产品分别回答：**测（C）→ 改（A/B）→ 还原（内核保证）**，并且**全部开源可审计**。

---

## 三款产品

| 产品 | 代号 | 一句话定位 | 形态 | 适合谁 |
|---|---|---|---|---|
| **A** | `GameModePilot` | 检测到游戏启动就**自动**进入"战斗状态"；退出游戏**自动**还原 | 常驻托盘守护程序（无感自动） | 不想手动折腾、要"开机即用"的玩家 |
| **B** | `GameOptBox` | 一个顶十个的游戏工具箱，每项调整**说人话、带风险等级、可一键还原** | 按需打开的工具箱（手动可控） | 喜欢自己掌控、逐项勾选的玩家 |
| **C** | `FrameLab` | 先测量、再优化、**前后对比报告**，用数据证明每项调整到底有没有用 | 测量 + 诊断工作台（数据驱动） | 想看数据、验证效果的玩家 |

三者共享内核 `PcOpt.Core`（tweak 引擎 / 还原日志 / 硬件检测 / 游戏检测 / 配置签名），
通过本地命名管道 IPC 松耦合协作：**任一产品缺失，其余产品优雅降级**，不会互相拖累。

### 产品 A · GameModePilot（自动调度）

- 订阅 ETW 内核进程事件识别游戏启动（≤3 秒触发），按预设"游戏档案"自动应用一组调整；
- 检测到内核级反作弊（Vanguard / EAC / BattlEye / RICOCHET / ACE）时，进程调度类操作**自动降级**为 IFEO 注册表方案，避免与反作弊冲突；
- 游戏退出即 LIFO 还原；异常崩溃/断电后下次启动自动恢复原状。

### 产品 B · GameOptBox（优化全家桶）

- 规则浏览器：搜索 ≤200ms、卡片三态（未应用/已应用/不支持）、风险标签（安全/注意/危险）、危险项二次确认；
- 功能页：服务与启动项管理、延迟优化（计时器/USB 轮询/音频独占）、电源计划、GPU 合规调优（HAGS/MPO）、内存管理、卸载清理；
- **还原中心**：单条/全部还原 + 系统还原点 + 日志导出。首发内置 **68 条签名规则**，全部"说人话 + 可还原"。

### 产品 C · FrameLab（帧时间诊断）

- 一键 60 秒测试（时长可配）采集帧时间，输出**平均帧率 / 1% Low（双算法）/ 卡顿事件 / 帧时间分布 / DPC 近似**；
- 报告可导出 **PNG / CSV**，并给出**优化前后对比**（有效 / 无效 / 退化 / 不稳定）；
- **诊断**：把"帧时间尖峰 + 高 DPC""待机内存膨胀""CPU 降频""GPU 瓶颈"等症状匹配到具体建议，每条都给出**证据 + 建议 + 预期收益（人话）**；
- **优化-复测闭环**：一键应用建议（优先调用产品 A，缺失时退化为内置最小引擎：电源 + 计时器 + 内存清理），引导复测后自动出结论，全程目标 **≤5 分钟**。

---

## 截图

> 📷 **占位说明**：当前开发环境无 GUI，无法生成真实截图。以下为**建议截图清单**，发布前请按此补齐，
> 并替换本节为真实图片（建议放在 `docs/images/` 下，宽 1280、PNG、单张 < 500KB）。

| # | 文件名（建议） | 拍摄界面 | 应呈现的关键内容 |
|---|---|---|---|
| 1 | `game-modepilot-main.png` | 产品 A 主界面 | 大状态卡（待机/游戏中）、当前会话的调整项网格 |
| 2 | `game-modepilot-tray.png` | 产品 A 托盘菜单 | 托盘图标、开机自启开关、"当前会话"入口 |
| 3 | `gameoptbox-health.png` | 产品 B 首页 | 电脑健康分 + 分项进度条 + 一键优化入口 |
| 4 | `gameoptbox-rules.png` | 产品 B 规则浏览器 | 搜索框、规则卡片三态、风险标签、二次确认弹窗 |
| 5 | `gameoptbox-restore.png` | 产品 B 还原中心 | 已应用列表、单条/全部还原、日志导出 |
| 6 | `framelab-testbench.png` | 产品 C 测试台 | 大启动按钮 + 实时帧时间迷你图 |
| 7 | `framelab-report.png` | 产品 C 报告页 | 帧时间曲线、1% Low 双口径标注、导出按钮 |
| 8 | `framelab-compare.png` | 产品 C 前后对比 | 对比表（绿/红）、结论文案、口径说明 |
| 9 | `framelab-diagnose.png` | 产品 C 诊断页 | 症状 + 证据 + 建议 + 预期收益卡片 |
| 10 | `theme-light-dark.png` | 任一界面 | 深浅色两态对比（Mica 背景） |

---

## 系统要求

- **操作系统**：Windows 10 22H2 或 Windows 11 23H2 及以上（x64）
- **运行时**：.NET 9 **Desktop** Runtime（框架依赖包）；开发需 .NET 9 SDK
- **权限**：多数调整为当前用户级；服务/计划任务/电源/待机内存等**需要管理员权限**，应用时会明确提示
- **产品 C 可选依赖**：帧时间采集需要 [PresentMon](https://github.com/GameTechDev/PresentMon)（不随包分发，见下文）

---

## 快速开始

### 构建

```bash
# Windows 10/11 + .NET 9 SDK
git clone <本仓库地址>
cd pcopt-suite

dotnet build pcopt-suite.sln -c Release
```

### 运行

```bash
# 产品 A：游戏模式自动调度器（常驻托盘）
dotnet run --project src/Products/GameModePilot -c Release

# 产品 B：游戏党优化全家桶
dotnet run --project src/Products/GameOptBox -c Release

# 产品 C：帧时间诊断工作台
dotnet run --project src/Products/FrameLab -c Release
```

> 建议以**管理员身份**运行才能使用全部系统级调整；非管理员启动时，需要提权的项目会被明确标注并跳过。

### 测试

```bash
dotnet test pcopt-suite.sln -c Release          # 全量：内核 + 三产品
```

### 产品 C：获取 PresentMon（可选，联网）

FrameLab 通过子进程调用 PresentMon 采集帧时间。出于"不签入第三方二进制"的原则，仓库只保留说明与许可：

```powershell
# 在仓库根目录执行（需联网，仅获取一次）
powershell -ExecutionPolicy Bypass -File .\scripts\fetch-presentmon.ps1
```

也可手动把 `PresentMon.exe` 放入程序目录下的 `PresentMon\` 文件夹。**未放置时 FrameLab 会给出明确中文提示并优雅降级（可浏览报告/诊断/设置），不会崩溃。** 详见 [ThirdParty/PresentMon/README.md](ThirdParty/PresentMon/README.md)。

---

## 仓库结构

```
pcopt-suite/
├── src/
│   ├── PcOpt.Core/                 # 共享内核（零 UI 依赖）：TweakEngine / 还原日志 / 硬件检测 / 游戏检测 / 签名校验
│   ├── PcOpt.UI/                   # 共享 WPF-UI 组件库与设计 Token（PRD 第 5 章）
│   ├── Products/
│   │   ├── GameModePilot/          # 产品 A
│   │   ├── GameOptBox/             # 产品 B
│   │   └── FrameLab/               # 产品 C
│   └── Tools/
│       ├── PcOpt.RuleSigner/       # 规则包 Ed25519 签名/验签工具（维护者离线使用，不随产品分发）
│       └── PcOpt.UI.Demo/          # 组件库演示
├── tests/                          # 单元 / 集成 / 还原一致性 / 合规测试（内核 + 三产品）
├── rules/                          # YAML 规则包源码 + Ed25519 签名 + 公钥
│   ├── public.key
│   ├── games.yaml                  # 游戏 executable 特征库
│   └── tweaks/*.yaml, *.sig        # 68 条首发规则（源码 + 分离签名）
├── ThirdParty/PresentMon/          # 产品 C 采集依赖的获取说明与许可（不含二进制）
├── scripts/                        # fetch-presentmon.ps1 / 许可证扫描 / 源码静态合规扫描
├── docs/                           # 01 调研 / 02 PRD / 03 开源计划 / 04 架构设计
├── .github/                        # CI、Release 流水线、Issue / PR 模板
├── LICENSE                         # MIT
├── NOTICE                          # 第三方署名与致谢
├── SECURITY.md                     # 安全策略与漏洞上报
└── CONTRIBUTING.md                 # 贡献指南（含规则包贡献流程）
```

---

## 规则包与签名

- 规则为 YAML 源码（`rules/tweaks/*.yaml`），Id 形如 `分类.对象.动作`（如 `svc.sysmain.disable`）；
- 每条规则配一个 Ed25519 **分离签名**（`*.sig`）；加载时**先验签**，未签名或与内容不符 → **拒绝执行**；
- 公钥随仓库发布（`rules/public.key`），任何人可验证官方规则包；私钥由维护者离线保管，**绝不入库、绝不进 CI 日志**；
- 社区可提交规则 PR，合入后由维护者签名并随下个 Release 发布；用户也可导入自签名/未签名规则，导入时会提示"非官方签名，风险自担"。

本地验签（无需联网）：

```bash
dotnet run --project src/Tools/PcOpt.RuleSigner -c Release -- rules/tweaks --verify
```

---

## 数据与隐私

- **无遥测、无广告、纯本地**：除产品 A/B 的"更新检查"外，套件**没有任何网络请求**；更新检查**默认关闭**，可一键关闭且不影响功能；
- 日志仅写入本机 `%LocalAppData%\pcopt\...\logs`，**绝不包含**用户数据、硬件序列号或含用户名的路径；
- 还原日志写入本机 `%ProgramData%\pcopt\journal`，仅用于崩溃恢复；
- CI 含**静态扫描**，一旦出现白名单外的网络调用、硬编码密钥或私钥文件即**构建失败**（见 `scripts/check-no-secrets.ps1`）。

---

## 安全边界与反作弊

> ⚠️ **反作弊兼容性声明**：本工具**仅使用用户态 Win32 API**，**不注入**游戏进程、**不读写**游戏内存、**不修改**游戏文件、**不 hook**、**无驱动**。检测到内核级反作弊时会自动切换到更保守的 IFEO 方案。但**游戏 ToS 由各厂商制定且可能变化，请自行验证**所用游戏的服务条款。

**所有调整均可还原**：任何 Apply 之前，还原日志（Journal）必须已原子落盘；退出、崩溃、断电后下次启动自动 LIFO 恢复原状。

---

## 合规红线（源码可验证）

1. 不做显卡型号伪装：GPU 类注册表键**只读**；2. 不注入游戏进程；3. 不改游戏文件；4. 不 hook 内存；
5. 内核级反作弊自动降级；6. Defender / 自动更新类调整**默认折叠且默认关闭**；
7. 所有调整先写 Journal 后执行、LIFO 还原；8. **无遥测、无广告、纯本地**。

---

## 免责声明

本软件按"原样"提供，不提供任何明示或暗示的担保。系统级调整存在固有风险，**使用前请创建系统还原点**，风险自担。本软件**不承诺任何 FPS 提升**。请遵守你所使用游戏与软件的服务条款。

---

## 贡献

欢迎提交 Issue 与 PR：Bug 报告 / 功能建议 / **规则包提案**（见 [CONTRIBUTING.md](CONTRIBUTING.md) 与 `.github/ISSUE_TEMPLATE/`）。
安全漏洞请按 [SECURITY.md](SECURITY.md) **私下**上报，勿公开 Issue。

---

## 许可证与致谢

本项目采用 **MIT** 许可，详见 [LICENSE](LICENSE)；全部第三方依赖与参考项目的署名见 [NOTICE](NOTICE)。

特别感谢以下开源项目（完整列表见 NOTICE）：GameShift、winutil、Win11Debloat、Sophia Script / SophiApp、
privacy.sexy、PresentMon、CapFrameX、BCU，以及 WPF-UI、CommunityToolkit.Mvvm、YamlDotNet、NSec.Cryptography、
LibreHardwareMonitor 等依赖的作者与社区。
