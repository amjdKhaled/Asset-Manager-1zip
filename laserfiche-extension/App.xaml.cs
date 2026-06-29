using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string[] args = e.Args;

            // Retry loop — mirrors the sample's while(true) { try { if (MainHandler(args)) break; } catch { ... } }
            while (true)
            {
                try
                {
                    if (MainHandler(args))
                        break;
                }
                catch (Exception ex)
                {
                    var result = MessageBox.Show(
                        ex.Message + ", Retry?",
                        "GovSearch AI",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error);

                    if (result == MessageBoxResult.No)
                        break;
                }
            }
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

                    // Add "AI Assistant" custom button.
                    CustomButtonInfo newButtonInfo = new CustomButtonInfo();
                    newButtonInfo.Description = "AI Assistant";
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
            // Build DI container exactly like the original extension.
            var services = new ServiceCollection();
            services.AddLaserficheAIExtension();
            var serviceProvider = services.BuildServiceProvider();

            var communication = serviceProvider.GetRequiredService<IWebAppCommunicationService>();
            var monitor = serviceProvider.GetRequiredService<IConnectionMonitorService>();
            var tracker = serviceProvider.GetRequiredService<IDocumentContextTracker>();
            var handler = serviceProvider.GetRequiredService<ICommandHandlerService>();
            var settings = serviceProvider.GetRequiredService<ExtensionSettings>();

            // Create and show the floating WPF popup (embedded WebView2, never an external browser).
            var currentApp = Application.Current;
            var popup = new AIPopupWindow(communication, monitor, tracker, handler, settings);

            popup.Closed += (s, e) => currentApp.Shutdown();

            // If Laserfiche passed selected entry IDs, update the document tracker.
            if (selectedentries != null && selectedentries.Count > 0)
            {
                tracker.UpdateSelectionAsync(selectedentries[0]).ConfigureAwait(false);
            }

            popup.Show();

            return true;
        }
    }
}
