// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Tweak;

/// <summary>
/// 风险三级（PRD 5.3 / 架构 3.1）：安全 / 注意 / 危险。
/// UI 配色：安全=绿 #22C55E、注意=琥珀 #F59E0B、危险=红 #EF4444（PRD 5.2）。
/// </summary>
public enum RiskLevel
{
    /// <summary>🟢 安全：影响小、随时可还原。</summary>
    Safe,

    /// <summary>🟠 注意：可能影响打印机 / 蓝牙等周边，需阅读后果提示。</summary>
    Notice,

    /// <summary>🔴 危险：改动较大，建议先创建还原点，需二次确认。</summary>
    Danger,
}
