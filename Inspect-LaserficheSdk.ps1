#Requires -Version 5.1
# Inspect Laserfiche SDK 11.0.2102.205 Assemblies using .NET Reflection
# Run this on your Windows machine where the Laserfiche Desktop Client is installed.

$ErrorActionPreference = "Stop"

$repoAccessPath   = "C:\Program Files\Common Files\Laserfiche\Client Helper\Laserfiche.RepositoryAccess.dll"
$docServicesPath  = "C:\Program Files\Common Files\Laserfiche\Client Helper\Laserfiche.DocumentServices.dll"

# Resolve full paths
$repoAccessPath  = (Resolve-Path $repoAccessPath  -ErrorAction Stop).Path
$docServicesPath = (Resolve-Path $docServicesPath -ErrorAction Stop).Path

$assemblies = @(
    @{ Path = $repoAccessPath;  Name = "Laserfiche.RepositoryAccess" }
    @{ Path = $docServicesPath; Name = "Laserfiche.DocumentServices" }
)

# Types we care about
$typeNames = @(
    "Laserfiche.RepositoryAccess.Session"
    "Laserfiche.RepositoryAccess.EntryInfo"
    "Laserfiche.RepositoryAccess.Entry"
    "Laserfiche.RepositoryAccess.FieldValueCollection"
    "Laserfiche.RepositoryAccess.LockType"
    "Laserfiche.RepositoryAccess.RepositoryRegistration"
    "Laserfiche.DocumentServices.DocumentInfo"
    "Laserfiche.DocumentServices.Document"
    "Laserfiche.DocumentServices.DocumentExporter"
    "Laserfiche.DocumentServices.EntryType"
)

function Get-TypeDetails($type) {
    $sb = [System.Text.StringBuilder]::new()

    # Header
    [void]$sb.AppendLine("=" * 60)
    [void]$sb.AppendLine("Type: $($type.FullName)")
    [void]$sb.AppendLine("Assembly: $($type.Assembly.GetName().Name)")
    [void]$sb.AppendLine("-" * 60)

    # Base class
    $base = $type.BaseType
    if ($base -and $base.FullName -ne "System.Object") {
        [void]$sb.AppendLine("Base Class: $($base.FullName)")
    } else {
        [void]$sb.AppendLine("Base Class: System.Object")
    }

    # Interfaces
    $interfaces = $type.GetInterfaces() | Sort-Object FullName
    if ($interfaces) {
        [void]$sb.AppendLine("Implemented Interfaces:")
        foreach ($i in $interfaces) {
            [void]$sb.AppendLine("  - $($i.FullName)")
        }
    } else {
        [void]$sb.AppendLine("Implemented Interfaces: (none)")
    }

    # IDisposable
    $implementsDisposable = $interfaces | Where-Object { $_.FullName -eq "System.IDisposable" }
    if ($implementsDisposable) {
        [void]$sb.AppendLine("Implements IDisposable: YES")
    } else {
        # Also check via GetInterface
        $disposable = $type.GetInterface("System.IDisposable")
        if ($disposable) {
            [void]$sb.AppendLine("Implements IDisposable: YES (via GetInterface)")
        } else {
            [void]$sb.AppendLine("Implements IDisposable: NO")
        }
    }

    # Constructors
    $ctors = $type.GetConstructors() | Sort-Object { $_.GetParameters().Count }
    [void]$sb.AppendLine("Constructors:")
    if ($ctors) {
        foreach ($ctor in $ctors) {
            $params = ($ctor.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
            [void]$sb.AppendLine("  .ctor($params)")
        }
    } else {
        [void]$sb.AppendLine("  (no public constructors)")
    }

    # Properties
    $props = $type.GetProperties() | Sort-Object Name
    [void]$sb.AppendLine("Public Properties:")
    if ($props) {
        foreach ($prop in $props) {
            $access = ""
            if ($prop.CanRead)  { $access += "get; " }
            if ($prop.CanWrite) { $access += "set; " }
            [void]$sb.AppendLine("  $($prop.PropertyType.Name) $($prop.Name) { $access}")
        }
    } else {
        [void]$sb.AppendLine("  (none)")
    }

    # Methods (instance, non-property accessor)
    $methods = $type.GetMethods() |
        Where-Object { -not $_.IsStatic } |
        Where-Object { -not $_.IsSpecialName -or ($_.Name -notmatch "^(get_|set_)") } |
        Sort-Object Name
    [void]$sb.AppendLine("Public Instance Methods:")
    if ($methods) {
        foreach ($m in $methods) {
            $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
            [void]$sb.AppendLine("  $($m.ReturnType.Name) $($m.Name)($params)")
        }
    } else {
        [void]$sb.AppendLine("  (none)")
    }

    # Static methods
    $staticMethods = $type.GetMethods() |
        Where-Object { $_.IsStatic } |
        Where-Object { -not $_.IsSpecialName -or ($_.Name -notmatch "^(get_|set_)") } |
        Sort-Object Name
    [void]$sb.AppendLine("Public Static Methods:")
    if ($staticMethods) {
        foreach ($m in $staticMethods) {
            $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
            [void]$sb.AppendLine("  $($m.ReturnType.Name) $($m.Name)($params)")
        }
    } else {
        [void]$sb.AppendLine("  (none)")
    }

    [void]$sb.AppendLine()
    return $sb.ToString()
}

function Get-CompatibilityTable($type) {
    $sb = [System.Text.StringBuilder]::new()

    [void]$sb.AppendLine("Type: $($type.FullName)")
    [void]$sb.AppendLine("-" * 40)

    # Constructors
    $ctors = $type.GetConstructors() | Sort-Object { $_.GetParameters().Count }
    [void]$sb.AppendLine("Constructors:")
    foreach ($ctor in $ctors) {
        $params = ($ctor.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        [void]$sb.AppendLine("  .ctor($params)")
    }
    [void]$sb.AppendLine()

    # Properties
    $props = $type.GetProperties() | Sort-Object Name
    [void]$sb.AppendLine("Properties:")
    foreach ($prop in $props) {
        [void]$sb.AppendLine("  $($prop.PropertyType.Name) $($prop.Name)")
    }
    [void]$sb.AppendLine()

    # Methods (all public, instance + static)
    $allMethods = $type.GetMethods() |
        Where-Object { -not $_.IsSpecialName -or ($_.Name -notmatch "^(get_|set_)") } |
        Sort-Object Name
    [void]$sb.AppendLine("Methods:")
    foreach ($m in $allMethods) {
        $staticFlag = if ($m.IsStatic) { "static " } else { "" }
        $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        [void]$sb.AppendLine("  $staticFlag$($m.ReturnType.Name) $($m.Name)($params)")
    }
    [void]$sb.AppendLine()

    # IDisposable
    $disposable = $type.GetInterface("System.IDisposable")
    if ($disposable) {
        [void]$sb.AppendLine("IDisposable: YES")
    } else {
        [void]$sb.AppendLine("IDisposable: NO")
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("=" * 40)
    [void]$sb.AppendLine()

    return $sb.ToString()
}

# Build outputs
$fullReflection = [System.Text.StringBuilder]::new()
$compatTable    = [System.Text.StringBuilder]::new()

[void]$fullReflection.AppendLine("LASERFICHE SDK REFLECTION REPORT")
[void]$fullReflection.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$fullReflection.AppendLine("=" * 60)
[void]$fullReflection.AppendLine()

[void]$compatTable.AppendLine("LASERFICHE SDK COMPATIBILITY TABLE")
[void]$compatTable.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$compatTable.AppendLine("=" * 40)
[void]$compatTable.AppendLine()

# Inspect assemblies header
foreach ($asm in $assemblies) {
    $assembly = [System.Reflection.Assembly]::LoadFrom($asm.Path)
    $name = $assembly.GetName()

    $header = @"
Assembly: $($name.Name)
  Version: $($name.Version)
  Path: $($asm.Path)
"@
    [void]$fullReflection.AppendLine($header)
    [void]$fullReflection.AppendLine()

    $compatHeader = @"
Assembly: $($name.Name)
Version: $($name.Version)
"@
    [void]$compatTable.AppendLine($compatHeader)
    [void]$compatTable.AppendLine("-" * 40)
}

# Full public types list
[void]$fullReflection.AppendLine("ALL PUBLIC TYPES")
[void]$fullReflection.AppendLine("-" * 60)
foreach ($asm in $assemblies) {
    $assembly = [System.Reflection.Assembly]::LoadFrom($asm.Path)
    $types = $assembly.GetTypes() | Where-Object { $_.IsPublic } | Sort-Object FullName
    [void]$fullReflection.AppendLine("$($assembly.GetName().Name) — $($types.Count) public types:")
    foreach ($t in $types) {
        [void]$fullReflection.AppendLine("  $($t.FullName)")
    }
    [void]$fullReflection.AppendLine()
}

# Inspect specific types
foreach ($typeName in $typeNames) {
    $type = $null
    foreach ($asm in $assemblies) {
        $assembly = [System.Reflection.Assembly]::LoadFrom($asm.Path)
        $type = $assembly.GetTypes() | Where-Object { $_.FullName -eq $typeName } | Select-Object -First 1
        if ($type) { break }
    }

    if (-not $type) {
        $msg = "WARNING: Type '$typeName' not found in either assembly."
        [void]$fullReflection.AppendLine($msg)
        [void]$compatTable.AppendLine($msg)
        continue
    }

    [void]$fullReflection.AppendLine((Get-TypeDetails $type))
    [void]$compatTable.AppendLine((Get-CompatibilityTable $type))
}

# Save files
$outDir = $PSScriptRoot
if (-not $outDir) { $outDir = (Get-Location).Path }

$reflectionFile = Join-Path $outDir "LaserficheSdkReflection.txt"
$compatFile     = Join-Path $outDir "LaserficheSdkCompatibility.txt"

$fullReflection.ToString() | Out-File -FilePath $reflectionFile -Encoding UTF8
$compatTable.ToString()     | Out-File -FilePath $compatFile     -Encoding UTF8

Write-Host "Done." -ForegroundColor Green
Write-Host "  Full report:  $reflectionFile" -ForegroundColor Cyan
Write-Host "  Compact:      $compatFile" -ForegroundColor Cyan
