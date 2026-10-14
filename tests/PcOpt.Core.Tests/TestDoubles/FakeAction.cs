// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>测试用动作（不在生产解析器类型表中，仅供引擎单测使用）。</summary>
internal sealed record FakeAction(string Tag) : TweakAction;
