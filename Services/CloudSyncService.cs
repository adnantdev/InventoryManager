namespace InventoryManager.Services;

public sealed class CloudSyncService
{
    private readonly DatabaseBackupService _backups;
    private readonly HttpClient _httpClient;

    public CloudSyncService(DatabaseBackupService backups, HttpClient? httpClient = null)
    {
        _backups = backups;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task PushBackupAsync(Uri endpoint, string bearerToken, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(endpoint, bearerToken);
        var file = Path.Combine(Path.GetTempPath(), $"inventory-cloud-{Guid.NewGuid():N}.db");
        try
        {
            _backups.CreateBackup(file);
            using var content = new MultipartFormDataContent();
            await using var stream = File.OpenRead(file);
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.sqlite3");
            content.Add(fileContent, "backup", Path.GetFileName(file));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    public async Task<string> PullAndRestoreAsync(Uri endpoint, string bearerToken, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(endpoint, bearerToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var file = Path.Combine(Path.GetTempPath(), $"inventory-cloud-restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var output = File.Create(file))
                await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return _backups.RestoreBackup(file);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static void ValidateConfiguration(Uri endpoint, string bearerToken)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Cloud synchronization requires an absolute HTTPS endpoint.");
        if (string.IsNullOrWhiteSpace(bearerToken))
            throw new ArgumentException("A cloud sync bearer token is required.");
    }
}
