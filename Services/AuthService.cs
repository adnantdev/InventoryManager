using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.Services;

public sealed class AuthService
{
    private readonly Database _db;
    private HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);
    public User? CurrentUser { get; private set; }

    public AuthService(Database db) => _db = db;

    public bool Login(string username, string password)
    {
        Logout();
        var normalizedUsername = username.Trim();
        using var connection = _db.GetConnection();
        connection.Open();

        using (var lockout = connection.CreateCommand())
        {
            lockout.CommandText = "SELECT COUNT(*) FROM LoginAttempts WHERE Username=@username AND Succeeded=0 AND AttemptAt>=@since";
            lockout.Parameters.AddWithValue("@username", normalizedUsername);
            lockout.Parameters.AddWithValue("@since", DateTime.Now.AddMinutes(-15).ToString("o"));
            if (Convert.ToInt32(lockout.ExecuteScalar()) >= 5)
                throw new InvalidOperationException("Too many failed sign-in attempts. Try again in 15 minutes.");
        }

        int userId = 0;
        string passwordHash = string.Empty;
        string fullName = string.Empty;
        string role = "Staff";
        bool isActive = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,PasswordHash,FullName,Role,IsActive FROM Users WHERE Username=@username";
            command.Parameters.AddWithValue("@username", normalizedUsername);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                userId = reader.GetInt32(0);
                passwordHash = reader.GetString(1);
                fullName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                role = reader.IsDBNull(3) ? "Staff" : reader.GetString(3);
                isActive = reader.GetInt32(4) == 1;
            }
        }

        var verified = userId > 0 && isActive && BCrypt.Net.BCrypt.Verify(password, passwordHash);
        using (var attempt = connection.CreateCommand())
        {
            attempt.CommandText = "INSERT INTO LoginAttempts (Username,AttemptAt,Succeeded) VALUES (@username,@time,@success)";
            attempt.Parameters.AddWithValue("@username", normalizedUsername);
            attempt.Parameters.AddWithValue("@time", DateTime.Now.ToString("o"));
            attempt.Parameters.AddWithValue("@success", verified ? 1 : 0);
            attempt.ExecuteNonQuery();
        }
        if (!verified) return false;

        _permissions = ReadPermissions(connection, role);
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTime.Now;
        using (var session = connection.CreateCommand())
        {
            session.CommandText = @"INSERT INTO AppSessions (SessionId,UserId,StartedAt,LastActivityAt,MachineName,IsActive)
                                    VALUES (@session,@user,@now,@now,@machine,1)";
            session.Parameters.AddWithValue("@session", sessionId);
            session.Parameters.AddWithValue("@user", userId);
            session.Parameters.AddWithValue("@now", now.ToString("o"));
            session.Parameters.AddWithValue("@machine", Environment.MachineName);
            session.ExecuteNonQuery();
        }
        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE Users SET LastLoginAt=@now WHERE Id=@id";
            update.Parameters.AddWithValue("@now", now.ToString("o"));
            update.Parameters.AddWithValue("@id", userId);
            update.ExecuteNonQuery();
        }
        CurrentUser = new User
        {
            Id = userId,
            Username = normalizedUsername,
            FullName = fullName,
            Role = role,
            SessionId = sessionId
        };
        using (var mustChange = connection.CreateCommand())
        {
            mustChange.CommandText = "SELECT MustChangePassword FROM Users WHERE Id=@id";
            mustChange.Parameters.AddWithValue("@id", userId);
            CurrentUser.MustChangePassword = Convert.ToInt32(mustChange.ExecuteScalar()) == 1;
        }
        WriteAudit(connection, userId, normalizedUsername, "Login", "Session", sessionId, "Interactive login");
        return true;
    }

    public bool HasUsers()
    {
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Users)";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public void SetupFirstAdministrator(string username, string fullName, string password)
    {
        if (HasUsers()) throw new InvalidOperationException("An administrator account already exists.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Username and full name are required.");
        ValidatePassword(password);

        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM Users";
            if (Convert.ToInt32(count.ExecuteScalar()) != 0)
                throw new InvalidOperationException("An administrator account already exists.");
        }
        using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = @"INSERT INTO Users (Username,PasswordHash,FullName,Role,IsActive,CreatedAt,LastLoginAt,MustChangePassword)
                               VALUES (@username,@hash,@name,'Admin',1,@now,@now,0);
                               SELECT last_insert_rowid();";
        var now = DateTime.Now;
        create.Parameters.AddWithValue("@username", username.Trim());
        create.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
        create.Parameters.AddWithValue("@name", fullName.Trim());
        create.Parameters.AddWithValue("@now", now.ToString("o"));
        var userId = Convert.ToInt32(create.ExecuteScalar());

        var permissions = AvailablePermissions;
        using (var addPermission = connection.CreateCommand())
        {
            addPermission.Transaction = transaction;
            addPermission.CommandText = "INSERT OR IGNORE INTO RolePermissions (Role,Permission) VALUES ('Admin',@permission)";
            addPermission.Parameters.Add("@permission", Microsoft.Data.Sqlite.SqliteType.Text);
            foreach (var permission in permissions)
            {
                addPermission.Parameters["@permission"].Value = permission;
                addPermission.ExecuteNonQuery();
            }
        }

        var sessionId = Guid.NewGuid().ToString("N");
        using (var session = connection.CreateCommand())
        {
            session.Transaction = transaction;
            session.CommandText = @"INSERT INTO AppSessions (SessionId,UserId,StartedAt,LastActivityAt,MachineName,IsActive)
                                    VALUES (@session,@user,@now,@now,@machine,1)";
            session.Parameters.AddWithValue("@session", sessionId);
            session.Parameters.AddWithValue("@user", userId);
            session.Parameters.AddWithValue("@now", now.ToString("o"));
            session.Parameters.AddWithValue("@machine", Environment.MachineName);
            session.ExecuteNonQuery();
        }
        WriteAudit(connection, userId, username.Trim(), "Initial administrator setup", "User", userId.ToString(),
            "First account created", transaction);
        transaction.Commit();
        CurrentUser = new User
        {
            Id = userId,
            Username = username.Trim(),
            FullName = fullName.Trim(),
            Role = "Admin",
            SessionId = sessionId
        };
        _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasPermission(string permission) =>
        CurrentUser != null && (string.Equals(CurrentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                                || _permissions.Contains(permission));

    public void RequirePermission(string permission)
    {
        if (!HasPermission(permission))
            throw new UnauthorizedAccessException($"Your account does not have the '{permission}' permission.");
    }

    public void UpdateSessionActivity()
    {
        if (CurrentUser == null) return;
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppSessions SET LastActivityAt=@now WHERE SessionId=@session AND IsActive=1";
        command.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("@session", CurrentUser.SessionId);
        command.ExecuteNonQuery();
    }

    public bool IsCurrentSessionActive()
    {
        if (CurrentUser == null) return false;
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT EXISTS(
            SELECT 1 FROM AppSessions s
            JOIN Users u ON u.Id=s.UserId
            WHERE s.SessionId=@session AND s.UserId=@user AND s.IsActive=1 AND u.IsActive=1)";
        command.Parameters.AddWithValue("@session", CurrentUser.SessionId);
        command.Parameters.AddWithValue("@user", CurrentUser.Id);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public void ChangePassword(string currentPassword, string newPassword)
    {
        if (CurrentUser == null) throw new UnauthorizedAccessException("Sign in to change your password.");
        ValidatePassword(newPassword);
        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var verify = connection.CreateCommand())
        {
            verify.Transaction = transaction;
            verify.CommandText = "SELECT PasswordHash FROM Users WHERE Id=@id AND IsActive=1";
            verify.Parameters.AddWithValue("@id", CurrentUser.Id);
            var hash = Convert.ToString(verify.ExecuteScalar());
            if (string.IsNullOrEmpty(hash) || !BCrypt.Net.BCrypt.Verify(currentPassword, hash))
                throw new UnauthorizedAccessException("Current password is incorrect.");
        }
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Users SET PasswordHash=@hash,MustChangePassword=0 WHERE Id=@id";
            update.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            update.Parameters.AddWithValue("@id", CurrentUser.Id);
            update.ExecuteNonQuery();
        }
        WriteAudit(connection, CurrentUser.Id, CurrentUser.Username, "Change password", "User", CurrentUser.Id.ToString(),
            "Password changed", transaction);
        transaction.Commit();
        CurrentUser.MustChangePassword = false;
    }

    public void Logout()
    {
        if (CurrentUser == null) return;
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppSessions SET IsActive=0,EndedAt=@now WHERE SessionId=@session AND IsActive=1";
        command.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("@session", CurrentUser.SessionId);
        command.ExecuteNonQuery();
        WriteAudit(connection, CurrentUser.Id, CurrentUser.Username, "Logout", "Session", CurrentUser.SessionId, "Interactive logout");
        CurrentUser = null;
        _permissions.Clear();
    }

    public List<User> GetUsers()
    {
        RequirePermission("users.manage");
        var result = new List<User>();
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Username,FullName,Role,IsActive FROM Users ORDER BY Username";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Role = reader.IsDBNull(3) ? "Staff" : reader.GetString(3),
                IsActive = reader.GetInt32(4) == 1
            });
        return result;
    }

    public List<string> GetRoles()
    {
        RequirePermission("admin.manage");
        using var connection = _db.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT Role FROM Users UNION SELECT DISTINCT Role FROM RolePermissions ORDER BY 1";
        using var reader = command.ExecuteReader();
        var roles = new List<string>();
        while (reader.Read()) roles.Add(reader.GetString(0));
        return roles;
    }

    public int SaveUser(int id, string username, string fullName, string role, string? newPassword)
    {
        RequirePermission("users.manage");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Username and role are required.");
        if (id == 0 && string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("A password is required for a new user.");

        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            int userId;
            if (id == 0)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO Users (Username,PasswordHash,FullName,Role,IsActive,CreatedAt,MustChangePassword)
                                       VALUES (@username,@hash,@name,@role,1,@now,1); SELECT last_insert_rowid();";
                insert.Parameters.AddWithValue("@username", username.Trim());
                insert.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
                insert.Parameters.AddWithValue("@name", fullName.Trim());
                insert.Parameters.AddWithValue("@role", role.Trim());
                insert.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
                userId = Convert.ToInt32(insert.ExecuteScalar());
            }
            else
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = string.IsNullOrWhiteSpace(newPassword)
                    ? "UPDATE Users SET Username=@username,FullName=@name,Role=@role WHERE Id=@id"
                    : "UPDATE Users SET Username=@username,FullName=@name,Role=@role,PasswordHash=@hash,MustChangePassword=1 WHERE Id=@id";
                update.Parameters.AddWithValue("@username", username.Trim());
                update.Parameters.AddWithValue("@name", fullName.Trim());
                update.Parameters.AddWithValue("@role", role.Trim());
                update.Parameters.AddWithValue("@id", id);
                if (!string.IsNullOrWhiteSpace(newPassword))
                    update.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
                if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("User was not found.");
                userId = id;
            }
            WriteAudit(connection, CurrentUser!.Id, CurrentUser.Username, id == 0 ? "Create user" : "Update user",
                "User", userId.ToString(), $"Role={role.Trim()}", transaction);
            transaction.Commit();
            return userId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void SetUserActive(int userId, bool isActive)
    {
        RequirePermission("users.manage");
        if (CurrentUser?.Id == userId && !isActive)
            throw new InvalidOperationException("You cannot deactivate your own signed-in account.");
        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Users SET IsActive=@active WHERE Id=@id AND (@active=1 OR Role<>'Admin' OR (SELECT COUNT(*) FROM Users WHERE Role='Admin' AND IsActive=1)>1)";
        command.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        command.Parameters.AddWithValue("@id", userId);
        if (command.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            throw new InvalidOperationException("User was not found, or this is the last active administrator.");
        }
        using var sessions = connection.CreateCommand();
        sessions.Transaction = transaction;
        sessions.CommandText = "UPDATE AppSessions SET IsActive=0,EndedAt=@now WHERE UserId=@id AND IsActive=1";
        sessions.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        sessions.Parameters.AddWithValue("@id", userId);
        sessions.ExecuteNonQuery();
        WriteAudit(connection, CurrentUser!.Id, CurrentUser.Username, isActive ? "Activate user" : "Deactivate user",
            "User", userId.ToString(), string.Empty, transaction);
        transaction.Commit();
    }

    public List<string> GetRolePermissions(string role)
    {
        RequirePermission("admin.manage");
        using var connection = _db.GetConnection();
        connection.Open();
        return ReadPermissions(connection, role).OrderBy(permission => permission).ToList();
    }

    public void SetRolePermissions(string role, IEnumerable<string> permissions)
    {
        RequirePermission("admin.manage");
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("Role is required.");
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The built-in Admin role always has all permissions.");
        var selected = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM RolePermissions WHERE Role=@role";
            delete.Parameters.AddWithValue("@role", role.Trim());
            delete.ExecuteNonQuery();
        }
        foreach (var permission in selected)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO RolePermissions (Role,Permission) VALUES (@role,@permission)";
            insert.Parameters.AddWithValue("@role", role.Trim());
            insert.Parameters.AddWithValue("@permission", permission);
            insert.ExecuteNonQuery();
        }
        WriteAudit(connection, CurrentUser!.Id, CurrentUser.Username, "Set role permissions", "Role", role.Trim(),
            string.Join(", ", selected), transaction);
        transaction.Commit();
    }

    public static readonly string[] AvailablePermissions =
    {
        "inventory.read", "inventory.write", "commerce.read", "commerce.write",
        "reports.read", "users.manage", "admin.manage", "backup.manage", "audit.read"
    };

    internal static void WriteAudit(Microsoft.Data.Sqlite.SqliteConnection connection, int? userId, string username,
                                    string action, string entityType, string entityId, string details,
                                    Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO AuditLog (UserId,Username,Action,EntityType,EntityId,Details,CreatedAt)
                                VALUES (@userId,@username,@action,@entityType,@entityId,@details,@createdAt)";
        command.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@entityType", entityType);
        command.Parameters.AddWithValue("@entityId", entityId);
        command.Parameters.AddWithValue("@details", details);
        command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static HashSet<string> ReadPermissions(Microsoft.Data.Sqlite.SqliteConnection connection, string role)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Permission FROM RolePermissions WHERE Role=@role";
        command.Parameters.AddWithValue("@role", role);
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12 || !password.Any(char.IsUpper) || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit) || !password.Any(character => !char.IsLetterOrDigit(character)))
            throw new ArgumentException("Password must be at least 12 characters and include uppercase, lowercase, number, and symbol.");
    }
}
