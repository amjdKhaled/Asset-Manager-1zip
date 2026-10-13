#Requires -Version 5.1
# Read-only fixture tests: no IIS installation or deployment is performed.
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$scriptPath = Join-Path $PSScriptRoot 'deploy-fast.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -gt 0) { throw "deploy-fast.ps1 has parser errors: $parseErrors" }
foreach ($name in @('Canonical', 'Assert-DashboardIisIsolation')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing deployment function: $name" }
    Invoke-Expression $definition.Extent.Text
}
$expected = Join-Path ([IO.Path]::GetTempPath()) 'Dashboard-IisFixture'
$escaped = [Security.SecurityElement]::Escape($expected)
$script:inventory = '<appcmd><APP APP.NAME="Dashboard/" APPPOOL.NAME="Dashboard" /><APP APP.NAME="Default Web Site/" APPPOOL.NAME="DefaultAppPool" /></appcmd>'
$script:commandExit = 0
function Mock-AppCmd {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
    if (($Arguments -join ' ') -ne 'list app /xml') { throw 'Unexpected IIS command in fixture.' }
    $global:LASTEXITCODE = $script:commandExit
    return $script:inventory
}
function Check-Fixture {
    param([string] $Name, [string] $Configuration, [string] $ErrorPattern = '')
    $failure = $null
    try { Assert-DashboardIisIsolation -Iis ([xml] $Configuration) -ExpectedPath $expected -AppCmd 'Mock-AppCmd' }
    catch { $failure = $_.Exception.Message }
    if ($ErrorPattern) {
        if (-not $failure -or $failure -notlike $ErrorPattern) {
            throw "${Name}: expected '$ErrorPattern', got '$failure'"
        }
    } elseif ($failure) { throw "${Name}: $failure" }
    Write-Host "PASS: $Name"
}
# The production failure: another site's application has no applicationPool attribute.
$fixture = '<configuration><system.applicationHost><sites><site name="Dashboard"><application path="/" applicationPool="Dashboard"><virtualDirectory path="/" physicalPath="' + $escaped + '" /></application></site><site name="Default Web Site"><application path="/"><virtualDirectory path="/" physicalPath="other" /></application></site></sites></system.applicationHost></configuration>'
Check-Fixture 'Missing optional applicationPool on another site' $fixture
Check-Fixture 'Dashboard inherits its effective dedicated pool' ($fixture.Replace(' applicationPool="Dashboard"', ''))
Check-Fixture 'Missing Dashboard site' ($fixture.Replace('name="Dashboard"', 'name="Other"')) '*site was not found*'
Check-Fixture 'Extra Dashboard application refused' ($fixture.Replace('</application></site><site', '</application><application path="/extra" /></site><site')) '*unexpected or shared application*'
Check-Fixture 'Unexpected virtual directory refused' ($fixture.Replace('<virtualDirectory path="/"', '<virtualDirectory path="/extra"')) '*virtual directories*'
Check-Fixture 'Missing physical path refused' ($fixture.Replace(' physicalPath="' + $escaped + '"', '')) '*physical path is missing*'
Check-Fixture 'Different physical path refused' ($fixture.Replace($escaped, $escaped + '-Other')) '*outside the Dashboard folder*'
$script:inventory = '<appcmd><APP APP.NAME="Dashboard/" APPPOOL.NAME="Dashboard" /><APP APP.NAME="Laserfiche/" APPPOOL.NAME="Dashboard" /></appcmd>'
Check-Fixture 'Shared effective pool refused' $fixture '*Another IIS application*'
$script:inventory = '<appcmd><APP APP.NAME="Dashboard/" APPPOOL.NAME="DefaultAppPool" /></appcmd>'
Check-Fixture 'Wrong Dashboard effective pool refused' $fixture '*dedicated Dashboard app pool*'
$script:inventory = '<appcmd><APP APP.NAME="Dashboard/" APPPOOL.NAME="Dashboard" /><APP APP.NAME="Other/" /></appcmd>'
Check-Fixture 'Incomplete effective inventory refused' $fixture '*incomplete application inventory*'
$script:inventory = '<invalid'
Check-Fixture 'Malformed AppCmd output refused' $fixture '*invalid application inventory*'
$script:commandExit = 1
Check-Fixture 'Failed AppCmd query refused' $fixture '*Could not inspect effective*'
Write-Host 'All deployment IIS isolation fixtures passed.'
