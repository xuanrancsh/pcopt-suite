// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PcOpt.Core.Native;

/// <summary>
/// 控制台子进程输出解码器（T43 修复）。
/// </summary>
/// <remarks>
/// <para>
/// 背景：中文 Windows 上控制台子进程（powercfg / powershell / PresentMon）按 OEM 代码页
/// （936，即 GBK）输出字节；此前三处调用固定按 UTF-8 解码，导致中文全乱码而 ASCII 的 GUID
/// 却正常显示，极具迷惑性。本类型统一按当前区域性的 OEM 代码页解码。
/// </para>
/// <para>
/// 实现说明：用户若开启"Beta：使用 Unicode UTF-8 提供全球语言支持"，OEM 代码页即为
/// 65001（UTF-8），天然覆盖该场景。.NET 5+ 默认不内置代码页编码表（需引用
/// System.Text.Encoding.CodePages 包，本仓库不引入新 NuGet 包），因此解析顺序为：
/// ① 运行时原生 <see cref="Encoding.GetEncoding(int)"/>（运行时带代码页表时可用）；
/// ② 基于 OS MultiByteToWideChar / WideCharToMultiByte 的自实现代码页编码（零依赖，
/// 任意 Windows 通用，构造后做一次 ASCII 往返自检）；③ 兜底 UTF-8（非 Windows 等场景）。
/// </para>
/// </remarks>
public static class ConsoleOutputDecoder
{
    /// <summary>
    /// 子进程标准输出编码：当前区域性的 OEM 代码页对应编码；
    /// 运行时与 OS API 均不可用时兜底 UTF-8。
    /// </summary>
    public static Encoding OutputEncoding { get; } = ResolveOutputEncoding();

    /// <summary>
    /// 按 <see cref="OutputEncoding"/> 解码子进程输出的原始字节。
    /// </summary>
    /// <param name="bytes">子进程标准输出字节（通常来自重定向管道）。</param>
    /// <returns>解码后的文本；空输入返回空字符串。</returns>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return bytes.Length == 0 ? string.Empty : OutputEncoding.GetString(bytes);
    }

    private static Encoding ResolveOutputEncoding()
    {
        var codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;

        // ① 运行时自带代码页表（.NET Framework / 注册了 CodePages provider 的运行时）
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // .NET 5+ 默认无代码页表：落入 ②
        }

        // ② 基于 OS API 的自实现代码页编码（零 NuGet 依赖；构造后自检确保 API 可用）
        try
        {
            var osEncoding = new OsCodePageEncoding(codePage, $"OEM 代码页 {codePage}");
            var probeBytes = osEncoding.GetBytes("pcopt");
            if (probeBytes.Length > 0 && osEncoding.GetString(probeBytes) == "pcopt")
            {
                return osEncoding;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // 非 Windows 或 API 不可用：落入 ③
        }

        // ③ 兜底 UTF-8（宁可变通用乱码，也不崩溃）
        return Encoding.UTF8;
    }
}

/// <summary>
/// 基于 OS <c>MultiByteToWideChar</c> / <c>WideCharToMultiByte</c> 的代码页编码。
/// .NET 5+ 默认不内置代码页编码表，而本套件不得引入新 NuGet 包，故直接委托 OS 完成转换
/// （与 System.Text.Encoding.CodePages 的实现同源，零依赖、任意 Windows 通用）。
/// </summary>
internal sealed class OsCodePageEncoding : Encoding
{
    private readonly int _codePage;
    private readonly string _name;

    /// <summary>初始化代码页编码。</summary>
    /// <param name="codePage">代码页编号（如中文 Windows OEM 936）。</param>
    /// <param name="name">编码显示名（仅诊断用）。</param>
    internal OsCodePageEncoding(int codePage, string name)
    {
        _codePage = codePage;
        _name = name;
    }

    /// <inheritdoc/>
    public override string EncodingName => _name;

    /// <inheritdoc/>
    public override string WebName => $"cp{_codePage}";

    /// <inheritdoc/>
    public override int CodePage => _codePage;

    /// <inheritdoc/>
    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars);
        var source = Normalize(chars, index, count, nameof(index));
        var bytes = NativeMethods.WideCharToMultiByte(
            (uint)_codePage, 0, source, source.Length, null, 0, IntPtr.Zero, IntPtr.Zero);
        return EnsureSuccess(bytes, "WideCharToMultiByte", source.Length);
    }

    /// <inheritdoc/>
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(bytes);
        var source = Normalize(chars, charIndex, charCount, nameof(charIndex));
        var needed = GetByteCount(source, 0, source.Length);
        if (byteIndex == 0)
        {
            var written = NativeMethods.WideCharToMultiByte(
                (uint)_codePage, 0, source, source.Length, bytes, needed, IntPtr.Zero, IntPtr.Zero);
            return EnsureSuccess(written, "WideCharToMultiByte", source.Length);
        }

        var buffer = new byte[needed];
        var count = NativeMethods.WideCharToMultiByte(
            (uint)_codePage, 0, source, source.Length, buffer, needed, IntPtr.Zero, IntPtr.Zero);
        EnsureSuccess(count, "WideCharToMultiByte", source.Length);
        Array.Copy(buffer, 0, bytes, byteIndex, count);
        return count;
    }

    /// <inheritdoc/>
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var source = Normalize(bytes, index, count, nameof(index));
        var chars = NativeMethods.MultiByteToWideChar((uint)_codePage, 0, source, source.Length, null, 0);
        return EnsureSuccess(chars, "MultiByteToWideChar", source.Length);
    }

    /// <inheritdoc/>
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(chars);
        var source = Normalize(bytes, byteIndex, byteCount, nameof(byteIndex));
        var needed = GetCharCount(source, 0, source.Length);
        if (charIndex == 0)
        {
            var written = NativeMethods.MultiByteToWideChar(
                (uint)_codePage, 0, source, source.Length, chars, needed);
            return EnsureSuccess(written, "MultiByteToWideChar", source.Length);
        }

        var buffer = new char[needed];
        var count = NativeMethods.MultiByteToWideChar((uint)_codePage, 0, source, source.Length, buffer, needed);
        EnsureSuccess(count, "MultiByteToWideChar", source.Length);
        Array.Copy(buffer, 0, chars, charIndex, count);
        return count;
    }

    /// <inheritdoc/>
    public override int GetMaxByteCount(int charCount) => (charCount + 1) * 2;

    /// <inheritdoc/>
    public override int GetMaxCharCount(int byteCount) => byteCount + 1;

    private static int EnsureSuccess(int result, string api, int inputLength)
    {
        // 输入为空时返回 0 属合法成功；仅当输入非空而返回 0 才判定为 API 失败
        if (result == 0 && inputLength > 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"{api} 失败（Win32 错误 {error}：{NativeMethods.FormatWin32Error(error)}）");
        }

        return result;
    }

    /// <summary>按 index/count 取出实际参与转换的区段（P/Invoke 只能从数组头开始，故必要时拷贝）。</summary>
    private static T[] Normalize<T>(T[] array, int index, int count, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (array.Length - index < count)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }

        return index == 0 && count == array.Length ? array : array[index..(index + count)];
    }
}
