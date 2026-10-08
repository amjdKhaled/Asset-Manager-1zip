# Dashboard installer isolation

The active installer is built from installer/Dashboard.Installer/Product.wxs by build/publish.ps1. LFDashboard.aip is a legacy project; its blanket APPDIR manual deletion rule has also been removed.

## Removal policy

- Windows Installer removes its explicitly packaged application files and shortcuts. Unknown files are not recursively removed.
- Saved configuration and credentials are kept by default. Opt-in cleanup deletes only extension.config.json, laserfiche.config.json, laserfiche.runtime.json and the default Dashboard DPAPI credential file under the dedicated ProgramData Dashboard directory. Logs and other files are retained.
- Uninstall preserves all Laserfiche Web Client files, including Browse.aspx, backups and the optional JavaScript button. The Web Client integration remains present after Dashboard is removed and should be managed independently.
- Desktop cleanup removes only buttons with the exact executable path of this installed Dashboard extension. It preserves the toolbar and any other buttons.
- Certificates, IIS/ANCM, .NET Framework, WebView2, Laserfiche services, repository files and database content are not removed.

## Guard before mutation

An embedded x64 .NET Framework custom action runs before InstallInitialize during installation, repair, upgrade and uninstall. It checks resolved application, data and Start Menu directories against dedicated default locations, checks every MSI payload file for reparse points and hard links, and refuses colliding IIS resources or an app pool shared with another site. Custom INSTALLFOLDER/child directory overrides are deliberately unsupported. A rejected operation logs the reason and stops before scheduling destructive actions.

This protection applies to newly built installers. It cannot change removal logic cached by Windows Installer for an already installed older product. A source pull does not update an installed MSI.

## Web Client installation and failure recovery

Optional deployment remains available. Page insertion preserves supported UTF-8 and UTF-16 encodings, BOM, newline style and existing content. Unexpected tags, encodings or missing anchors stop deployment. Preimages and expected postimages are saved in an ACL-restricted transaction-specific journal. Rollback restores a file only if it still matches this transaction's output; concurrent changes and unrelated backups are preserved. A successful commit removes only that transaction's journal.

## Configuration failure recovery

MSI configuration writes use a separate transaction journal covering the known connection/extension settings, default encrypted credential file and application port setting. On a late install/repair failure, rollback restores a prior file only when it still matches the installer's expected write. A concurrent change is preserved. The optional Desktop registration feature no longer controls availability of the required SetupHelper payload.

The manual Web Client deployment script delegates to the same safe helper and requires an exact transaction ID for rollback; it no longer selects a recent unrelated backup. Configuration script writes are atomic and refuse reparse points.

## Fast deployment

deploy-fast.ps1 requires the dedicated Dashboard location and an exclusive Dashboard IIS app pool. It uses a fresh temporary publish directory and copies without destination purge or recursive source deletion. Source and destination links are refused; robocopy retries are bounded. A previously running pool is restarted in finally even if copying fails. Saved appsettings are excluded. With SkipPublish, supply the previous output explicitly.

## Verification

Windows CI runs the source-linked installer safety executable, existing application tests, builds the native embedded guard and complete bundle, and exercises the staged SetupHelper against isolated Web Client fixtures. Safety cases cover unknown files, ancestor/credential junctions, path redirection, Arabic/BOM preservation, IIS name collisions, shared pools, exact rollback and concurrent page changes.

Windows CI also exercises installation, repair, deferred failure rollback, same-version MajorUpgrade, shared-pool refusal and uninstall against unrelated file/IIS sentinels on a disposable runner. ANCM presence is supplied for these isolation tests; they do not verify serving application requests. This tests the revised MSI lifecycle, not every historically installed release. The checked-in Release executable is not updated by this branch; the build produces a new artifact. An upgrade executes the previous product's cached uninstall logic, so legacy versions must be included in acceptance testing.

