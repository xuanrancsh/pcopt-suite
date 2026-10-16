<#
.SYNOPSIS
    下载并放置 PresentMon（帧时间捕获工具）到 ThirdParty\PresentMon。

.DESCRIPTION
    产品 C（FrameLab）以子进程方式调用 PresentMon 采集帧时间。出于"套件可离线构建、不签入第三方二进制"
    的考虑，仓库只保留说明与许可声明，可执行文件由本脚本在**联网环境**按需获取。

    脚本流程：
      1. 解析下载地址（默认取 PresentMon 官方 GitHub Releases 的压缩包）；
      2. 下载到临时目录，校验（可选）SHA256；
      3. 解压并定位 PresentMon.exe（兼容 zip 内多级目录）；
      4. 复制到目标目录（默认 ThirdParty\PresentMon）；
      5. 打印后续操作提示。

    说明：本脚本**不随构建自动执行**，需人工在联网机器上运行一次。重复运行会覆盖已存在的
    PresentMon.exe（除非指定 -SkipIfExists）。

.PARAMETER Url
    下载地址（zip 压缩包）。默认指向 PresentMon 官方 Releases 的 latest 压缩包；如官方结构调整，
    可用本参数指定具体版本地址。

.PARAMETER Destination
    目标目录（相对仓库根目录）。默认 ThirdParty\PresentMon。

.PARAMETER ExpectedSha256
    可选的 SHA256 十六进制摘要；提供时会校验下载文件，不匹配则中止并要求人工确认。

.PARAMETER SkipIfExists
    目标目录已存在 PresentMon.exe 时跳过下载。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\fetch-presentmon.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\fetch-presentmon.ps1 -SkipIfExists
#>
[CmdletBinding()]
param(
    [string]$Url = 'https://github.com/GameTechDev/PresentMon/releases/latest/download/PresentMon.zip',
    [string]$Destination = 'ThirdParty\PresentMon',
    [string]$ExpectedSha256 = '',
    [switch]$SkipIfExists
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Warn([string]$Message) { Write-Host "!!  $Message" -ForegroundColor Yellow }

# 仓库根目录 = 本脚本所在目录的上一级
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$targetDir = if ([System.IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $repoRoot $Destination }
$targetExe = Join-Path $targetDir 'PresentMon.exe'

Write-Step "目标目录：$targetDir"

if ($SkipIfExists -and (Test-Path -LiteralPath $targetExe)) {
    Write-Host "已存在 PresentMon.exe，按 -SkipIfExists 跳过下载。"
    return
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("presentmon-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$zipPath = Join-Path $tempRoot 'PresentMon.zip'

try {
    Write-Step "下载：$Url"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $zipPath -UseBasicParsing
    }
    catch {
        Write-Warn "下载失败：$($_.Exception.Message)"
        Write-Warn "请在联网环境手动从 https://github.com/GameTechDev/PresentMon/releases 下载与系统架构匹配的包，"
        Write-Warn "将其中的 PresentMon.exe 复制到：$targetExe"
        throw
    }

    if ($ExpectedSha256) {
        Write-Step "校验 SHA256…"
        $actual = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        if ($actual -ine $ExpectedSha256) {
            throw "SHA256 不匹配：期望 $ExpectedSha256，实际 $actual"
        }
        Write-Host "SHA256 校验通过。"
    }

    Write-Step "解压…"
    $extractDir = Join-Path $tempRoot 'extract'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force

    Write-Step "定位 PresentMon.exe…"
    $found = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'PresentMon.exe' -File |
        Select-Object -First 1
    if (-not $found) {
        throw "压缩包内未找到 PresentMon.exe（目录结构可能已变化，请手动放置）。"
    }

    Copy-Item -LiteralPath $found.FullName -Destination $targetExe -Force
    Write-Host "已放置：$targetExe" -ForegroundColor Green
    Write-Step "完成。重新生成 / 运行产品 C 即可开始采集。"
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
