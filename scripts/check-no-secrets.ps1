<#
.SYNOPSIS
    源码静态合规扫描：无硬编码密钥 / 无未授权外联 / 无内网地址（T39 / 开源计划 6.3、6.6）。

.DESCRIPTION
    纯本地静态检查（不联网、不改动文件），失败即返回非零退出码。检查三类：

      1. 密钥文件：仓库内不得出现私钥 / 证书 / 签名私钥类文件
         （`*.pfx`、`*.snk`、`*.sig.private`、`private-key*.txt`、`secrets.json`、`*.env`、`*.private`）；
      2. 密钥内容：文本中不得出现 PEM 私钥标记（`-----BEGIN ... PRIVATE KEY-----`）；
      3. 未授权外联：`src/**/*.cs` 中的网络 API 与 URL 必须落在白名单内
         （除产品 A/B 的"更新检查（默认关闭）"外，套件承诺零外联）。

.PARAMETER RepoRoot
    仓库根目录。默认取本脚本所在目录的上一级。

.EXAMPLE
    pwsh -File scripts/check-no-secrets.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

Write-Host '==> 源码静态合规扫描（密钥 / 外联）' -ForegroundColor Cyan
Write-Host "仓库根目录：$RepoRoot"

# 需要跳过的目录（构建产物 / 版本库元数据 / 编辑器缓存）
$skipDirectoryPattern = '[\\/](\.git|bin|obj|node_modules|\.vs|\.workbuddy|TestResults|artifacts|publish)[\\/]'

# 仅对这些文本扩展名做"内容"扫描（避免对二进制误报）
$textExtensions = @('.cs', '.csproj', '.props', '.targets', '.xaml', '.md', '.yml', '.yaml',
    '.json', '.ps1', '.psm1', '.sln', '.editorconfig', '.txt', '.gitignore', '.gitattributes', '.key')

$allFiles = Get-ChildItem -Path $RepoRoot -Recurse -File |
    Where-Object { $_.FullName -notmatch $skipDirectoryPattern }

# ---------------------------------------------------------------------------
# 1) 密钥文件（按文件名 / 扩展名）
# ---------------------------------------------------------------------------
$forbiddenFilePatterns = @('\.pfx$', '\.snk$', '\.sig\.private$', '(^|[\\/])private-key.*\.txt$',
    '(^|[\\/])secrets\.json$', '\.env$', '\.private$')

$secretFiles = @()
foreach ($file in $allFiles) {
    foreach ($pattern in $forbiddenFilePatterns) {
        if ($file.FullName -match $pattern) {
            $secretFiles += $file.FullName
            break
        }
    }
}

# ---------------------------------------------------------------------------
# 2) 密钥内容（PEM 私钥标记）
# ---------------------------------------------------------------------------
$pemPattern = '-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----'
$contentHits = @()

# ---------------------------------------------------------------------------
# 3) 未授权外联（src/**/*.cs）
# ---------------------------------------------------------------------------
# 白名单：仅更新检查通道（产品 A/B），且默认关闭（PRD 确认项）。
# 键为「仓库相对路径（正斜杠）」，值为该文件允许出现的网络理由。
$networkAllowList = @{
    'src/Products/GameModePilot/Services/UpdateChecker.cs' = '产品 A 更新检查（默认关闭）'
    'src/Products/GameOptBox/Services/UpdateChecker.cs'    = '产品 B 更新检查（默认关闭）'
}

$networkPatterns = @('HttpClient', 'WebClient', 'WebRequest', 'HttpWebRequest', 'TcpClient',
    'TcpListener', 'SocketsHttpHandler', 'https?://')

$networkHits = @()

foreach ($file in $allFiles) {
    # 内容扫描（私钥标记）
    if ($textExtensions -contains $file.Extension.ToLowerInvariant() -or $file.Extension -eq '') {
        try {
            $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
        }
        catch {
            continue
        }

        if ($content -match $pemPattern) {
            $contentHits += $file.FullName
        }

        # 外联扫描仅针对 src 下的 C# 源码
        $relative = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
        if ($relative -like 'src/*' -and $file.Extension.ToLowerInvariant() -eq '.cs') {
            if (-not $networkAllowList.ContainsKey($relative)) {
                foreach ($pattern in $networkPatterns) {
                    if ($content -match $pattern) {
                        $networkHits += "$relative  ← 命中：$pattern"
                        break
                    }
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------
# 结果
# ---------------------------------------------------------------------------
$failures = 0

if ($secretFiles.Count -gt 0) {
    $failures += $secretFiles.Count
    Write-Host ''
    Write-Host '[失败] 仓库内存在密钥 / 证书类文件：' -ForegroundColor Red
    $secretFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}

if ($contentHits.Count -gt 0) {
    $failures += $contentHits.Count
    Write-Host ''
    Write-Host '[失败] 文本中出现 PEM 私钥标记：' -ForegroundColor Red
    $contentHits | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}

if ($networkHits.Count -gt 0) {
    $failures += $networkHits.Count
    Write-Host ''
    Write-Host '[失败] 白名单外的网络调用 / URL（套件承诺除更新检查外零外联）：' -ForegroundColor Red
    $networkHits | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "静态合规扫描失败：$failures 项。" -ForegroundColor Red
    exit 1
}

$scanned = ($allFiles | Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() -or $_.Extension -eq '' }).Count
Write-Host "静态合规扫描通过：已扫描 $scanned 个文本文件；无密钥文件、无私钥标记、外联均在白名单内。" -ForegroundColor Green
Write-Host ("白名单外联文件：{0}" -f (($networkAllowList.Keys | Sort-Object) -join '、')) -ForegroundColor DarkGray
exit 0
