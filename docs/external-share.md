# External Share Mode

External Share Mode provides a separate, read-only Dashboard entry point for a
temporary public tunnel. It authenticates directly against the configured
Laserfiche Repository API with a repository username and password. It does not
use LFDS, OAuth, `StartSso`, `Callback`, or `SsoDiagnostic`.

## Configuration

Set a unique, high-entropy access key before exposing the application:

```json
"ExternalShare": {
  "Enabled": true,
  "AccessKey": "replace-with-a-long-random-value",
  "ReadOnly": true,
  "Repositories": [ "TestEmployee" ]
}
```

When `Repositories` is omitted, `Laserfiche:RepositoryId` is the only repository
allowed by the external login form. Never keep the example access key in a real
deployment.

## Tunnel usage

1. Run the Dashboard normally and verify that it can reach the Repository API.
2. Configure the tunnel to forward HTTPS traffic to the Dashboard application
   only. Do **not** expose `/LFRepositoryAPI` directly.
3. Open the relative application route through the tunnel:
   `/Share/Login?key=replace-with-a-long-random-value`.
4. Sign in with a repository account. Successful authentication redirects to
   `/Share/Dashboard` and uses the same live Dashboard services as the internal
   Dashboard.

The access-key query parameter is required only to start the external session.
The resulting HttpOnly cookie expires after two hours. The repository password
is used only for the sign-in request and is not stored in the cookie or server
session. Repository API tokens remain in the server-side per-session token cache
and never enter browser JavaScript, URLs, or HTML.

Use an HTTPS tunnel, restrict who receives its URL and key, and disable
`ExternalShare:Enabled` when the review is complete.
