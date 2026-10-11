using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InventoryManager.Data;
using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Services;

public sealed class LocalApiService : IAsyncDisposable
{
    private readonly Database _database;
    private readonly AuthService _auth;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private string? _tokenHash;

    public bool IsRunning => _listener.IsListening;
    public int Port { get; private set; }

    public LocalApiService(Database database, AuthService auth)
    {
        _database = database;
        _auth = auth;
    }

    public string GenerateToken()
    {
        _auth.RequirePermission("admin.manage");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        using var connection = _database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var deactivate = connection.CreateCommand())
        {
            deactivate.Transaction = transaction;
            deactivate.CommandText = "UPDATE ApiKeys SET IsActive=0";
            deactivate.ExecuteNonQuery();
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO ApiKeys (Name,KeyHash,IsActive,CreatedAt) VALUES ('Local API',@hash,1,@now)";
            insert.Parameters.AddWithValue("@hash", hash);
            insert.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
            insert.ExecuteNonQuery();
        }
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Generate API key", "API", "Local API", "New token generated; shown once", transaction);
        transaction.Commit();
        _tokenHash = hash;
        return token;
    }

    public void Start(int port)
    {
        _auth.RequirePermission("admin.manage");
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Choose a port from 1024 to 65535.");
        if (IsRunning) throw new InvalidOperationException("The local API is already running.");
        _tokenHash ??= LoadActiveTokenHash();
        if (string.IsNullOrEmpty(_tokenHash))
            throw new InvalidOperationException("Generate an API token before starting the API.");
        Port = port;
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _cancellation = new CancellationTokenSource();
        _worker = Task.Run(() => ListenAsync(_cancellation.Token));
        using var connection = _database.GetConnection();
        connection.Open();
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Start local API", "API", port.ToString(), "Bound to loopback only");
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        _cancellation?.Cancel();
        _listener.Stop();
        if (_worker != null)
        {
            try { await _worker.ConfigureAwait(false); }
            catch (HttpListenerException) { }
            catch (OperationCanceledException) { }
        }
        _cancellation?.Dispose();
        _cancellation = null;
        _worker = null;
        using var connection = _database.GetConnection();
        connection.Open();
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Stop local API", "API", Port.ToString(), "Local API stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { break; }
            _ = Task.Run(() => HandleAsync(context), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/api/health")
            {
                await WriteJson(context, 200, new { status = "ok" }).ConfigureAwait(false);
                return;
            }
            if (!ValidateToken(context.Request.Headers["Authorization"]))
            {
                await WriteJson(context, 401, new { error = "Unauthorized" }).ConfigureAwait(false);
                return;
            }
            if (context.Request.HttpMethod != "GET" || context.Request.Url?.AbsolutePath != "/api/products")
            {
                await WriteJson(context, 404, new { error = "Not found" }).ConfigureAwait(false);
                return;
            }
            var products = ReadProducts();
            UpdateKeyLastUsed();
            await WriteJson(context, 200, products).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await WriteJson(context, 500, new { error = "Request failed", detail = exception.Message }).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private bool ValidateToken(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var token = authorization["Bearer ".Length..].Trim();
        var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var hash = _tokenHash ?? LoadActiveTokenHash();
        if (string.IsNullOrEmpty(hash)) return false;
        byte[] expected;
        try { expected = Convert.FromHexString(hash); }
        catch (FormatException) { return false; }
        return candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    private List<object> ReadProducts()
    {
        using var connection = _database.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT p.Id,p.SKU,p.Barcode,p.Name,p.SellPrice,
                                  IFNULL(SUM(sl.Quantity),0) AS Quantity
                               FROM Products p LEFT JOIN StockLevels sl ON sl.ProductId=p.Id
                               WHERE p.IsActive=1 GROUP BY p.Id ORDER BY p.Name";
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        while (reader.Read())
            rows.Add(new
            {
                id = reader.GetInt32(0),
                sku = reader.GetString(1),
                barcode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                name = reader.GetString(3),
                price = reader.GetDouble(4),
                quantity = reader.GetInt32(5)
            });
        return rows;
    }

    private string? LoadActiveTokenHash()
    {
        using var connection = _database.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT KeyHash FROM ApiKeys WHERE IsActive=1 ORDER BY Id DESC LIMIT 1";
        return Convert.ToString(command.ExecuteScalar());
    }

    private void UpdateKeyLastUsed()
    {
        using var connection = _database.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ApiKeys SET LastUsedAt=@now WHERE KeyHash=@hash AND IsActive=1";
        command.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("@hash", _tokenHash ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static async Task WriteJson(HttpListenerContext context, int statusCode, object value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = payload.Length;
        await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
    }
}
