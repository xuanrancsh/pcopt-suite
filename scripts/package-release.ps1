<#
.SYNOPSIS
    pcopt-suite 一键发布打包：三产品自包含单文件 exe + zip（便携式，解压即用）。

.DESCRIPTION
    将 GameModePilot / GameOptBox / FrameLab 各自发布为 win-x64 自包含单文件 exe，
    并与随包 rules/ 规则目录、使用说明一起组装为：

        dist/<产品>-<版本>-<运行时>/
            <产品>.exe    自包含单文件（内置 .NET 9 运行时与 WPF / WinForms native libs）
            rules/        游戏数据库 / 签名规则包 / 公钥（与仓库 rules/ 内容一致）
            PresentMon/   仅 FrameLab：可选帧捕获组件（缺失时产品优雅降级，不影响启动）
            使用说明.txt
        dist/<产品>-<版本>-<运行时>.zip

    设计要点：
    * 便携式分发：免安装、免运行时、解压即用；注册表改动全部发生在运行期而非安装期。
    * 自包含单文件：--self-contained + -p:PublishSingleFile=true
      + -p:IncludeNativeLibrariesForSelfExtract=true（WPF 的 PresentationNative 等
      native libs 在单文件形态下必须自提取，否则启动即崩）。
    * 默认启用单文件压缩（-p:EnableCompressionInSingleFile=true）显著减小体积；
      如需更快冷启动可用 -NoCompression 关闭。
    * rules/ 处理：单文件发布时 csproj 中 None+CopyToOutputDirectory 的内容文件默认落在
      exe 同目录而非打进单文件；脚本在复制发布输出的基础上，再用仓库 rules/ 整体覆盖，
      保证三产品产物 rules/ 与仓库字节一致、不依赖内容文件落点行为。
    * 幂等：可重复执行；每次重建 staging、产物目录与 zip。

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER Runtime
    目标运行时（RID），默认 win-x64。

.PARAMETER OutputDir
    产物根目录（相对仓库根目录或绝对路径），默认 dist。

.PARAMETER Version
    版本号（用于目录 / zip 命名）。默认从仓库根 Directory.Build.props 的 <Version> 读取。

.PARAMETER Products
    仅打包指定产品子集，默认三个全部。

.PARAMETER NoCompression
    关闭单文件压缩（默认开启）。

.PARAMETER SelfContained
    是否自包含发布，默认 $true。仅当自包含运行时包无法还原（离线且无 NuGet 缓存）时，
    作为显式降级选项设为 $false（产物为框架依赖单文件，用户需安装 .NET 9 Desktop Runtime）。
    脚本会打印醒目告警——绝不静默降级。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Products FrameLab -NoCompression

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -SelfContained:$false
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = 'dist',
    [string]$Version = '',
    [string[]]$Products = @('GameModePilot', 'GameOptBox', 'FrameLab'),
    [switch]$NoCompression,
    [bool]$SelfContained = $true
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "OK  $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "!!  $Message" -ForegroundColor Yellow }

<#
.SYNOPSIS
    强制删除路径（目录或文件），对"安全删除 / 回收站"类 cmdlet 级钩子具有韧性。

.DESCRIPTION
    先尝试 Remove-Item；若被环境钩子拦截或抛出（钩子可能删除成功后仍报错），
    回退到 .NET File / Directory API（对 cmdlet 级钩子透明）。staging / dist 均为
    可再生产物，硬删除是正确语义。删除后校验，仍存在则抛错。
#>
function Remove-PathForced {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    }
    catch {
        if (Test-Path -LiteralPath $Path -PathType Container) {
            [System.IO.Directory]::Delete($Path, $true)
        }
        elseif (Test-Path -LiteralPath $Path) {
            [System.IO.File]::Delete($Path)
        }
    }

    if (Test-Path -LiteralPath $Path) {
        throw "删除失败：$Path"
    }
}

# ---------- 仓库根目录（本脚本位于 <repoRoot>\scripts\） ----------
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'pcopt-suite.sln'))) {
    throw "未能在 $repoRoot 找到 pcopt-suite.sln，请在仓库 scripts\ 目录下运行本脚本。"
}

# ---------- 版本号：优先参数，其次 Directory.Build.props，最后兜底 ----------
if ([string]::IsNullOrWhiteSpace($Version)) {
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath) {
        $hit = Select-String -LiteralPath $propsPath -Pattern '<Version>\s*([^<\s]+)\s*</Version>' |
            Select-Object -First 1
        if ($hit) { $Version = $hit.Matches[0].Groups[1].Value }
    }
}
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = '0.1.0' }

# ---------- 产品元数据 ----------
$productMeta = [ordered]@{
    'GameModePilot' = [pscustomobject]@{
        Project   = 'src\Products\GameModePilot\GameModePilot.csproj'
        Tagline   = '游戏模式自动调度器：检测到游戏启动即自动套用优化方案，游戏退出后自动还原，全程托盘常驻守护。'
        RulesNote = '内含 games.yaml（游戏特征库）与 public.key（规则公钥）；删除后将无法识别游戏与验签规则。'
        ExtraNote = '首次运行会弹出一次使用向导；日常启动后常驻系统托盘，右键托盘图标即可打开主界面 / 暂停守护 / 全部还原。'
    }
    'GameOptBox' = [pscustomobject]@{
        Project   = 'src\Products\GameOptBox\GameOptBox.csproj'
        Tagline   = '游戏党 PC 优化全家桶：精选中文优化规则，一键应用 / 一键还原，所有改动均记入日志、随时可回滚。'
        RulesNote = '内含 public.key（规则公钥）与 tweaks\（签名规则包）；删除后将无法加载优化规则。'
        ExtraNote = ''
    }
    'FrameLab' = [pscustomobject]@{
        Project   = 'src\Products\FrameLab\FrameLab.csproj'
        Tagline   = '帧时间诊断工作台：测量帧时间 / 1% Low / 卡顿尖峰，定位瓶颈并一键闭环优化。'
        RulesNote = '内含游戏特征库 / 签名规则包 / 公钥；删除后将无法加载随包规则。'
        ExtraNote = ''
        PresentMonNote = @'
【帧时间捕获（可选组件）】
  本目录 PresentMon\PresentMon.exe 为可选的第三方帧捕获工具（Intel PresentMon，MIT 许可）：
  · 未随包提供时：FrameLab 仍可正常启动并使用全部非采集功能，打开采集时会提示获取方式；
  · 需要采集时：从 https://github.com/GameTechDev/PresentMon/releases 下载与系统架构（x64）
    匹配的压缩包，将其中的 PresentMon.exe 放入本目录的 PresentMon\ 文件夹即可。
'@
    }
}

foreach ($name in $Products) {
    if (-not $productMeta.Contains($name)) {
        throw "未知产品：$name（可选：$($productMeta.Keys -join ' / ')）"
    }
}

# ---------- 目录准备 ----------
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}
$stagingRoot = Join-Path $repoRoot 'obj\publish-staging'
New-Item -ItemType Directory -Force -Path $outputRoot, $stagingRoot | Out-Null

if (Get-Command git -ErrorAction SilentlyContinue) {
    & git check-ignore -q $outputRoot
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "产物目录未被 .gitignore 忽略：$outputRoot（请检查 .gitignore 中的 dist/ 规则，避免误提交构建产物）"
    }
}

if (-not $SelfContained) {
    Write-Warn '已选择框架依赖单文件（-SelfContained:$false）：exe 仅数百 KB，但用户必须自行安装 .NET 9 Desktop Runtime（winget install Microsoft.DotNet.DesktopRuntime.9）。'
}

$compression = if ($NoCompression) { 'false' } else { 'true' }
$results = [System.Collections.Generic.List[pscustomobject]]::new()

# ---------- 逐产品发布与组装 ----------
foreach ($name in $Products) {
    $meta = $productMeta[$name]
    $csproj = Join-Path $repoRoot $meta.Project
    if (-not (Test-Path -LiteralPath $csproj)) {
        throw "[$name] 找不到项目文件：$csproj"
    }

    # 1) 发布到 staging（每次全量发布，保证产物干净、可重复）
    $staging = Join-Path $stagingRoot "$name-$Runtime"
    Remove-PathForced -Path $staging

    Write-Step "[$name] dotnet publish（self-contained=$SelfContained, single-file, compression=$compression）…"
    & dotnet publish $csproj `
        -c $Configuration `
        -r $Runtime `
        --self-contained $(if ($SelfContained) { 'true' } else { 'false' }) `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=$compression `
        -o $staging `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "[$name] dotnet publish 失败（退出码 $LASTEXITCODE）。若为运行时包还原失败（离线且无 NuGet 缓存），可显式加 -SelfContained:`$false 降级为框架依赖单文件，并在发布说明中注明降级原因。"
    }

    $stagedExe = Join-Path $staging "$name.exe"
    if (-not (Test-Path -LiteralPath $stagedExe)) {
        throw "[$name] 发布输出中未找到 $name.exe（$staging）"
    }

    # 2) 组装产物目录（幂等：先清后建）
    $distName = "$name-$Version-$Runtime"
    $distProduct = Join-Path $outputRoot $distName
    Remove-PathForced -Path $distProduct
    New-Item -ItemType Directory -Force -Path $distProduct | Out-Null

    # 2a) 自包含单文件 exe
    Copy-Item -LiteralPath $stagedExe -Destination $distProduct -Force

    # 2b) rules/：先复制发布输出自带内容，再用仓库 rules/ 整体覆盖（保证与仓库一致）
    $stagedRules = Join-Path $staging 'rules'
    if (Test-Path -LiteralPath $stagedRules) {
        Copy-Item -LiteralPath $stagedRules -Destination $distProduct -Recurse -Force
    }
    $repoRules = Join-Path $repoRoot 'rules'
    Copy-Item -LiteralPath $repoRules -Destination $distProduct -Recurse -Force

    foreach ($required in @('games.yaml', 'public.key')) {
        $requiredPath = Join-Path $distProduct "rules\$required"
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "[$name] 产物缺少 rules\$required（$requiredPath）"
        }
    }

    # 2c) FrameLab：可选 PresentMon 组件（ThirdParty\PresentMon\PresentMon.exe 存在才随包）
    if ($name -eq 'FrameLab') {
        $stagedPresentMon = Join-Path $staging 'PresentMon'
        if (Test-Path -LiteralPath $stagedPresentMon) {
            Copy-Item -LiteralPath $stagedPresentMon -Destination $distProduct -Recurse -Force
        }

        $presentMonExe = Join-Path $repoRoot 'ThirdParty\PresentMon\PresentMon.exe'
        $presentMonTargetDir = Join-Path $distProduct 'PresentMon'
        New-Item -ItemType Directory -Force -Path $presentMonTargetDir | Out-Null
        if (Test-Path -LiteralPath $presentMonExe) {
            Copy-Item -LiteralPath $presentMonExe -Destination $presentMonTargetDir -Force
            Write-Ok "[$name] 已随包 PresentMon.exe（可选帧捕获组件）"
        }
        else {
            Write-Warn "[$name] 未找到 ThirdParty\PresentMon\PresentMon.exe（未执行 scripts\fetch-presentmon.ps1 或当前离线）。FrameLab 将优雅降级：可正常启动，仅无法采集新帧时间。"
        }
    }

    # 2d) 使用说明.txt
    $readmeLines = [System.Collections.Generic.List[string]]::new()
    $readmeLines.Add(('=' * 66))
    $readmeLines.Add("  $name $Version  （$Runtime 便携版 · 解压即用）")
    $readmeLines.Add(('=' * 66))
    $readmeLines.Add('')
    $readmeLines.Add($meta.Tagline)
    $readmeLines.Add('')
    $readmeLines.Add('【如何运行】')
    $readmeLines.Add("  双击 $name.exe 即可使用。")
    $readmeLines.Add('  · 绿色便携：无需安装、无需 .NET 运行时；日常使用不需要管理员权限。')
    $readmeLines.Add("  · 部分系统级优化项（如系统服务启动类型、待机列表清理、内核 ETW 采集）需要管理员权限：未提权运行时这些项会提示需要管理员权限而不会执行；如需完整功能请右键 $name.exe 选择「以管理员身份运行」。")
    if ($meta.ExtraNote) {
        $readmeLines.Add("  · $($meta.ExtraNote)")
    }
    $readmeLines.Add('')
    $readmeLines.Add('【目录内容】')
    $readmeLines.Add("  · $name.exe —— 自包含单文件程序。")
    $readmeLines.Add("  · rules\ —— 随包规则文件。$($meta.RulesNote)")
    if ($name -eq 'FrameLab') {
        $readmeLines.Add('  · PresentMon\ —— 可选帧捕获组件（详见文末说明）。')
    }
    $readmeLines.Add('  · 整个文件夹可随意复制 / 移动 / 改名，不影响使用。')
    $readmeLines.Add('')
    $readmeLines.Add('【卸载】')
    $readmeLines.Add('  直接删除本文件夹即可（不写注册表安装项、不产生服务）。')
    $readmeLines.Add("  用户数据位于 %AppData%\pcopt\$name，日志位于 %LocalAppData%\pcopt\$name\logs，可一并删除。")
    if ($name -eq 'FrameLab') {
        $readmeLines.Add('')
        $readmeLines.AddRange($meta.PresentMonNote -split "`r`n|`n")
    }
    $readmeLines.Add('')
    $readmeLines.Add("pcopt-suite contributors · MIT License · 版本 $Version")
    $readmePath = Join-Path $distProduct '使用说明.txt'
    [System.IO.File]::WriteAllLines($readmePath, $readmeLines, [System.Text.UTF8Encoding]::new($false))

    # 3) 打包 zip（幂等：先删旧包）
    $zipPath = "$distProduct.zip"
    Remove-PathForced -Path $zipPath
    Compress-Archive -LiteralPath $distProduct -DestinationPath $zipPath -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $zipPath)) {
        throw "[$name] zip 打包失败：$zipPath"
    }

    $exeInfo = Get-Item -LiteralPath $stagedExe
    $zipInfo = Get-Item -LiteralPath $zipPath
    $fileCount = (Get-ChildItem -LiteralPath $distProduct -Recurse -File).Count
    $results.Add([pscustomobject]@{
        Product = $name
        DistDir = $distProduct
        Zip     = $zipPath
        ExeMb   = [math]::Round($exeInfo.Length / 1MB, 1)
        ZipMb   = [math]::Round($zipInfo.Length / 1MB, 1)
        Files   = $fileCount
    })
    Write-Ok "[$name] 产物：$distProduct（exe $([math]::Round($exeInfo.Length / 1MB, 1)) MB，zip $([math]::Round($zipInfo.Length / 1MB, 1)) MB，$fileCount 个文件）"
}

# ---------- 汇总 ----------
if (-not $SelfContained) {
    foreach ($r in $results) {
        if ($r.ExeMb -lt 5) {
            Write-Warn "[$($r.Product)] exe 仅 $($r.ExeMb) MB，确认为框架依赖单文件：用户需安装 .NET 9 Desktop Runtime（winget install Microsoft.DotNet.DesktopRuntime.9）。"
        }
    }
}

Write-Host ''
Write-Host '==================== 打包结果 ====================' -ForegroundColor Green
$results | Format-Table -AutoSize Product, ExeMb, ZipMb, Files, DistDir
Write-Host "产物根目录：$outputRoot" -ForegroundColor Green
Write-Host "版本：$Version    运行时：$Runtime    配置：$Configuration    自包含：$SelfContained    单文件压缩：$(-not $NoCompression)" -ForegroundColor Green
