using InventoryManager.Data;
using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Services;

public sealed class DatabaseBackupService
{
    private readonly Database _database;
    private readonly AuthService _auth;

    public DatabaseBackupService(Database database, AuthService auth)
    {
        _database = database;
        _auth = auth;
    }

    public void CreateBackup(string destinationPath)
    {
        _auth.RequirePermission("backup.manage");
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, Path.GetFullPath(_database.DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a different path from the live database.", nameof(destinationPath));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var source = _database.GetConnection();
        source.Open();
        using var target = new SqliteConnection($"Data Source={destination}");
        target.Open();
        source.BackupDatabase(target);
        AuthService.WriteAudit(source, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Create backup", "Database", destination, "SQLite online backup");
    }

    public string RestoreBackup(string sourcePath)
    {
        _auth.RequirePermission("backup.manage");
        var sourcePathFull = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePathFull)) throw new FileNotFoundException("Backup file was not found.", sourcePathFull);
        if (string.Equals(sourcePathFull, Path.GetFullPath(_database.DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected file is the live database, not a backup.", nameof(sourcePath));

        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePathFull,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        backup.Open();
        using (var validate = backup.CreateCommand())
        {
            validate.CommandText = "PRAGMA quick_check";
            if (!string.Equals(Convert.ToString(validate.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected backup did not pass SQLite integrity validation.");
        }
        using (var tables = backup.CreateCommand())
        {
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Products'";
            if (Convert.ToInt32(tables.ExecuteScalar()) == 0)
                throw new InvalidDataException("The selected SQLite file does not contain an inventory database.");
        }

        var safetyPath = _database.DatabasePath + $".pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        using (var live = _database.GetConnection())
        {
            live.Open();
            using var safety = new SqliteConnection($"Data Source={safetyPath}");
            safety.Open();
            live.BackupDatabase(safety);
        }
        using (var live = _database.GetConnection())
        {
            live.Open();
            backup.BackupDatabase(live);
            using var createSession = live.CreateCommand();
            var sessionId = Guid.NewGuid().ToString("N");
            createSession.CommandText = @"INSERT INTO AppSessions (SessionId,UserId,StartedAt,LastActivityAt,MachineName,IsActive)
                                          VALUES (@session,@user,@now,@now,@machine,1)";
            createSession.Parameters.AddWithValue("@session", sessionId);
            createSession.Parameters.AddWithValue("@user", _auth.CurrentUser!.Id);
            createSession.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
            createSession.Parameters.AddWithValue("@machine", Environment.MachineName);
            createSession.ExecuteNonQuery();
            _auth.CurrentUser.SessionId = sessionId;
            AuthService.WriteAudit(live, _auth.CurrentUser.Id, _auth.CurrentUser.Username, "Restore backup",
                "Database", sourcePathFull, $"Safety backup: {safetyPath}");
        }
        return safetyPath;
    }
}
