// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Tweak;

/// <summary>
/// 动作处理器（开闭原则扩展点，架构 3.1）。
/// 每种 <see cref="TweakAction"/> 对应一个成对实现：
/// <list type="bullet">
/// <item><see cref="CaptureOriginal"/>：执行前捕获原值快照（JSON，含"不存在"语义），写入 Journal；</item>
/// <item><see cref="Apply"/>：执行动作，必须幂等（重复执行结果一致）；</item>
/// <item><see cref="Revert"/>：按快照还原原值（LIFO 由引擎保证）；</item>
/// <item><see cref="Verify"/>：读回校验（可选，引擎在需要时调用）。</item>
/// </list>
/// 新增动作类型只需新增 record + 注册本接口的新实现，TweakEngine 不需修改。
/// 所有 handler 的公开 API 返回 <see cref="Result"/>，不跨模块抛异常（架构 8.2）。
/// </summary>
public interface ITweakActionHandler
{
    /// <summary>本处理器是否能处理指定动作。</summary>
    bool CanHandle(TweakAction action);

    /// <summary>捕获动作的当前原值，返回 JSON 快照（供 Journal 存储与还原）。</summary>
    Result<string> CaptureOriginal(TweakAction action);

    /// <summary>应用动作（幂等）。</summary>
    Result Apply(TweakAction action);

    /// <summary>按 Journal 快照还原。</summary>
    Result Revert(TweakAction action, string originalValueJson);

    /// <summary>读回校验动作是否达到目标状态。</summary>
    Result Verify(TweakAction action);
}
