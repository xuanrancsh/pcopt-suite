// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core;

/// <summary>
/// 内核统一结果类型（无返回值场景）。
/// 架构 8.2：内核公开 API 返回 <see cref="Result"/> / <see cref="Result{T}"/>，不跨模块抛异常；
/// 失败时 <see cref="Error"/> 必须包含明确、面向用户可读的中文信息。
/// </summary>
public sealed class Result
{
    private Result(bool isSuccess, string error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>失败原因；成功时为空字符串（永不为 null，便于 UI 层直接展示）。</summary>
    public string Error { get; }

    /// <summary>构造成功结果。</summary>
    public static Result Ok() => new(true, string.Empty);

    /// <summary>
    /// 构造失败结果。<paramref name="error"/> 必须是明确、面向用户可读的中文信息，
    /// 不得包含用户数据、硬件序列号或含用户名的路径（架构 8.3）。
    /// </summary>
    public static Result Fail(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("失败原因不能为空", nameof(error));
        }

        return new Result(false, error);
    }
}

/// <summary>
/// 内核统一结果类型（带返回值场景）。
/// </summary>
/// <typeparam name="T">成功时返回的值类型。</typeparam>
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, string error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>成功时的值；失败时为默认值（调用前必须检查 <see cref="IsSuccess"/>）。</summary>
    public T? Value { get; }

    /// <summary>失败原因；成功时为空字符串（永不为 null）。</summary>
    public string Error { get; }

    /// <summary>构造成功结果。</summary>
    public static Result<T> Ok(T value) => new(true, value, string.Empty);

    /// <summary>构造失败结果。<paramref name="error"/> 必须是明确、面向用户可读的中文信息。</summary>
    public static Result<T> Fail(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("失败原因不能为空", nameof(error));
        }

        return new Result<T>(false, default, error);
    }
}
