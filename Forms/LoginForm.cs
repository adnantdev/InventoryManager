using InventoryManager.Services;

namespace InventoryManager.Forms;

public sealed class LoginForm : Form
{
    private readonly AuthService _auth;
    private readonly bool _setupMode;
    private TextBox _username = null!;
    private TextBox _password = null!;
    private TextBox _fullName = null!;
    private TextBox _confirmPassword = null!;

    public LoginForm(AuthService auth)
    {
        _auth = auth;
        _setupMode = !auth.HasUsers();
        Text = _setupMode ? "Create administrator - Inventory Manager" : "Login - Inventory Manager";
        ClientSize = new Size(440, _setupMode ? 390 : 260);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            ColumnCount = 2,
            RowCount = _setupMode ? 7 : 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = _setupMode ? "First-run administrator setup" : "Inventory Manager sign in",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        if (_setupMode)
        {
            AddInput(layout, 1, "Username", out _username);
            AddInput(layout, 2, "Full name", out _fullName);
            AddInput(layout, 3, "Password", out _password, true);
            AddInput(layout, 4, "Confirm password", out _confirmPassword, true);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            var hint = new Label
            {
                Text = "Use at least 12 characters, including uppercase, lowercase, number, and symbol.",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleCenter
            };
            layout.Controls.Add(hint, 0, 5);
            layout.SetColumnSpan(hint, 2);
        }
        else
        {
            AddInput(layout, 1, "Username", out _username);
            AddInput(layout, 2, "Password", out _password, true);
        }

        var submit = new Button
        {
            Text = _setupMode ? "Create administrator" : "Sign in",
            Dock = DockStyle.Fill,
            Height = 38,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        submit.FlatAppearance.BorderSize = 0;
        var submitRow = _setupMode ? 6 : 3;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(submit, 0, submitRow);
        layout.SetColumnSpan(submit, 2);
        submit.Click += (_, _) => SignInOrCreate();
        Controls.Add(layout);
        AcceptButton = submit;
    }

    private static void AddInput(TableLayoutPanel layout, int row, string label, out TextBox input, bool password = false)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        input = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = password };
        layout.Controls.Add(input, 1, row);
    }

    private void SignInOrCreate()
    {
        try
        {
            if (_setupMode)
            {
                if (_password.Text != _confirmPassword.Text) throw new ArgumentException("Passwords do not match.");
                _auth.SetupFirstAdministrator(_username.Text, _fullName.Text, _password.Text);
            }
            else if (!_auth.Login(_username.Text.Trim(), _password.Text))
            {
                MessageBox.Show(this, "Invalid credentials, or this account is inactive.", "Sign in failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_auth.CurrentUser?.MustChangePassword == true && !PromptPasswordChange())
            {
                _auth.Logout();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Sign in failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool PromptPasswordChange()
    {
        using var dialog = new Form
        {
            Text = "Change temporary password",
            ClientSize = new Size(430, 220),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var current = new TextBox { Left = 155, Top = 20, Width = 240, UseSystemPasswordChar = true };
        var next = new TextBox { Left = 155, Top = 58, Width = 240, UseSystemPasswordChar = true };
        var confirm = new TextBox { Left = 155, Top = 96, Width = 240, UseSystemPasswordChar = true };
        dialog.Controls.AddRange(new Control[]
        {
            new Label { Text = "Current password", Left = 20, Top = 23, AutoSize = true }, current,
            new Label { Text = "New password", Left = 20, Top = 61, AutoSize = true }, next,
            new Label { Text = "Confirm", Left = 20, Top = 99, AutoSize = true }, confirm
        });
        var save = new Button { Text = "Change password", Left = 155, Top = 145, Width = 140 };
        var cancel = new Button { Text = "Cancel", Left = 305, Top = 145, Width = 90, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try
            {
                if (next.Text != confirm.Text) throw new ArgumentException("Passwords do not match.");
                _auth.ChangePassword(current.Text, next.Text);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(dialog, exception.Message, "Password change failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        dialog.Controls.AddRange(new Control[] { save, cancel });
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
