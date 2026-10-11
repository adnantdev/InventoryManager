using InventoryManager.Services;

namespace InventoryManager.Forms;

public class MainForm : Form
{
    private static readonly Color SidebarColor = Color.FromArgb(30, 40, 55);
    private static readonly Color ActiveColor = Color.FromArgb(0, 120, 215);
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;
    private readonly CommerceService _commerce;
    private readonly SecurityAdminService _securityAdmin;
    private readonly DatabaseBackupService _backups;
    private readonly AutomatedReorderService _reorder;
    private readonly LocalApiService _api;
    private readonly CloudSyncService _cloud;
    private readonly ExternalNotificationService _notifications;
    private readonly ProductCsvService _productCsv;
    private readonly Panel _sidebar;
    private readonly Panel _contentPanel;
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Button _alertsButton;
    private readonly ToolTip _alertToolTip = new();
    private readonly System.Windows.Forms.Timer _alertTimer;
    private readonly ActivityMessageFilter _activityFilter;
    private DateTime _lastActivity = DateTime.Now;
    private DateTime _lastActivityWrite = DateTime.MinValue;
    private string? _activePage;

    public MainForm(InventoryService inv, AuthService auth, CommerceService commerce,
        SecurityAdminService securityAdmin, DatabaseBackupService backups, AutomatedReorderService reorder,
        LocalApiService api, CloudSyncService cloud, ExternalNotificationService notifications, ProductCsvService productCsv)
    {
        _inventory = inv;
        _auth = auth;
        _commerce = commerce;
        _securityAdmin = securityAdmin;
        _backups = backups;
        _reorder = reorder;
        _api = api;
        _cloud = cloud;
        _notifications = notifications;
        _productCsv = productCsv;
        Text = $"Inventory Manager — {auth.CurrentUser?.FullName} ({auth.CurrentUser?.Role})";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(720, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9);
        IsMdiContainer = false;

        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 220,
            BackColor = SidebarColor,
            AutoScroll = true
        };
        var title = new Label
        {
            Text = "  INVENTORY",
            ForeColor = Color.White,
            Dock = DockStyle.Top,
            Height = 60,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var navigation = new[]
        {
            (Key: "dashboard", Text: "📊  Dashboard"),
            (Key: "products", Text: "📦  Products"),
            (Key: "categories", Text: "🗂  Categories"),
            (Key: "units", Text: "📏  Units"),
            (Key: "warehouses", Text: "🏢  Warehouses"),
            (Key: "stock", Text: "🔄  Stock In / Out"),
            (Key: "transfers", Text: "↔  Transfers"),
            (Key: "adjustments", Text: "⚖  Adjustments"),
            (Key: "transactions", Text: "📜  Transactions"),
            (Key: "barcodes", Text: "🏷  Barcodes / QR"),
            (Key: "purchasing", Text: "🛒  Purchasing"),
            (Key: "sales", Text: "💳  Sales / POS"),
            (Key: "reports", Text: "📈  Reports"),
            (Key: "security", Text: "🔐  Users & Admin"),
            (Key: "alerts", Text: "🔔  Alerts (0)")
        };

        foreach (var item in navigation.Reverse())
        {
            var button = MakeNavButton(item.Text);
            button.Click += (_, _) => Navigate(item.Key);
            button.AccessibleDescription = item.Text;
            _navButtons.Add(item.Key, button);
            _sidebar.Controls.Add(button);
        }
        _alertsButton = _navButtons["alerts"];
        var logoutButton = MakeNavButton("Sign out");
        logoutButton.Dock = DockStyle.Bottom;
        logoutButton.Click += (_, _) => Navigate("logout");
        _sidebar.Controls.Add(logoutButton);
        _sidebar.Controls.Add(title);
        foreach (var (key, button) in _navButtons)
            button.Visible = CanNavigate(key);

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        Controls.Add(_contentPanel);
        Controls.Add(_sidebar);

        _alertTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _alertTimer.Tick += (_, _) => CheckSession();
        _alertTimer.Start();
        _activityFilter = new ActivityMessageFilter(this);
        Application.AddMessageFilter(_activityFilter);
        Resize += (_, _) => UpdateResponsiveNavigation();
        UpdateResponsiveNavigation();
        FormClosed += (_, _) =>
        {
            Application.RemoveMessageFilter(_activityFilter);
            _alertTimer.Stop();
            _alertTimer.Dispose();
            _alertToolTip.Dispose();
            _auth.Logout();
            _api.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };

        Navigate("dashboard");
        RefreshAlerts();
    }

    private static Button MakeNavButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 45,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = SidebarColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(15, 0, 0, 0),
            Font = new Font("Segoe UI", 10)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void Navigate(string key)
    {
        if (key == "logout")
        {
            Close();
            return;
        }
        if (!CanNavigate(key)) throw new UnauthorizedAccessException("Your account does not have permission to open this page.");
        if (_activePage == key) return;

        var nextPage = CreatePage(key);
        nextPage.Dock = DockStyle.Fill;

        if (_contentPanel.Controls.Count > 0)
        {
            var currentPage = _contentPanel.Controls[0];
            _contentPanel.Controls.Remove(currentPage);
            currentPage.Dispose();
        }
        _contentPanel.Controls.Add(nextPage);
        _activePage = key;

        foreach (var (buttonKey, button) in _navButtons)
        {
            var active = buttonKey == key;
            button.BackColor = active ? ActiveColor : SidebarColor;
            button.ForeColor = buttonKey == "alerts" && !active && button.Tag is int criticalCount && criticalCount > 0
                ? Color.FromArgb(255, 120, 120)
                : Color.White;
        }

        if (key == "alerts") RefreshAlerts();
    }

    private UserControl CreatePage(string key) => key switch
    {
        "dashboard" => new DashboardPage(_inventory),
        "products" => new ProductPage(_inventory, _auth, _productCsv),
        "categories" => new CategoryPage(_inventory),
        "units" => new UnitPage(_inventory),
        "warehouses" => new WarehousePage(_inventory),
        "stock" => new TransactionPage(_inventory, _auth),
        "transfers" => new StockTransferPage(_inventory, _auth),
        "adjustments" => new StockAdjustmentPage(_inventory, _auth),
        "transactions" => new TransactionHistoryPage(_inventory),
        "barcodes" => new BarcodePage(_inventory),
        "purchasing" => new PurchasingPage(_inventory, _commerce, _auth),
        "sales" => new SalesPage(_inventory, _commerce, _auth),
        "reports" => new ReportsPage(_inventory, _commerce),
        "security" => new SecurityAdminPage(_auth, _securityAdmin, _backups, _reorder, _api, _cloud, _notifications, _inventory),
        "alerts" => new AlertsPage(_inventory),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown navigation page.")
    };

    private void RefreshAlerts()
    {
        try
        {
            var total = _inventory.Alerts.TotalCount();
            var critical = _inventory.Alerts.CriticalCount();
            _alertsButton.Text = $"🔔  Alerts ({total})";
            _alertsButton.Tag = critical;
            _alertsButton.ForeColor = _activePage == "alerts"
                ? Color.White
                : critical > 0 ? Color.FromArgb(255, 120, 120) : Color.White;
            _alertToolTip.SetToolTip(_alertsButton, string.Empty);
        }
        catch (Exception ex)
        {
            _alertsButton.Text = "🔔  Alerts (unavailable)";
            _alertsButton.Tag = 0;
            _alertToolTip.SetToolTip(_alertsButton, ex.Message);
        }
    }

    private bool CanNavigate(string key) => key switch
        {
            "dashboard" or "products" or "categories" or "units" or "warehouses" or "stock" or "transfers"
                or "adjustments" or "transactions" or "barcodes" or "alerts" =>
                _auth.HasPermission("inventory.read") || _auth.HasPermission("inventory.write"),
            "purchasing" or "sales" => _auth.HasPermission("commerce.read") || _auth.HasPermission("commerce.write"),
            "reports" => _auth.HasPermission("reports.read"),
            "security" => _auth.HasPermission("admin.manage") || _auth.HasPermission("users.manage")
                          || _auth.HasPermission("backup.manage") || _auth.HasPermission("audit.read"),
            _ => false
        };

        private void CheckSession()
        {
            if (_auth.CurrentUser == null)
            {
                Close();
                return;
            }
            try
            {
                if (!_auth.IsCurrentSessionActive())
                {
                    MessageBox.Show(this, "This session was ended by an administrator.", "Session ended",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Close();
                    return;
                }
                if (DateTime.Now - _lastActivity > TimeSpan.FromMinutes(30))
                {
                    MessageBox.Show(this, "You have been signed out after 30 minutes of inactivity.", "Session expired",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                    return;
                }
                if (DateTime.Now - _lastActivityWrite >= TimeSpan.FromMinutes(1))
                {
                    _auth.UpdateSessionActivity();
                    _lastActivityWrite = DateTime.Now;
                }
                RefreshAlerts();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, $"Unable to validate the current session: {exception.Message}", "Session validation failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void UpdateResponsiveNavigation()
        {
            var compact = ClientSize.Width < 920;
            _sidebar.Width = compact ? 76 : 220;
            foreach (var (key, button) in _navButtons)
            {
                var label = Convert.ToString(button.AccessibleDescription) ?? button.Text;
                button.Width = _sidebar.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                button.Padding = compact ? new Padding(0) : new Padding(15, 0, 0, 0);
                button.TextAlign = compact ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
                button.Text = compact ? key switch
                {
                    "dashboard" => "Home",
                    "products" => "SKU",
                    "categories" => "Cat",
                    "units" => "Unit",
                    "warehouses" => "Loc",
                    "stock" => "Stock",
                    "transfers" => "Move",
                    "adjustments" => "Adj",
                    "transactions" => "Log",
                    "barcodes" => "Scan",
                    "purchasing" => "Buy",
                    "sales" => "POS",
                    "reports" => "Rpt",
                    "security" => "Admin",
                    "alerts" => "Alert",
                    _ => key
                } : label;
                _alertToolTip.SetToolTip(button, compact ? label : string.Empty);
            }
        }

        private sealed class ActivityMessageFilter : IMessageFilter
        {
            private readonly MainForm _form;
            public ActivityMessageFilter(MainForm form) => _form = form;

            public bool PreFilterMessage(ref Message message)
            {
                if (message.Msg is >= 0x0100 and <= 0x0109 or >= 0x0200 and <= 0x020E)
                    _form._lastActivity = DateTime.Now;
                return false;
            }
        }
    }
