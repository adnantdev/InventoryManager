using InventoryManager.Data;
using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Services;

public sealed class SecurityAdminService
{
    private readonly Database _db;
    private readonly AuthService _auth;

    public SecurityAdminService(Database db, AuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    public List<AuditEntry> GetAuditEntries(int limit = 1000)
    {
        _auth.RequirePermission("audit.read");
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT CreatedAt,Username,Action,EntityType,EntityId,Details
                                FROM AuditLog ORDER BY Id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 5000));
        using var reader = command.ExecuteReader();
        var entries = new List<AuditEntry>();
        while (reader.Read())
            entries.Add(new AuditEntry
            {
                CreatedAt = DateTime.Parse(reader.GetString(0)),
                Username = reader.GetString(1),
                Action = reader.GetString(2),
                EntityType = reader.GetString(3),
                EntityId = reader.GetString(4),
                Details = reader.GetString(5)
            });
        return entries;
    }

    public List<SessionEntry> GetSessions()
    {
        _auth.RequirePermission("admin.manage");
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT s.SessionId,s.UserId,u.Username,s.StartedAt,s.LastActivityAt,
                                       s.MachineName,s.IsActive
                                FROM AppSessions s JOIN Users u ON u.Id=s.UserId
                                ORDER BY s.IsActive DESC,s.StartedAt DESC";
        using var reader = command.ExecuteReader();
        var sessions = new List<SessionEntry>();
        while (reader.Read())
            sessions.Add(new SessionEntry
            {
                SessionId = reader.GetString(0),
                UserId = reader.GetInt32(1),
                Username = reader.GetString(2),
                StartedAt = DateTime.Parse(reader.GetString(3)),
                LastActivityAt = DateTime.Parse(reader.GetString(4)),
                MachineName = reader.GetString(5),
                IsActive = reader.GetInt32(6) == 1
            });
        return sessions;
    }

    public void EndSession(string sessionId)
    {
        _auth.RequirePermission("admin.manage");
        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE AppSessions SET IsActive=0,EndedAt=@now WHERE SessionId=@id AND IsActive=1";
        command.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("@id", sessionId);
        if (command.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("The selected session is no longer active.");
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "End session", "Session", sessionId, "Session terminated by administrator", transaction);
        transaction.Commit();
    }

    public List<Branch> GetBranches()
    {
        _auth.RequirePermission("admin.manage");
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Code,Name,Address,Phone,IsActive FROM Branches ORDER BY Name";
        using var reader = command.ExecuteReader();
        var branches = new List<Branch>();
        while (reader.Read())
            branches.Add(new Branch
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Address = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1
            });
        return branches;
    }

    public void SaveBranch(Branch branch)
    {
        _auth.RequirePermission("admin.manage");
        if (string.IsNullOrWhiteSpace(branch.Code) || string.IsNullOrWhiteSpace(branch.Name))
            throw new ArgumentException("Branch code and name are required.");
        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = branch.Id == 0
            ? @"INSERT INTO Branches (Code,Name,Address,Phone,IsActive,CreatedAt)
                VALUES (@code,@name,@address,@phone,@active,@now)"
            : @"UPDATE Branches SET Code=@code,Name=@name,Address=@address,Phone=@phone,IsActive=@active WHERE Id=@id";
        command.Parameters.AddWithValue("@code", branch.Code.Trim());
        command.Parameters.AddWithValue("@name", branch.Name.Trim());
        command.Parameters.AddWithValue("@address", branch.Address.Trim());
        command.Parameters.AddWithValue("@phone", branch.Phone.Trim());
        command.Parameters.AddWithValue("@active", branch.IsActive ? 1 : 0);
        if (branch.Id == 0) command.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        else command.Parameters.AddWithValue("@id", branch.Id);
        command.ExecuteNonQuery();
        var entityId = branch.Id.ToString();
        if (branch.Id == 0)
        {
            using var idCommand = connection.CreateCommand();
            idCommand.Transaction = transaction;
            idCommand.CommandText = "SELECT last_insert_rowid()";
            entityId = Convert.ToString(idCommand.ExecuteScalar()) ?? string.Empty;
        }
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            branch.Id == 0 ? "Create branch" : "Update branch", "Branch", entityId, branch.Code.Trim(), transaction);
        transaction.Commit();
    }

    public void SetWarehouseBranch(int warehouseId, int? branchId)
    {
        _auth.RequirePermission("admin.manage");
        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Warehouses SET BranchId=@branch WHERE Id=@id";
        command.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", warehouseId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Warehouse was not found.");
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Assign branch", "Warehouse", warehouseId.ToString(), $"BranchId={branchId?.ToString() ?? "unassigned"}", transaction);
        transaction.Commit();
    }

    public void SetSetting(string key, string value)
    {
        _auth.RequirePermission("admin.manage");
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Setting key is required.");
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"INSERT INTO AppSettings (Key,Value) VALUES (@key,@value)
                                ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key=@key";
        command.Parameters.AddWithValue("@key", key);
        return Convert.ToString(command.ExecuteScalar()) ?? defaultValue;
    }
}
