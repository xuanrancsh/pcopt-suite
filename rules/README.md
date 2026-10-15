# rules/ — 规则包（源码）

本目录存放 pcopt-suite 的 YAML 规则包**源码**与签名公钥。

## 结构

| 路径 | 说明 |
|---|---|
| `public.key` | 官方 Ed25519 签名公钥（Base64，32 字节原始密钥） |
| `tweaks/*.yaml` | 规则包源码（一个文件一条规则，Id 形如 `分类.对象.动作`） |
| `tweaks/*.sig` | 对应的 Ed25519 分离签名（由维护者线下私钥签发，随 Release 发布） |

## 签名与验签机制

1. 规则加载时（TweakEngine）必须先验签：未签名或签名与内容不符 → **拒绝执行**并给出明确错误；
2. 签名对象为 YAML 文件的原始字节；签名格式为 64 字节 Ed25519 分离签名（Base64 存放于 `.sig`）；
3. 公钥随仓库发布，任何人可验证官方规则包。

## 贡献规则

社区可对 `tweaks/*.yaml` 提 PR（完整流程与评审要点见 [CONTRIBUTING.md](../CONTRIBUTING.md) 第 7 节）。合入官方包的规则
由维护者签名后随下个 Release 发布；社区亦可自建规则包并附带自己的公钥，用户在导入时会
看到"非官方签名，风险自担"提示。

## 当前内容

- 官方规则包：`tweaks/*.yaml` **68 条** + 对应 `*.sig` 签名（首发规则包，已随仓库入库）；
- 游戏数据库：`games.yaml`（游戏 executable 特征库，供产品 A 的启动识别使用）；
- 公钥：`public.key`。

## 本地验签（无需联网）

```bash
dotnet run --project src/Tools/PcOpt.RuleSigner -c Release -- rules/tweaks --verify
```

CI（`.github/workflows/ci.yml`）会在每次提交/PR 上自动执行上述验签，任一条不过即构建失败。
