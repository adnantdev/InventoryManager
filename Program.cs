using InventoryManager.Data;
using InventoryManager.Forms;
using InventoryManager.Services;

namespace InventoryManager;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Set up database (creates inventory.db in the working folder)
        var dbPath = Path.Combine(AppContext.BaseDirectory, "inventory.db");
        var db = new Database(dbPath);

        var inventoryService = new InventoryService(db);
        var authService = new AuthService(db);
        var commerceService = new CommerceService(db);
        var securityAdminService = new SecurityAdminService(db, authService);
        var backupService = new DatabaseBackupService(db, authService);
        var reorderService = new AutomatedReorderService(db, authService);
        var apiService = new LocalApiService(db, authService);
        var cloudSyncService = new CloudSyncService(backupService);
        var notificationService = new ExternalNotificationService();
        var productCsvService = new ProductCsvService(db, inventoryService, authService);

        // Show login first
        using var login = new LoginForm(authService);
        if (login.ShowDialog() != DialogResult.OK)
            return;

        Application.Run(new MainForm(inventoryService, authService, commerceService, securityAdminService, backupService,
            reorderService, apiService, cloudSyncService, notificationService, productCsvService));
    }
}