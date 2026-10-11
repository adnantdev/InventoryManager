namespace InventoryManager.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Staff";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
}
