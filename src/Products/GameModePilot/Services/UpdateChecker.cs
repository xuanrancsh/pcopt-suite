// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 更新检查服务（PRD A-P1-6）：检查 GitHub Releases 最新版本，提示更新。
/// 约束：不自动下载、不强制更新；<b>默认关闭</b>，仅在用户显式开启时调用
/// （合规红线 #8：除更新检查外无网络请求）。
/// </summary>
public sealed class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/pcopt-suite/pcopt-suite/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateChecker> _logger;

    /// <summary>初始化更新检查服务。</summary>
    public UpdateChecker(ILogger<UpdateChecker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("pcopt-suite/GameModePilot");
    }

    /// <summary>
    /// 检查最新版本标签（如 "v0.2.0"）。网络 / 解析失败返回明确失败；无新版本返回 Ok(null)。
    /// </summary>
    public async Task<Result<string?>> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ReleasesApiUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result<string?>.Fail($"检查更新失败（HTTP {(int)response.StatusCode}）");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String)
            {
                return Result<string?>.Ok(tag.GetString());
            }

            return Result<string?>.Ok(null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "检查更新失败");
            return Result<string?>.Fail($"检查更新失败：{ex.Message}");
        }
    }
}
