About the CustomButtonManager sample project:

This project demonstrates how the ClientAutomation SDK can be used to add custom toolbar buttons
to the Laserfiche client. When the program is run without any arguments, it uses the SDK to
create a new toolbar and adds a few sample buttons to the toolbar. These buttons call 
CustomButtonManager.exe to perform their actions.

The buttons call CustomButtonManager.exe with the following command line arguments:

CustomButtonManager.exe -buttonclick -connguid "%(ConnectionGUID)" -hwnd "%(hwnd)"
                        -instanceguid "%(InstanceGUID)" -DocumentID "%(DocumentID)"
                        -SelectedPages "%(SelectedPages)" -SelectedEntries "%(SelectedEntries)"

The following tokens are replaced by the client:

 %(PID):            The LF.exe process ID
 %(ProcessID):      The LF.exe process ID
 %(InstanceGUID):   The LF.exe instance GUID
 %(ConnectionGUID): The LFSO connection GUID
 %(DatabaseName):   Current database name
 %(DatabaseGUID):   Current database GUID
 %(Username):       Current user name
 %(SID):            Current user security identifier
 %(hwnd):           Current window handle
 %(DocumentID):     Current document ID (Doc viewer only).
 %(FolderID):       Current folder ID (Entry listing only).
 %(SelectedEntries):Comma delimited list of selected entry IDs.
 %(SelectedPages):  Comma delimited list of selected page numbers.