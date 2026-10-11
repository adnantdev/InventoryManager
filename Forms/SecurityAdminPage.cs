using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public sealed class SecurityAdminPage : UserControl
{
    private readonly AuthService _auth;
    private readonly SecurityAdminService _admin;
    private readonly DatabaseBackupService _backups;
    private readonly AutomatedReorderService _reorder;
    private readonly LocalApiService _api;
    private readonly CloudSyncService _cloud;
    private readonly ExternalNotificationService _notifications;
    private readonly DataGridView _users = CommerceVisuals.Grid();
    private readonly DataGridView _sessions = CommerceVisuals.Grid();
    private readonly DataGridView _audit = CommerceVisuals.Grid();
    private readonly DataGridView _branches = CommerceVisuals.Grid();
    private readonly DataGridView _reorders = CommerceVisuals.Grid();
    private readonly ComboBox _role = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckedListBox _permissions = new() { CheckOnClick = true, Dock = DockStyle.Fill };
    private readonly ComboBox _warehouse = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly NumericUpDown _apiPort = new() { Minimum = 1024, Maximum = 65535, Value = 5080, Width = 100 };
    private readonly Label _apiState = new() { AutoSize = true, Padding = new Padding(5, 10, 0, 0) };
    private readonly InventoryService _inventory;

    public SecurityAdminPage(AuthService auth, SecurityAdminService admin, DatabaseBackupService backups,
        AutomatedReorderService reorder, LocalApiService api, CloudSyncService cloud,
        ExternalNotificationService notifications, InventoryService inventory)
    {
        _auth = auth;
        _admin = admin;
        _backups = backups;
        _reorder = reorder;
        _api = api;
        _cloud = cloud;
        _notifications = notifications;
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = CommerceVisuals.Canvas;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        if (_auth.HasPermission("users.manage"))
        {
            tabs.TabPages.Add(BuildUsersTab());
            RefreshUsers();
        }
        if (_auth.HasPermission("admin.manage"))
        {
            tabs.TabPages.Add(BuildRolesTab());
            tabs.TabPages.Add(BuildSessionsTab());
            tabs.TabPages.Add(BuildBranchesTab());
            tabs.TabPages.Add(BuildAutomationTab());
            tabs.TabPages.Add(BuildIntegrationsTab());
            RefreshSessions();
            RefreshBranches();
            RefreshPolicies();
            UpdateApiState();
        }
        if (_auth.HasPermission("audit.read"))
        {
            tabs.TabPages.Add(BuildAuditTab());
            RefreshAudit();
        }
        if (_auth.HasPermission("backup.manage"))
            tabs.TabPages.Add(BuildBackupTab());
        Controls.Add(tabs);
    }

    private TabPage BuildUsersTab()
    {
        var page = new TabPage("Users");
        var panel = new Panel { Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        toolbar.Controls.Add(CommerceVisuals.Button("Add user", (_, _) => Run(AddUser)));
        toolbar.Controls.Add(CommerceVisuals.Button("Edit / reset password", (_, _) => Run(EditUser)));
        toolbar.Controls.Add(CommerceVisuals.Button("Activate / deactivate", (_, _) => Run(ToggleUser)));
        toolbar.Controls.Add(CommerceVisuals.Button("Refresh", (_, _) => Run(RefreshUsers)));
        panel.Controls.Add(_users);
        panel.Controls.Add(toolbar);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildRolesTab()
    {
        var page = new TabPage("Roles & permissions");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        toolbar.Controls.Add(new Label { Text = "Role", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        toolbar.Controls.Add(_role);
        toolbar.Controls.Add(CommerceVisuals.Button("New role", (_, _) => Run(CreateRole)));
        var permissionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        permissionPanel.Controls.Add(_permissions);
        var save = CommerceVisuals.Button("Save permissions", (_, _) => Run(SaveRole));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(permissionPanel, 0, 1);
        root.Controls.Add(save, 0, 2);
        page.Controls.Add(root);
        _permissions.Items.AddRange(AuthService.AvailablePermissions.Cast<object>().ToArray());
        _role.SelectedIndexChanged += (_, _) => LoadRolePermissions();
        RefreshRoles();
        return page;
    }

    private TabPage BuildSessionsTab()
    {
        var page = new TabPage("Sessions");
        var panel = new Panel { Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        toolbar.Controls.Add(CommerceVisuals.Button("End selected session", (_, _) => Run(EndSelectedSession)));
        toolbar.Controls.Add(CommerceVisuals.Button("Refresh", (_, _) => Run(RefreshSessions)));
        panel.Controls.Add(_sessions);
        panel.Controls.Add(toolbar);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildAuditTab()
    {
        var page = new TabPage("Activity log");
        var panel = new Panel { Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        toolbar.Controls.Add(CommerceVisuals.Button("Refresh audit log", (_, _) => Run(RefreshAudit)));
        panel.Controls.Add(_audit);
        panel.Controls.Add(toolbar);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildBackupTab()
    {
        var page = new TabPage("Backup & restore");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(18), AutoSize = true };
        panel.Controls.Add(new Label
        {
            Text = "Backups use SQLite's online backup API. Restore first creates a safety backup beside the live database.",
            AutoSize = true,
            Margin = new Padding(5, 12, 12, 12)
        });
        panel.Controls.Add(CommerceVisuals.Button("Create backup", (_, _) => Run(CreateBackup)));
        panel.Controls.Add(CommerceVisuals.Button("Restore backup", (_, _) => Run(RestoreBackup)));
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildBranchesTab()
    {
        var page = new TabPage("Branches / stores");
        var panel = new Panel { Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
        toolbar.Controls.Add(CommerceVisuals.Button("Add branch", (_, _) => Run(() => EditBranch(null))));
        toolbar.Controls.Add(CommerceVisuals.Button("Edit selected", (_, _) => Run(() => EditBranch(Selected<Branch>(_branches)))));
        toolbar.Controls.Add(CommerceVisuals.Button("Refresh", (_, _) => Run(RefreshBranches)));
        var assign = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        assign.Controls.Add(new Label { Text = "Warehouse", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        assign.Controls.Add(_warehouse);
        assign.Controls.Add(new Label { Text = "Branch", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        assign.Controls.Add(_branch);
        assign.Controls.Add(CommerceVisuals.Button("Assign", (_, _) => Run(AssignBranch)));
        panel.Controls.Add(_branches);
        panel.Controls.Add(toolbar);
        panel.Controls.Add(assign);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildAutomationTab()
    {
        var page = new TabPage("Automatic reorder levels");
        var panel = new Panel { Dock = DockStyle.Fill };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
        toolbar.Controls.Add(CommerceVisuals.Button("Configure selected", (_, _) => Run(EditReorderPolicy)));
        toolbar.Controls.Add(CommerceVisuals.Button("Recalculate enabled", (_, _) => Run(RecalculateReorder)));
        toolbar.Controls.Add(CommerceVisuals.Button("Refresh", (_, _) => Run(RefreshPolicies)));
        panel.Controls.Add(_reorders);
        panel.Controls.Add(toolbar);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildIntegrationsTab()
    {
        var page = new TabPage("API & notifications");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var apiPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        apiPanel.Controls.Add(new Label { Text = "Local API port", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        apiPanel.Controls.Add(_apiPort);
        apiPanel.Controls.Add(CommerceVisuals.Button("Generate API key", (_, _) => Run(GenerateApiToken)));
        apiPanel.Controls.Add(CommerceVisuals.Button("Start / stop API", (_, _) => Run(ToggleApi)));
        apiPanel.Controls.Add(_apiState);
        var info = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Text = "API is restricted to this PC (127.0.0.1). GET /api/health is public; GET /api/products requires Authorization: Bearer <key>.\n\n" +
                   "Email environment variables: INVENTORY_SMTP_HOST, INVENTORY_SMTP_PORT, INVENTORY_SMTP_USER, INVENTORY_SMTP_PASSWORD, INVENTORY_SMTP_FROM, INVENTORY_ALERT_EMAIL.\n" +
                   "WhatsApp Cloud API variables: INVENTORY_WHATSAPP_PHONE_NUMBER_ID, INVENTORY_WHATSAPP_ACCESS_TOKEN, INVENTORY_WHATSAPP_TO.\n" +
                   "Secrets are read from environment variables and are not saved in the inventory database."
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46 };
        buttons.Controls.Add(CommerceVisuals.Button("Test email", async (_, _) => await RunAsync(() => _notifications.SendTestEmailAsync())));
        buttons.Controls.Add(CommerceVisuals.Button("Test WhatsApp", async (_, _) => await RunAsync(() => _notifications.SendTestWhatsAppAsync())));
        buttons.Controls.Add(CommerceVisuals.Button("Cloud push backup", (_, _) => Run(CloudPush)));
        buttons.Controls.Add(CommerceVisuals.Button("Cloud pull & restore", (_, _) => Run(CloudPull)));
        var detailPanel = new Panel { Dock = DockStyle.Fill };
        detailPanel.Controls.Add(info);
        detailPanel.Controls.Add(buttons);
        root.Controls.Add(apiPanel, 0, 0);
        root.Controls.Add(detailPanel, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private void AddUser()
    {
        var values = CommerceDialogs.Prompt(FindForm(), "Create user",
            ("Username", ""), ("Full name", ""), ("Role", "Staff"), ("Initial password", ""));
        if (values == null) return;
        _auth.SaveUser(0, values[0], values[1], values[2], values[3]);
        RefreshUsers();
    }

    private void EditUser()
    {
        var user = Selected<User>(_users) ?? throw new InvalidOperationException("Select a user.");
        var values = CommerceDialogs.Prompt(FindForm(), $"Edit {user.Username}",
            ("Username", user.Username), ("Full name", user.FullName), ("Role", user.Role), ("New password (optional)", ""));
        if (values == null) return;
        _auth.SaveUser(user.Id, values[0], values[1], values[2], values[3]);
        RefreshUsers();
    }

    private void ToggleUser()
    {
        var user = Selected<User>(_users) ?? throw new InvalidOperationException("Select a user.");
        _auth.SetUserActive(user.Id, !user.IsActive);
        RefreshUsers();
    }

    private void CreateRole()
    {
        var values = CommerceDialogs.Prompt(FindForm(), "Create role", ("Role name", ""));
        if (values == null) return;
        if (string.IsNullOrWhiteSpace(values[0])) throw new ArgumentException("Enter a role name.");
        _auth.SetRolePermissions(values[0], Array.Empty<string>());
        RefreshRoles(values[0]);
    }

    private void SaveRole()
    {
        var role = _role.SelectedItem as string ?? throw new InvalidOperationException("Select a role.");
        var selected = _permissions.CheckedItems.Cast<string>().ToArray();
        _auth.SetRolePermissions(role, selected);
        RefreshRoles(role);
    }

    private void LoadRolePermissions()
    {
        if (_role.SelectedItem is not string role) return;
        var selected = _auth.GetRolePermissions(role);
        for (var index = 0; index < _permissions.Items.Count; index++)
            _permissions.SetItemChecked(index, selected.Contains(Convert.ToString(_permissions.Items[index]) ?? string.Empty));
        _permissions.Enabled = !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private void EndSelectedSession()
    {
        var session = Selected<SessionEntry>(_sessions) ?? throw new InvalidOperationException("Select a session.");
        if (!session.IsActive) throw new InvalidOperationException("The selected session is not active.");
        if (session.SessionId == _auth.CurrentUser?.SessionId)
            throw new InvalidOperationException("Use Sign out to end your own session.");
        if (!CommerceDialogs.Confirm(FindForm(), $"End the active session for {session.Username} on {session.MachineName}?")) return;
        _admin.EndSession(session.SessionId);
        RefreshSessions();
    }

    private void CreateBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "SQLite database backup (*.db)|*.db",
            DefaultExt = "db",
            AddExtension = true,
            FileName = $"inventory-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _backups.CreateBackup(dialog.FileName);
        MessageBox.Show(FindForm(), "Backup created.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RestoreBackup()
    {
        using var dialog = new OpenFileDialog { Filter = "SQLite database backup (*.db)|*.db|All files (*.*)|*.*" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (!CommerceDialogs.Confirm(FindForm(), "Restore this database? The current data will be replaced after a safety backup is created.")) return;
        var safetyBackup = _backups.RestoreBackup(dialog.FileName);
        MessageBox.Show(FindForm(), $"Backup restored. Safety copy: {safetyBackup}\nRestart the application to refresh all open pages.",
            "Restore complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void EditBranch(Branch? selected)
    {
        var values = CommerceDialogs.Prompt(FindForm(), selected == null ? "Add branch" : "Edit branch",
            ("Code", selected?.Code ?? ""), ("Name", selected?.Name ?? ""),
            ("Address", selected?.Address ?? ""), ("Phone", selected?.Phone ?? ""));
        if (values == null) return;
        _admin.SaveBranch(new Branch
        {
            Id = selected?.Id ?? 0, Code = values[0], Name = values[1], Address = values[2], Phone = values[3],
            IsActive = selected?.IsActive ?? true
        });
        RefreshBranches();
    }

    private void AssignBranch()
    {
        var warehouse = _warehouse.SelectedItem as Warehouse ?? throw new InvalidOperationException("Select a warehouse.");
        var branch = _branch.SelectedItem as Branch;
        _admin.SetWarehouseBranch(warehouse.Id, branch?.Id);
    }

    private void EditReorderPolicy()
    {
        var policy = Selected<ReorderPolicy>(_reorders) ?? throw new InvalidOperationException("Select a product.");
        var values = CommerceDialogs.Prompt(FindForm(), $"Automatic reorder: {policy.ProductName}",
            ("Enabled (yes/no)", policy.AutoReorderEnabled ? "yes" : "no"),
            ("Lead days", policy.LeadDays.ToString()), ("Safety days", policy.SafetyDays.ToString()));
        if (values == null) return;
        if (!int.TryParse(values[1], out var lead) || !int.TryParse(values[2], out var safety))
            throw new FormatException("Lead and safety days must be whole numbers.");
        policy.AutoReorderEnabled = values[0].Equals("yes", StringComparison.OrdinalIgnoreCase)
            || values[0].Equals("true", StringComparison.OrdinalIgnoreCase);
        policy.LeadDays = lead;
        policy.SafetyDays = safety;
        _reorder.SavePolicy(policy);
        RefreshPolicies();
    }

    private void RecalculateReorder()
    {
        var count = _reorder.RecalculateEnabledPolicies();
        RefreshPolicies();
        MessageBox.Show(FindForm(), $"Updated reorder levels for {count} products using sales from the last 30 days.",
            "Reorder levels", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void GenerateApiToken()
    {
        var token = _api.GenerateToken();
        MessageBox.Show(FindForm(), $"Copy and save this API key now. It will not be shown again.\n\n{token}",
            "Local API key", MessageBoxButtons.OK, MessageBoxIcon.Information);
        UpdateApiState();
    }

    private void ToggleApi()
    {
        if (_api.IsRunning) _api.StopAsync().GetAwaiter().GetResult();
        else _api.Start(decimal.ToInt32(_apiPort.Value));
        UpdateApiState();
    }

    private void CloudPush()
    {
        var (endpoint, token) = PromptCloudConfig();
        _cloud.PushBackupAsync(endpoint, token).GetAwaiter().GetResult();
        MessageBox.Show(FindForm(), "Cloud backup uploaded.", "Cloud synchronization", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CloudPull()
    {
        var (endpoint, token) = PromptCloudConfig();
        if (!CommerceDialogs.Confirm(FindForm(), "Download and restore the cloud database? A local safety backup will be created.")) return;
        var safety = _cloud.PullAndRestoreAsync(endpoint, token).GetAwaiter().GetResult();
        MessageBox.Show(FindForm(), $"Cloud database restored. Safety copy: {safety}", "Cloud synchronization",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private (Uri endpoint, string token) PromptCloudConfig()
    {
        var values = CommerceDialogs.Prompt(FindForm(), "Cloud sync endpoint",
            ("HTTPS endpoint", ""), ("Bearer token", ""));
        if (values == null) throw new OperationCanceledException("Cloud sync was cancelled.");
        if (!Uri.TryCreate(values[0], UriKind.Absolute, out var endpoint))
            throw new FormatException("Enter a valid absolute HTTPS endpoint.");
        _admin.SetSetting("CloudSyncEndpoint", endpoint.ToString());
        return (endpoint, values[1]);
    }

    private void RefreshUsers() => _users.DataSource = _auth.GetUsers();

    private void RefreshRoles(string? selectedRole = null)
    {
        var roles = _auth.GetRoles();
        _role.DataSource = null;
        _role.Items.Clear();
        _role.Items.AddRange(roles.Cast<object>().ToArray());
        if (_role.Items.Count == 0)
        {
            _role.Enabled = false;
            _permissions.Enabled = false;
            return;
        }

        _role.Enabled = true;
        _role.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selectedRole))
        {
            var requestedIndex = _role.FindStringExact(selectedRole);
            if (requestedIndex >= 0 && requestedIndex < _role.Items.Count)
                _role.SelectedIndex = requestedIndex;
        }
    }

    private void RefreshSessions()
    {
        _sessions.DataSource = _admin.GetSessions();
        HideColumns(_sessions, "SessionId", "UserId");
    }

    private void RefreshAudit() => _audit.DataSource = _admin.GetAuditEntries();

    private void RefreshBranches()
    {
        _branches.DataSource = _admin.GetBranches();
        HideColumns(_branches, "Id", "IsActive");
        _warehouse.DataSource = _inventory.Warehouses.GetAll();
        _branch.DataSource = _admin.GetBranches().Where(branch => branch.IsActive).ToList();
    }

    private void RefreshPolicies() => _reorders.DataSource = _reorder.GetPolicies();

    private void UpdateApiState()
    {
        _apiState.Text = _api.IsRunning ? $"Running at http://127.0.0.1:{_api.Port}/" : "Stopped";
        _apiState.ForeColor = _api.IsRunning ? Color.DarkGreen : Color.Gray;
    }

    private static void HideColumns(DataGridView grid, params string[] names)
    {
        foreach (var name in names)
            if (grid.Columns.Contains(name)) grid.Columns[name].Visible = false;
    }

    private static T? Selected<T>(DataGridView grid) where T : class => grid.CurrentRow?.DataBoundItem as T;

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception exception) { CommerceDialogs.ShowError(FindForm(), exception); }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            MessageBox.Show(FindForm(), "Notification sent successfully.", "Integration test",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { CommerceDialogs.ShowError(FindForm(), exception); }
    }
}
