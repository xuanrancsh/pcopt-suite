<#
.SYNOPSIS
    依赖许可证合规扫描（T39 / 开源计划 2.3 铁律 #3）。

.DESCRIPTION
    枚举仓库内全部 `*.csproj` 的 `PackageReference`，对照本脚本内置的
    「允许清单」与「拒绝清单」判定合规性：

      * 允许清单内的包 → 通过（含许可证与义务说明）；
      * 拒绝清单（GPL / AGPL / SSPL / 未知许可证）→ 失败；
      * 未登记的包 → 失败（集成任何第三方库前必须先登记，见 NOTICE / docs/03-开源计划.md）。

    纯本地能力实现，**不联网**、不调用任何外部 Action，可在 GitHub Actions 与本机离线执行。

.PARAMETER RepoRoot
    仓库根目录。默认取本脚本所在目录的上一级。

.EXAMPLE
    pwsh -File scripts/check-licenses.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

# ---------------------------------------------------------------------------
# 允许清单：包 Id（小写）→ 许可证 + 分类 + 义务说明
#   分类：runtime（编译进产品）/ dev（仅开发与测试，不随产品分发）
# ---------------------------------------------------------------------------
$allowed = @{
    'wpf-ui'                                      = @{ License = 'MIT';          Category = 'runtime' }
    'communitytoolkit.mvvm'                       = @{ License = 'MIT';          Category = 'runtime' }
    'microsoft.extensions.dependencyinjection'    = @{ License = 'MIT';          Category = 'runtime' }
    'microsoft.extensions.logging'                = @{ License = 'MIT';          Category = 'runtime' }
    'microsoft.extensions.logging.abstractions'   = @{ License = 'MIT';          Category = 'runtime' }
    'microsoft.win32.registry'                    = @{ License = 'MIT';          Category = 'runtime' }
    'system.management'                           = @{ License = 'MIT';          Category = 'runtime' }
    'yamldotnet'                                  = @{ License = 'MIT';          Category = 'runtime' }
    'microsoft.diagnostics.tracing.traceevent'    = @{ License = 'MIT';          Category = 'runtime' }
    'nsec.cryptography'                           = @{ License = 'BSD-3-Clause'; Category = 'runtime'; Note = '仅作 NuGet 依赖调用，未改源码；随附其版权声明' }
    'librehardwaremonitorlib'                     = @{ License = 'MPL-2.0';      Category = 'runtime'; Note = '仅作 NuGet 依赖调用，未改源码；分发须随附其 LICENSE' }
    'microsoft.net.test.sdk'                      = @{ License = 'MIT';          Category = 'dev' }
    'xunit'                                       = @{ License = 'Apache-2.0';   Category = 'dev' }
    'xunit.runner.visualstudio'                   = @{ License = 'Apache-2.0';   Category = 'dev' }
    'fluentassertions'                            = @{ License = 'Apache-2.0';   Category = 'dev' }
    'moq'                                         = @{ License = 'BSD-3-Clause'; Category = 'dev' }
}

# ---------------------------------------------------------------------------
# 拒绝清单：命中即失败（Copyleft / 非 OSI / 待核验）
# ---------------------------------------------------------------------------
$denied = @(
    @{ Pattern = '^gpl';   Reason = 'GPL 系 Copyleft，禁止引入（开源计划 2.3 铁律 #2）' }
    @{ Pattern = 'agpl';   Reason = 'AGPL 系 Copyleft，禁止引入' }
    @{ Pattern = 'sspl';   Reason = 'SSPL 非 OSI 许可，禁止引入' }
    @{ Pattern = 'polyform'; Reason = 'PolyForm 非 OSI 许可，禁止引入' }
)

Write-Host '==> 依赖许可证扫描' -ForegroundColor Cyan
Write-Host "仓库根目录：$RepoRoot"

$projectFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object FullName

if ($projectFiles.Count -eq 0) {
    Write-Host '!! 未找到任何 *.csproj，扫描失败。' -ForegroundColor Red
    exit 1
}

# 收集 PackageReference（Id -> @{ Version; Projects })
$references = [ordered]@{}
$packagePattern = [regex]'<PackageReference\s+Include="([^"]+)"(?:\s+Version="([^"]+)")?\s*/?>'

foreach ($project in $projectFiles) {
    $content = Get-Content -LiteralPath $project.FullName -Raw
    foreach ($match in $packagePattern.Matches($content)) {
        $id = $match.Groups[1].Value.Trim()
        $version = $match.Groups[2].Value.Trim()
        $key = $id.ToLowerInvariant()
        if (-not $references.Contains($key)) {
            $references[$key] = @{ Id = $id; Version = $version; Projects = @() }
        }
        if ($version -and -not $references[$key].Version) {
            $references[$key].Version = $version
        }
        $references[$key].Projects += $project.Name
    }
}

if ($references.Count -eq 0) {
    Write-Host '!! 未从任何项目中发现 PackageReference。' -ForegroundColor Red
    exit 1
}

$failures = @()

Write-Host ''
Write-Host ('{0,-46} {1,-15} {2,-9} {3}' -f '包', '版本', '许可证', '分类') -ForegroundColor DarkGray
Write-Host ('-' * 96) -ForegroundColor DarkGray

foreach ($entry in $references.Values) {
    $key = $entry.Id.ToLowerInvariant()

    # 1) 拒绝清单
    $deniedHit = $null
    foreach ($rule in $denied) {
        if ($key -match $rule.Pattern) {
            $deniedHit = $rule
            break
        }
    }

    if ($null -ne $deniedHit) {
        Write-Host ('{0,-46} {1,-15} {2,-9} {3}' -f $entry.Id, $entry.Version, 'DENIED', '拒绝') -ForegroundColor Red
        $failures += "$($entry.Id) $($entry.Version)：$($deniedHit.Reason)"
        continue
    }

    # 2) 允许清单
    if (-not $allowed.ContainsKey($key)) {
        Write-Host ('{0,-46} {1,-15} {2,-9} {3}' -f $entry.Id, $entry.Version, 'UNKNOWN', '未登记') -ForegroundColor Red
        $failures += "$($entry.Id) $($entry.Version)：未在允许清单中登记；引入前必须核验许可证并在 NOTICE 登记（开源计划 2.3 铁律 #3）"
        continue
    }

    $info = $allowed[$key]
    Write-Host ('{0,-46} {1,-15} {2,-9} {3}' -f $entry.Id, $entry.Version, $info.License, $info.Category) -ForegroundColor Green
    if ($info.ContainsKey('Note')) {
        Write-Host ("    └─ {0}" -f $info.Note) -ForegroundColor DarkGray
    }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "许可证合规扫描失败：$($failures.Count) 项" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "许可证合规扫描通过：共 $($references.Count) 个直接依赖均已登记且许可证允许。" -ForegroundColor Green
exit 0
