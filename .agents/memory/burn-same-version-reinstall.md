---
name: Burn same-version reinstall strips shared integrations
description: Why a rebuilt same-version bundle uninstalled the Web Client button after the new install, and the required WiX settings.
---

**Rule:** Any MSI whose uninstall CA removes a shared external integration (e.g. a script tag in Laserfiche Browse.aspx) MUST have `MajorUpgrade AllowSameVersionUpgrades="yes"` and guard the remove CA with `NOT UPGRADINGPRODUCTCODE`.

**Why:** Builds reuse ProductVersion 1.0.0 with auto-generated ProductCode. Without AllowSameVersionUpgrades, a rebuilt bundle installs side-by-side with the old MSI; Burn uninstalls the old related bundle AFTER the new chain, and the old MSI's remove CA runs with its `Persisted="yes"` bundle variables (non-empty path) — stripping the integration the new install just verified (1 -> 0 button, everything "succeeds").

**How to apply:** Keep both guards in Product.wxs. With them, the new MSI removes the old MSI early (RemoveExistingProducts, where the path property is empty so the old remove CA skips), and Burn's later old-bundle uninstall finds the MSI absent and executes nothing.

Also: DeployWebClient is now Return="check" — a user-requested web-button deployment that fails verification fails the install loudly. Known tradeoff: repair with a stale persisted Web Client path (Browse.aspx moved/removed) hard-fails; surface in BA preflight if this bites.
