# ThirdParty\PresentMon

本目录用于放置 **PresentMon**（帧时间捕获工具）二进制，供产品 C（FrameLab）在采集帧率时以子进程方式调用。

## 为什么是"用户自备"

PresentMon 由 Intel 开源维护，采用 **MIT 许可**。为了让套件本身可离线构建、且不把第三方二进制
直接签入版本库，仓库**只保留本说明与许可声明**，实际可执行文件由使用者按需放入（详见下方步骤）。

> 未放置 PresentMon 时，FrameLab **不会崩溃**：测试台会给出明确的中文提示与获取指引，
> 然后优雅降级（无法开始测试），其余功能（报告、诊断框架、设置）均可正常浏览。

## 如何获取并放置

1. 在**可联网**的机器上运行仓库脚本：

   ```powershell
   # 在仓库根目录执行
   powershell -ExecutionPolicy Bypass -File .\scripts\fetch-presentmon.ps1
   ```

   脚本会把 `PresentMon.exe` 下载/解压到 `ThirdParty\PresentMon\`。

2. 也可以手动获取：从 PresentMon 官方发布页下载与系统架构匹配的压缩包
   （`PresentMon-*-x64.exe` 或 zip），将其中的 `PresentMon.exe`
   复制到本目录，最终路径形如：

   ```
   ThirdParty\PresentMon\PresentMon.exe
   ```

3. 放置后重新**生成 / 运行**产品 C，程序会把本目录内容复制到输出目录的 `PresentMon\`
   子目录（见 `src/Products/FrameLab/FrameLab.csproj`）。

## 运行时如何被找到

`FrameLab.Services.ResourcePaths.FindPresentMonExecutable()` 会依次探测：

1. 程序目录下 `ThirdParty\PresentMon\PresentMon.exe`（部署后 = 输出目录 `PresentMon\PresentMon.exe`）；
2. 从程序目录逐级向上最多 6 层，查找任一层的 `ThirdParty\PresentMon\PresentMon.exe`
   （覆盖"源码树根目录放一份即可"的开发场景）；
3. 同层目录下直接名为 `PresentMon.exe` 的文件。

## 许可

PresentMon 采用 MIT 许可，版权归 Intel Corporation 所有。详见同目录 `NOTICE`。
