// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 更新检查（PRD B-P1-6 / 合规红线 #8：除更新检查外无任何网络请求，且默认关闭）。
/// 仅在用户显式点击"检查更新"时发起一次 GitHub Releases API 请求；<see cref="AutoCheckEnabled"/> 默认 false。
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>GitHub Releases 最新版本接口。</summary>
    public const string ReleasesApiUrl = "https://api.github.com/repos/xuanrancsh/pcopt-suite/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly ILogger<UpdateChecker> _logger;

    /// <summary>初始化更新检查器（默认离线，不自动检查）。</summary>
    public UpdateChecker(ILogger<UpdateChecker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>是否启用启动时自动检查更新（默认关闭，PRD 8#7 默认完全离线）。</summary>
    public bool AutoCheckEnabled { get; set; }

    /// <summary>
    /// 检查是否有新版本（仅在用户显式触发时调用）。
    /// 返回最新版本号；网络异常返回人话错误（不影响主流程）。
    /// </summary>
    public async Task<Result<string>> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await HttpClient.GetAsync(ReleasesApiUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Fail($"检查更新失败：服务器返回 {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String)
            {
                var version = tag.GetString() ?? "（未知）";
                return Result<string>.Ok($"最新版本：{version}");
            }

            return Result<string>.Fail("检查更新失败：响应格式不正确");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("检查更新失败（网络/解析）：{Message}", ex.Message);
            return Result<string>.Fail("检查更新失败：无法连接网络（当前为离线模式）");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("pcopt-suite-GameOptBox");
        return client;
    }
}
