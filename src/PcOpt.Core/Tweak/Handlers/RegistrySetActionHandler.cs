// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Security;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PcOpt.Core.Compliance;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// 注册表值写入处理器（架构 3.1 / T4）。
/// 成对实现：CaptureOriginal（快照原值，含"键 / 值不存在"语义）→ Apply（写入目标值）→
/// Revert（按快照恢复：不存在则删除，存在则写回原值原类型）→ Verify（读回比对）。
/// IFEO 降级方案（PRD A-P0-6）复用本处理器——IFEO 本质是 HKLM 注册表写入，
/// 路径由 <see cref="IfeoPaths.PerfOptions"/> 构造。
/// </summary>
public sealed class RegistrySetActionHandler : ITweakActionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        // 快照与还原日志可能包含中文等非 ASCII 内容：禁用默认转义，保证文件人类可读
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogger<RegistrySetActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public RegistrySetActionHandler(ILogger<RegistrySetActionHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is RegistrySetAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        var typed = RequireAction(action);
        var guard = ComplianceGuard.EnsureWritable(typed.Path);
        if (!guard.IsSuccess)
        {
            return Result<string>.Fail(guard.Error);
        }

        var baseKeyResult = OpenBaseKey(typed.Path);
        if (!baseKeyResult.IsSuccess)
        {
            return Result<string>.Fail(baseKeyResult.Error);
        }

        using (baseKeyResult.Value!)
        {
            using var key = baseKeyResult.Value!.OpenSubKey(ToRelativePath(typed.Path));
            var snapshot = new RegistryValueSnapshot
            {
                KeyExists = key is not null,
                ValueExists = key is not null && key.GetValue(typed.Name) is not null,
            };

            if (snapshot.ValueExists)
            {
                snapshot.Kind = key!.GetValueKind(typed.Name);
                snapshot.ValueText = ReadValueAsText(key, typed.Name, snapshot.Kind);
            }

            return Result<string>.Ok(JsonSerializer.Serialize(snapshot, JsonOptions));
        }
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        var guard = ComplianceGuard.EnsureWritable(typed.Path);
        if (!guard.IsSuccess)
        {
            return Result.Fail(guard.Error);
        }

        var baseKeyResult = OpenBaseKey(typed.Path);
        if (!baseKeyResult.IsSuccess)
        {
            return Result.Fail(baseKeyResult.Error);
        }

        using (baseKeyResult.Value!)
        {
            try
            {
                using var key = baseKeyResult.Value!.CreateSubKey(ToRelativePath(typed.Path));
                key.SetValue(typed.Name, typed.Value, typed.Kind);
                _logger.LogDebug("注册表值已写入（规则动作路径: {RegistryPath}）", typed.Path);
                return Result.Ok();
            }
            catch (System.Security.SecurityException ex)
            {
                return Result.Fail($"写入注册表被拒绝（需要管理员权限）：{ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result.Fail($"写入注册表被拒绝（需要管理员权限）：{ex.Message}");
            }
            catch (IOException ex)
            {
                return Result.Fail($"写入注册表失败：{ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        var typed = RequireAction(action);
        var guard = ComplianceGuard.EnsureWritable(typed.Path);
        if (!guard.IsSuccess)
        {
            return Result.Fail(guard.Error);
        }

        RegistryValueSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<RegistryValueSnapshot>(originalValueJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("还原日志中的注册表快照损坏");
        }

        if (snapshot is null)
        {
            return Result.Fail("还原日志中的注册表快照为空");
        }

        var baseKeyResult = OpenBaseKey(typed.Path);
        if (!baseKeyResult.IsSuccess)
        {
            return Result.Fail(baseKeyResult.Error);
        }

        using (baseKeyResult.Value!)
        {
            try
            {
                if (!snapshot.KeyExists)
                {
                    // 原键不存在：删除我们创建的子键（只删最后一级，绝不动父键其他内容）
                    DeleteCreatedSubKey(baseKeyResult.Value!, typed.Path);
                    return Result.Ok();
                }

                using var key = baseKeyResult.Value!.OpenSubKey(ToRelativePath(typed.Path), writable: true)
                    ?? baseKeyResult.Value!.CreateSubKey(ToRelativePath(typed.Path));
                if (!snapshot.ValueExists)
                {
                    key.DeleteValue(typed.Name, throwOnMissingValue: false);
                    return Result.Ok();
                }

                // 先删后写，保证值类型（Kind）变更也能正确生效
                key.DeleteValue(typed.Name, throwOnMissingValue: false);
                key.SetValue(typed.Name, ValueFromSnapshot(snapshot), snapshot.Kind);
                return Result.Ok();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result.Fail($"还原注册表被拒绝（需要管理员权限）：{ex.Message}");
            }
            catch (IOException ex)
            {
                return Result.Fail($"还原注册表失败：{ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        var typed = RequireAction(action);
        var guard = ComplianceGuard.EnsureWritable(typed.Path);
        if (!guard.IsSuccess)
        {
            return Result.Fail(guard.Error);
        }

        var baseKeyResult = OpenBaseKey(typed.Path);
        if (!baseKeyResult.IsSuccess)
        {
            return Result.Fail(baseKeyResult.Error);
        }

        using (baseKeyResult.Value!)
        {
            using var key = baseKeyResult.Value!.OpenSubKey(ToRelativePath(typed.Path));
            var current = key?.GetValue(typed.Name);
            if (current is null)
            {
                return Result.Fail($"校验失败：注册表值 {typed.Path}\\{typed.Name} 不存在");
            }

            var expected = ToCanonicalText(typed.Value, typed.Kind);
            var actual = ToCanonicalText(current, typed.Kind);
            return string.Equals(expected, actual, StringComparison.Ordinal)
                ? Result.Ok()
                : Result.Fail($"校验失败：注册表值 {typed.Path}\\{typed.Name} 与目标不一致");
        }
    }

    private static RegistrySetAction RequireAction(TweakAction action) =>
        action as RegistrySetAction
        ?? throw new ArgumentException($"本处理器只接受 RegistrySetAction，收到 {action.GetType().Name}", nameof(action));

    private static Result<RegistryKey> OpenBaseKey(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');
        var separatorIndex = normalized.IndexOf('\\');
        var hiveText = separatorIndex < 0 ? normalized : normalized[..separatorIndex];
        var hive = hiveText.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
            _ => (RegistryHive?)null,
        };

        if (hive is null)
        {
            return Result<RegistryKey>.Fail($"无法识别的注册表根键：{hiveText}");
        }

        // 固定 64 位视图：套件仅面向 64 位 Windows，不干预 WOW6432Node（架构 3.3）
        return Result<RegistryKey>.Ok(RegistryKey.OpenBaseKey(hive.Value, RegistryView.Registry64));
    }

    private static string ToRelativePath(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');
        var separatorIndex = normalized.IndexOf('\\');
        return separatorIndex < 0 ? string.Empty : normalized[(separatorIndex + 1)..];
    }

    private static void DeleteCreatedSubKey(RegistryKey baseKey, string fullPath)
    {
        var relativePath = ToRelativePath(fullPath);
        var lastSeparator = relativePath.LastIndexOf('\\');
        var parentRelativePath = lastSeparator < 0 ? string.Empty : relativePath[..lastSeparator];
        var subKeyName = lastSeparator < 0 ? relativePath : relativePath[(lastSeparator + 1)..];
        if (string.IsNullOrEmpty(subKeyName))
        {
            return;
        }

        using var parent = string.IsNullOrEmpty(parentRelativePath)
            ? baseKey
            : baseKey.OpenSubKey(parentRelativePath, writable: true);
        parent?.DeleteSubKey(subKeyName, throwOnMissingSubKey: false);
    }

    private static string? ReadValueAsText(RegistryKey key, string name, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => Convert.ToInt32(key.GetValue(name), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => Convert.ToInt64(key.GetValue(name), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.ToBase64String(key.GetValue(name) as byte[] ?? Array.Empty<byte>()),
        RegistryValueKind.MultiString => JsonSerializer.Serialize(key.GetValue(name) as string[] ?? Array.Empty<string>()),
        _ => key.GetValue(name)?.ToString() ?? string.Empty,
    };

    private static object ValueFromSnapshot(RegistryValueSnapshot snapshot) => snapshot.Kind switch
    {
        RegistryValueKind.DWord => int.Parse(snapshot.ValueText ?? "0", CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(snapshot.ValueText ?? "0", CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromBase64String(snapshot.ValueText ?? string.Empty),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(snapshot.ValueText ?? "[]") ?? Array.Empty<string>(),
        _ => snapshot.ValueText ?? string.Empty,
    };

    private static string ToCanonicalText(object? value, RegistryValueKind kind)
    {
        try
        {
            return kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.Binary => Convert.ToBase64String(value as byte[] ?? Array.Empty<byte>()),
                RegistryValueKind.MultiString => JsonSerializer.Serialize(value as string[] ?? Array.Empty<string>()),
                _ => value?.ToString() ?? string.Empty,
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return string.Empty;
        }
    }

    /// <summary>注册表原值快照（JSON 结构；含"键 / 值不存在"语义，架构 3.2）。</summary>
    private sealed class RegistryValueSnapshot
    {
        public bool KeyExists { get; set; }

        public bool ValueExists { get; set; }

        public RegistryValueKind Kind { get; set; }

        public string? ValueText { get; set; }
    }
}
