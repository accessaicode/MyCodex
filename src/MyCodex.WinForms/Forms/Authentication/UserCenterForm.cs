using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class UserCenterForm : Form
    {
        private readonly IAppServerAuthenticationClient _authenticationClient;
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IScreenReaderService _screenReaderService;
        private readonly ListBox _detailsListBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly ComboBox _resetCreditComboBox;
        private readonly Button _resetQuotaButton;
        private readonly Button _closeButton;
        private readonly Panel _mainPanel;
        private readonly Func<ProviderUsageForm> _createProviderUsageForm;
        private readonly Func<Task<bool>> _ensureConnection;
        private ProviderUsageForm _providerUsageForm;

        private AppServerAuthenticationState _authenticationState;
        private AppServerRateLimitsSnapshot _rateLimitsSnapshot;
        private AppServerAccountTokenUsageSnapshot _tokenUsageSnapshot;
        private bool _isRefreshing;
        private bool _isResettingQuota;

        internal AppServerAuthenticationState AuthenticationState => _authenticationState;

        internal UserCenterForm(IAppServerClient client, Func<ProviderUsageForm> createProviderUsageForm,
            Func<Task<bool>> ensureConnection, IScreenReaderService screenReaderService)
            : this(client, client, null, screenReaderService)
        {
            _createProviderUsageForm = createProviderUsageForm ?? throw new ArgumentNullException(nameof(createProviderUsageForm));
            _ensureConnection = ensureConnection ?? throw new ArgumentNullException(nameof(ensureConnection));
        }

        public UserCenterForm(
            IAppServerClient appServerClient,
            AppServerAuthenticationState initialAuthenticationState,
            IScreenReaderService screenReaderService = null)
            : this(appServerClient, appServerClient, initialAuthenticationState, screenReaderService)
        {
        }

        internal UserCenterForm(
            IAppServerAuthenticationClient authenticationClient,
            IAppServerConnectionClient connectionClient,
            AppServerAuthenticationState initialAuthenticationState)
            : this(authenticationClient, connectionClient, initialAuthenticationState, null)
        {
        }

        internal UserCenterForm(
            IAppServerAuthenticationClient authenticationClient,
            IAppServerConnectionClient connectionClient,
            AppServerAuthenticationState initialAuthenticationState,
            IScreenReaderService screenReaderService)
        {
            _authenticationClient = authenticationClient ?? throw new ArgumentNullException(nameof(authenticationClient));
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _screenReaderService = screenReaderService;
            _authenticationState = initialAuthenticationState;
            _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
            _tokenUsageSnapshot = null;

            Text = UiLocalization.Text("UserCenterForm.Title", "User Center");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 620);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _detailsListBox = CreateDetailsListBox();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            _refreshButton = new Button
            {
                Text = UiLocalization.Text("UserCenterForm.Button.Refresh", "&Refresh"),
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.Refresh", "Refresh"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            _resetCreditComboBox = CreateResetCreditComboBox();
            _resetQuotaButton = new Button
            {
                Text = UiLocalization.Text("UserCenterForm.ResetQuota.Unavailable", "Reset &quota (unavailable)"),
                AccessibleName = UiLocalization.Text("UserCenterForm.Action.ResetQuota", "Reset quota"),
                Width = 168,
                Height = 32,
                Enabled = false,
                UseMnemonic = true,
                TabIndex = 3
            };
            UiTheme.ApplyButton(_resetQuotaButton, UiGlyph.Refresh);
            _closeButton = new Button
            {
                Text = UiLocalization.Text("UserCenterForm.Button.Close", "&Close"),
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.Close", "Close"),
                Width = 96,
                Height = 32,
                DialogResult = DialogResult.Cancel,
                UseMnemonic = true,
                TabIndex = 4
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);

            _refreshButton.Click += OnRefreshButtonClick;
            _resetQuotaButton.Click += OnResetQuotaButtonClick;
            CancelButton = _closeButton;
            KeyDown += UserCenterForm_KeyDown;

            _mainPanel = BuildLayout();
            if (_authenticationState == null)
            {
                ShowLoadingAccount();
                _refreshButton.Enabled = false;
            }
            else RefreshDetailsList();
            RefreshResetCreditSelector();
            RefreshResetQuotaButton();
            Shown += OnShown;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            this.BeginInvokeSafe(FocusDetailsList);
            await RefreshDataAsync();
        }

        private Panel BuildLayout()
        {
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var titleLabel = new Label
            {
                Text = UiLocalization.Text("UserCenterForm.Header", "User Information"),
                Dock = DockStyle.Top,
                Height = 32,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 680,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            buttonsPanel.Controls.Add(_refreshButton);
            buttonsPanel.Controls.Add(_resetCreditComboBox);
            buttonsPanel.Controls.Add(_resetQuotaButton);
            buttonsPanel.Controls.Add(_closeButton);
            footerPanel.Controls.Add(buttonsPanel);
            footerPanel.Controls.Add(_statusLabel);

            mainPanel.Controls.Add(_detailsListBox);
            mainPanel.Controls.Add(footerPanel);
            mainPanel.Controls.Add(titleLabel);

            Controls.Add(mainPanel);
            return mainPanel;
        }

        private async void OnRefreshButtonClick(object sender, EventArgs e)
        {
            await RefreshDataAsync();
        }

        private async void OnResetQuotaButtonClick(object sender, EventArgs e)
        {
            await ResetQuotaAsync();
        }

        private async void UserCenterForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && !_isRefreshing && !_isResettingQuota)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await RefreshDataAsync();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async Task RefreshDataAsync()
        {
            if (_isRefreshing || _isResettingQuota || IsDisposed || Disposing) return;
            if (_providerUsageForm != null)
            {
                await _providerUsageForm.RefreshAsync(false);
                return;
            }
            _isRefreshing = true;
            RefreshActionButtons();
            ShowLoadingAccount();
            try
            {
                if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("UserCenterForm.Action.RefreshUserInfo", "Refresh user info")))
                {
                    if (!IsDisposed && !Disposing) ShowAccountLoadFailure();
                    return;
                }
                if (IsDisposed || Disposing) return;
                var state = await _authenticationClient.GetAuthenticationStateAsync(false);
                if (IsDisposed || Disposing) return;
                _authenticationState = state;
                if (!IsChatGptAuthActive(state))
                {
                    if (_createProviderUsageForm != null)
                    {
                        ShowProviderUsage();
                        return;
                    }
                    _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
                    _tokenUsageSnapshot = null;
                    RefreshDetailsList();
                    SetStatusAndSpeak(UiLocalization.Text("UserCenterForm.Status.UnavailableForApi", "User Center is only available when signed in with a ChatGPT account."));
                    return;
                }
                try
                {
                    _rateLimitsSnapshot = await _authenticationClient.GetRateLimitsAsync();
                }
                catch (Exception ex)
                {
                    if (IsDisposed || Disposing) return;
                    Trace.TraceWarning("Failed to refresh user center rate limits: {0}", ex.Message);
                    _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
                    _tokenUsageSnapshot = null;
                    RefreshDetailsList();
                    SetStatusAndSpeak(UiLocalization.Text("UserCenterForm.Status.RateLimitsUnavailable", "User info refreshed. Quota information is temporarily unavailable."));
                    return;
                }
                if (IsDisposed || Disposing) return;
                try
                {
                    _tokenUsageSnapshot = await _authenticationClient.GetAccountTokenUsageAsync();
                }
                catch (Exception ex)
                {
                    if (IsDisposed || Disposing) return;
                    Trace.TraceWarning("Failed to refresh user center token usage: {0}", ex.Message);
                    _tokenUsageSnapshot = null;
                }
                if (IsDisposed || Disposing) return;
                RefreshDetailsList();
                SetStatus(UiLocalization.Text("UserCenterForm.Status.RefreshCompleted", "Refresh completed."));
            }
            catch (Exception)
            {
                if (!IsDisposed && !Disposing) ShowAccountLoadFailure();
            }
            finally
            {
                _isRefreshing = false;
                if (!IsDisposed && !Disposing)
                {
                    RefreshResetCreditSelector();
                    RefreshActionButtons();
                }
            }
        }

        private void ShowLoadingAccount()
        {
            ShowAccountMessage(UiLocalization.Text("UserCenterForm.Status.LoadingAccount", "Loading information…"));
        }

        private void ShowAccountLoadFailure()
        {
            ShowAccountMessage(UiLocalization.Text("UserCenterForm.Status.AccountLoadFailed", "Account information could not be loaded. Refresh to try again."));
        }

        private void ShowAccountMessage(string message)
        {
            SetStatus(message);
            _detailsListBox.Items.Clear();
            _detailsListBox.Items.Add(message);
            _detailsListBox.SelectedIndex = 0;
        }

        private void ShowProviderUsage()
        {
            _providerUsageForm = _createProviderUsageForm();
            _providerUsageForm.TopLevel = false;
            _providerUsageForm.FormBorderStyle = FormBorderStyle.None;
            _providerUsageForm.Dock = DockStyle.Fill;
            var close = (Button)_providerUsageForm.CancelButton;
            close.Click += (sender, args) => Close();
            CancelButton = close;
            _mainPanel.Hide();
            Controls.Add(_providerUsageForm);
            _providerUsageForm.Show();
        }

        private void RefreshDetailsList()
        {
            var selectedIndex = _detailsListBox.SelectedIndex;
            var lines = UserCenterDetailsFormatter.BuildDetailLines(
                _authenticationState,
                _rateLimitsSnapshot,
                _tokenUsageSnapshot,
                DateTime.Now);

            _detailsListBox.BeginUpdate();
            _detailsListBox.Items.Clear();
            foreach (var line in lines)
            {
                _detailsListBox.Items.Add(line);
            }

            if (_detailsListBox.Items.Count > 0)
            {
                if (selectedIndex >= 0 && selectedIndex < _detailsListBox.Items.Count)
                {
                    _detailsListBox.SelectedIndex = selectedIndex;
                }
                else
                {
                    _detailsListBox.SelectedIndex = 0;
                }
            }

            _detailsListBox.EndUpdate();
        }

        private void RefreshActionButtons()
        {
            _refreshButton.Enabled = !_isRefreshing && !_isResettingQuota;
            RefreshResetQuotaButton();
        }

        private void FocusDetailsList()
        {
            if (_detailsListBox.Items.Count > 0 && _detailsListBox.SelectedIndex < 0)
            {
                _detailsListBox.SelectedIndex = 0;
            }

            _detailsListBox.Focus();
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void SetStatusAndSpeak(string text)
        {
            SetStatus(text);
            _screenReaderService?.Speak(text, true, false, true);
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)
        {
            try
            {
                if (_isRefreshing && _ensureConnection != null) return await _ensureConnection();
                await _connectionClient.ConnectAsync();
                return true;
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return false;
                if (_isRefreshing)
                {
                    ShowAccountLoadFailure();
                    return false;
                }
                SetStatusAndSpeak(string.Format(
                    UiLocalization.Text(
                        "UserCenterForm.Status.ActionFailedDisconnected",
                        "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static ListBox CreateDetailsListBox()
        {
            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.UserInfo", "User Information"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                ItemHeight = 28,
                TabIndex = 0
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static ComboBox CreateResetCreditComboBox()
        {
            var comboBox = new ThemedComboBox
            {
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.ResetCredit", "Reset credit"),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 320,
                TabIndex = 2
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        internal static bool IsChatGptAuthActive(AppServerAuthenticationState state)
        {
            return UserCenterDetailsFormatter.IsChatGptAuthActive(state);
        }
    }
}
