using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;
using Laserfiche.ClientAutomation;
using LFSO104Lib;

namespace Laserfiche.Samples
{
    static class CustomButtonManagerApp
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            string strCommands = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                    strCommands += " ";
                strCommands += args[i];
            }

            while (true)
            {
                try
                {
                    if (MainHandler(args))
                        break;
                }
                catch (Exception e)
                {
                    DialogResult result = MessageBox.Show(e.Message + ", Retry?", "Error", MessageBoxButtons.YesNo);
                    if (result == DialogResult.No)
                        break;
                }
            }
        }

        static bool MainHandler(string[] _args)
        {
            int buttonid = 0;
            Guid connguid = new Guid();
            int pid = 0;
            int hwnd = 0;
            bool silent = false;
            string command = "";
            PageSet selectedpages = null;
            List<int> selectedentries = null;

            List<string> args = new List<string>();

            for (int i = 0; i < _args.Length; i++)
            {
                args.Add(_args[i]);
            }

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
                                foreach (string strEntryId in pagenumArray)
                                {
                                    selectedpages.AddPage(int.Parse(strEntryId));
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
                                string[] pagenumArray = args[i + 1].Split(',');
                                foreach (string strEntryId in pagenumArray)
                                {
                                    selectedentries.Add(int.Parse(strEntryId));
                                }
                            }
                        }
                        i++;
                    }
                    else if (args[i] == "-silent")
                        silent = true;
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message);
                }
            }

            if (args.Count > 0)
            {
                if (args.Count > 0 && args[0] == "-buttonclick")
                {
                    ButtonClick(buttonid, connguid, pid, hwnd, command, selectedentries, selectedpages);
                    return true;
                }

                string strCommands = "Unknown parameters:\r\n";
                for (int i = 0; i < args.Count; i++)
                {
                    if (i > 0)
                        strCommands += " ";
                    strCommands += args[i];
                }
                MessageBox.Show(strCommands);
            }
            else
            {
                if (silent)
                    SetupToolbar(true);
                else
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new CustomButtonManagerDialog());
                }
                return true;
            }
            return true;
        }

        class MyCustomButtonInfo
        {
            public MyCustomButtonInfo(ClientWindowType windowtype, string caption, string args)
            {
                m_windowtype = windowtype;
                m_args = args;
                m_caption = caption;
            }

            public ClientWindowType m_windowtype = ClientWindowType.Unknown;
            public string m_args = "";
            public string m_caption = "";
        }

        static string GetClientPath()
        {
            RegistryKey clientInfoKey = Registry.LocalMachine.OpenSubKey("Software\\Laserfiche\\Client", false);
            if (clientInfoKey == null)
                throw new Exception("Error reading Laserfiche client version");
            string strCurrentVersion = clientInfoKey.GetValue("CurrentVersion") as string;
            if (strCurrentVersion == null)
                throw new Exception("Error reading Laserfiche client version");
            double dblCurrentVersion = double.Parse(strCurrentVersion);
            if (dblCurrentVersion < 8.4 || dblCurrentVersion > 20.0)
                throw new Exception("Incompatible Laserfiche client version");
            RegistryKey clientKey = clientInfoKey.OpenSubKey(strCurrentVersion);
            if (clientKey == null)
                throw new Exception("Error reading Laserfiche client version. " +
                    strCurrentVersion + " key not found");
            string strInstallPath = clientKey.GetValue("InstallPath") as string;
            if (!strInstallPath.EndsWith("\\"))
                strInstallPath += "\\";

            clientInfoKey.Close();
            clientKey.Close();

            string clientpath = strInstallPath + "LF.exe";
            return clientpath;
        }

        // Create a toolbar and add a variety of sample buttons
        public static void SetupToolbar(bool silent)
        {
            RemoveToolbar(true);

            string argsbase = " -buttonclick -connguid \"%(ConnectionGUID)\" -hwnd \"%(hwnd)\" -pid \"%(PID)\" ";
            string mainargsbase = argsbase + " -SelectedEntries \"%(SelectedEntries)\" ";
            string docviewerargsbase = argsbase + " -DocumentID \"%(DocumentID)\" ";
            string strProcessPath = Application.ExecutablePath;

            ToolbarPosition toolbarPosition = ToolbarPosition.Top;

            List<MyCustomButtonInfo> buttons = new List<MyCustomButtonInfo>();

            ///////////////////////////
            // Main window buttons
            ///////////////////////////
            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "OpenMetadata",
                mainargsbase + "-command openmetadata"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "SearchByName",
                mainargsbase + "-command searchbyname"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "UpOneLevel",
                mainargsbase + "-command uponelevel"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "OpenDocumentViewer",
                mainargsbase + "-command opendocviewer"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "Refresh",
                mainargsbase + "-command refresh"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "Print",
                mainargsbase + "-command print"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "ContextHits",
                mainargsbase + "-command contexthits"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "Export",
                mainargsbase + "-command export"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "SetColumns",
                mainargsbase + "-command setcolumns"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "SetFieldColumns",
                mainargsbase + "-command setfieldcolumns"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.Main,
                "CloseAll",
                mainargsbase + "-command closeall"));

            ///////////////////////////
            // Doc viewer buttons
            ///////////////////////////
            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "Preview",
                docviewerargsbase + "-command preview"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "FirstPage",
                docviewerargsbase + "-command firstpage"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "LastPage",
                docviewerargsbase + "-command lastpage"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "Refresh",
                docviewerargsbase + "-command refresh"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "Print",
                docviewerargsbase + "-command print"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "Export",
                docviewerargsbase + "-command export"));

            buttons.Add(new MyCustomButtonInfo(ClientWindowType.DocumentViewer,
                "CloseAll",
                docviewerargsbase + "-command closeall"));

            using (ClientManager lfclient = new ClientManager())
            {
                List<ClientWindowType> windowTypes = new List<ClientWindowType>();
                windowTypes.Add(ClientWindowType.Main);
                windowTypes.Add(ClientWindowType.DocumentViewer);

                foreach (ClientWindowType windowtype in windowTypes)
                {
                    using (ToolbarManager toolbarmgr = lfclient.GetToolbarManager(windowtype))
                    {
                        string strToolbarName = strProcessPath;
                        int nSlashPos = strToolbarName.LastIndexOf("\\");
                        if (nSlashPos >= 0)
                            strToolbarName = strToolbarName.Substring(nSlashPos + 1);

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

                        toolbarmgr.AddToolbar(strToolbarName, toolbarPosition);

                        foreach (MyCustomButtonInfo buttonInfo in buttons)
                        {
                            if (buttonInfo.m_windowtype != windowtype)
                                continue;

                            int nButtonID = -1;
                            int nCustomButtons = toolbarmgr.GetCustomToolbarButtonCount();
                            for (int i = 0; i < nCustomButtons; i++)
                            {
                                CustomButtonInfo existingButtonInfo = toolbarmgr.GetCustomToolbarButton(i);
                                if (existingButtonInfo.Description.Equals(buttonInfo.m_caption, StringComparison.CurrentCultureIgnoreCase) &&
                                    existingButtonInfo.Command.Contains(buttonInfo.m_args))
                                {
                                    nButtonID = existingButtonInfo.Id;
                                    break;
                                }
                            }

                            if (nButtonID == -1)
                            {
                                CustomButtonInfo newButtonInfo = new CustomButtonInfo();
                                newButtonInfo.Description = buttonInfo.m_caption;

                                newButtonInfo.Command = "\"" + strProcessPath + "\"" + buttonInfo.m_args;

                                int nPathSlashPos = strProcessPath.LastIndexOf("\\");
                                string strIconDir = strProcessPath.Substring(0, nPathSlashPos) + "\\Resources\\";

                                newButtonInfo.IconPath = strIconDir + buttonInfo.m_caption + ".ico";
                                nButtonID = toolbarmgr.AddCustomToolbarButton(newButtonInfo);
                            }

                            ToolbarButtonInfo newbuttonInfo = new ToolbarButtonInfo();
                            newbuttonInfo.Id = nButtonID;
                            newbuttonInfo.IsSeparator = true;

                            toolbarmgr.AddButton(strToolbarName, newbuttonInfo, -1);
                        }
                    }
                }
            }

            if (!silent)
                MessageBox.Show("Successfully added toolbar");
        }

        // Remove the custom toolbar and all custom buttons
        public static void RemoveToolbar(bool silent)
        {
            bool bRemovedAnything = false;

            string strProcessName = Application.ExecutablePath;
            int nSlashPos = strProcessName.LastIndexOf("\\");
            if (nSlashPos >= 0)
                strProcessName = strProcessName.Substring(nSlashPos + 1);

            using (ClientManager lfclient = new ClientManager())
            {
                using (ToolbarManager maintoolbarmgr = lfclient.GetToolbarManager(ClientWindowType.Main))
                {
                    using (ToolbarManager doctoolbarmgr = lfclient.GetToolbarManager(ClientWindowType.DocumentViewer))
                    {
                        ToolbarManager[] toolbarmgrs = new ToolbarManager[2];
                        toolbarmgrs[0] = maintoolbarmgr;
                        toolbarmgrs[1] = doctoolbarmgr;

                        foreach (ToolbarManager toolbarmgr in toolbarmgrs)
                        {
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
                        }
                    }

                    int nCustomButtons = maintoolbarmgr.GetCustomToolbarButtonCount();
                    for (int i = 0; i < nCustomButtons; i++)
                    {
                        maintoolbarmgr.RemoveCustomToolbarButton(0);
                        bRemovedAnything = true;
                    }
                }
            }

            if (!silent)
            {
                if (bRemovedAnything)
                    MessageBox.Show("Successfully removed toolbar");
                else
                    MessageBox.Show("Toolbar not found");
            }
        }

        public static void LaunchClient()
        {
            using (ClientManager lfclient = new ClientManager())
            {
                LaunchOptions options = new LaunchOptions();
                lfclient.LaunchClient(options);
            }
        }

        [DllImport("ole32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease, ref IStream istream);

        // Custom button click handler (when -buttonclick is specified on the command line)
        static bool ButtonClick(int buttonid, Guid connguid, int pid, int hwnd, string command,
                                List<int> selectedentries, PageSet selectedpages)
        {
            using (ClientManager lfclient = new ClientManager())
            {
                IEnumerable<ClientInstance> clients = lfclient.GetAllClientInstances();
                foreach (ClientInstance client in clients)
                {
                    if (client.ProcessID == pid)
                    {
                        IEnumerable<ClientWindow> windows = client.GetAllClientWindows();
                        foreach (ClientWindow window in windows)
                        {
                            if (window.Hwnd == (IntPtr)hwnd)
                            {
                                RepositoryConnection repoconn = window.GetCurrentRepository();

                                if (window.GetWindowType() == ClientWindowType.Main)
                                {
                                    MainWindow mainwindow = (MainWindow)window;

                                    IList<int> listEntryIDs = mainwindow.GetSelectedEntries();
                                    if (listEntryIDs.Count == 1)
                                    {
                                        if (command == "openmetadata")
                                        {
                                            // Open the metadata dialog for the currently selected entry, with the position and tabs preset.
                                            OpenOptions options = new OpenOptions();
                                            options.OpenStyle = DocumentOpenType.Metadata;
                                            options.MetadataVisibleTabs = MetadataTab.Signatures | MetadataTab.Fields;
                                            options.MetadataStartTab = MetadataTab.Signatures;

                                            System.Drawing.Rectangle screenrect = new System.Drawing.Rectangle();
                                            screenrect = System.Windows.Forms.Screen.GetBounds(screenrect);

                                            options.Position = new WindowPosition(
                                                screenrect.Right - 600, screenrect.Bottom - 400,
                                                screenrect.Right, screenrect.Bottom, false);
                                            mainwindow.OpenDocumentById(listEntryIDs[0], options);
                                        }
                                        else if (command == "print")
                                        {
                                            // Print silently
                                            PrintOptions printoptions = new PrintOptions();
                                            printoptions.PageNumbers.AddPage(1);
                                            printoptions.PageNumbers.AddPage(6);
                                            printoptions.DoNotPrompt = false;
                                            printoptions.DocumentPart = PrintType.Images;
                                            printoptions.PrinterName = "Microsoft XPS Document Writer";
                                            //printoptions.printername = "Send To OneNote 2010";
                                            //printoptions.printername = @"\\v-services\HP LaserJet CP3525 - Dev";

                                            mainwindow.PrintById(listEntryIDs[0], printoptions);
                                        }
                                        else if (command == "scan")
                                        {
                                            // Launch scanning
                                            ScanOptions options = new ScanOptions();
                                            options.EntryId = listEntryIDs[0];
                                            options.ScanMode = ScanMode.Standard;
                                            options.InsertPagesAt = (int)InsertAt.End;

                                            mainwindow.LaunchScanningFromClient(options);
                                        }
                                        else if (command == "searchbyname")
                                        {
                                            // Run a search for all entries with the same name as the currently selected entry
                                            ILFConnection pLFConnection = Util.GetLFSOConnection(repoconn);
                                            ILFDatabase pLFDatabase = pLFConnection.Database;
                                            ILFEntry pLFCurrentEntry = pLFDatabase.GetEntryByID(listEntryIDs[0]);

                                            SearchOptions options = new SearchOptions();
                                            options.Query = "({Lf:Name=\"" + pLFCurrentEntry.Name + "\", Type=\"DBFS\"}) & {LF:ID<>" + pLFCurrentEntry.ID + "}";
                                            options.NewWindow = false;
                                            mainwindow.LaunchSearch(options);
                                        }
                                    }

                                    if (command == "refresh")
                                    {
                                        // Refresh the current window
                                        mainwindow.Refresh();
                                    }
                                    else if (command == "uponelevel")
                                    {
                                        // Move up one level to the parent folder
                                        int nCurrentFolderID = mainwindow.GetCurrentFolderId();

                                        if (repoconn != null && nCurrentFolderID != 0)
                                        {
                                            ILFConnection pLFConnection = Util.GetLFSOConnection(repoconn);
                                            ILFDatabase pLFDatabase = pLFConnection.Database;
                                            ILFFolder pLFCurrentFolder = pLFDatabase.GetEntryByID(nCurrentFolderID);
                                            ILFFolder pLFParentFolder = pLFCurrentFolder.ParentFolder;
                                            if (pLFParentFolder != null)
                                                mainwindow.SetCurrentFolder(pLFCurrentFolder.ParentFolder.ID);
                                        }
                                    }
                                    else if (command == "contexthits")
                                    {
                                        // Display a message box showing the context hits for the currently selected search result
                                        IList<int> entryids = mainwindow.GetSelectedEntries();
                                        IList<ContextHitInfo> contexthits = mainwindow.GetSelectedContextHits();
                                        if (contexthits.Count == 0)
                                            MessageBox.Show("No context hits selected");
                                        else
                                        {
                                            string strdetails = "";
                                            for (int i = 0; i < contexthits.Count; i++)
                                            {
                                                if (i > 0)
                                                    strdetails += "\r\n\r\n";
                                                ContextHitInfo info = contexthits[i];
                                                strdetails += "EntryID: " + info.EntryId.ToString() + "\r\nPageNum: " + info.PageNumber.ToString() + "\r\nInfo: " + info.Context + "\r\nText: " + info.HitText;
                                            }
                                            MessageBox.Show(strdetails);
                                        }
                                    }
                                    else if (command == "opendocviewer")
                                    {
                                        // Open the currently selected document in the document viewer, with the position and layout preset.
                                        IList<int> entryids = mainwindow.GetSelectedEntries();
                                        if (entryids.Count == 1)
                                        {
                                            int entryid = entryids[0];
                                            OpenOptions options = new OpenOptions();
                                            options.OpenStyle = DocumentOpenType.DocumentViewer;
                                            options.VisiblePanes = DocViewerPane.MetadataPane | DocViewerPane.ThumbnailPane;
                                            options.MetadataVisibleTabs = MetadataTab.Signatures | MetadataTab.Fields;
                                            options.MetadataStartTab = MetadataTab.Signatures;
                                            options.Position = new WindowPosition(0, 0, 800, 800, false);
                                            mainwindow.OpenDocumentById(entryid, options);
                                        }
                                    }
                                    else if (command == "setcolumns")
                                    {
                                        // Set the current column layout to a preset list
                                        SetColumnsOptions options = new SetColumnsOptions();
                                        options.Columns.Add(new ClientColumn((int)Column_Type.COLUMN_TYPE_TAGS, 100));
                                        options.Columns.Add(new ClientColumn((int)Column_Type.COLUMN_TYPE_MODIFIER_NAME, 100));
                                        options.Columns.Add(new ClientColumn("Document", 100));
                                        options.SortFieldName = "Document";

                                        options.SortDirection = SortDirection.Ascending;
                                        mainwindow.SetColumns(options);
                                    }
                                    else if (command == "setfieldcolumns" && listEntryIDs.Count > 0)
                                    {
                                        // Set the current column layout to the template and fields for the currently selected entries.
                                        ILFConnection pLFConnection = Util.GetLFSOConnection(repoconn);
                                        ILFDatabase pLFDatabase = pLFConnection.Database;

                                        SetColumnsOptions options = new SetColumnsOptions();
                                        options.Columns.Add(new ClientColumn((int)Column_Type.COLUMN_TYPE_TEMPLATENAME, 100));

                                        foreach (int entryID in listEntryIDs)
                                        {
                                            ILFEntry pLFCurrentEntry = pLFDatabase.GetEntryByID(entryID);
                                            if (pLFCurrentEntry.EntryType == Entry_Type.ENTRY_TYPE_SHORTCUT)
                                                pLFCurrentEntry = ((ILFShortcut)pLFCurrentEntry).EntryReferenced;

                                            ILFHasTemplate pLFHasTemplate = (ILFHasTemplate)pLFCurrentEntry;

                                            ILFFieldData pLFFielddata = pLFHasTemplate.FieldData;
                                            ILFTemplate pLFTemplate = pLFHasTemplate.Template;
                                            if (pLFTemplate != null)
                                            {
                                                for (int i = 1; i <= pLFTemplate.Count; i++)
                                                {
                                                    ILFTemplateField pLFField = pLFTemplate.get_Item(i);
                                                    string strfieldname = pLFField.Name;
                                                    ClientColumn column = new ClientColumn(strfieldname, 100);
                                                    options.Columns.Add(column);
                                                }
                                            }

                                            for (int i = 1; i <= pLFFielddata.FieldCount; i++)
                                            {
                                                string strfieldname = pLFFielddata.get_FieldName(i);
                                                ClientColumn column = new ClientColumn(strfieldname, 100);
                                                options.Columns.Add(column);
                                            }
                                        }

                                        options.AreColumnsPersistent = false;
                                        mainwindow.SetColumns(options);
                                    }
                                    else if (command == "export")
                                    {
                                        // Export the current entries
                                        ExportOptions options = new ExportOptions();
                                        if (selectedpages != null)
                                            options.PageNumbers = selectedpages;
                                        options.DestinationPath = "c:\\test";
                                        options.DocumentPart = ExportType.Edoc;
                                        options.ImageFormat = ImageType.TiffG4;
                                        options.DoNotPrompt = true;
                                        options.UseMultiPageFile = true;

                                        mainwindow.ExportById(listEntryIDs, options);
                                    }
                                }
                                else if (window.GetWindowType() == ClientWindowType.DocumentViewer)
                                {
                                    DocumentViewer docwindow = (DocumentViewer)window;
                                    if (command == "nextpage")
                                    {
                                        // Move the doc viewer to the next page
                                        int nCurrentPage = docwindow.GetCurrentPageNumber();
                                        int nPageCount = docwindow.GetPageCount();
                                        if (nCurrentPage < nPageCount)
                                            docwindow.GoToPage(nCurrentPage + 1);
                                        else
                                            docwindow.GoToPage(1);
                                    }
                                    if (command == "firstpage")
                                    {
                                        // Jump to the first page in the document
                                        docwindow.GoToPage(1);
                                    }
                                    if (command == "lastpage")
                                    {
                                        // Jump to the last page in the document
                                        int nPageCount = docwindow.GetPageCount();
                                        docwindow.GoToPage(nPageCount);
                                    }
                                    else if (command == "print")
                                    {
                                        // Print the current document silently
                                        PrintOptions printoptions = new PrintOptions();
                                        printoptions.PrinterName = "Microsoft XPS Document Writer";
                                        printoptions.DoNotPrompt = true;
                                        int nPageCount = docwindow.GetPageCount();
                                        if (nPageCount > 0)
                                        {
                                            printoptions.PageNumbers.AddPage(nPageCount);
                                            printoptions.DocumentPart = PrintType.Images;
                                        }

                                        docwindow.Print(printoptions);
                                    }
                                    else if (command == "refresh")
                                    {
                                        // Refresh the doc viewer
                                        docwindow.Refresh();
                                    }
                                    else if (command == "scan")
                                    {
                                        // Launch scanning
                                        ScanOptions options = new ScanOptions();
                                        options.EntryId = docwindow.GetDocumentId();
                                        options.ScanMode = ScanMode.Standard;
                                        options.InsertPagesAt = (int)InsertAt.End;

                                        docwindow.LaunchScanningFromClient(options);
                                    }
                                    else if (command == "export")
                                    {
                                        // Export the currently selected pages
                                        ExportOptions options = new ExportOptions();
                                        if (selectedpages != null)
                                            options.PageNumbers = selectedpages;
                                        options.DestinationPath = "c:\\test";
                                        options.DocumentPart = ExportType.Images;
                                        options.ImageFormat = ImageType.Jpeg;

                                        docwindow.Export(options);
                                    }
                                    else if (command == "preview")
                                    {
                                        // Open the current document in the preview pane
                                        foreach (ClientWindow _window in windows)
                                        {
                                            if (_window.GetWindowType() == ClientWindowType.Main)
                                            {
                                                MainWindow mainwindow = (MainWindow)_window;
                                                OpenOptions options = new OpenOptions();
                                                options.OpenStyle = DocumentOpenType.Preview;
                                                options.VisiblePanes = DocViewerPane.ThumbnailPane | DocViewerPane.MetadataPane;
                                                mainwindow.OpenDocumentById(docwindow.GetDocumentId(), options);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (command == "closeall")
                {
                    foreach (ClientInstance client in clients)
                    {
                        client.Close(false);
                    }
                }

                return false;
            }
        }
    }

    static class Util
    {
        public static ILFConnection GetLFSOConnection(RepositoryConnection repoconn)
        {
            string strSerializedConnection = repoconn.GetConnectionString();
            LFConnection lfsoconn = new LFConnection();
            lfsoconn.CloneFromSerializedConnectionString(strSerializedConnection);
            LFDatabase lfdb = lfsoconn.Database;
            lfdb.GetEntryByID(1);
            return lfsoconn;
        }

        static void OpenFindRefresh(string[] args)
        {
            if (args.Count() != 4)
                return;

            // Find a client that is logged into AutoUpdate. If one isn't running,
            // launch the client and log in. Then refresh all open windows.
            //using ClientAutomation;
            //using LFSO100Lib;

            string server = "v-qa-autoupdate";
            string repository = "AutoUpdate";
            ILFApplication pLFApp = new LFApplication();
            ILFServer pLFServer = pLFApp.GetServerByName(server);
            ILFDatabase pLFDatabase = pLFServer.GetDatabaseByName(repository);

            using (ClientManager lfclient = new ClientManager())
            {
                // Find an existing client instance that is logged in to the repository
                IEnumerable<ClientInstance> clients = lfclient.GetAllClientInstances();
                ClientInstance client = null;
                foreach (ClientInstance _client in clients)
                {
                    IEnumerable<RepositoryConnection> repos = _client.RepositoryConnections;
                    foreach (RepositoryConnection repo in repos)
                    {
                        if (repo.RepositoryGuid.ToString().ToLower() == pLFDatabase.GUID.ToLower())
                        {
                            client = _client;
                            break;
                        }
                    }
                    if (client != null)
                        break;
                }

                // No matching client found, launch a new one
                if (client == null)
                {
                    LaunchOptions options = new LaunchOptions();
                    options.ServerName = server;
                    options.RepositoryName = repository;
                    options.ShowSplashScreen = false;
                    options.UserName = "admin"; // Leave username blank for windows authentication
                    client = lfclient.LaunchClient(options);
                }

                // Get all of the open windows and refresh them
                IEnumerable<ClientWindow> windows = client.GetAllClientWindows();
                foreach (ClientWindow window in windows)
                {
                    if (window.GetWindowType() == ClientWindowType.Main)
                    {
                        MainWindow mainwindow = (MainWindow)window;
                        mainwindow.Refresh();
                    }
                    else if (window.GetWindowType() == ClientWindowType.DocumentViewer)
                    {
                        DocumentViewer docwindow = (DocumentViewer)window;
                        docwindow.Refresh();
                    }
                }
            }
        }

        static void ButtonClickHandler(string[] args)
        {
            if (args.Count() != 4)
                return;

            int pid = 0;  // The LF.exe process ID that the button was clicked from
            int hwnd = 0; // The window that the button was clicked from
            if (args[0] == "-pid")
                pid = int.Parse(args[1]);
            if (args[2] == "-hwnd")
                hwnd = int.Parse(args[3]);

            using (ClientManager lfclient = new ClientManager())
            {
                // Find an existing client instance that is logged in to the repository
                IEnumerable<ClientInstance> clients = lfclient.GetAllClientInstances();
                foreach (ClientInstance client in clients)
                {
                    if (client.ProcessID == pid)
                    {
                        IEnumerable<ClientWindow> windows = client.GetAllClientWindows();
                        foreach (ClientWindow window in windows)
                        {
                            if (window.Hwnd == (IntPtr)hwnd)
                            {
                                // Found the window, now get the selected context hits
                                if (window.GetWindowType() == ClientWindowType.Main)
                                {
                                    MainWindow mainwindow = (MainWindow)window;
                                    IList<ContextHitInfo> contexthits = mainwindow.GetSelectedContextHits();
                                    string strdetails = "";
                                    for (int i = 0; i < contexthits.Count; i++)
                                    {
                                        if (i > 0)
                                            strdetails += "\r\n\r\n";
                                        ContextHitInfo info = contexthits[i];
                                        strdetails += "EntryID: " + info.EntryId.ToString() + "\r\nPageNum: " +
                                        info.PageNumber.ToString() + "\r\nInfo: " + info.HitType + "\r\nText: " + info.HitText;
                                    }
                                    Console.Write(strdetails);
                                }
                            }
                        }
                    }
                }
            }
        }

        static LFSO104Lib.LFFolder GetRootFolder()
        {
            using (ClientManager lfclient = new ClientManager())
            {
                // Find an existing client instance that is logged in to the repository
                IEnumerable<ClientInstance> clients = lfclient.GetAllClientInstances();
                foreach (ClientInstance _client in clients)
                {
                    IEnumerable<RepositoryConnection> repos = _client.RepositoryConnections;
                    foreach (RepositoryConnection repo in repos)
                    {
                        // Retrieve the serialized connection string and use it to initialize the LFSO connection object
                        string strSerializedConnection = repo.GetConnectionString();
                        LFSO104Lib.LFConnection lfsoconn = new LFConnection();
                        lfsoconn.CloneFromSerializedConnectionString(strSerializedConnection);
                        LFSO104Lib.LFDatabase lfdb = lfsoconn.Database;
                        LFFolder folder = lfdb.GetEntryByID(1);
                        return folder;
                    }
                }
            }

            return null;
        }

    }
}