using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BluetoothAudioRelay;

internal sealed record UpdateCheckResult(
    bool Succeeded,
    bool UpdateAvailable,
    Version? LatestVersion,
    Uri? ReleaseUri,
    string Message);

internal static class UpdateChecker
{
    private const string RepositoryApi = "https://api.github.com/repos/kkai524152/BluetoothAudioRelay";
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(
                $"{RepositoryApi}/releases/latest",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return await CheckLatestTagAsync(currentVersion, cancellationToken);
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            var url = root.GetProperty("html_url").GetString();
            return BuildResult(currentVersion, tag, url);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, null, null, $"检查更新失败：{ex.Message}");
        }
    }

    private static async Task<UpdateCheckResult> CheckLatestTagAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(
            $"{RepositoryApi}/tags?per_page=1",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.GetArrayLength() == 0)
        {
            return new UpdateCheckResult(true, false, null, null, "当前仓库还没有发布版本。");
        }

        var tag = document.RootElement[0].GetProperty("name").GetString();
        var url = $"https://github.com/kkai524152/BluetoothAudioRelay/releases/tag/{Uri.EscapeDataString(tag ?? string.Empty)}";
        return BuildResult(currentVersion, tag, url);
    }

    private static UpdateCheckResult BuildResult(Version currentVersion, string? tag, string? url)
    {
        if (!TryParseVersion(tag, out var latestVersion))
        {
            return new UpdateCheckResult(false, false, null, null, $"无法识别远程版本：{tag ?? "空"}");
        }

        var releaseUri = Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ? parsedUri : null;
        var updateAvailable = latestVersion > currentVersion;
        return new UpdateCheckResult(
            true,
            updateAvailable,
            latestVersion,
            releaseUri,
            updateAvailable
                ? $"发现新版本 {latestVersion}。"
                : $"当前已是最新版本 {currentVersion}。");
    }

    internal static bool TryParseVersion(string? value, out Version version)
    {
        var normalized = value?.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        return Version.TryParse(normalized, out version!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothAudioRelay", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
