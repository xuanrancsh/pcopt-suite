# pcopt-suite

> 面向游戏党的 Windows PC 优化套件：自动调度 + 手动工具箱 + 帧时间诊断，共享一个可审计的开源内核。

**状态：开发中（阶段 0：内核地基）。** 当前仓库包含共享内核 `PcOpt.Core` 的还原日志（RestoreJournal）
与 tweak 引擎（TweakEngine）及其动作处理器，UI 产品为可编译空壳。接口可能随开发推进而调整。

## 三产品定位

| 产品 | 工作名 | 一句话定位 | 形态 |
|---|---|---|---|
| A | GameModePilot | 检测到游戏启动，自动进入"战斗状态"；退出游戏，全部自动还原 | 常驻托盘守护程序（无感自动） |
| B | GameOptBox | 一个顶十个的游戏工具箱，每项调整说人话、带风险等级、可一键还原 | 按需打开的工具箱（手动可控） |
| C | FrameLab | 先测量、再优化、前后对比报告，用数据证明每项调整有没有用 | 测量 + 诊断工作台（数据驱动） |

三产品共享内核 `PcOpt.Core`（tweak 引擎 / 还原日志 / 硬件检测 / 游戏检测 / 配置签名），
通过本地命名管道 IPC 松耦合协作，任一缺失时其他产品优雅降级。

## 系统要求

- Windows 10 22H2 或 Windows 11 23H2 及以上
- .NET 9 SDK（开发）；运行态 .NET 9 Desktop Runtime
- 部分系统级调整需要管理员权限（应用时会明确提示）

## 构建与测试

```bash
dotnet build pcopt-suite.sln -c Release   # 编译全部项目（内核 + UI 组件库 + 3 个产品 + 测试）
dotnet test tests/PcOpt.Core.Tests -c Release   # 运行内核单元测试
```

## 仓库结构

```
pcopt-suite/
├── src/
│   ├── PcOpt.Core/            # 共享内核（零 UI 依赖）：TweakEngine / RestoreJournal / handlers
│   ├── PcOpt.UI/              # 共享 WPF-UI 组件库
│   └── Products/              # GameModePilot(A) / GameOptBox(B) / FrameLab(C)
├── tests/PcOpt.Core.Tests/    # 内核单元测试（含还原一致性、签名拒绝、幂等）
├── rules/                     # YAML 规则包源码 + Ed25519 签名公钥
├── docs/                      # PRD / 开源计划 / 架构设计
├── .github/workflows/ci.yml   # CI：restore → build → test
├── NOTICE                     # 第三方署名
└── LICENSE                    # MIT
```

## 安全与合规承诺

本项目为系统级优化工具，公开承诺以下红线（详见 `NOTICE` 与 docs/02 PRD 第 6 章）：

- **不注入游戏进程**：无 DLL 注入、无远程线程、无驱动级 hook；
- **不改游戏文件、不读写游戏内存**；
- **不做显卡型号伪装**：硬件信息只真实展示，GPU 类注册表键只读；
- **所有调整可还原**：任何应用前先写还原日志（Journal），退出或崩溃恢复后系统恢复原状；
- **无遥测、无广告、纯本地**：除更新检查（默认关闭）外无任何网络请求。

> ⚠️ 反作弊兼容性声明：本工具仅使用用户态 Win32 API，不触碰游戏进程；但游戏 ToS 由
> 各厂商制定并可能变化，请自行验证所用游戏的服务条款。检测到内核级反作弊
> （Vanguard / EAC / BattlEye / RICOCHET / ACE）时，进程调度类操作自动切换为 IFEO 注册表方案。

## 免责声明

本软件按"原样"提供，不提供任何明示或暗示的担保。系统级调整存在固有风险，
使用前请创建系统还原点，风险自担。本软件不承诺任何 FPS 提升。

## 致谢

感谢开源社区提供的参考与依赖（完整列表见 `NOTICE`）：GameShift、winutil、Win11Debloat、
Sophia Script/SophiApp、PresentMon、CapFrameX、BCU、privacy.sexy 等。

## 许可证

MIT，详见 [LICENSE](LICENSE)。每个源文件首行包含 MIT 短版权头。
