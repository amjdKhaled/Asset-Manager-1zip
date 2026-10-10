#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $repo 'artifacts\staging\Extension\Dashboard.SetupHelper.exe'
if (-not (Test-Path -LiteralPath $helper)) { throw "Staged helper not found: $helper" }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('DashboardWebClientSafety-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'assets\custom') -Force | Out-Null
$page = Join-Path $fixture 'Browse.aspx'
$js = Join-Path $fixture 'assets\custom\lf-dashboard-button.js'
function Run-Helper([string[]] $Arguments) {
    & $helper @Arguments
    if ($LASTEXITCODE -ne 0) { throw "SetupHelper failed with $LASTEXITCODE" }
}
function Assert-Bytes([string] $Path, [byte[]] $Expected) {
    $actual = [IO.File]::ReadAllBytes($Path)
    if ([Convert]::ToBase64String($actual) -cne [Convert]::ToBase64String($Expected)) {
        throw "Unexpected change: $Path"
    }
}
try {
    $original = [Text.Encoding]::Unicode.GetPreamble() + [Text.Encoding]::Unicode.GetBytes("<head>`r`n<script>foreign()</script></head><body>وثيقة</body>")
    [IO.File]::WriteAllBytes($page, $original)
    $oldJs = [Text.Encoding]::UTF8.GetBytes("var DASHBOARD_BASE_URL = 'http://old';")
    [IO.File]::WriteAllBytes($js, $oldJs)
    $transaction = [Guid]::NewGuid().ToString('N')
    Run-Helper @('--deploy-webclient', '--url', 'http://localhost:5000', '--path', $fixture, '--transaction', $transaction)
    Run-Helper @('--rollback-webclient', '--path', $fixture, '--transaction', $transaction)
    Assert-Bytes $page $original
    Assert-Bytes $js $oldJs

    [IO.File]::WriteAllText(($page + '.bak-99999999'), 'unrelated backup')
    Run-Helper @('--rollback-webclient', '--path', $fixture, '--transaction', ([Guid]::NewGuid().ToString('N')))
    Assert-Bytes $page $original

    $transaction = [Guid]::NewGuid().ToString('N')
    Run-Helper @('--deploy-webclient', '--url', 'http://localhost:5000', '--path', $fixture, '--transaction', $transaction)
    $concurrent = [IO.File]::ReadAllBytes($page) + [Text.Encoding]::Unicode.GetBytes('<footer>concurrent Laserfiche change</footer>')
    [IO.File]::WriteAllBytes($page, $concurrent)
    Run-Helper @('--rollback-webclient', '--path', $fixture, '--transaction', $transaction)
    Assert-Bytes $page $concurrent
    Assert-Bytes $js $oldJs

    Run-Helper @('--remove-webclient', '--path', $fixture)
    Assert-Bytes $page $concurrent
    Assert-Bytes $js $oldJs
    if (-not (Test-Path -LiteralPath ($page + '.bak-99999999'))) { throw 'Unrelated backup was deleted.' }
    Write-Host 'Web Client safety checks passed.'
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
