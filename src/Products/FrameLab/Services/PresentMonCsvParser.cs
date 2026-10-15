// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.IO;
using System.Text;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// PresentMon CSV 解析器（T32，架构 4.3 / PRD C-P0-1）。
/// <para>
/// <b>表头驱动</b>：按列名（含多版本别名）定位，容忍缺列 / 多列 / 列顺序变化；
/// <b>健壮性</b>：损坏行、字段数不符、非数字、负帧时间、超长行、BOM、空文件一律安全跳过或返回空，
/// 绝不抛异常（架构 8.2）。帧时间为必需列；缺失时返回空集合。
/// </para>
/// </summary>
public static class PresentMonCsvParser
{
    /// <summary>单行最大长度（超过视为损坏行，直接跳过，防超长行拖垮内存）。</summary>
    private const int MaxLineLength = 200_000;

    // 各逻辑列的别名（规范化后精确匹配：去空格 + 小写）。
    private static readonly string[] ApplicationAliases = { "application", "processname", "applicationname", "process" };
    private static readonly string[] TimeAliases = { "timeinseconds", "timeinsecond", "time", "mstime", "timems", "timestamp" };
    private static readonly string[] FrameTimeAliases = { "msbetweenpresents", "frametime", "msframetime", "frametimems" };
    private static readonly string[] CpuAliases = { "msinpresentapi", "cpubusy", "cpuframetime", "mscpuframetime", "mscpu", "cpu" };
    private static readonly string[] GpuAliases = { "gpubusy", "gpu", "msgpuactive", "msgpu", "gputime", "gpuframetime" };
    private static readonly string[] DisplayAliases = { "msuntildisplayed", "displaylatency", "displaylatencyms", "msbetweendisplaychange" };

    /// <summary>解析 CSV 文本，返回帧样本（按出现顺序）。</summary>
    /// <param name="content">CSV 文本（可含 BOM）。</param>
    public static IReadOnlyList<FrameSample> Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<FrameSample>();
        }

        using var reader = new StringReader(content);
        return Parse(reader);
    }

    /// <summary>解析 CSV 文件（UTF-8 / 带 BOM 自适应；读取失败返回空集合）。</summary>
    /// <param name="path">CSV 文件路径。</param>
    public static IReadOnlyList<FrameSample> ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<FrameSample>();
        }

        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(reader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<FrameSample>();
        }
    }

    /// <summary>解析 CSV 流，返回帧样本（按出现顺序）。</summary>
    /// <param name="reader">CSV 文本读取器。</param>
    public static IReadOnlyList<FrameSample> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var samples = new List<FrameSample>();

        string? line;
        string[]? header = null;
        var columns = default(ColumnMap);
        var hasColumns = false;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > MaxLineLength)
            {
                // 超长行：视为损坏行，跳过（不解析、不保留引用）
                continue;
            }

            if (header is null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                header = SplitCsvLine(line);
                columns = ColumnMap.Build(header);
                hasColumns = columns.HasFrameTime;
                if (!hasColumns)
                {
                    // 没有帧时间列：无法产出有用样本，视为无数据
                    return Array.Empty<FrameSample>();
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = SplitCsvLine(line);
            if (fields.Length != header.Length)
            {
                // 字段数不符（含损坏行 / 结尾多余分隔）：跳过
                continue;
            }

            var frameTime = columns.ReadDouble(fields, columns.FrameTime);
            if (frameTime is null || frameTime <= 0 || double.IsNaN(frameTime.Value) || double.IsInfinity(frameTime.Value))
            {
                // 非数字 / 非正 / 非有限帧时间：损坏行，跳过
                continue;
            }

            var timeSeconds = columns.ReadDouble(fields, columns.Time) ?? double.NaN;
            samples.Add(new FrameSample
            {
                Application = columns.ReadString(fields, columns.Application),
                TimeInSeconds = timeSeconds,
                FrameTimeMs = frameTime.Value,
                CpuMs = columns.ReadDouble(fields, columns.Cpu),
                GpuMs = columns.ReadDouble(fields, columns.Gpu),
                DisplayLatencyMs = columns.ReadDouble(fields, columns.Display),
            });
        }

        if (samples.Count == 0)
        {
            return Array.Empty<FrameSample>();
        }

        // 无时间列时，用累计帧时间补齐相对时刻（秒）
        if (double.IsNaN(samples[0].TimeInSeconds))
        {
            var accumulatedMs = 0.0;
            for (var i = 0; i < samples.Count; i++)
            {
                samples[i] = samples[i] with { TimeInSeconds = accumulatedMs / 1000.0 };
                accumulatedMs += samples[i].FrameTimeMs;
            }
        }

        return samples;
    }

    /// <summary>
    /// 按 RFC4180 简化规则切分一行 CSV（支持双引号包裹与转义 <c>""</c>）。
    /// 解析失败时退化为朴素逗号切分。
    /// </summary>
    private static string[] SplitCsvLine(string line)
    {
        // 去 BOM（可能残留在首行首位）
        if (line.Length > 0 && line[0] == '\uFEFF')
        {
            line = line[1..];
        }

        if (line.IndexOf('"') < 0)
        {
            return line.Split(',');
        }

        var fields = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        fields.Add(builder.ToString());
        return fields.ToArray();
    }

    private static string Normalize(string name) =>
        name.Replace(" ", string.Empty).Replace("_", string.Empty).Trim().ToLowerInvariant();

    /// <summary>表头列索引映射（-1 表示该逻辑列不存在）。</summary>
    private readonly record struct ColumnMap(
        int Application,
        int Time,
        int FrameTime,
        int Cpu,
        int Gpu,
        int Display)
    {
        public bool HasFrameTime => FrameTime >= 0;

        public static ColumnMap Build(string[] header)
        {
            return new ColumnMap(
                Find(header, ApplicationAliases),
                Find(header, TimeAliases),
                Find(header, FrameTimeAliases),
                Find(header, CpuAliases),
                Find(header, GpuAliases),
                Find(header, DisplayAliases));
        }

        public string ReadString(string[] fields, int index) =>
            index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

        public double? ReadDouble(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length)
            {
                return null;
            }

            var raw = fields[index].Trim();
            if (raw.Length == 0)
            {
                return null;
            }

            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static int Find(string[] header, string[] aliases)
        {
            for (var i = 0; i < header.Length; i++)
            {
                var normalized = Normalize(header[i]);
                if (aliases.Contains(normalized))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
