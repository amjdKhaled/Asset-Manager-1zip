// WizardForm.cs
// 5-page setup wizard for the Dashboard managed bootstrapper application.
//
// PAGES (by PAGE_* constant):
//   0  Welcome   -- product overview / prerequisites; background detection starts
//   1  Config    -- Laserfiche connection (required) + Advanced Settings (optional)
//   2  Ready     -- summary before installing
//   3  Progress  -- progress bar and log during installation
//   4  Complete  -- success or failure message; Finish button closes the form
//
// Detection runs silently in the background (BackgroundWorker) while the user
// is on the Welcome page.  Results pre-fill config fields when PAGE_CONFIG is shown.
//
// All controls are created programmatically (no Designer.cs).
// All UI mutations happen on the UI thread; engine callbacks use BeginInvoke.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace Dashboard.BA
{
    internal sealed class WizardForm : Form
    {
        // ---------------------------------------------------------------- State
        private readonly DashboardBA  _ba;
        private readonly InstallConfig _config   = new InstallConfig();
        private DetectionResult        _detection = new DetectionResult();
        private bool   _detectionDone  = false;
        private bool   _installDone    = false;
        private bool   _installSuccess = false;
        private string _installMessage = "";
        private int    _pageIndex      = 0;

        // Hostname captured at startup; used to decide whether Dashboard URL is
        // still auto-generated and can be updated when the port field changes.
        private string _autoDetectedHost = "";

        // ---------------------------------------------------------------- Pages
        private const int PAGE_WELCOME  = 0;
        private const int PAGE_CONFIG   = 1;
        private const int PAGE_READY    = 2;
        private const int PAGE_PROGRESS = 3;
        private const int PAGE_COMPLETE = 4;

        private readonly Panel[] _pages = new Panel[5];

        // ---------------------------------------------------------------- Layout controls
        private Panel  _headerPanel       = null!;
        private Label  _lblHeaderTitle    = null!;
        private Label  _lblHeaderSubtitle = null!;
        private Panel  _contentPanel      = null!;
        private Button _btnBack           = null!;
        private Button _btnNext           = null!;
        private Button _btnCancel         = null!;

        // ---------------------------------------------------------------- Config page controls
        private TextBox  _txtLFApiUrl      = null!;
        private TextBox  _txtDashboardUrl  = null!;
        private TextBox  _txtPort          = null!;
        private CheckBox _chkDesktop       = null!;
        private CheckBox _chkWebClient     = null!;
        private TextBox  _txtWebClientPath = null!;
        private Panel    _pnlWebClientPath = null!;
        private Label    _lblWebClientDetected = null!;

        // True only when the IIS-detected Web Client path still contains
        // Browse.aspx at the time detection results are applied.  A stale
        // persisted path (Web Client moved/uninstalled) leaves this false so
        // the wizard falls back to manual entry instead of letting the MSI
        // DeployWebClient action fail later.
        private bool _webClientDetectedValid = false;

        // ---------------------------------------------------------------- Ready page controls
        private Label _lblReadySummary = null!;

        // ---------------------------------------------------------------- Progress page controls
        private ProgressBar _progressBar      = null!;
        private Label       _lblCurrentAction = null!;
        private TextBox     _txtLog           = null!;

        // ---------------------------------------------------------------- Complete page controls
        private Label _lblCompleteTitle  = null!;
        private Label _lblCompleteDetail = null!;

        // Single-instance guard — exactly ONE WizardForm may exist per Burn process.
        // A second construction means a stale Dashboard.BA.dll is being loaded; this
        // throws immediately so Burn surfaces the error rather than silently opening
        // a second, unexpected configuration window.
        private static int _instanceCount = 0;

        // ---------------------------------------------------------------- Page metadata
        private static readonly string[] PageTitles =
        {
            "Welcome to Laserfiche Dashboard Setup",
            "Dashboard Configuration",
            "Ready to Install",
            "Installing Dashboard...",
            "Setup Complete"
        };
        private static readonly string[] PageSubtitles =
        {
            "This wizard will install and configure Laserfiche Dashboard.",
            "Enter your Laserfiche connection details.",
            "Review your choices, then click Install to begin.",
            "Please wait while Dashboard is being configured.",
            ""
        };

        // ================================================================
        // Constructor and layout
        // ================================================================

        public WizardForm(DashboardBA ba)
        {
            StartupLogger.Log("WizardForm constructor entered");

            int count = System.Threading.Interlocked.Increment(ref _instanceCount);
            StartupLogger.Log(
                $"WizardForm instance #{count}  PID={System.Diagnostics.Process.GetCurrentProcess().Id}  " +
                $"Assembly={System.Reflection.Assembly.GetExecutingAssembly().Location}  " +
                $"Time={DateTime.UtcNow:O}");

            if (count > 1)
            {
                string bug =
                    $"WizardForm instance #{count} constructed in a single Burn process. " +
                    "A stale or duplicate Dashboard.BA.dll is being loaded. " +
                    "Clean installer\\Dashboard.BA\\bin\\ and rebuild.";
                StartupLogger.Log("FATAL: " + bug);
                throw new InvalidOperationException(bug);
            }

            _ba = ba;
            _ba.ProgressUpdated += OnProgressUpdated;
            _ba.InstallFinished += OnInstallFinished;

            StartupLogger.Log("WizardForm: event subscriptions done, calling BuildForm()");
            try
            {
                BuildForm();
                StartupLogger.Log("WizardForm: BuildForm() completed successfully");
            }
            catch (Exception ex)
            {
                StartupLogger.LogException("WizardForm.BuildForm() FAILED", ex);
                throw;
            }
        }

        private void BuildForm()
        {
            // AutoScaleMode=Font makes the window scale correctly with Windows
            // DPI / text-size settings (125 %, 150 %, etc.).
            Text                = "Laserfiche Dashboard Setup";
            AutoScaleMode       = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);   // Segoe UI 9pt at 96 DPI baseline
            ClientSize          = new Size(620, 490);
            FormBorderStyle     = FormBorderStyle.FixedSingle;
            MaximizeBox         = false;
            StartPosition       = FormStartPosition.CenterScreen;
            BackColor           = Color.White;
            Font                = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // ---- Header (dark-blue banner) ----
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

            var hdrLine = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 1,
                BackColor = Color.FromArgb(0, 40, 100)
            };

            // ---- Footer (light-gray band with navigation buttons) ----
            var footerPanel = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 52,
                BackColor = Color.FromArgb(240, 240, 240)
            };
            var ftrLine = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 1,
                BackColor = Color.FromArgb(210, 210, 210)
            };

            _btnCancel = MakeButton("Cancel", 88);
            _btnNext   = MakeButton("Next >", 88);
            _btnBack   = MakeButton("< Back", 80);

            // Right-align: Cancel | Next | Back
            _btnCancel.Location = new Point(620 - 88 - 12,           14);
            _btnNext.Location   = new Point(620 - 88 - 88 - 18,      14);
            _btnBack.Location   = new Point(620 - 88 - 88 - 80 - 24, 14);

            footerPanel.Controls.AddRange(new Control[] { _btnBack, _btnNext, _btnCancel });

            // ---- Content area (fills middle between header and footer) ----
            _contentPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.White,
                Padding   = new Padding(24, 20, 24, 12)
            };

            // Dock-add order: Fill first, then Bottom, then Top.
            // (WinForms processes dock in reverse z-order; last-added = index 0 = docked first.)
            Controls.Add(_contentPanel);
            Controls.Add(ftrLine);
            Controls.Add(footerPanel);
            Controls.Add(hdrLine);
            Controls.Add(_headerPanel);

            // ---- Build all pages ----
            _pages[PAGE_WELCOME]  = CreateWelcomePage();
            _pages[PAGE_CONFIG]   = CreateConfigPage();
            _pages[PAGE_READY]    = CreateReadyPage();
            _pages[PAGE_PROGRESS] = CreateProgressPage();
            _pages[PAGE_COMPLETE] = CreateCompletePage();

            foreach (var page in _pages)
            {
                page.Dock    = DockStyle.Fill;
                page.Visible = false;
                _contentPanel.Controls.Add(page);
            }

            // ---- Wire navigation buttons ----
            _btnBack.Click   += (s, e) => GoBack();
            _btnNext.Click   += (s, e) => GoNext();
            _btnCancel.Click += (s, e) => OnCancelClicked();

            // ---- Form load: show Welcome; start background scans ----
            Load += (s, e) =>
            {
                NavigateTo(PAGE_WELCOME);
                _ba.StartDetect(Handle);   // Burn package-state detection
                StartDetection();          // DetectionService environment scan
            };
        }

        // ================================================================
        // Page creation
        // ================================================================

        private Panel CreateWelcomePage()
        {
            var p = new Panel();

            var heading = PageHeading("Welcome to Laserfiche Dashboard Setup");
            heading.Location = new Point(0, 0);
            p.Controls.Add(heading);

            p.Controls.Add(new Label
            {
                Text =
                    "This wizard installs the Dashboard web application on this server and " +
                    "optionally integrates with the Laserfiche Desktop Client and Web Client.\r\n\r\n" +
                    "Before continuing, ensure the following are present on this server:\r\n\r\n" +
                    "  \u2022  IIS (Internet Information Services)  \u2014  enabled via Windows Features\r\n\r\n" +
                    "  \u2022  ASP.NET Core 8 Windows Hosting Bundle\r\n" +
                    "       https://dotnet.microsoft.com/download/dotnet/8.0\r\n\r\n" +
                    "  \u2022  Microsoft Edge WebView2 Runtime\r\n" +
                    "       (required only for the Laserfiche Desktop Client Extension)\r\n\r\n" +
                    "Click Next to continue. Your system will be scanned in the background " +
                    "and values on the next page will be pre-filled where possible.",
                AutoSize = false,
                Size     = new Size(560, 270),
                Location = new Point(0, 36),
                Font     = new Font("Segoe UI", 9F)
            });

            return p;
        }

        // ----------------------------------------------------------------
        // Config page: required fields at top; Advanced Settings GroupBox below.
        // The middle section scrolls so the form never clips at high DPI.
        // ----------------------------------------------------------------
        private Panel CreateConfigPage()
        {
            // Column layout constants (relative to scroll panel origin)
            const int LBL_X    = 0;    // label left edge
            const int FLD_X    = 162;  // field left edge
            const int FLD_W    = 396;  // field width  (162+396=558 < 572 content width)
            const int GRP_W    = 558;  // GroupBox width

            // ---- Outer panel (Dock=Fill set by BuildForm) ----
            var outer = new Panel();

            // ---- Fixed heading at top (docked) ----
            var heading = PageHeading("Dashboard Configuration");
            heading.Dock = DockStyle.Top;   // AutoSize=true → height auto-sizes

            var headingSpacer = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.White };

            // ---- Scrollable content fills remaining space ----
            var scroll = new Panel
            {
                Dock       = DockStyle.Fill,
                AutoScroll = true,
                Padding    = new Padding(0)
            };

            int y = 2;  // y position inside scroll panel

            // ---- Helper: add label + textbox (+ optional hint) to scroll ----
            TextBox AddField(string labelText, string defaultText, string hint)
            {
                var lbl = new Label
                {
                    Text     = labelText,
                    AutoSize = true,
                    Location = new Point(LBL_X, y + 3)
                };
                scroll.Controls.Add(lbl);

                var box = new TextBox
                {
                    Location = new Point(FLD_X, y),
                    Size     = new Size(FLD_W, 22),
                    Text     = defaultText
                };
                scroll.Controls.Add(box);

                if (!string.IsNullOrEmpty(hint))
                {
                    scroll.Controls.Add(new Label
                    {
                        Text      = hint,
                        AutoSize  = true,
                        Location  = new Point(FLD_X, y + 25),
                        ForeColor = Color.FromArgb(100, 100, 100),
                        Font      = new Font("Segoe UI", 7.5F)
                    });
                    y += 52;
                }
                else
                {
                    y += 30;
                }

                return box;
            }

            // ---- Section label ----
            scroll.Controls.Add(new Label
            {
                Text      = "Laserfiche Connection",
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 62, 134),
                AutoSize  = true,
                Location  = new Point(LBL_X, y)
            });
            y += 20;

            // ---- Required fields ----
            _txtLFApiUrl = AddField(
                "LF API URL *",
                "https://YOUR-LF-SERVER/LFRepositoryAPI",
                "Full URL of the Laserfiche Repository API.  Example: https://lf-server/LFRepositoryAPI");

            // Repository ID / Display Name fields intentionally removed:
            // the repository is selected per session at runtime (Desktop/Web
            // Client context or login page), never fixed at install time.

            y += 6;  // gap before Advanced section

            // ================================================================
            // Advanced Settings GroupBox
            // ================================================================
            var grp = new GroupBox
            {
                Text     = "Advanced Settings",
                Font     = new Font("Segoe UI", 8.5F),
                Location = new Point(LBL_X, y),
                Width    = GRP_W
            };

            // Inner layout (GroupBox-relative coordinates)
            const int G_LBL_X = 8;
            const int G_FLD_X = 162;
            const int G_FLD_W = GRP_W - G_FLD_X - 16;  // right margin 16
            int gy = 22;

            // Helper: add label + textbox (+ optional hint) inside the GroupBox
            TextBox AddGrpField(string labelText, string defaultText, string hint)
            {
                grp.Controls.Add(new Label
                {
                    Text     = labelText,
                    AutoSize = true,
                    Location = new Point(G_LBL_X, gy + 3)
                });

                var box = new TextBox
                {
                    Location = new Point(G_FLD_X, gy),
                    Size     = new Size(G_FLD_W, 22),
                    Text     = defaultText
                };
                grp.Controls.Add(box);

                if (!string.IsNullOrEmpty(hint))
                {
                    grp.Controls.Add(new Label
                    {
                        Text      = hint,
                        AutoSize  = true,
                        Location  = new Point(G_FLD_X, gy + 25),
                        ForeColor = Color.FromArgb(100, 100, 100),
                        Font      = new Font("Segoe UI", 7.5F)
                    });
                    gy += 52;
                }
                else
                {
                    gy += 30;
                }

                return box;
            }

            // Build the suggested Dashboard URL from this machine's hostname
            try   { _autoDetectedHost = Dns.GetHostName(); }
            catch { _autoDetectedHost = "localhost"; }

            _txtDashboardUrl = AddGrpField(
                "Dashboard URL *",
                $"http://{_autoDetectedHost}:5000",
                "URL browsers use to reach Dashboard.  Uses this server's hostname; changes with Port below.");

            _txtPort = AddGrpField(
                "IIS Port *",
                "5000",
                "HTTP port for the IIS site binding.  Changing this updates Dashboard URL automatically.");

            // Wire port-change → Dashboard URL auto-update
            _txtPort.TextChanged += (s, e) => AutoUpdateDashboardUrl();

            // ---- Separator ----
            gy += 2;
            grp.Controls.Add(new Panel
            {
                BackColor = Color.FromArgb(210, 210, 210),
                Location  = new Point(G_LBL_X, gy),
                Size      = new Size(GRP_W - G_LBL_X * 2, 1)
            });
            gy += 10;

            // ---- Desktop Client checkbox ----
            _chkDesktop = new CheckBox
            {
                Text     = "Register Laserfiche Desktop Client toolbar button",
                AutoSize = true,
                Location = new Point(G_LBL_X, gy),
                Checked  = true
            };
            grp.Controls.Add(_chkDesktop);
            gy += 22;

            grp.Controls.Add(new Label
            {
                Text      = "Installs extension files and registers the toolbar button.  " +
                            "Non-fatal if Desktop Client is not yet installed on this machine.",
                AutoSize  = false,
                Size      = new Size(GRP_W - G_LBL_X - 16, 28),
                Location  = new Point(G_LBL_X + 16, gy),
                ForeColor = Color.FromArgb(100, 100, 100),
                Font      = new Font("Segoe UI", 7.5F)
            });
            gy += 32;

            // ---- Web Client checkbox ----
            _chkWebClient = new CheckBox
            {
                Text     = "Deploy Laserfiche Web Client button (patches Browse.aspx)",
                AutoSize = true,
                Location = new Point(G_LBL_X, gy),
                Checked  = false
            };
            grp.Controls.Add(_chkWebClient);
            gy += 22;

            // Detected-path label (filled in when detection results are applied)
            _lblWebClientDetected = new Label
            {
                Text      = "",
                AutoSize  = false,
                Size      = new Size(GRP_W - G_LBL_X - 32, 28),
                Location  = new Point(G_LBL_X + 16, gy),
                ForeColor = Color.FromArgb(100, 100, 100),
                Font      = new Font("Segoe UI", 7.5F),
                Visible   = false
            };
            grp.Controls.Add(_lblWebClientDetected);
            gy += 32;

            // Manual path entry (shown when checked but Web Client not auto-detected)
            _pnlWebClientPath = new Panel
            {
                Location = new Point(G_LBL_X + 16, gy),
                Size     = new Size(GRP_W - G_LBL_X - 32, 26),
                Visible  = false
            };
            grp.Controls.Add(_pnlWebClientPath);

            _pnlWebClientPath.Controls.Add(new Label
            {
                Text     = "Web Files path:",
                AutoSize = true,
                Location = new Point(0, 4)
            });
            _txtWebClientPath = new TextBox
            {
                Location = new Point(110, 0),
                Size     = new Size(240, 22),
                Text     = ""
            };
            _pnlWebClientPath.Controls.Add(_txtWebClientPath);

            var browseBtn = new Button
            {
                Text     = "Browse...",
                Location = new Point(356, 0),
                Size     = new Size(72, 22)
            };
            browseBtn.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description         = "Select the Laserfiche Web Files directory (contains Browse.aspx)",
                    ShowNewFolderButton = false
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtWebClientPath.Text = dlg.SelectedPath;
            };
            _pnlWebClientPath.Controls.Add(browseBtn);

            gy += 30;  // space for path panel whether visible or not

            _chkWebClient.CheckedChanged += (s, e) =>
            {
                _pnlWebClientPath.Visible = _chkWebClient.Checked && !_webClientDetectedValid;
            };

            // ---- Size GroupBox to fit its content ----
            grp.Height = gy + 10;

            scroll.Controls.Add(grp);
            y += grp.Height + 8;

            // ---- Required note at bottom of scroll area ----
            scroll.Controls.Add(new Label
            {
                Text      = "* Required  \u2014  Advanced Settings work for most installations with their defaults.",
                AutoSize  = true,
                Location  = new Point(LBL_X, y),
                ForeColor = Color.FromArgb(100, 100, 100),
                Font      = new Font("Segoe UI", 7.5F)
            });

            // ---- Assemble outer: Fill first, then Top (z-order makes Top dock first) ----
            outer.Controls.Add(scroll);       // Fill  — added first → higher z-index → docked second
            outer.Controls.Add(headingSpacer); // Top   — docked before scroll
            outer.Controls.Add(heading);       // Top   — docked first (lowest z-index)

            return outer;
        }

        private Panel CreateReadyPage()
        {
            var p = new Panel();
            p.Controls.Add(PageHeading("Ready to Install"));

            _lblReadySummary = new Label
            {
                Text     = "",
                AutoSize = false,
                Size     = new Size(560, 280),
                Location = new Point(0, 36),
                Font     = new Font("Segoe UI", 9F)
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
                Location = new Point(0, 30),
                Size     = new Size(560, 22),
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
                Location   = new Point(0, 80),
                Size       = new Size(560, 222),
                Multiline  = true,
                ReadOnly   = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor  = Color.FromArgb(250, 250, 250),
                Font       = new Font("Consolas", 8F),
                WordWrap   = false
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
                Text     = "",
                AutoSize = false,
                Size     = new Size(560, 240),
                Location = new Point(0, 44),
                Font     = new Font("Segoe UI", 9F)
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
            _pageIndex                 = idx;
            _pages[idx].Visible        = true;

            _lblHeaderTitle.Text    = PageTitles[idx];
            _lblHeaderSubtitle.Text = PageSubtitles[idx];

            UpdateButtons();
            OnPageActivated(idx);
        }

        private void OnPageActivated(int page)
        {
            switch (page)
            {
                case PAGE_CONFIG:
                    // Detection may have finished while user was on Welcome
                    if (_detectionDone) ApplyDetectionToConfig();
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
            if (_pageIndex == PAGE_CONFIG)
            {
                if (!ValidateConfigPage()) return;
                CollectConfigPage();
            }

            if (_pageIndex < PAGE_COMPLETE)
                NavigateTo(_pageIndex + 1);
            else
                Close();   // Finish button on PAGE_COMPLETE → close the installer
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

        // Block the X-button (and programmatic Close()) while installation
        // is actively running so Burn is not left in an inconsistent state.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_pageIndex == PAGE_PROGRESS && !_installDone)
            {
                e.Cancel = true;
                MessageBox.Show(this,
                    "Installation is in progress. Please wait for it to complete.",
                    "Installation in Progress",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            base.OnFormClosing(e);
        }

        // ================================================================
        // Background detection
        // ================================================================

        private void StartDetection()
        {
            _detectionDone = false;
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = DetectionService.Detect(); };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed || e.Error != null || e.Result == null) return;
                _detection     = (DetectionResult)e.Result;
                _detectionDone = true;

                // If user already navigated to Config, apply now; otherwise
                // ApplyDetectionToConfig() will be called in OnPageActivated.
                if (_pageIndex == PAGE_CONFIG && !IsDisposed)
                    ApplyDetectionToConfig();
            };
            worker.RunWorkerAsync();
        }

        // Pre-fill config fields from detection results.
        // Idempotent: only updates Dashboard URL when it still looks auto-generated.
        private void ApplyDetectionToConfig()
        {
            // Dashboard URL: update only when the field still holds the auto value
            if (!string.IsNullOrEmpty(_detection.SuggestedDashboardUrl)
                && IsDashboardUrlAutoGenerated())
            {
                try
                {
                    var detectedUri = new Uri(_detection.SuggestedDashboardUrl);
                    // Preserve the port the user may have already changed in the Port field
                    int port = 5000;
                    int.TryParse(_txtPort.Text.Trim(), out port);
                    _txtDashboardUrl.Text = $"http://{detectedUri.Host}:{port}";
                    _autoDetectedHost     = detectedUri.Host;
                }
                catch { /* leave URL as-is */ }
            }

            // Web Client path / checkbox
            //
            // Re-verify the detected path RIGHT NOW: detection may have surfaced a
            // stale persisted path (Web Client moved or uninstalled since the last
            // install).  Only a path that still contains Browse.aspx pre-checks the
            // deploy checkbox; otherwise fall back to manual entry.
            if (_detection.WebClientFound)
            {
                _webClientDetectedValid = IsValidWebClientPath(_detection.WebClientPath);

                if (string.IsNullOrEmpty(_txtWebClientPath.Text))
                    _txtWebClientPath.Text = _detection.WebClientPath;

                if (_webClientDetectedValid)
                {
                    _lblWebClientDetected.Text =
                        $"Detected Web Client: {_detection.WebClientPath}  (Browse.aspx verified)";
                    _lblWebClientDetected.ForeColor = Color.FromArgb(100, 100, 100);
                    _chkWebClient.Checked = true;
                }
                else
                {
                    _lblWebClientDetected.Text =
                        $"Detected path is invalid \u2014 Browse.aspx not found in: {_detection.WebClientPath}. " +
                        "Enter the Web Files path manually to deploy the button.";
                    _lblWebClientDetected.ForeColor = Color.FromArgb(180, 80, 0);
                    _chkWebClient.Checked = false;
                }
                _lblWebClientDetected.Visible = true;
            }
        }

        // A Web Client path is valid only if Browse.aspx exists inside it.
        private static bool IsValidWebClientPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                return File.Exists(Path.Combine(path.Trim().TrimEnd('\\', '/'), "Browse.aspx"));
            }
            catch
            {
                return false;
            }
        }

        // The Web Client path that will actually be used: the verified detected
        // path, or whatever the user typed into the manual entry box.
        private string GetEffectiveWebClientPath()
        {
            return _webClientDetectedValid
                ? _detection.WebClientPath
                : _txtWebClientPath.Text.Trim().TrimEnd('\\', '/');
        }

        // Updates the Dashboard URL when the port field changes, but only while
        // the URL still looks like the auto-generated value.
        private void AutoUpdateDashboardUrl()
        {
            if (string.IsNullOrEmpty(_autoDetectedHost)) return;
            if (!IsDashboardUrlAutoGenerated())           return;

            string portText = _txtPort.Text.Trim();
            if (string.IsNullOrEmpty(portText)) return;

            _txtDashboardUrl.Text = $"http://{_autoDetectedHost}:{portText}";
        }

        // Returns true when Dashboard URL still matches the auto-generated pattern
        // (http://MACHINENAME:port or http://localhost:port), indicating it has not
        // been manually replaced by the user.
        private bool IsDashboardUrlAutoGenerated()
        {
            string url = _txtDashboardUrl.Text.Trim();
            return url.StartsWith($"http://{_autoDetectedHost}:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // Config page — validation and collection
        // ================================================================

        private bool ValidateConfigPage()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(_txtLFApiUrl.Text))
                errors.Add("Laserfiche API URL is required.");
            else if (!_txtLFApiUrl.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                     !_txtLFApiUrl.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                errors.Add("Laserfiche API URL must start with http:// or https://");

            if (string.IsNullOrWhiteSpace(_txtDashboardUrl.Text))
                errors.Add("Dashboard URL is required (in Advanced Settings).");
            else if (!_txtDashboardUrl.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                     !_txtDashboardUrl.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                errors.Add("Dashboard URL must start with http:// or https://  (in Advanced Settings).");

            if (!int.TryParse(_txtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
                errors.Add("IIS Port must be a number between 1 and 65535 (in Advanced Settings).");

            if (_chkWebClient.Checked)
            {
                string wcPath = GetEffectiveWebClientPath();
                if (string.IsNullOrWhiteSpace(wcPath))
                    errors.Add("Web Client deployment is selected, but no Web Files path was provided.");
                else if (!IsValidWebClientPath(wcPath))
                    errors.Add($"Browse.aspx was not found in \"{wcPath}\". " +
                               "Select the Laserfiche Web Files directory that contains Browse.aspx, " +
                               "or uncheck the Web Client button option.");
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join("\r\n\r\n", errors),
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        // Called from GoNext() after validation succeeds.
        private void CollectConfigPage()
        {
            _config.LaserficheApiUrl = _txtLFApiUrl.Text.Trim().TrimEnd('/');
            _config.DashboardUrl     = _txtDashboardUrl.Text.Trim().TrimEnd('/');
            _config.DashboardPort    = _txtPort.Text.Trim();

            _config.InstallDesktopButton = _chkDesktop.Checked;
            _config.InstallWebButton     = _chkWebClient.Checked;

            if (_chkWebClient.Checked)
            {
                _config.LFWebClientPath = GetEffectiveWebClientPath();
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
            sb.AppendLine("    - Repository: selected automatically per user session");
            sb.AppendLine();

            if (_config.InstallDesktopButton)
                sb.AppendLine("  Laserfiche Desktop Client Extension (toolbar button)");

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
            _progressBar.Value     = 0;
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
            _installDone    = true;
            _installSuccess = success;
            _installMessage = message;
            AppendLog(success ? "[SUCCESS] " + message : "[FAILED] " + message);
            NavigateTo(PAGE_COMPLETE);
        }

        private void AppendLog(string text)
        {
            if (IsDisposed) return;
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
                    "(Credentials are stored encrypted using Windows DPAPI \u2014 never in plain text.)\r\n\r\n" +
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
                Text      = text,
                Font      = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize  = true,
                Location  = new Point(0, 0),
                ForeColor = Color.FromArgb(0, 62, 134)
            };

        private static Button MakeButton(string text, int width) =>
            new Button
            {
                Text    = text,
                Size    = new Size(width, 26),
                TabStop = true
            };
    }
}
