---
name: Installer API host selection
description: Certificate-driven Laserfiche API URL detection; localhost never a blind default
---

# Rule
The installer must not default the Laserfiche API URL to https://localhost/...:
the IIS certificate frequently lacks "localhost", so the Dashboard's HttpClient
fails TLS even though the API works in a browser/PowerShell.

**Why:** Production TLS login failure traced to hostname mismatch — cert issued
to the machine name, config pointed at localhost.

**How to apply:**
- `ApiHostSelector` (installer/Dashboard.BA, pure BCL, no WinForms/WiX) picks
  binding-host > FQDN > machine name > localhost, each requiring an explicit
  cert SAN/CN match (wildcards single-label); expired cert = no selection;
  untrusted chain = host + LocalMachine-trust warning, never a bypass.
- It is compile-linked into LFPortal.Web.Tests (net8) so tests run on Linux
  against the real net48-consumed source. Keep it dependency-free.
- DetectionService resolves the cert via applicationHost.config (site with app
  path /LFRepositoryAPI) + `netsh http show sslcert` matched by EXACT endpoint
  (hostname:port for SNI, ip:port else, port-only fallback), and builds the
  chain with `new X509Chain(useMachineContext: true)` — IIS app pools use
  machine trust, not CurrentUser.
- Wizard "Next" does a live HTTPS probe with a RECORDING (never bypassing)
  per-request ServerCertificateValidationCallback: hard-block on TLS failure,
  Yes/No for plain unreachability.
