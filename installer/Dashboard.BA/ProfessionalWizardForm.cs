using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace Dashboard.BA
{
    /// <summary>
    /// Responsive, task-focused setup experience. Configuration is split into
    /// short steps so there is never a clipped label or horizontal scrollbar.
    /// </summary>
    internal sealed class ProfessionalWizardForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(14, 42, 78);
        private static readonly Color Blue = Color.FromArgb(20, 94, 168);
        private static readonly Color PaleBlue = Color.FromArgb(235, 244, 253);
        private static readonly Color Canvas = Color.FromArgb(247, 249, 252);
        private static readonly Color Border = Color.FromArgb(220, 226, 234);
        private static readonly Color TextColor = Color.FromArgb(31, 41, 55);
        private static readonly Color Muted = Color.FromArgb(98, 108, 124);
        private static readonly Color Success = Color.FromArgb(22, 128, 82);
        private static readonly Color Warning = Color.FromArgb(180, 105, 20);

        private const int PAGE_WELCOME = 0;
        private const int PAGE_CONNECTION = 1;
        private const int PAGE_DEPLOYMENT = 2;
        private const int PAGE_READY = 3;
        private const int PAGE_PROGRESS = 4;
        private const int PAGE_COMPLETE = 5;

        private readonly string[] _titles =
        {
            "Welcome",
            "Connect to Laserfiche",
            "Choose deployment options",
            "Review and install",
            "Installing Dashboard",
            "Setup complete"
        };

        private readonly string[] _subtitles =
        {
            "A guided setup will prepare the server and configure Dashboard.",
            "Enter the same connection information used by the Dashboard Settings page.",
            "Confirm the Dashboard address and optional Laserfiche integrations.",
            "Everything is ready. Review the configuration before installing.",
            "Please keep this window open while setup completes.",
            "Laserfiche Dashboard is ready to use."
        };

        private readonly DashboardBA _ba;
        private readonly InstallConfig _config = new InstallConfig();
        private DetectionResult _detection = new DetectionResult();
        private bool _detectionDone;
        private bool _installDone;
        private bool _installSuccess;
        private string _installMessage = "";
        private int _pageIndex;
        private SetupOperation _operation = SetupOperation.Install;
        private bool _maintenanceMode;
        private bool _removeUserData;
        private bool _connectionTestPassed;
        private string _autoDetectedHost = "";

        private enum SetupOperation
        {
            Install,
            Repair,
            Uninstall
        }

        private readonly Panel[] _pages = new Panel[6];
        private readonly Panel[] _stepRows = new Panel[4];
        private readonly Label[] _stepNumbers = new Label[4];
        private readonly Label[] _stepLabels = new Label[4];

        private Label _headerTitle = null!;
        private Label _headerSubtitle = null!;
        private Button _backButton = null!;
        private Button _nextButton = null!;
        private Button _cancelButton = null!;

        private Label _welcomeHeading = null!;
        private Label _welcomeBody = null!;
        private Label _environmentState = null!;
        private Label _iisState = null!;
        private Label _hostingState = null!;
        private Label _desktopState = null!;

        private TextBox _serverUrl = null!;
        private TextBox _apiBasePath = null!;
        private ComboBox _apiVersion = null!;
        private TextBox _repositoryId = null!;
        private TextBox _displayName = null!;
        private TextBox _rootEntryId = null!;
        private TextBox _timeoutSeconds = null!;
        private TextBox _username = null!;
        private TextBox _password = null!;
        private CheckBox _showPassword = null!;
        private Label _connectionStatus = null!;
        private Label _certificateStatus = null!;
        private CheckBox _trustCertificate = null!;

        private TextBox _dashboardUrl = null!;
        private TextBox _dashboardPort = null!;
        private CheckBox _desktopButton = null!;
        private CheckBox _webButton = null!;
        private Panel _webPathPanel = null!;
        private TextBox _webClientPath = null!;
        private Label _webClientDetected = null!;
        private bool _webClientDetectedValid;

        private RichTextBox _readySummary = null!;
        private ProgressBar _progress = null!;
        private Label _currentAction = null!;
        private TextBox _log = null!;
        private Label _completeIcon = null!;
        private Label _completeTitle = null!;
        private Label _completeDetail = null!;
        private CheckBox _launchDashboard = null!;

        public ProfessionalWizardForm(DashboardBA ba)
        {
            _ba = ba;
            _ba.ProgressUpdated += OnProgressUpdated;
            _ba.InstallFinished += OnInstallFinished;
            _ba.PackageStateChanged += OnPackageStateChanged;
            BuildForm();
        }

        private void BuildForm()
        {
            Text = "Laserfiche Dashboard Setup";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(920, 620);
            MinimumSize = new Size(840, 590);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            BackColor = Canvas;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            var sidebar = BuildSidebar();
            var header = BuildHeader();
            var footer = BuildFooter();
            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(30, 18, 30, 18),
                BackColor = Canvas
            };

            _pages[PAGE_WELCOME] = CreateWelcomePage();
            _pages[PAGE_CONNECTION] = CreateConnectionPage();
            _pages[PAGE_DEPLOYMENT] = CreateDeploymentPage();
            _pages[PAGE_READY] = CreateReadyPage();
            _pages[PAGE_PROGRESS] = CreateProgressPage();
            _pages[PAGE_COMPLETE] = CreateCompletePage();

            foreach (var page in _pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                body.Controls.Add(page);
            }

            main.Controls.Add(body);
            main.Controls.Add(footer);
            main.Controls.Add(header);
            Controls.Add(main);
            Controls.Add(sidebar);

            Load += (s, e) =>
            {
                NavigateTo(PAGE_WELCOME);
                _ba.StartDetect(Handle);
                StartDetection();
            };
        }

        private Panel BuildSidebar()
        {
            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 218,
                BackColor = Navy,
                Padding = new Padding(20)
            };

            var brandMark = new Panel
            {
                Location = new Point(20, 26),
                Size = new Size(34, 34),
                BackColor = Color.FromArgb(38, 132, 221)
            };
            brandMark.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "L",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold)
            });

            sidebar.Controls.Add(brandMark);
            sidebar.Controls.Add(new Label
            {
                Text = "Laserfiche\nDashboard",
                Location = new Point(64, 23),
                Size = new Size(130, 46),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold)
            });
            sidebar.Controls.Add(new Label
            {
                Text = "SETUP",
                Location = new Point(20, 91),
                AutoSize = true,
                ForeColor = Color.FromArgb(143, 169, 201),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold)
            });

            string[] names = { "Welcome", "Connection", "Deployment", "Install" };
            for (int i = 0; i < names.Length; i++)
            {
                int y = 122 + (i * 58);
                var row = new Panel
                {
                    Location = new Point(12, y),
                    Size = new Size(194, 48),
                    BackColor = Navy
                };
                var number = new Label
                {
                    Text = (i + 1).ToString(),
                    Location = new Point(8, 10),
                    Size = new Size(28, 28),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(167, 190, 218),
                    BackColor = Color.FromArgb(28, 60, 99),
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                var label = new Label
                {
                    Text = names[i],
                    Location = new Point(48, 13),
                    Size = new Size(136, 24),
                    ForeColor = Color.FromArgb(167, 190, 218),
                    Font = new Font("Segoe UI", 9.5F)
                };
                row.Controls.Add(number);
                row.Controls.Add(label);
                sidebar.Controls.Add(row);
                _stepRows[i] = row;
                _stepNumbers[i] = number;
                _stepLabels[i] = label;
            }

            var privacy = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Text = "On-premises deployment\nConfiguration stays on this computer",
                ForeColor = Color.FromArgb(143, 169, 201),
                Font = new Font("Segoe UI", 7.5F),
                TextAlign = ContentAlignment.BottomLeft
            };
            sidebar.Controls.Add(privacy);
            return sidebar;
        }

        private Panel BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 88,
                BackColor = Color.White,
                Padding = new Padding(30, 17, 24, 10)
            };
            header.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Border
            });
            _headerTitle = new Label
            {
                Text = _titles[0],
                Location = new Point(30, 16),
                AutoSize = true,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold)
            };
            _headerSubtitle = new Label
            {
                Text = _subtitles[0],
                Location = new Point(32, 51),
                Size = new Size(620, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 9F)
            };
            header.Controls.Add(_headerTitle);
            header.Controls.Add(_headerSubtitle);
            return header;
        }

        private Panel BuildFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = Color.White
            };
            footer.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Border
            });

            _backButton = CreateButton("Back", false, 94);
            _nextButton = CreateButton("Continue", true, 112);
            _cancelButton = CreateButton("Cancel", false, 94);
            _backButton.Click += (s, e) => GoBack();
            _nextButton.Click += (s, e) => GoNext();
            _cancelButton.Click += (s, e) => CancelSetup();
            footer.Controls.AddRange(new Control[] { _backButton, _nextButton, _cancelButton });

            void PositionButtons()
            {
                int y = 17;
                _cancelButton.Location = new Point(footer.ClientSize.Width - _cancelButton.Width - 24, y);
                _nextButton.Location = new Point(_cancelButton.Left - _nextButton.Width - 10, y);
                _backButton.Location = new Point(_nextButton.Left - _backButton.Width - 10, y);
            }
            footer.Layout += (s, e) => PositionButtons();
            PositionButtons();
            return footer;
        }

        private Panel CreateWelcomePage()
        {
            var page = new Panel { BackColor = Canvas };
            _welcomeHeading = new Label
            {
                Text = "Set up Dashboard in a few steps",
                Location = new Point(0, 2),
                AutoSize = true,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold)
            };
            _welcomeBody = new Label
            {
                Text = "Setup will install the web application, configure IIS, save the Laserfiche connection, " +
                       "secure the service credentials, and add the client integrations you select.",
                Location = new Point(2, 44),
                Size = new Size(610, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 10F)
            };
            page.Controls.Add(_welcomeHeading);
            page.Controls.Add(_welcomeBody);

            var features = new TableLayoutPanel
            {
                Location = new Point(0, 112),
                Size = new Size(630, 142),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Canvas
            };
            features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            features.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
            features.Controls.Add(CreateFeatureCard("1", "Connect", "Laserfiche server, repository and secure credentials"), 0, 0);
            features.Controls.Add(CreateFeatureCard("2", "Deploy", "IIS site, public address and client buttons"), 1, 0);
            features.Controls.Add(CreateFeatureCard("3", "Start", "Open Dashboard immediately with no extra setup"), 2, 0);
            page.Controls.Add(features);

            var statusCard = CreateCard();
            statusCard.Location = new Point(0, 278);
            statusCard.Size = new Size(630, 156);
            statusCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusCard.Controls.Add(new Label
            {
                Text = "System readiness",
                Location = new Point(18, 15),
                AutoSize = true,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
            });
            _environmentState = new Label
            {
                Text = "Checking this computer...",
                Location = new Point(18, 42),
                Size = new Size(570, 22),
                ForeColor = Blue,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _iisState = StatusLabel("IIS", 72);
            _hostingState = StatusLabel("ASP.NET Core Hosting Module", 96);
            _desktopState = StatusLabel("Laserfiche client integrations", 120);
            statusCard.Controls.AddRange(new Control[]
            {
                _environmentState, _iisState, _hostingState, _desktopState
            });
            page.Controls.Add(statusCard);
            return page;
        }

        private Panel CreateConnectionPage()
        {
            var page = new Panel { BackColor = Canvas };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Canvas };
            var grid = CreateGrid();
            grid.Width = 630;

            // Keep this page limited to the Laserfiche connection itself.
            // Detection replaces the defaults when this computer already has
            // a Laserfiche API binding.
            _serverUrl = CreateTextBox("https://localhost");
            _serverUrl.Text = "https://localhost";
            _apiBasePath = CreateTextBox("/LFRepositoryAPI");
            _apiBasePath.Text = "/LFRepositoryAPI";
            _apiVersion = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F)
            };
            _apiVersion.Items.AddRange(new object[] { "Auto Detect (recommended)", "v1", "v2" });
            _apiVersion.SelectedIndex = 0;
            int row = 0;
            AddSectionHeader(grid, row++, "Laserfiche API");
            AddField(grid, row++, 0, "Server URL *", "Default: https://localhost. A full API URL is also accepted.", _serverUrl, 2);
            AddField(grid, row, 0, "API base path *", "Default: /LFRepositoryAPI", _apiBasePath);
            AddField(grid, row++, 1, "API version", "Auto Detect tries v2 and then v1.", _apiVersion);

            var certificate = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4), Height = 64 };
            _certificateStatus = new Label
            {
                Text = "Certificate details will appear after automatic detection.",
                Location = new Point(0, 2),
                Size = new Size(600, 22),
                ForeColor = Muted
            };
            _trustCertificate = new CheckBox
            {
                Text = "Trust the detected self-signed certificate on this computer",
                Location = new Point(0, 29),
                AutoSize = true,
                Visible = false
            };
            certificate.Controls.AddRange(new Control[] { _certificateStatus, _trustCertificate });
            AddFullRow(grid, row++, certificate, 70);

            scroll.Controls.Add(grid);
            page.Controls.Add(scroll);
            return page;
        }

        private Panel CreateDeploymentPage()
        {
            var page = new Panel { BackColor = Canvas };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Canvas };
            var grid = CreateGrid();
            grid.Width = 630;

            try { _autoDetectedHost = Dns.GetHostName(); } catch { _autoDetectedHost = Environment.MachineName; }
            if (string.IsNullOrWhiteSpace(_autoDetectedHost)) _autoDetectedHost = Environment.MachineName;

            _dashboardUrl = CreateTextBox("Dashboard address");
            _dashboardUrl.Text = string.IsNullOrWhiteSpace(_autoDetectedHost)
                ? ""
                : "http://" + _autoDetectedHost + ":5000";
            _dashboardPort = CreateTextBox("5000");
            _dashboardPort.Text = "5000";
            _dashboardPort.TextChanged += (s, e) => AutoUpdateDashboardUrl();
            _desktopButton = new CheckBox
            {
                Text = "Laserfiche Desktop Client button",
                Checked = true,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
            };
            _webButton = new CheckBox
            {
                Text = "Laserfiche Web Client button",
                Checked = false,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
            };
            _webButton.CheckedChanged += (s, e) =>
                _webPathPanel.Visible = _webButton.Checked && !_webClientDetectedValid;

            int row = 0;
            AddSectionHeader(grid, row++, "Dashboard website");
            AddField(grid, row++, 0, "Public Dashboard URL *", "Users will open this address after setup.", _dashboardUrl, 2);
            AddField(grid, row++, 0, "IIS port *", "Setup validates that the port is available.", _dashboardPort);
            AddSectionHeader(grid, row++, "Laserfiche integrations");

            var integrations = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Canvas
            };
            integrations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            integrations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            integrations.Controls.Add(CreateOptionCard(
                _desktopButton,
                "Adds Dashboard to the Desktop Client toolbar when the client is installed."), 0, 0);
            integrations.Controls.Add(CreateOptionCard(
                _webButton,
                "Adds a Dashboard button to Browse.aspx after verifying the Web Files path."), 1, 0);
            AddFullRow(grid, row++, integrations, 116);

            _webClientDetected = new Label
            {
                Text = "Web Client path will be detected automatically.",
                Dock = DockStyle.Top,
                Height = 25,
                ForeColor = Muted
            };
            _webClientPath = CreateTextBox(@"C:\Program Files\Laserfiche\Web Access\Web Files");
            var browse = CreateButton("Browse", false, 88);
            browse.Dock = DockStyle.Right;
            browse.Click += (s, e) => BrowseForWebClient();
            _webClientPath.Dock = DockStyle.Fill;
            var pathInput = new Panel { Dock = DockStyle.Top, Height = 38 };
            pathInput.Controls.Add(_webClientPath);
            pathInput.Controls.Add(browse);
            _webPathPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Padding = new Padding(0, 3, 0, 0),
                Visible = false
            };
            _webPathPanel.Controls.Add(pathInput);
            _webPathPanel.Controls.Add(_webClientDetected);
            AddFullRow(grid, row++, _webPathPanel, 72);

            var note = new Label
            {
                Text = "Setup will create the IIS site, app pool, shortcuts, uninstall entry, configuration, logs and secure credential storage.",
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 12, 4, 4),
                ForeColor = Muted
            };
            AddFullRow(grid, row++, note, 52);
            scroll.Controls.Add(grid);
            page.Controls.Add(scroll);
            return page;
        }

        private Panel CreateReadyPage()
        {
            var page = new Panel { BackColor = Canvas };
            var card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(22);
            card.Controls.Add(new Label
            {
                Text = "Installation summary",
                Dock = DockStyle.Top,
                Height = 32,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold)
            });
            _readySummary = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = TextColor,
                Font = new Font("Segoe UI", 9.5F),
                DetectUrls = false,
                TabStop = false
            };
            var security = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Text = "Your Laserfiche password is transferred only as a temporary DPAPI-encrypted package and is never written to setup logs or JSON files.",
                BackColor = PaleBlue,
                ForeColor = Navy,
                Padding = new Padding(12, 9, 12, 6)
            };
            card.Controls.Add(_readySummary);
            card.Controls.Add(security);
            page.Controls.Add(card);
            return page;
        }

        private Panel CreateProgressPage()
        {
            var page = new Panel { BackColor = Canvas };
            var card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(24);
            _currentAction = new Label
            {
                Text = "Preparing installation...",
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold)
            };
            _progress = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 18,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            var logTitle = new Label
            {
                Text = "Setup details",
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(0, 17, 0, 0),
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(249, 250, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8F),
                WordWrap = true,
                TabStop = false
            };
            card.Controls.Add(_log);
            card.Controls.Add(logTitle);
            card.Controls.Add(_progress);
            card.Controls.Add(_currentAction);
            page.Controls.Add(card);
            return page;
        }

        private Panel CreateCompletePage()
        {
            var page = new Panel { BackColor = Canvas };
            var card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(34);
            _completeIcon = new Label
            {
                Text = "✓",
                Location = new Point(34, 32),
                Size = new Size(62, 62),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(226, 246, 237),
                ForeColor = Success,
                Font = new Font("Segoe UI", 26F, FontStyle.Bold)
            };
            _completeTitle = new Label
            {
                Text = "Installation complete",
                Location = new Point(116, 36),
                AutoSize = true,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold)
            };
            _completeDetail = new Label
            {
                Text = "",
                Location = new Point(118, 77),
                Size = new Size(450, 130),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 9.5F)
            };
            _launchDashboard = new CheckBox
            {
                Text = "Launch Laserfiche Dashboard when setup closes",
                Location = new Point(118, 228),
                AutoSize = true,
                Checked = true,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
            };
            var logs = CreateButton("Open setup logs", false, 132);
            logs.Location = new Point(118, 273);
            logs.Click += (s, e) => OpenSetupLogs();
            card.Controls.AddRange(new Control[]
            {
                _completeIcon, _completeTitle, _completeDetail, _launchDashboard, logs
            });
            page.Controls.Add(card);
            return page;
        }

        private void NavigateTo(int page)
        {
            if (_pageIndex >= PAGE_PROGRESS && page < _pageIndex && page != PAGE_COMPLETE)
                return;

            _pages[_pageIndex].Visible = false;
            _pageIndex = page;
            _pages[page].Visible = true;
            _pages[page].BringToFront();
            _headerTitle.Text = _titles[page];
            _headerSubtitle.Text = _subtitles[page];
            UpdateStepRail();
            UpdateButtons();

            if ((page == PAGE_CONNECTION || page == PAGE_DEPLOYMENT) && _detectionDone)
                ApplyDetection();
            if (page == PAGE_READY)
                UpdateReadySummary();
            if (page == PAGE_PROGRESS)
                StartInstallation();
            if (page == PAGE_COMPLETE)
                UpdateCompletePage();
        }

        private void UpdateStepRail()
        {
            int active = _pageIndex switch
            {
                PAGE_WELCOME => 0,
                PAGE_CONNECTION => 1,
                PAGE_DEPLOYMENT => 2,
                _ => 3
            };

            for (int i = 0; i < _stepRows.Length; i++)
            {
                bool current = i == active;
                bool complete = i < active;
                _stepRows[i].BackColor = current ? Color.FromArgb(25, 68, 116) : Navy;
                _stepNumbers[i].BackColor = current || complete ? Blue : Color.FromArgb(28, 60, 99);
                _stepNumbers[i].ForeColor = current || complete ? Color.White : Color.FromArgb(167, 190, 218);
                _stepNumbers[i].Text = complete ? "✓" : (i + 1).ToString();
                _stepLabels[i].ForeColor = current ? Color.White : Color.FromArgb(167, 190, 218);
                _stepLabels[i].Font = new Font("Segoe UI", 9.5F, current ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private void UpdateButtons()
        {
            _backButton.Visible = true;
            _nextButton.Visible = true;
            _cancelButton.Visible = true;
            _backButton.Enabled = true;
            _nextButton.Enabled = true;
            _cancelButton.Enabled = true;
            _backButton.Text = "Back";
            _nextButton.Text = "Continue";
            _cancelButton.Text = "Cancel";

            if (_maintenanceMode && _pageIndex == PAGE_WELCOME)
            {
                _backButton.Visible = true;
                _backButton.Text = "Uninstall";
                _nextButton.Text = "Repair";
                _cancelButton.Text = "Close";
                return;
            }

            if (_pageIndex == PAGE_WELCOME)
                _backButton.Visible = false;
            else if (_pageIndex == PAGE_READY)
                _nextButton.Text = "Install";
            else if (_pageIndex == PAGE_PROGRESS)
            {
                _backButton.Visible = false;
                _nextButton.Visible = false;
                _cancelButton.Enabled = false;
            }
            else if (_pageIndex == PAGE_COMPLETE)
            {
                _backButton.Visible = false;
                _cancelButton.Visible = false;
                _nextButton.Text = "Finish";
            }
        }

        private void GoNext()
        {
            if (_maintenanceMode && _pageIndex == PAGE_WELCOME)
            {
                _operation = SetupOperation.Repair;
                NavigateTo(PAGE_PROGRESS);
                return;
            }

            if (_pageIndex == PAGE_CONNECTION && !ValidateConnectionPage()) return;
            if (_pageIndex == PAGE_DEPLOYMENT)
            {
                if (!ValidateDeploymentPage()) return;
                CollectConfiguration();
            }

            if (_pageIndex == PAGE_COMPLETE)
            {
                if (_installSuccess && _operation != SetupOperation.Uninstall && _launchDashboard.Checked)
                    LaunchDashboard();
                Close();
                return;
            }

            NavigateTo(_pageIndex + 1);
        }

        private void GoBack()
        {
            if (_maintenanceMode && _pageIndex == PAGE_WELCOME)
            {
                if (ConfirmUninstall(out bool removeData))
                {
                    _removeUserData = removeData;
                    _operation = SetupOperation.Uninstall;
                    NavigateTo(PAGE_PROGRESS);
                }
                return;
            }

            if (_pageIndex > PAGE_WELCOME && _pageIndex < PAGE_PROGRESS)
                NavigateTo(_pageIndex - 1);
        }

        private void CancelSetup()
        {
            if (_maintenanceMode && _pageIndex == PAGE_WELCOME)
            {
                Close();
                return;
            }
            if (MessageBox.Show(this, "Cancel Dashboard setup?", "Cancel setup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_pageIndex == PAGE_PROGRESS && !_installDone)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Setup is still running. Please wait for it to finish.",
                    "Installation in progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            base.OnFormClosing(e);
        }

        private void StartDetection()
        {
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = DetectionService.Detect();
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed || e.Error != null || e.Result == null) return;
                _detection = (DetectionResult)e.Result;
                _detectionDone = true;
                UpdateEnvironmentStatus();
                ApplyDetection();
            };
            worker.RunWorkerAsync();
        }

        private void UpdateEnvironmentStatus()
        {
            _iisState.Text = (_detection.IisInstalled ? "✓  " : "!  ") +
                             "IIS: " + (_detection.IisInstalled ? "Ready" : "Required");
            _hostingState.Text = (_detection.AncmInstalled ? "✓  " : "!  ") +
                                 "ASP.NET Core Hosting Module: " + (_detection.AncmInstalled ? "Ready" : "Required");
            bool integrationReady = _detection.DesktopClientFound || _detection.WebClientFound;
            _desktopState.Text = (integrationReady ? "✓  " : "•  ") +
                                 "Laserfiche clients: " + (integrationReady ? "Detected" : "Optional / not detected");
            _environmentState.Text = _detection.AllRequiredPresent
                ? "This computer is ready for Dashboard."
                : "One or more required Windows components are missing.";
            _environmentState.ForeColor = _detection.AllRequiredPresent ? Success : Warning;
        }

        private void ApplyDetection()
        {
            if (!_detectionDone) return;

            if (!string.IsNullOrWhiteSpace(_detection.LaserficheApiUrl))
            {
                SplitApiUrl(_detection.LaserficheApiUrl, out string server, out string path);
                _serverUrl.Text = server;
                _apiBasePath.Text = path;
            }

            if (_apiVersion.SelectedIndex == 0)
            {
                if (string.Equals(_detection.ExistingApiVersion, "v1", StringComparison.OrdinalIgnoreCase))
                    _apiVersion.SelectedIndex = 1;
                else if (string.Equals(_detection.ExistingApiVersion, "v2", StringComparison.OrdinalIgnoreCase))
                    _apiVersion.SelectedIndex = 2;
            }

            if (!string.IsNullOrWhiteSpace(_detection.LaserficheCertSubject))
            {
                bool offerTrust = _detection.LaserficheCertSelfSigned &&
                                  !_detection.LaserficheCertTrusted &&
                                  !string.IsNullOrWhiteSpace(_detection.LaserficheApiUrl);
                _certificateStatus.Text = _detection.LaserficheCertTrusted
                    ? "✓ Certificate trusted: " + _detection.LaserficheCertSubject
                    : "Certificate requires attention: " + _detection.LaserficheCertSubject;
                _certificateStatus.ForeColor = _detection.LaserficheCertTrusted ? Success : Warning;
                _trustCertificate.Visible = offerTrust;
                _trustCertificate.Checked = offerTrust;
            }

            if (!string.IsNullOrWhiteSpace(_detection.SuggestedDashboardUrl) &&
                IsDashboardUrlAutoGenerated())
            {
                try
                {
                    var uri = new Uri(_detection.SuggestedDashboardUrl);
                    _autoDetectedHost = uri.Host;
                    _dashboardUrl.Text = "http://" + uri.Host + ":" + _dashboardPort.Text.Trim();
                }
                catch { }
            }

            if (_detection.WebClientFound)
            {
                _webClientDetectedValid = IsValidWebClientPath(_detection.WebClientPath);
                _webClientPath.Text = _detection.WebClientPath;
                _webClientDetected.Text = _webClientDetectedValid
                    ? "✓ Detected and verified: " + _detection.WebClientPath
                    : "Browse.aspx was not found. Select the Web Files folder manually.";
                _webClientDetected.ForeColor = _webClientDetectedValid ? Success : Warning;
                _webButton.Checked = _webClientDetectedValid;
                _webPathPanel.Visible = _webButton.Checked && !_webClientDetectedValid;
            }
        }

        private void OnPackageStateChanged(bool installed)
        {
            if (!installed || _installDone || _pageIndex >= PAGE_PROGRESS) return;
            _maintenanceMode = true;
            _operation = SetupOperation.Repair;
            _welcomeHeading.Text = "Laserfiche Dashboard is installed";
            _welcomeBody.Text =
                "Repair restores application files and integrations while preserving settings. " +
                "Uninstall removes the application; you can choose whether saved configuration, credentials and logs are also deleted.";
            _headerTitle.Text = "Dashboard maintenance";
            _headerSubtitle.Text = "Repair or remove the existing installation.";
            if (_pageIndex != PAGE_WELCOME) NavigateTo(PAGE_WELCOME);
            UpdateButtons();
        }

        private bool ValidateConnectionPage()
        {
            NormalizeServerUrlInput();
            var errors = new List<string>();
            if (!TryHttpUrl(_serverUrl.Text.Trim(), out _))
                errors.Add("Enter a valid Laserfiche server URL beginning with http:// or https://.");
            if (string.IsNullOrWhiteSpace(_apiBasePath.Text))
                errors.Add("API base path is required.");
            else if (!_apiBasePath.Text.Trim().StartsWith("/"))
                errors.Add("API base path must begin with /.");
            if (ContainsCommandBreakingText(_apiBasePath.Text))
                errors.Add("API path cannot contain quotes or line breaks.");

            if (errors.Count > 0) return ShowValidation(errors);

            if (_trustCertificate.Visible && !_trustCertificate.Checked)
            {
                if (MessageBox.Show(this,
                        "The detected Laserfiche certificate is self-signed and is not trusted. Continue without trusting it?",
                        "Certificate not trusted", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return false;
            }

            return ValidateLaserficheTls(GetLaserficheApiUrl());
        }

        private bool ValidateDeploymentPage()
        {
            var errors = new List<string>();
            if (!_detection.AllRequiredPresent)
                errors.Add("IIS and the ASP.NET Core Hosting Module must be installed before Dashboard can be deployed.");

            if (!TryHttpUrl(_dashboardUrl.Text.Trim(), out Uri? dashboardUri))
                errors.Add("Enter a valid Dashboard URL beginning with http:// or https://.");
            if (!int.TryParse(_dashboardPort.Text.Trim(), out int port) || port < 1 || port > 65535)
                errors.Add("IIS port must be between 1 and 65535.");
            else if (IsTcpPortInUse(port) && !DetectionService.DashboardSiteUsesPort(port))
                errors.Add("TCP port " + port + " is already used by another application.");

            if (dashboardUri != null && port >= 1 && port <= 65535)
            {
                if (dashboardUri.Scheme == Uri.UriSchemeHttp && dashboardUri.Port != port)
                    errors.Add("The Dashboard URL port must match the IIS port.");
                if (dashboardUri.Scheme == Uri.UriSchemeHttps &&
                    !DetectionService.HttpsBindingExists(dashboardUri.Host, dashboardUri.Port, out _))
                    errors.Add("The HTTPS Dashboard URL has no matching IIS certificate binding. Configure the HTTPS binding first or use http://.");
            }

            if (_webButton.Checked)
            {
                string path = GetEffectiveWebClientPath();
                if (!IsValidWebClientPath(path))
                    errors.Add("Web Client integration is selected, but Browse.aspx was not found in the selected Web Files folder.");
            }

            return errors.Count == 0 || ShowValidation(errors);
        }

        private void TestConnection()
        {
            if (!ValidateConnectionInputsForTest()) return;
            _connectionStatus.Text = "Testing credentials and repository...";
            _connectionStatus.ForeColor = Blue;
            Cursor = Cursors.WaitCursor;
            try
            {
                string[] versions = _apiVersion.SelectedIndex switch
                {
                    1 => new[] { "v1" },
                    2 => new[] { "v2" },
                    _ => new[] { "v2", "v1" }
                };
                string lastError = "The server did not accept the connection.";
                foreach (string version in versions)
                {
                    if (TryRequestToken(version, out lastError))
                    {
                        _connectionTestPassed = true;
                        _connectionStatus.Text = "✓ Connected successfully using " + version + ".";
                        _connectionStatus.ForeColor = Success;
                        return;
                    }
                }
                _connectionTestPassed = false;
                _connectionStatus.Text = "Connection failed: " + lastError;
                _connectionStatus.ForeColor = Color.Firebrick;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private bool ValidateConnectionInputsForTest()
        {
            NormalizeServerUrlInput();
            var errors = new List<string>();
            if (!TryHttpUrl(_serverUrl.Text.Trim(), out _)) errors.Add("Enter a valid Server URL.");
            if (string.IsNullOrWhiteSpace(_repositoryId.Text)) errors.Add("Enter the Repository ID.");
            if (string.IsNullOrWhiteSpace(_username.Text)) errors.Add("Enter the username.");
            if (string.IsNullOrEmpty(_password.Text)) errors.Add("Enter the password.");
            return errors.Count == 0 || ShowValidation(errors);
        }

        private bool TryRequestToken(string version, out string error)
        {
            error = "Unknown error";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string url = GetLaserficheApiUrl().TrimEnd('/') + "/" + version +
                    "/Repositories/" + Uri.EscapeDataString(_repositoryId.Text.Trim()) + "/Token";
                byte[] body = Encoding.UTF8.GetBytes(
                    "grant_type=password&username=" + FormEncode(_username.Text.Trim()) +
                    "&password=" + FormEncode(_password.Text));
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Timeout = 15000;
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                Array.Clear(body, 0, body.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 400) return true;
                    error = "HTTP " + (int)response.StatusCode;
                    return false;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse response)
                {
                    int code = (int)response.StatusCode;
                    error = code == 400 || code == 401 || code == 403
                        ? "The repository, username or password was rejected (HTTP " + code + ")."
                        : "Laserfiche returned HTTP " + code + ".";
                }
                else
                {
                    error = ex.Status + ": " + ex.Message;
                }
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void CollectConfiguration()
        {
            NormalizeServerUrlInput();
            _config.LaserficheServerUrl = _serverUrl.Text.Trim().TrimEnd('/');
            _config.LaserficheApiBasePath = "/" + _apiBasePath.Text.Trim().Trim('/');
            _config.LaserficheApiUrl = GetLaserficheApiUrl();
            _config.LaserficheApiVersion = _apiVersion.SelectedIndex switch
            {
                1 => "v1",
                2 => "v2",
                _ => "Auto"
            };
            // Repository selection and credentials are part of Dashboard's
            // normal sign-in flow, not the installation wizard.
            _config.RepositoryId = "";
            _config.DisplayName = "";
            _config.RootEntryId = "1";
            _config.TimeoutSeconds = "30";
            _config.Username = "";
            _config.Password = "";
            _config.CredentialImportPath = "";
            _config.DashboardUrl = _dashboardUrl.Text.Trim().TrimEnd('/');
            _config.DashboardPort = _dashboardPort.Text.Trim();
            _config.InstallDesktopButton = _desktopButton.Checked;
            _config.InstallWebButton = _webButton.Checked;
            _config.LFWebClientPath = _webButton.Checked ? GetEffectiveWebClientPath() : "";
            _config.TrustSelfSignedCert = _trustCertificate.Visible && _trustCertificate.Checked;
            _config.LaunchDashboard = true;
        }

        private bool PrepareCredentialImport()
        {
            CredentialStager.TryDelete(_config.CredentialImportPath);
            try
            {
                _config.CredentialImportPath = CredentialStager.Create(_config.Username, _config.Password);
                _config.Password = "";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Setup could not protect the Laserfiche credentials.\r\n\r\n" + ex.Message,
                    "Credential protection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void UpdateReadySummary()
        {
            var text = new StringBuilder();
            text.AppendLine("DASHBOARD");
            text.AppendLine("  Address:  " + _config.DashboardUrl);
            text.AppendLine("  IIS port: " + _config.DashboardPort);
            text.AppendLine();
            text.AppendLine("LASERFICHE CONNECTION");
            text.AppendLine("  API:        " + _config.LaserficheApiUrl);
            text.AppendLine("  Version:    " + (_config.LaserficheApiVersion == "Auto" ? "Auto Detect" : _config.LaserficheApiVersion));
            text.AppendLine();
            text.AppendLine("INTEGRATIONS");
            text.AppendLine("  Desktop Client button: " + (_config.InstallDesktopButton ? "Install" : "Skip"));
            text.AppendLine("  Web Client button:     " + (_config.InstallWebButton ? "Install" : "Skip"));
            if (_config.InstallWebButton) text.AppendLine("  Web Files path:        " + _config.LFWebClientPath);
            _readySummary.Text = text.ToString();
        }

        private void StartInstallation()
        {
            string action = _operation == SetupOperation.Uninstall
                ? "uninstallation"
                : _operation == SetupOperation.Repair ? "repair" : "installation";
            AppendLog("Starting " + action + "...");
            _progress.Value = 0;
            _currentAction.Text = "Preparing " + action + "...";
            if (_operation == SetupOperation.Uninstall)
                _ba.StartUninstall(Handle, _removeUserData);
            else if (_operation == SetupOperation.Repair)
                _ba.StartRepair(Handle);
            else
                _ba.StartInstall(_config, Handle);
        }

        private void OnProgressUpdated(int percent, string? message)
        {
            if (message != null) AppendLog(message);
            if (percent >= 0 && percent <= 100)
            {
                _progress.Value = percent;
                _currentAction.Text = message ?? "Installing... " + percent + "%";
            }
        }

        private void OnInstallFinished(bool success, string message)
        {
            _installDone = true;
            _installSuccess = success;
            _installMessage = message;
            AppendLog((success ? "SUCCESS: " : "FAILED: ") + message);
            NavigateTo(PAGE_COMPLETE);
        }

        private void UpdateCompletePage()
        {
            _launchDashboard.Visible = _installSuccess && _operation != SetupOperation.Uninstall;
            _launchDashboard.Checked = _config.LaunchDashboard;
            if (_installSuccess)
            {
                _completeIcon.Text = "✓";
                _completeIcon.ForeColor = Success;
                _completeIcon.BackColor = Color.FromArgb(226, 246, 237);
                _completeTitle.Text = _operation == SetupOperation.Uninstall
                    ? "Dashboard removed"
                    : _operation == SetupOperation.Repair ? "Repair complete" : "Dashboard is ready";
                if (_operation == SetupOperation.Uninstall)
                {
                    _completeDetail.Text = _removeUserData
                        ? "The application and its saved configuration, credentials and logs were removed."
                        : "The application was removed. Saved configuration, credentials and logs were kept for a future reinstall.";
                }
                else if (_operation == SetupOperation.Repair)
                {
                    _completeDetail.Text = "Application files and integrations were repaired. Your saved configuration and credentials were preserved.";
                }
                else
                {
                    _completeDetail.Text =
                        "Dashboard was installed and fully configured. No additional Settings-page setup is required.\r\n\r\n" +
                        "Address: " + _config.DashboardUrl;
                }
            }
            else
            {
                _completeIcon.Text = "!";
                _completeIcon.ForeColor = Color.Firebrick;
                _completeIcon.BackColor = Color.FromArgb(253, 232, 232);
                _completeTitle.Text = "Setup did not complete";
                _completeDetail.Text = _installMessage +
                    "\r\n\r\nOpen setup logs for more information, then run the installer again.";
            }
        }

        private void AppendLog(string value)
        {
            if (IsDisposed) return;
            if (_log.InvokeRequired)
            {
                _log.BeginInvoke(new Action(() => AppendLog(value)));
                return;
            }
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + value + "\r\n");
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void LaunchDashboard()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _config.DashboardUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Dashboard was installed, but the browser could not be opened.\r\n\r\n" +
                    _config.DashboardUrl + "\r\n\r\n" + ex.Message,
                    "Open Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OpenSetupLogs()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Dashboard", "Logs");
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Setup logs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool ConfirmUninstall(out bool removeUserData)
        {
            removeUserData = false;
            using var dialog = new Form
            {
                Text = "Uninstall Laserfiche Dashboard",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(500, 222),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            var heading = new Label
            {
                Text = "Remove Laserfiche Dashboard?",
                Location = new Point(22, 20),
                AutoSize = true,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold)
            };
            var body = new Label
            {
                Text = "The IIS site, application pool, shortcuts, installed files and client integrations will be removed.",
                Location = new Point(24, 56),
                Size = new Size(450, 46),
                ForeColor = Muted
            };
            var remove = new CheckBox
            {
                Text = "Also delete saved configuration, credentials and logs",
                Location = new Point(24, 112),
                AutoSize = true,
                ForeColor = Color.Firebrick
            };
            var uninstall = CreateButton("Uninstall", true, 100);
            uninstall.DialogResult = DialogResult.OK;
            uninstall.Location = new Point(270, 169);
            var cancel = CreateButton("Cancel", false, 90);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(382, 169);
            dialog.Controls.AddRange(new Control[] { heading, body, remove, uninstall, cancel });
            dialog.AcceptButton = uninstall;
            dialog.CancelButton = cancel;
            bool confirmed = dialog.ShowDialog(this) == DialogResult.OK;
            removeUserData = confirmed && remove.Checked;
            return confirmed;
        }

        private bool ValidateLaserficheTls(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
            Cursor = Cursors.WaitCursor;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                System.Net.Security.SslPolicyErrors errors = System.Net.Security.SslPolicyErrors.None;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 10000;
                request.ServerCertificateValidationCallback = (s, cert, chain, policyErrors) =>
                {
                    errors = policyErrors;
                    return policyErrors == System.Net.Security.SslPolicyErrors.None;
                };
                try
                {
                    using (request.GetResponse()) return true;
                }
                catch (WebException ex)
                {
                    if (ex.Response != null) return true;
                    if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors &&
                        _trustCertificate.Visible && _trustCertificate.Checked)
                        return true;
                    if (errors != System.Net.Security.SslPolicyErrors.None ||
                        ex.Status == WebExceptionStatus.TrustFailure ||
                        ex.Status == WebExceptionStatus.SecureChannelFailure)
                    {
                        MessageBox.Show(this,
                            "Laserfiche API certificate validation failed. Verify the server name and certificate, or use the detected certificate trust option.",
                            "TLS validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    return MessageBox.Show(this,
                        "The Laserfiche API is not reachable right now. Continue with this address anyway?\r\n\r\n" + ex.Message,
                        "Server not reachable", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                }
            }
            catch
            {
                return true;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void NormalizeServerUrlInput()
        {
            string value = _serverUrl.Text.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return;
            string path = uri.AbsolutePath.TrimEnd('/');
            int marker = path.IndexOf("/LFRepositoryAPI", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                _serverUrl.Text = uri.GetLeftPart(UriPartial.Authority) + path.Substring(0, marker);
                string api = path.Substring(marker);
                string[] parts = api.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                _apiBasePath.Text = parts.Length > 0 ? "/" + parts[0] : "/LFRepositoryAPI";
                if (parts.Length > 1)
                {
                    if (string.Equals(parts[1], "v1", StringComparison.OrdinalIgnoreCase)) _apiVersion.SelectedIndex = 1;
                    if (string.Equals(parts[1], "v2", StringComparison.OrdinalIgnoreCase)) _apiVersion.SelectedIndex = 2;
                }
            }
        }

        private string GetLaserficheApiUrl()
        {
            string server = _serverUrl.Text.Trim().TrimEnd('/');
            string path = "/" + _apiBasePath.Text.Trim().Trim('/');
            if (server.EndsWith(path, StringComparison.OrdinalIgnoreCase))
                return server;
            return server + path;
        }

        private static void SplitApiUrl(string fullUrl, out string server, out string path)
        {
            server = fullUrl.TrimEnd('/');
            path = "/LFRepositoryAPI";
            if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out Uri? uri)) return;
            string absolutePath = uri.AbsolutePath.TrimEnd('/');
            int marker = absolutePath.IndexOf("/LFRepositoryAPI", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                server = uri.GetLeftPart(UriPartial.Authority) + absolutePath.Substring(0, marker);
                path = absolutePath.Substring(marker);
            }
        }

        private void AutoUpdateDashboardUrl()
        {
            if (string.IsNullOrWhiteSpace(_autoDetectedHost) || !IsDashboardUrlAutoGenerated()) return;
            string port = _dashboardPort.Text.Trim();
            if (port.Length > 0) _dashboardUrl.Text = "http://" + _autoDetectedHost + ":" + port;
        }

        private bool IsDashboardUrlAutoGenerated()
        {
            string value = _dashboardUrl.Text.Trim();
            return string.IsNullOrWhiteSpace(value) ||
                   value.StartsWith("http://" + _autoDetectedHost + ":", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTcpPortInUse(int port)
        {
            try
            {
                foreach (var endpoint in System.Net.NetworkInformation.IPGlobalProperties
                             .GetIPGlobalProperties().GetActiveTcpListeners())
                    if (endpoint.Port == port) return true;
            }
            catch { }
            return false;
        }

        private string GetEffectiveWebClientPath() => _webClientDetectedValid
            ? _detection.WebClientPath
            : _webClientPath.Text.Trim().TrimEnd('\\', '/');

        private static bool IsValidWebClientPath(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Browse.aspx")); }
            catch { return false; }
        }

        private void BrowseForWebClient()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the Laserfiche Web Files folder containing Browse.aspx",
                SelectedPath = _webClientPath.Text
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _webClientPath.Text = dialog.SelectedPath;
                _webClientDetectedValid = false;
            }
        }

        private static bool TryHttpUrl(string value, out Uri? uri)
        {
            bool parsed = Uri.TryCreate(value, UriKind.Absolute, out uri);
            return parsed && uri != null &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                   !string.IsNullOrWhiteSpace(uri.Host);
        }

        private static bool ContainsCommandBreakingText(string value) =>
            value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;

        private bool ShowValidation(IReadOnlyCollection<string> errors)
        {
            MessageBox.Show(this, string.Join("\r\n\r\n", errors), "Check setup information",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private static string FormEncode(string value) => Uri.EscapeDataString(value).Replace("%20", "+");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ba.ProgressUpdated -= OnProgressUpdated;
                _ba.InstallFinished -= OnInstallFinished;
                _ba.PackageStateChanged -= OnPackageStateChanged;
                CredentialStager.TryDelete(_config.CredentialImportPath);
            }
            base.Dispose(disposing);
        }

        private static TableLayoutPanel CreateGrid()
        {
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 0,
                BackColor = Canvas,
                Padding = new Padding(0, 0, 12, 12)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            return grid;
        }

        private static void AddSectionHeader(TableLayoutPanel grid, int row, string text)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            grid.RowCount = Math.Max(grid.RowCount, row + 1);
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Padding = new Padding(4, 11, 0, 0),
                ForeColor = Navy,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
            };
            grid.Controls.Add(label, 0, row);
            grid.SetColumnSpan(label, 2);
        }

        private static void AddField(
            TableLayoutPanel grid, int row, int column, string label, string hint, Control input, int span = 1)
        {
            if (grid.RowStyles.Count <= row) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            grid.RowCount = Math.Max(grid.RowCount, row + 1);
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 8, 2) };
            var title = new Label
            {
                Text = label,
                Dock = DockStyle.Top,
                Height = 23,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold)
            };
            input.Dock = DockStyle.Top;
            input.Height = 30;
            var help = new Label
            {
                Text = hint,
                Dock = DockStyle.Bottom,
                Height = 23,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 7.7F)
            };
            panel.Controls.Add(help);
            panel.Controls.Add(input);
            panel.Controls.Add(title);
            grid.Controls.Add(panel, column, row);
            if (span > 1) grid.SetColumnSpan(panel, span);
        }

        private static void AddFullRow(TableLayoutPanel grid, int row, Control control, int height)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            grid.RowCount = Math.Max(grid.RowCount, row + 1);
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
        }

        private static TextBox CreateTextBox(string placeholder)
        {
            var box = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F),
                Tag = placeholder
            };
            // .NET Framework WinForms has no PlaceholderText. A tooltip gives
            // the same guidance without placing fake values in the field.
            new ToolTip().SetToolTip(box, placeholder);
            return box;
        }

        private static Button CreateButton(string text, bool primary, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 36),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                BackColor = primary ? Blue : Color.White,
                ForeColor = primary ? Color.White : TextColor,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private static Panel CreateCard() => new Panel
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        private static Panel CreateFeatureCard(string number, string title, string body)
        {
            var panel = CreateCard();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 0, 10, 0);
            panel.Padding = new Padding(14);
            panel.Controls.Add(new Label
            {
                Text = body,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 7, 0, 0),
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8.2F)
            });
            panel.Controls.Add(new Label
            {
                Text = number + "   " + title,
                Dock = DockStyle.Top,
                Height = 29,
                ForeColor = Navy,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
            });
            return panel;
        }

        private static Panel CreateOptionCard(CheckBox option, string help)
        {
            var card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(4, 2, 8, 6);
            card.Padding = new Padding(14);
            option.Dock = DockStyle.Top;
            option.Height = 28;
            var hint = new Label
            {
                Text = help,
                Dock = DockStyle.Fill,
                Padding = new Padding(21, 7, 0, 0),
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8F)
            };
            card.Controls.Add(hint);
            card.Controls.Add(option);
            return card;
        }

        private static Label StatusLabel(string text, int top) => new Label
        {
            Text = "•  " + text + ": checking...",
            Location = new Point(18, top),
            Size = new Size(575, 22),
            ForeColor = Muted
        };
    }
}
