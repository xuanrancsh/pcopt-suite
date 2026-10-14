// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Runtime.InteropServices;
using PcOpt.Core.Native;

namespace PcOpt.Core.Hardware;

/// <summary>CPU 大小核拓扑读取结果（架构 3.3）。</summary>
/// <param name="PhysicalCores">物理核心总数。</param>
/// <param name="LogicalProcessors">逻辑处理器总数。</param>
/// <param name="PerformanceCores">性能核数；大小核拓扑不可用时为 null。</param>
/// <param name="EfficiencyCores">能效核数；大小核拓扑不可用时为 null。</param>
public sealed record ProcessorTopology(
    int PhysicalCores,
    int LogicalProcessors,
    int? PerformanceCores,
    int? EfficiencyCores);

/// <summary>
/// 通过 <c>GetLogicalProcessorInformationEx</c> 读取 CPU 拓扑（架构 3.3：大小核）。
/// 说明：
/// <list type="bullet">
/// <item>Windows 10 不填充 EfficiencyClass（全部核心同一等级），此时性能核 / 能效核返回 null（不可用而非猜测）；</item>
/// <item>Windows 11 22H2+ 的混合架构 CPU 中，EfficiencyClass 越大性能越强（Intel 12 代起 P 核 = 1、E 核 = 0）；</item>
/// <item>缓冲区解析全部带边界检查，任何异常按"不可用"处理，绝不影响主检测流程。</item>
/// </list>
/// </summary>
internal static class ProcessorTopologyReader
{
    /// <summary>读取拓扑；失败或不受支持时返回全零 / null 结果（调用方降级到 WMI 数据）。</summary>
    public static ProcessorTopology Read()
    {
        try
        {
            uint returnedLength = 0;
            _ = NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, IntPtr.Zero, ref returnedLength);
            if (returnedLength == 0)
            {
                return Empty;
            }

            var buffer = Marshal.AllocHGlobal((int)returnedLength);
            try
            {
                if (!NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, buffer, ref returnedLength))
                {
                    return Empty;
                }

                return ParseBuffer(buffer, returnedLength);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is AccessViolationException or InsufficientMemoryException or BadImageFormatException)
        {
            return Empty;
        }
    }

    /// <summary>空结果（拓扑不可用）。</summary>
    public static ProcessorTopology Empty { get; } = new(0, 0, null, null);

    private static ProcessorTopology ParseBuffer(IntPtr buffer, uint length)
    {
        var physicalCores = 0;
        var logicalProcessors = 0;
        var coresByEfficiencyClass = new Dictionary<byte, int>();

        var offset = 0L;
        while (offset + 8 <= length)
        {
            var relationship = Marshal.ReadInt32(buffer, (int)offset);
            var entrySize = Marshal.ReadInt32(buffer, (int)offset + 4);
            if (entrySize < 8 || offset + entrySize > length)
            {
                // 损坏 / 越界：停止解析（已读到的部分仍有效，但保守起见整体按不可用）
                return Empty;
            }

            if (relationship == NativeMethods.RelationProcessorCore)
            {
                var relationOffset = offset + 8;
                if (relationOffset + NativeMethods.ProcessorRelationGroupMaskOffset <= length)
                {
                    var efficiencyClass = Marshal.ReadByte(buffer, (int)relationOffset + NativeMethods.ProcessorRelationEfficiencyClassOffset);
                    var groupCount = Marshal.ReadInt16(buffer, (int)relationOffset + NativeMethods.ProcessorRelationGroupCountOffset);

                    var logicalForCore = 0;
                    var maskOffset = relationOffset + NativeMethods.ProcessorRelationGroupMaskOffset;
                    for (var group = 0; group < groupCount; group++)
                    {
                        if (maskOffset + NativeMethods.GroupAffinitySize > length)
                        {
                            break;
                        }

                        // GROUP_AFFINITY.Mask（KAFFINITY，8 字节位掩码）弹出位数 = 该组逻辑处理器数
                        var mask = unchecked((ulong)Marshal.ReadInt64(buffer, (int)maskOffset));
                        logicalForCore += PopCount(mask);
                        maskOffset += NativeMethods.GroupAffinitySize;
                    }

                    if (logicalForCore > 0)
                    {
                        physicalCores++;
                        logicalProcessors += logicalForCore;
                        coresByEfficiencyClass.TryGetValue(efficiencyClass, out var count);
                        coresByEfficiencyClass[efficiencyClass] = count + logicalForCore;
                    }
                }
            }

            offset += entrySize;
        }

        int? performanceCores = null;
        int? efficiencyCores = null;
        if (coresByEfficiencyClass.Count > 1)
        {
            // 存在多个能效等级才算混合架构：最高等级为性能核，其余合计为能效核
            var maxClass = coresByEfficiencyClass.Keys.Max();
            performanceCores = coresByEfficiencyClass[maxClass];
            efficiencyCores = coresByEfficiencyClass.Where(pair => pair.Key != maxClass).Sum(pair => pair.Value);
        }

        return new ProcessorTopology(physicalCores, logicalProcessors, performanceCores, efficiencyCores);
    }

    /// <summary>弹出 64 位掩码中置位位数（逻辑处理器计数）。</summary>
    private static int PopCount(ulong mask)
    {
        var count = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }

        return count;
    }
}
