param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

function Resolve-WindowsAppRuntimeMsixDirectory {
    param(
        [string]$RuntimeIdentifier
    )

    $architecture = switch -Regex ($RuntimeIdentifier) {
        'x64$' { 'win10-x64'; break }
        'x86$' { 'win10-x86'; break }
        'arm64$' { 'win10-arm64'; break }
        default { '' }
    }

    if ([string]::IsNullOrWhiteSpace($architecture)) {
        return ''
    }

    $runtimePackageRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windowsappsdk.runtime'
    if (-not (Test-Path -LiteralPath $runtimePackageRoot)) {
        return ''
    }

    $runtimeVersionDirectory = Get-ChildItem -LiteralPath $runtimePackageRoot -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    if ($null -eq $runtimeVersionDirectory) {
        return ''
    }

    $candidate = Join-Path $runtimeVersionDirectory.FullName ("tools\MSIX\{0}" -f $architecture)
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }

    return ''
}

$resolvedOutputDirectory = $OutputDirectory.Trim().Trim('"')
if (-not [System.IO.Path]::IsPathRooted($resolvedOutputDirectory)) {
    $resolvedOutputDirectory = Join-Path (Get-Location) $resolvedOutputDirectory
}

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($resolvedOutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$runtimeMsixDirectory = Resolve-WindowsAppRuntimeMsixDirectory -RuntimeIdentifier $RuntimeIdentifier
if ([string]::IsNullOrWhiteSpace($runtimeMsixDirectory)) {
    Write-Warning "Cannot find Windows App Runtime MSIX directory for $RuntimeIdentifier."
    exit 0
}

$runtimeMsixPath = Join-Path $runtimeMsixDirectory 'Microsoft.WindowsAppRuntime.2.msix'
if (-not (Test-Path -LiteralPath $runtimeMsixPath)) {
    Write-Warning "Cannot find $runtimeMsixPath."
    exit 0
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($runtimeMsixPath)
try {
    $entry = $archive.Entries |
        Where-Object { $_.FullName -eq 'Microsoft.WindowsAppRuntime.Insights.Resource.dll' } |
        Select-Object -First 1
    if ($null -eq $entry) {
        Write-Warning 'Cannot find Microsoft.WindowsAppRuntime.Insights.Resource.dll inside Windows App Runtime MSIX.'
        exit 0
    }

    $destinationPath = Join-Path $resolvedOutputDirectory 'Microsoft.WindowsAppRuntime.Insights.Resource.dll'
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destinationPath, $true)
    Write-Host "Copied Microsoft.WindowsAppRuntime.Insights.Resource.dll to $destinationPath"
} finally {
    $archive.Dispose()
}
