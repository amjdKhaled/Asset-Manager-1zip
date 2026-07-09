using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Laserfiche.ClientAutomation;
using LaserficheAIExtension.Infrastructure.DependencyInjection;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Popup;
using LaserficheAIExtension.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LaserficheAIExtension
{
    /// <summary>
    /// Entry point that mirrors the SDK 10.4 CustomButtonManager sample exactly.
    ///
    /// Modes:
    ///   * No arguments (or /setup)    — registers the "AI Assistant" toolbar button in Laserfiche Desktop Client.
    ///   * /unregister                 — removes the toolbar button.
    ///   * /silent                     — registers without UI.
    ///   * -buttonclick ...            — Laserfiche launches us with selected-entry tokens; we show the AI popup.
    /// </summary>
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(
            Path.GetTempPath(), "GovSearchAI_Extension.log");
        private static Mutex _singleInstanceMutex;

        public App()
        {
            // --- Global exception handlers (must attach before any UI runs) ---
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string[] args = e.Args;
            Log("=== OnStartup === Args: " + string.Join(" ", args));

            // --- Single-instance enforcement for popup mode ---
            if (args.Length > 0 && args[0] == "-buttonclick")
            {
                bool createdNew;
                _singleInstanceMutex = new Mutex(true, "GovSearchAIExtension_SingleInstance", out createdNew);
                if (!createdNew)
                {
                    Log("Single instance already running. Activating existing window and exiting.");
                    // Try to activate the existing window via named event / window handle lookup
                    try { ActivateExistingWindow(); } catch { /* best effort */ }
                    Shutdown(0);
                    return;
                }
            }

            try
            {
                bool success = MainHandler(args);
                if (!success)
                {
                    Log("MainHandler returned false. Shutting down.");
                    Shutdown(1);
                }
            }
            catch (Exception ex)
            {
                Log("FATAL: MainHandler threw exception: " + ex);
                MessageBox.Show(
                    "GovSearch AI extension failed to start.\r\n\r\n" + ex.Message,
                    "GovSearch AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        // ------------------------------------------------------------------
        // Global exception handlers — never allow an exception to crash Laserfiche
        // ------------------------------------------------------------------
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("DispatcherUnhandledException: " + e.Exception);
            e.Handled = true;
            try
            {
                MessageBox.Show(
                    "An unexpected error occurred in the AI extension.\r\n\r\n" + e.Exception.Message,
                    "GovSearch AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { /* MessageBox can also fail */ }
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log("UnobservedTaskException: " + e.Exception);
            e.SetObserved();
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("AppDomainUnhandledException: " + e.ExceptionObject);
        }

        // ------------------------------------------------------------------
        // Logging helper — simple file append, no external dependencies
        // ------------------------------------------------------------------
        private static void Log(string message)
        {
            try
            {
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, message);
                File.AppendAllText(LogPath, line + "\r\n");
            }
            catch { /* logging must never throw */ }
        }

        private static void ActivateExistingWindow()
        {
            try
            {
                // Find existing GovSearch AI popup window by class name
                var current = Process.GetCurrentProcess();
                foreach (var proc in Process.GetProcessesByName(current.ProcessName))
                {
                    if (proc.Id != current.Id)
                    {
                        // Bring to foreground via Win32 (best effort)
                        var hwnd = proc.MainWindowHandle;
                        if (hwnd != IntPtr.Zero)
                        {
                            NativeMethods.SetForegroundWindow(hwnd);
                            if (NativeMethods.IsIconic(hwnd))
                                NativeMethods.ShowWindow(hwnd, 9 /* SW_RESTORE */);
                        }
                    }
                }
            }
            catch { }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool IsIconic(IntPtr hWnd);
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        }

        /// <summary>
        /// Parses command-line arguments and branches to setup or button-click handling.
        /// Mirrors CustomButtonManagerApp.MainHandler exactly.
        /// </summary>
        static bool MainHandler(string[] _args)
        {
            int buttonid = 0;
            Guid connguid = new Guid();
            int pid = 0;
            int hwnd = 0;
            bool silent = false;
            bool unregister = false;
            string command = "";
            PageSet selectedpages = null;
            List<int> selectedentries = null;

            List<string> args = _args.ToList();

            // Parse arguments — mirrors the sample's argument-parsing loop exactly.
            for (int i = 0; i < args.Count; i++)
            {
                try
                {
                    if (args[i] == "-connguid" && args.Count > i + 1)
                    {
                        if (!args[i + 1].StartsWith("%"))
                            connguid = new Guid(args[i + 1]);
                        i++;
                    }
                    else if (args[i] == "-pid" && args.Count > i + 1)
                    {
                        if (!args[i + 1].StartsWith("%"))
                            pid = int.Parse(args[i + 1]);
                        i++;
                    }
                    else if (args[i] == "-buttonid" && args.Count > i + 1)
                    {
                        if (!args[i + 1].StartsWith("%"))
                            buttonid = int.Parse(args[i + 1]);
                        i++;
                    }
                    else if (args[i] == "-hwnd" && args.Count > i + 1)
                    {
                        if (!args[i + 1].StartsWith("%"))
                            hwnd = int.Parse(args[i + 1]);
                        i++;
                    }
                    else if (args[i] == "-command" && args.Count > i + 1)
                    {
                        command = args[i + 1];
                        i++;
                    }
                    else if (args[i] == "-SelectedPages" && args.Count > i + 1)
                    {
                        if (!args[i + 1].StartsWith("%"))
                        {
                            selectedpages = new PageSet();
                            if (args[i + 1].Length > 0)
                            {
                                string[] pagenumArray = args[i + 1].Split(',');
                                foreach (string strPageNum in pagenumArray)
                                {
                                    selectedpages.AddPage(int.Parse(strPageNum));
                                }
                            }
                        }
                        i++;
                    }
                    else if (args[i] == "-SelectedEntries" && args.Count > i + 1)
                    {
                        if (!args[i + 1].StartsWith("%"))
                        {
                            selectedentries = new List<int>();
                            if (args[i + 1].Length > 0)
                            {
                                string[] entryIdArray = args[i + 1].Split(',');
                                foreach (string strEntryId in entryIdArray)
                                {
                                    selectedentries.Add(int.Parse(strEntryId));
                                }
                            }
                        }
                        i++;
                    }
                    else if (args[i] == "-silent")
                        silent = true;
                    else if (args[i] == "/unregister" || args[i] == "-unregister")
                        unregister = true;
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, "GovSearch AI", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // Button-click mode — Laserfiche launched us because the user clicked the toolbar button.
            if (args.Count > 0 && args[0] == "-buttonclick")
            {
                return HandleButtonClick(buttonid, connguid, pid, hwnd, command, selectedentries, selectedpages);
            }

            // Setup / registration mode
            if (unregister)
            {
                RemoveToolbar(silent);
                return true;
            }

            SetupToolbar(silent);
            return true;
        }

        // ------------------------------------------------------------------
        // Setup / Registration — mirrors CustomButtonManagerApp.SetupToolbar
        // ------------------------------------------------------------------

        static void SetupToolbar(bool silent)
        {
            RemoveToolbar(true);

            // Command-line template that Laserfiche will fill with tokens when the button is clicked.
            string argsbase =
                " -buttonclick -connguid \"%(ConnectionGUID)\" -hwnd \"%(hwnd)\" -pid \"%(PID)\" -SelectedEntries \"%(SelectedEntries)\" ";

            string strProcessPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ToolbarPosition toolbarPosition = ToolbarPosition.Top;

            using (ClientManager lfclient = new ClientManager())
            {
                using (ToolbarManager toolbarmgr = lfclient.GetToolbarManager(ClientWindowType.Main))
                {
                    // Derive toolbar name from EXE filename (mirrors sample).
                    string strToolbarName = strProcessPath;
                    int nSlashPos = strToolbarName.LastIndexOf("\\");
                    if (nSlashPos >= 0)
                        strToolbarName = strToolbarName.Substring(nSlashPos + 1);

                    // Delete existing toolbar if present (mirrors sample).
                    int nToolbarCount = toolbarmgr.GetToolbarCount();
                    for (int i = 0; i < nToolbarCount; i++)
                    {
                        string strToolbar = toolbarmgr.GetToolbarName(i);
                        if (strToolbar == strToolbarName)
                        {
                            toolbarmgr.DeleteToolbar(strToolbarName);
                            break;
                        }
                    }

                    // Add toolbar (mirrors sample).
                    toolbarmgr.AddToolbar(strToolbarName, toolbarPosition);

                    // Add "AI" custom button.
                    CustomButtonInfo newButtonInfo = new CustomButtonInfo();
                    newButtonInfo.Description = "AI";
                    newButtonInfo.Command = "\"" + strProcessPath + "\"" + argsbase + "-command ai";

                    int nPathSlashPos = strProcessPath.LastIndexOf("\\");
                    string strIconDir = strProcessPath.Substring(0, nPathSlashPos) + "\\Resources\\";
                    newButtonInfo.IconPath = strIconDir + "AIAssistant.ico";

                    int nButtonID = toolbarmgr.AddCustomToolbarButton(newButtonInfo);

                    ToolbarButtonInfo toolbarButtonInfo = new ToolbarButtonInfo();
                    toolbarButtonInfo.Id = nButtonID;
                    toolbarButtonInfo.IsSeparator = true;

                    toolbarmgr.AddButton(strToolbarName, toolbarButtonInfo, -1);
                }
            }

            if (!silent)
            {
                MessageBox.Show(
                    "AI Assistant toolbar button added successfully.",
                    "GovSearch AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ------------------------------------------------------------------
        // Removal — mirrors CustomButtonManagerApp.RemoveToolbar
        // ------------------------------------------------------------------

        static void RemoveToolbar(bool silent)
        {
            bool bRemovedAnything = false;

            string strProcessName = System.Reflection.Assembly.GetExecutingAssembly().Location;
            int nSlashPos = strProcessName.LastIndexOf("\\");
            if (nSlashPos >= 0)
                strProcessName = strProcessName.Substring(nSlashPos + 1);

            using (ClientManager lfclient = new ClientManager())
            {
                using (ToolbarManager toolbarmgr = lfclient.GetToolbarManager(ClientWindowType.Main))
                {
                    // Delete matching toolbar (mirrors sample).
                    int nToolbarCount = toolbarmgr.GetToolbarCount();
                    for (int i = 0; i < nToolbarCount; i++)
                    {
                        string strToolbar = toolbarmgr.GetToolbarName(i);
                        if (strToolbar.Equals(strProcessName, StringComparison.CurrentCultureIgnoreCase))
                        {
                            toolbarmgr.DeleteToolbar(strToolbar);
                            bRemovedAnything = true;
                        }
                    }

                    // Remove all custom buttons (mirrors sample).
                    int nCustomButtons = toolbarmgr.GetCustomToolbarButtonCount();
                    for (int i = 0; i < nCustomButtons; i++)
                    {
                        toolbarmgr.RemoveCustomToolbarButton(0);
                        bRemovedAnything = true;
                    }
                }
            }

            if (!silent)
            {
                if (bRemovedAnything)
                {
                    MessageBox.Show(
                        "AI Assistant toolbar button removed successfully.",
                        "GovSearch AI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Toolbar not found.",
                        "GovSearch AI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        // ------------------------------------------------------------------
        // Button Click Handler — replaces the sample's Laserfiche operations
        // with our WPF popup containing the embedded WebView2 AI interface.
        // ------------------------------------------------------------------

        static bool HandleButtonClick(int buttonid, Guid connguid, int pid, int hwnd, string command,
                                        List<int> selectedentries, PageSet selectedpages)
        {
            var sw = Stopwatch.StartNew();
            Log("HandleButtonClick started");

            try
            {
                // Build DI container exactly like the original extension.
                var services = new ServiceCollection();
                services.AddLaserficheAIExtension();
                var serviceProvider = services.BuildServiceProvider();

                var communication = serviceProvider.GetRequiredService<IWebAppCommunicationService>();
                var monitor = serviceProvider.GetRequiredService<IConnectionMonitorService>();
                var tracker = serviceProvider.GetRequiredService<IDocumentContextTracker>();
                var handler = serviceProvider.GetRequiredService<ICommandHandlerService>();
                var settings = serviceProvider.GetRequiredService<ExtensionSettings>();

                Log("DI container built in " + sw.ElapsedMilliseconds + "ms");

                // Create and show the floating WPF popup (embedded WebView2, never an external browser).
                var currentApp = Application.Current;
                var popup = new AIPopupWindow(communication, monitor, tracker, handler, settings);

                popup.Closed += (s, e) =>
                {
                    Log("Popup closed. Shutting down application.");
                    try { serviceProvider.Dispose(); }
                    catch (Exception ex) { Log("ServiceProvider dispose failed: " + ex); }
                    currentApp.Shutdown();
                };

                // If Laserfiche passed selected entry IDs, update the document tracker.
                if (selectedentries != null && selectedentries.Count > 0)
                {
                    _ = tracker.UpdateSelectionAsync(selectedentries[0]);
                }

                popup.Show();
                Log("Popup shown. Total startup: " + sw.ElapsedMilliseconds + "ms");
                return true;
            }
            catch (Exception ex)
            {
                Log("HandleButtonClick FAILED after " + sw.ElapsedMilliseconds + "ms: " + ex);
                throw;
            }
        }
    }
}
