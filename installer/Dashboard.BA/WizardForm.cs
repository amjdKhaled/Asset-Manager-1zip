// WizardForm.cs
// 7-page setup wizard for the Dashboard managed bootstrapper application.
//
// PAGES (by PAGE_* constant):
//   0  Welcome      -- product overview and prerequisites notice
//   1  Detection    -- auto-scans for IIS, ASP.NET Core 8, WebView2, LF clients
//   2  Config       -- Dashboard URL, Laserfiche API URL, Repository, Port
//   3  Integration  -- checkboxes for Desktop Extension and Web Client button
//   4  Ready        -- summary before installing
//   5  Progress     -- progress bar and log during installation
//   6  Complete     -- success or failure message
//
// All controls are created programmatically (no Designer.cs).
// All UI mutations happen on the UI thread; engine callbacks use BeginInvoke.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Dashboard.BA
{
    internal sealed class WizardForm : Form
    {
        // ---------------------------------------------------------------- State
        private readonly DashboardBA _ba;
        private readonly InstallConfig _config  = new InstallConfig();
        private DetectionResult _detection       = new DetectionResult();
        private bool _detectionDone              = false;
        private bool _installSuccess             = false;
        private string _installMessage           = "";
        private int _pageIndex                   = 0;

        // ---------------------------------------------------------------- Pages
        private const int PAGE_WELCOME     = 0;
        private const int PAGE_DETECTION   = 1;
        private const int PAGE_CONFIG      = 2;
        private const int PAGE_INTEGRATION = 3;
        private const int PAGE_READY       = 4;
        private const int PAGE_PROGRESS    = 5;
        private const int PAGE_COMPLETE    = 6;

        private readonly Panel[] _pages = new Panel[7];

        // ---------------------------------------------------------------- Layout controls
        private Panel  _headerPanel;
        private Label  _lblHeaderTitle;
        private Label  _lblHeaderSubtitle;
        private Panel  _contentPanel;
        private Panel  _footerPanel;
        private Button _btnBack;
        private Button _btnNext;
        private Button _btnCancel;

        // ---------------------------------------------------------------- Detection page controls
        private Label  _lblDetectStatus;
        private Label  _lblIisStatus;
        private Label  _lblAspNetStatus;
        private Label  _lblWebView2Status;
        private Label  _lblDesktopStatus;
        private Label  _lblWebClientStatus;
        private Button _btnReDetect;

        // ---------------------------------------------------------------- Config page controls
        private TextBox _txtDashboardUrl;
        private TextBox _txtLFApiUrl;
        private TextBox _txtRepoId;
        private TextBox _txtDisplayName;
        private TextBox _txtPort;

        // ---------------------------------------------------------------- Integration page controls
        private CheckBox _chkDesktop;
        private Label    _lblDesktopInfo;
        private CheckBox _chkWebClient;
        private Label    _lblWebClientInfo;
        private TextBox  _txtWebClientPath;
        private Panel    _pnlWebClientPath;

        // ---------------------------------------------------------------- Ready page controls
        private Label _lblReadySummary;

        // ---------------------------------------------------------------- Progress page controls
        private ProgressBar _progressBar;
        private Label       _lblCurrentAction;
        private TextBox     _txtLog;

        // ---------------------------------------------------------------- Complete page controls
        private Label _lblCompleteTitle;
        private Label _lblCompleteDetail;

        // ---------------------------------------------------------------- Page metadata
        private static readonly string[] PageTitles =
        {
            "Welcome to Laserfiche Dashboard Setup",
            "Checking Your System",
            "Dashboard Configuration",
            "Integration Options",
            "Ready to Install",
            "Installing Dashboard...",
            "Setup Complete"
        };
        private static readonly string[] PageSubtitles =
        {
            "This wizard will install and configure Laserfiche Dashboard.",
            "The installer is scanning for required components.",
            "Enter the server addresses and repository details.",
            "Choose which Laserfiche components to integrate.",
            "Review your choices, then click Install to begin.",
            "Please wait while Dashboard is being configured.",
            ""
        };

        // ================================================================
        // Constructor and layout
        // ================================================================

        public WizardForm(DashboardBA ba)
        {
            _ba = ba;
            _ba.ProgressUpdated += OnProgressUpdated;
            _ba.InstallFinished += OnInstallFinished;
            BuildForm();
        }

        private void BuildForm()
        {
            // Form
            Text            = "Laserfiche Dashboard Setup";
            Size            = new Size(620, 490);
            MinimumSize     = new Size(620, 490);
            MaximumSize     = new Size(620, 490);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = Color.White;
            Font            = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // Header (60 px, dark blue)
            _headerPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 60,
                BackColor = Color.FromArgb(0, 62, 134)
            };
            _lblHeaderTitle = new Label
            {
                Text      = PageTitles[0],
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize  = true,
                Location  = new Point(16, 8)
            };
            _lblHeaderSubtitle = new Label
            {
                Text      = PageSubtitles[0],
                ForeColor = Color.FromArgb(180, 210, 240),
                Font      = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                AutoSize  = false,
                Size      = new Size(588, 20),
                Location  = new Point(16, 36)
            };
            _headerPanel.Controls.Add(_lblHeaderTitle);
            _headerPanel.Controls.Add(_lblHeaderSubtitle);

            // Header separator
            var hdrLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(0, 40, 100) };

            // Footer (52 px, light gray)
            _footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(240, 240, 240) };
            var ftrLine  = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(210, 210, 210) };

            _btnCancel = MakeButton("Cancel", 88);
            _btnNext   = MakeButton("Next >", 88);
            _btnBack   = MakeButton("< Back", 80);

            // Right-align buttons: Cancel | Next | Back
            _btnCancel.Location = new Point(620 - 88 - 12, 14);
            _btnNext.Location   = new Point(620 - 88 - 88 - 12 - 6, 14);
            _btnBack.Location   = new Point(620 - 88 - 88 - 80 - 12 - 12, 14);

            _footerPanel.Controls.AddRange(new Control[] { _btnBack, _btnNext, _btnCancel });

            // Content (fills middle)
            _contentPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.White,
                Padding   = new Padding(24, 20, 24, 12)
            };

            // Add controls to form (dock order: top-down, bottom-up)
            Controls.Add(_contentPanel);
            Controls.Add(ftrLine);
            Controls.Add(_footerPanel);
            Controls.Add(hdrLine);
            Controls.Add(_headerPanel);

            // Build pages
            _pages[PAGE_WELCOME]     = CreateWelcomePage();
            _pages[PAGE_DETECTION]   = CreateDetectionPage();
            _pages[PAGE_CONFIG]      = CreateConfigPage();
            _pages[PAGE_INTEGRATION] = CreateIntegrationPage();
            _pages[PAGE_READY]       = CreateReadyPage();
            _pages[PAGE_PROGRESS]    = CreateProgressPage();
            _pages[PAGE_COMPLETE]    = CreateCompletePage();

            foreach (var page in _pages)
            {
                page.Dock    = DockStyle.Fill;
                page.Visible = false;
                _contentPanel.Controls.Add(page);
            }

            _btnBack.Click   += (s, e) => GoBack();
            _btnNext.Click   += (s, e) => GoNext();
            _btnCancel.Click += (s, e) => OnCancelClicked();

            Load += (s, e) =>
            {
                NavigateTo(PAGE_WELCOME);
                // Kick off Burn's package-state detection (runs in background).
                _ba.StartDetect(Handle);
            };
        }

        // ================================================================
        // Page creation
        // ================================================================

        private Panel CreateWelcomePage()
        {
            var p = new Panel();

            var lbl = PageHeading("Welcome to Laserfiche Dashboard Setup");
            lbl.Location = new Point(0, 0);
            p.Controls.Add(lbl);

            var body = new Label
            {
                Text      = "This wizard installs the Dashboard web application on this server and " +
                            "optionally integrates with the Laserfiche Desktop Client and Web Client.\r\n\r\n" +
                            "Before continuing, ensure the following are installed:\r\n\r\n" +
                            "  \u2022  IIS (Internet Information Services)\r\n" +
                            "  \u2022  ASP.NET Core 8 Windows Hosting Bundle\r\n" +
                            "       https://dotnet.microsoft.com/download/dotnet/8.0\r\n\r\n" +
                            "  \u2022  Microsoft Edge WebView2 Runtime (for Desktop Extension only)\r\n" +
                            "       https://developer.microsoft.com/microsoft-edge/webview2/\r\n\r\n" +
                            "Click Next to scan your system and continue.",
                AutoSize  = false,
                Size      = new Size(560, 260),
                Location  = new Point(0, 36),
                Font      = new Font("Segoe UI", 9F)
            };
            p.Controls.Add(body);

            return p;
        }

        private Panel CreateDetectionPage()
        {
            var p = new Panel();
            int y = 0;

            p.Controls.Add(PageHeading("System Detection Results"));
            y += 32;

            _lblDetectStatus = new Label
            {
                Text     = "Scanning your system...",
                Location = new Point(0, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 80, 160)
            };
            p.Controls.Add(_lblDetectStatus);
            y += 28;

            var grid = new TableLayoutPanel
            {
                Location    = new Point(0, y),
                Size        = new Size(560, 180),
                ColumnCount = 2,
                RowCount    = 5,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            var componentLabels = new[]
            {
                "IIS (Internet Information Services):",
                "ASP.NET Core 8 Hosting Bundle:",
                "Microsoft Edge WebView2 Runtime:",
                "Laserfiche Desktop Client:",
                "Laserfiche Web Client:"
            };
            var statusLabels = new Label[5];
            for (int i = 0; i < 5; i++)
            {
                var nameLabel = new Label
                {
                    Text     = componentLabels[i],
                    Anchor   = AnchorStyles.Left | AnchorStyles.Top,
                    AutoSize = true,
                    Padding  = new Padding(0, 8, 0, 0)
                };
                statusLabels[i] = new Label
                {
                    Text     = "Checking...",
                    Anchor   = AnchorStyles.Left | AnchorStyles.Top,
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Padding  = new Padding(0, 8, 0, 0)
                };
                grid.Controls.Add(nameLabel,        0, i);
                grid.Controls.Add(statusLabels[i], 1, i);
            }
            _lblIisStatus       = statusLabels[0];
            _lblAspNetStatus    = statusLabels[1];
            _lblWebView2Status  = statusLabels[2];
            _lblDesktopStatus   = statusLabels[3];
            _lblWebClientStatus = statusLabels[4];
            p.Controls.Add(grid);
            y += 188;

            _btnReDetect = new Button
            {
                Text     = "Re-run Detection",
                Size     = new Size(130, 26),
                Location = new Point(0, y)
            };
            _btnReDetect.Click += (s, e) => StartDetection();
            p.Controls.Add(_btnReDetect);

            return p;
        }

        private Panel CreateConfigPage()
        {
            var p = new Panel();
            p.Controls.Add(PageHeading("Dashboard Configuration"));

            int y = 36;

            // Field helper: label + text box + optional hint
            void AddField(string label, string hint, ref TextBox box, bool isRequired = true,
                           string? placeholder = null)
            {
                var lbl = new Label
                {
                    Text     = label + (isRequired ? "*" : ""),
                    AutoSize = true,
                    Location = new Point(0, y + 3)
                };
                p.Controls.Add(lbl);

                box = new TextBox
                {
                    Size     = new Size(430, 22),
                    Location = new Point(160, y),
                    Text     = placeholder ?? ""
                };
                p.Controls.Add(box);

                if (hint.Length > 0)
                {
                    var hintLbl = new Label
                    {
                        Text      = hint,
                        AutoSize  = true,
                        Location  = new Point(160, y + 24),
                        ForeColor = Color.FromArgb(100, 100, 100),
                        Font      = new Font("Segoe UI", 8F)
                    };
                    p.Controls.Add(hintLbl);
                    y += 50;
                }
                else
                {
                    y += 34;
                }
            }

            // Suggest http://MACHINENAME:5000 as default URL
            string suggestedUrl = "http://" + System.Net.Dns.GetHostName() + ":5000";

            AddField("Dashboard URL",   "The URL users' browsers navigate to. Use your server name or IP, not localhost.",
                     ref _txtDashboardUrl!, true, suggestedUrl);
            AddField("Laserfiche API",  "Full URL of your Laserfiche Repository API. Example: https://lf-server/LFRepositoryAPI",
                     ref _txtLFApiUrl!,    true, "https://YOUR-LF-SERVER/LFRepositoryAPI");
            AddField("Repository ID",  "Case-sensitive repository identifier. Example: Documents",
                     ref _txtRepoId!,      true, "");
            AddField("Display Name",   "Human-readable name shown in Dashboard. Leave blank to use Repository ID.",
                     ref _txtDisplayName!, false, "");
            AddField("IIS Port",       "HTTP port for the IIS site. Must match the port in Dashboard URL above.",
                     ref _txtPort!,        true, "5000");

            // Required fields note
            p.Controls.Add(new Label
            {
                Text      = "* Required",
                AutoSize  = true,
                Location  = new Point(0, y + 4),
                ForeColor = Color.FromArgb(100, 100, 100),
                Font      = new Font("Segoe UI", 8F)
            });

            return p;
        }

        private Panel CreateIntegrationPage()
        {
            var p = new Panel();
            p.Controls.Add(PageHeading("Integration Options"));

            int y = 36;

            // -------- Desktop Extension --------
            _chkDesktop = new CheckBox
            {
                Text     = "Install Laserfiche Desktop Client Extension",
                Font     = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, y),
                Checked  = true
            };
            p.Controls.Add(_chkDesktop);
            y += 26;

            _lblDesktopInfo = new Label
            {
                Text      = "Checking for Desktop Client...",
                AutoSize  = false,
                Size      = new Size(540, 36),
                Location  = new Point(24, y),
                ForeColor = Color.FromArgb(80, 80, 80),
                Font      = new Font("Segoe UI", 8.5F)
            };
            p.Controls.Add(_lblDesktopInfo);
            y += 44;

            // Separator line
            var sep = new Panel { BackColor = Color.FromArgb(210, 210, 210), Size = new Size(560, 1), Location = new Point(0, y) };
            p.Controls.Add(sep);
            y += 12;

            // -------- Web Client --------
            _chkWebClient = new CheckBox
            {
                Text     = "Deploy Laserfiche Web Client Button",
                Font     = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, y),
                Checked  = false
            };
            p.Controls.Add(_chkWebClient);
            y += 26;

            _lblWebClientInfo = new Label
            {
                Text      = "Checking for Web Client...",
                AutoSize  = false,
                Size      = new Size(540, 38),
                Location  = new Point(24, y),
                ForeColor = Color.FromArgb(80, 80, 80),
                Font      = new Font("Segoe UI", 8.5F)
            };
            p.Controls.Add(_lblWebClientInfo);
            y += 46;

            // Manual path entry panel (shown when web client not auto-detected)
            _pnlWebClientPath = new Panel
            {
                Size     = new Size(540, 50),
                Location = new Point(24, y),
                Visible  = false
            };
            var pathLabel = new Label
            {
                Text     = "Web Client path:",
                AutoSize = true,
                Location = new Point(0, 4)
            };
            _txtWebClientPath = new TextBox
            {
                Size     = new Size(380, 22),
                Location = new Point(120, 0),
                Text     = ""
            };
            var browseBtn = new Button
            {
                Text     = "Browse...",
                Size     = new Size(80, 22),
                Location = new Point(506, 0)
            };
            browseBtn.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description  = "Select the Laserfiche Web Files directory (contains Browse.aspx)",
                    ShowNewFolderButton = false
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtWebClientPath.Text = dlg.SelectedPath;
            };
            _pnlWebClientPath.Controls.AddRange(new Control[] { pathLabel, _txtWebClientPath, browseBtn });
            p.Controls.Add(_pnlWebClientPath);

            // Toggle path panel visibility based on checkbox and detection
            _chkWebClient.CheckedChanged += (s, e) =>
            {
                _pnlWebClientPath.Visible = _chkWebClient.Checked && !_detection.WebClientFound;
            };

            return p;
        }

        private Panel CreateReadyPage()
        {
            var p = new Panel();
            p.Controls.Add(PageHeading("Ready to Install"));

            _lblReadySummary = new Label
            {
                Text      = "",
                AutoSize  = false,
                Size      = new Size(560, 300),
                Location  = new Point(0, 36),
                Font      = new Font("Segoe UI", 9F)
            };
            p.Controls.Add(_lblReadySummary);

            return p;
        }

        private Panel CreateProgressPage()
        {
            var p = new Panel();

            _lblCurrentAction = new Label
            {
                Text     = "Preparing installation...",
                AutoSize = true,
                Location = new Point(0, 4),
                Font     = new Font("Segoe UI", 9F)
            };
            p.Controls.Add(_lblCurrentAction);

            _progressBar = new ProgressBar
            {
                Size     = new Size(560, 22),
                Location = new Point(0, 30),
                Minimum  = 0,
                Maximum  = 100,
                Value    = 0,
                Style    = ProgressBarStyle.Continuous
            };
            p.Controls.Add(_progressBar);

            p.Controls.Add(new Label
            {
                Text     = "Installation log:",
                AutoSize = true,
                Location = new Point(0, 62),
                Font     = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            });

            _txtLog = new TextBox
            {
                Multiline    = true,
                ReadOnly     = true,
                ScrollBars   = ScrollBars.Vertical,
                Size         = new Size(560, 222),
                Location     = new Point(0, 80),
                BackColor    = Color.FromArgb(250, 250, 250),
                Font         = new Font("Consolas", 8F),
                WordWrap     = false
            };
            p.Controls.Add(_txtLog);

            return p;
        }

        private Panel CreateCompletePage()
        {
            var p = new Panel();

            _lblCompleteTitle = new Label
            {
                Text      = "Installation Complete",
                Font      = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 128, 0),
                AutoSize  = true,
                Location  = new Point(0, 4)
            };
            p.Controls.Add(_lblCompleteTitle);

            _lblCompleteDetail = new Label
            {
                Text      = "",
                AutoSize  = false,
                Size      = new Size(560, 200),
                Location  = new Point(0, 44),
                Font      = new Font("Segoe UI", 9F)
            };
            p.Controls.Add(_lblCompleteDetail);

            return p;
        }

        // ================================================================
        // Navigation
        // ================================================================

        private void NavigateTo(int idx)
        {
            _pages[_pageIndex].Visible = false;
            _pageIndex = idx;
            _pages[_pageIndex].Visible = true;

            _lblHeaderTitle.Text    = PageTitles[idx];
            _lblHeaderSubtitle.Text = PageSubtitles[idx];

            UpdateButtons();
            OnPageActivated(idx);
        }

        private void OnPageActivated(int page)
        {
            switch (page)
            {
                case PAGE_DETECTION:
                    if (!_detectionDone) StartDetection();
                    break;

                case PAGE_INTEGRATION:
                    UpdateIntegrationPage();
                    break;

                case PAGE_READY:
                    UpdateReadySummary();
                    break;

                case PAGE_PROGRESS:
                    StartInstallation();
                    break;

                case PAGE_COMPLETE:
                    UpdateCompletePage();
                    break;
            }
        }

        private void UpdateButtons()
        {
            _btnBack.Visible   = true;
            _btnNext.Visible   = true;
            _btnCancel.Visible = true;
            _btnNext.Enabled   = true;
            _btnBack.Enabled   = true;
            _btnCancel.Enabled = true;
            _btnNext.Text      = "Next >";
            _btnCancel.Text    = "Cancel";

            switch (_pageIndex)
            {
                case PAGE_WELCOME:
                    _btnBack.Visible = false;
                    break;

                case PAGE_DETECTION:
                    _btnBack.Visible = true;
                    _btnNext.Enabled = _detectionDone;
                    break;

                case PAGE_READY:
                    _btnNext.Text = "Install";
                    break;

                case PAGE_PROGRESS:
                    _btnBack.Visible   = false;
                    _btnNext.Visible   = false;
                    _btnCancel.Enabled = false;
                    break;

                case PAGE_COMPLETE:
                    _btnBack.Visible   = false;
                    _btnCancel.Visible = false;
                    _btnNext.Text      = "Finish";
                    break;
            }
        }

        private void GoNext()
        {
            if (_pageIndex == PAGE_CONFIG && !ValidateConfigPage()) return;
            if (_pageIndex == PAGE_INTEGRATION) CollectIntegrationPage();
            if (_pageIndex < PAGE_COMPLETE)
                NavigateTo(_pageIndex + 1);
        }

        private void GoBack()
        {
            if (_pageIndex > PAGE_WELCOME && _pageIndex != PAGE_PROGRESS)
                NavigateTo(_pageIndex - 1);
        }

        private void OnCancelClicked()
        {
            if (_pageIndex == PAGE_COMPLETE) { Close(); return; }
            if (MessageBox.Show(this,
                    "Are you sure you want to cancel the installation?",
                    "Cancel Setup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Close();
            }
        }

        // ================================================================
        // Detection
        // ================================================================

        private void StartDetection()
        {
            _detectionDone = false;
            _btnNext.Enabled = false;
            _btnReDetect.Enabled = false;
            _lblDetectStatus.Text = "Scanning your system...";
            SetDetectionRowStatus(_lblIisStatus,       "Checking...", Color.Gray);
            SetDetectionRowStatus(_lblAspNetStatus,    "Checking...", Color.Gray);
            SetDetectionRowStatus(_lblWebView2Status,  "Checking...", Color.Gray);
            SetDetectionRowStatus(_lblDesktopStatus,   "Checking...", Color.Gray);
            SetDetectionRowStatus(_lblWebClientStatus, "Checking...", Color.Gray);

            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = DetectionService.Detect(); };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Error != null)
                {
                    _lblDetectStatus.Text = "Detection error: " + e.Error.Message;
                    _detectionDone = true;
                    _btnNext.Enabled = true;
                    _btnReDetect.Enabled = true;
                    return;
                }

                _detection     = (DetectionResult)e.Result!;
                _detectionDone = true;

                UpdateDetectionUI();

                _btnReDetect.Enabled = true;
                _btnNext.Enabled     = true;

                // Pre-fill suggested Dashboard URL on config page
                if (!string.IsNullOrEmpty(_detection.SuggestedDashboardUrl))
                    _txtDashboardUrl.Text = _detection.SuggestedDashboardUrl;

                // Default web client path from detection
                if (_detection.WebClientFound)
                    _txtWebClientPath.Text = _detection.WebClientPath;
            };
            worker.RunWorkerAsync();
        }

        private void UpdateDetectionUI()
        {
            // IIS
            if (_detection.IisInstalled)
                SetDetectionRowStatus(_lblIisStatus, "Found", Color.Green);
            else
                SetDetectionRowStatus(_lblIisStatus, "NOT FOUND  [REQUIRED]", Color.Red);

            // ASP.NET Core 8
            if (_detection.AspNetCore8Installed)
                SetDetectionRowStatus(_lblAspNetStatus,
                    $"Found (v{_detection.AspNetCore8Version})", Color.Green);
            else
                SetDetectionRowStatus(_lblAspNetStatus,
                    "NOT FOUND  [REQUIRED - install Hosting Bundle]", Color.Red);

            // WebView2
            if (_detection.WebView2Installed)
                SetDetectionRowStatus(_lblWebView2Status,
                    $"Found (v{_detection.WebView2Version})", Color.Green);
            else
                SetDetectionRowStatus(_lblWebView2Status,
                    "Not found  (required for Desktop Extension)", Color.FromArgb(180, 100, 0));

            // Desktop Client
            if (_detection.DesktopClientFound)
                SetDetectionRowStatus(_lblDesktopStatus,
                    string.IsNullOrEmpty(_detection.DesktopClientPath)
                        ? "Found"
                        : $"Found: {_detection.DesktopClientPath}",
                    Color.Green);
            else
                SetDetectionRowStatus(_lblDesktopStatus,
                    "Not detected on this machine", Color.Gray);

            // Web Client
            if (_detection.WebClientFound)
                SetDetectionRowStatus(_lblWebClientStatus,
                    $"Found: {_detection.WebClientPath}", Color.Green);
            else
                SetDetectionRowStatus(_lblWebClientStatus,
                    "Not detected on this machine", Color.Gray);

            // Overall status
            string status = _detection.AllRequiredPresent
                ? "Detection complete. All required components found."
                : "Detection complete. Required components are missing (see red items above).";
            _lblDetectStatus.Text      = status;
            _lblDetectStatus.ForeColor = _detection.AllRequiredPresent
                ? Color.FromArgb(0, 128, 0)
                : Color.Red;
        }

        private static void SetDetectionRowStatus(Label lbl, string text, Color color)
        {
            lbl.Text      = text;
            lbl.ForeColor = color;
        }

        // ================================================================
        // Config page
        // ================================================================

        private bool ValidateConfigPage()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(_txtDashboardUrl.Text))
                errors.Add("Dashboard URL is required.");
            else if (!_txtDashboardUrl.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                     !_txtDashboardUrl.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                errors.Add("Dashboard URL must start with http:// or https://");

            if (string.IsNullOrWhiteSpace(_txtLFApiUrl.Text))
                errors.Add("Laserfiche API URL is required.");
            else if (!_txtLFApiUrl.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                     !_txtLFApiUrl.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                errors.Add("Laserfiche API URL must start with http:// or https://");

            if (string.IsNullOrWhiteSpace(_txtRepoId.Text))
                errors.Add("Repository ID is required.");

            if (!int.TryParse(_txtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
                errors.Add("IIS Port must be a number between 1 and 65535.");

            if (errors.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join("\r\n\r\n", errors),
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            // Commit values to config model
            _config.DashboardUrl     = _txtDashboardUrl.Text.Trim().TrimEnd('/');
            _config.LaserficheApiUrl = _txtLFApiUrl.Text.Trim().TrimEnd('/');
            _config.RepositoryId     = _txtRepoId.Text.Trim();
            _config.DisplayName      = _txtDisplayName.Text.Trim();
            _config.DashboardPort    = _txtPort.Text.Trim();

            return true;
        }

        // ================================================================
        // Integration page
        // ================================================================

        private void UpdateIntegrationPage()
        {
            if (_detection.DesktopClientFound)
            {
                _lblDesktopInfo.Text =
                    "Laserfiche Desktop Client detected. The toolbar button will be registered automatically.";
                _chkDesktop.Enabled = true;
                _chkDesktop.Checked = true;
            }
            else
            {
                _lblDesktopInfo.Text =
                    "Laserfiche Desktop Client was not detected. You can still install the extension files; " +
                    "registration will be attempted and will succeed once the Desktop Client is installed.";
                _chkDesktop.Enabled = true;
                _chkDesktop.Checked = true;
            }

            if (_detection.WebClientFound)
            {
                _lblWebClientInfo.Text =
                    $"Web Client found at: {_detection.WebClientPath}\r\n" +
                    "Browse.aspx will be patched to add the Dashboard button.";
                _chkWebClient.Enabled = true;
                _chkWebClient.Checked = true;
                _txtWebClientPath.Text = _detection.WebClientPath;
                _pnlWebClientPath.Visible = false;
            }
            else
            {
                _lblWebClientInfo.Text =
                    "Laserfiche Web Client was not detected automatically.\r\n" +
                    "Enable and enter the path below to configure it manually.";
                _chkWebClient.Enabled = true;
                _chkWebClient.Checked = false;
                _pnlWebClientPath.Visible = false;
            }
        }

        private void CollectIntegrationPage()
        {
            _config.InstallDesktopButton = _chkDesktop.Checked;
            _config.InstallWebButton     = _chkWebClient.Checked;

            if (_chkWebClient.Checked)
            {
                string wcPath = _detection.WebClientFound
                    ? _detection.WebClientPath
                    : _txtWebClientPath.Text.Trim().TrimEnd('\\', '/');
                _config.LFWebClientPath = wcPath;
            }
            else
            {
                _config.LFWebClientPath = "";
            }
        }

        // ================================================================
        // Ready page
        // ================================================================

        private void UpdateReadySummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("The following will be installed and configured:\r\n");
            sb.AppendLine("  Dashboard Web Application");
            sb.AppendLine($"    - IIS site on port {_config.DashboardPort}");
            sb.AppendLine($"    - URL: {_config.DashboardUrl}");
            sb.AppendLine();
            sb.AppendLine("  Laserfiche Connection");
            sb.AppendLine($"    - API: {_config.LaserficheApiUrl}");
            sb.AppendLine($"    - Repository: {_config.RepositoryId}");
            if (!string.IsNullOrEmpty(_config.DisplayName))
                sb.AppendLine($"    - Display Name: {_config.DisplayName}");
            sb.AppendLine();

            if (_config.InstallDesktopButton)
                sb.AppendLine("  Laserfiche Desktop Client Extension (will be registered)");

            if (_config.InstallWebButton && !string.IsNullOrEmpty(_config.LFWebClientPath))
            {
                sb.AppendLine("  Laserfiche Web Client Button");
                sb.AppendLine($"    - Path: {_config.LFWebClientPath}");
            }

            sb.AppendLine();
            sb.AppendLine("Click Install to begin. This may take a few minutes.");
            sb.AppendLine("Config files will be written to: %ProgramData%\\Dashboard\\");

            _lblReadySummary.Text = sb.ToString();
        }

        // ================================================================
        // Installation
        // ================================================================

        private void StartInstallation()
        {
            AppendLog("Starting installation...");
            _progressBar.Value = 0;
            _lblCurrentAction.Text = "Preparing...";
            _ba.StartInstall(_config, Handle);
        }

        // Called by DashboardBA on the UI thread (via BeginInvoke).
        private void OnProgressUpdated(int percent, string? message)
        {
            if (message != null) AppendLog(message);
            if (percent >= 0 && percent <= 100)
            {
                _progressBar.Value     = percent;
                _lblCurrentAction.Text = message ?? $"Progress: {percent}%";
            }
        }

        // Called by DashboardBA on the UI thread (via BeginInvoke).
        private void OnInstallFinished(bool success, string message)
        {
            _installSuccess = success;
            _installMessage = message;
            AppendLog(success ? "[SUCCESS] " + message : "[FAILED] " + message);
            NavigateTo(PAGE_COMPLETE);
        }

        private void AppendLog(string text)
        {
            if (_txtLog.InvokeRequired)
            {
                _txtLog.BeginInvoke(new Action(() => AppendLog(text)));
                return;
            }
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText($"[{stamp}] {text}\r\n");
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }

        // ================================================================
        // Complete page
        // ================================================================

        private void UpdateCompletePage()
        {
            if (_installSuccess)
            {
                _lblCompleteTitle.Text      = "Installation Complete";
                _lblCompleteTitle.ForeColor = Color.FromArgb(0, 128, 0);
                _lblCompleteDetail.Text     =
                    "Dashboard has been installed successfully.\r\n\r\n" +
                    $"Open your browser and navigate to:\r\n  {_config.DashboardUrl}\r\n\r\n" +
                    "Log in to the Dashboard Settings page to enter your Laserfiche credentials.\r\n" +
                    "(Credentials are stored encrypted using Windows DPAPI -- never in plain text.)\r\n\r\n" +
                    "Click Finish to close the installer.";
            }
            else
            {
                _lblCompleteTitle.Text      = "Installation Failed";
                _lblCompleteTitle.ForeColor = Color.Red;
                _lblCompleteDetail.Text     =
                    "The installation did not complete successfully.\r\n\r\n" +
                    _installMessage + "\r\n\r\n" +
                    "Check the Windows Event Log and the MSI log for details.\r\n" +
                    "You can re-run LFDashboard-Setup.exe to try again.\r\n\r\n" +
                    "Click Finish to close the installer.";
            }

            _lblHeaderSubtitle.Text = _installSuccess
                ? "Dashboard has been installed on this computer."
                : "The installation could not be completed.";
        }

        // ================================================================
        // Dispose
        // ================================================================

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ba.ProgressUpdated -= OnProgressUpdated;
                _ba.InstallFinished -= OnInstallFinished;
            }
            base.Dispose(disposing);
        }

        // ================================================================
        // Factory helpers
        // ================================================================

        private static Label PageHeading(string text) =>
            new Label
            {
                Text     = text,
                Font     = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = true,
                Location = new Point(0, 0),
                ForeColor = Color.FromArgb(0, 62, 134)
            };

        private static Button MakeButton(string text, int width) =>
            new Button
            {
                Text   = text,
                Size   = new Size(width, 26),
                TabStop = true
            };
    }
}
