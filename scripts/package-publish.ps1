param(
    [string]$Profile = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$PublishDir = '',
    [string]$OutputDir = 'artifacts',
    [string]$ZipName = '',
    [string]$ManifestBaseUrl = 'https://cdn.nuone.cl/nuone-tools',
    [string]$Channel = 'stable',
    [string]$ReleaseNotes = '',
    [string]$MinSupportedVersion = '',
    [string]$ManifestDeployDir = '\\dsm\web\cdn\nuone-tools',
    [switch]$Mandatory
)

$ErrorActionPreference = 'Stop'

function Resolve-ProjectValue {
    param(
        [xml]$ProjectXml,
        [string]$Name
    )

    $propertyGroups = @($ProjectXml.Project.PropertyGroup)
    foreach ($group in $propertyGroups) {
        if ($null -ne $group.$Name -and -not [string]::IsNullOrWhiteSpace($group.$Name)) {
            return [string]$group.$Name
        }
    }

    return ''
}

function Resolve-ExpandedProjectValue {
    param(
        [xml]$ProjectXml,
        [string]$Name,
        [hashtable]$Cache = $null,
        [System.Collections.Generic.HashSet[string]]$Stack = $null
    )

    if ($null -eq $Cache) {
        $Cache = @{}
    }

    if ($Cache.ContainsKey($Name)) {
        return [string]$Cache[$Name]
    }

    if ($null -eq $Stack) {
        $Stack = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    }

    if (-not $Stack.Add($Name)) {
        return Resolve-ProjectValue -ProjectXml $ProjectXml -Name $Name
    }

    $value = Resolve-ProjectValue -ProjectXml $ProjectXml -Name $Name
    while ($value -match '\$\(([^)]+)\)') {
        $referencedName = $matches[1]
        $referencedValue = Resolve-ExpandedProjectValue -ProjectXml $ProjectXml -Name $referencedName -Cache $Cache -Stack $Stack
        $value = $value.Replace('$(' + $referencedName + ')', $referencedValue)
    }

    $null = $Stack.Remove($Name)
    $Cache[$Name] = $value
    return $value
}

function Expand-PublishPath {
    param(
        [string]$RawPath,
        [hashtable]$Tokens
    )

    $expanded = $RawPath
    foreach ($key in $Tokens.Keys) {
        $expanded = $expanded.Replace('$(' + $key + ')', [string]$Tokens[$key])
    }

    return $expanded
}

function Get-7ZipExecutable {
    $command = Get-Command -Name '7z.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files\7-Zip\7z.exe',
        'C:\Program Files (x86)\7-Zip\7z.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return ''
}

function Join-Url {
    param(
        [string]$BaseUrl,
        [string]$ChildPath
    )

    $trimmedBaseUrl = $BaseUrl.Trim().TrimEnd('/')
    $trimmedChildPath = $ChildPath.Trim().TrimStart('/')

    if ([string]::IsNullOrWhiteSpace($trimmedBaseUrl)) {
        return $trimmedChildPath
    }

    if ([string]::IsNullOrWhiteSpace($trimmedChildPath)) {
        return $trimmedBaseUrl
    }

    return "$trimmedBaseUrl/$trimmedChildPath"
}

function New-UpdateManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [string]$ZipPath,
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [string]$ManifestBaseUrl,
        [string]$Channel,
        [string]$ReleaseNotes,
        [string]$MinSupportedVersion,
        [bool]$Mandatory
    )

    $zipFile = Get-Item -LiteralPath $ZipPath
    $zipHash = Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256
    $zipUrl = Join-Url -BaseUrl $ManifestBaseUrl -ChildPath $zipFile.Name

    $manifest = [ordered]@{
        app = 'nuone-tools'
        channel = $Channel
        version = $Version
        published_at = [DateTime]::UtcNow.ToString('O')
        package = [ordered]@{
            platform = $RuntimeIdentifier
            type = 'zip'
            filename = $zipFile.Name
            url = $zipUrl
            sha256 = $zipHash.Hash.ToLowerInvariant()
            size = $zipFile.Length
        }
        release_notes = $ReleaseNotes
        mandatory = $Mandatory
    }

    if (-not [string]::IsNullOrWhiteSpace($MinSupportedVersion)) {
        $manifest.min_supported_version = $MinSupportedVersion
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    Set-Content -LiteralPath $ManifestPath -Value $manifestJson -Encoding UTF8
}

function Publish-ReleaseFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DeployDirectory
    )

    if ([string]::IsNullOrWhiteSpace($DeployDirectory)) {
        return ''
    }

    New-Item -ItemType Directory -Path $DeployDirectory -Force | Out-Null
    $destinationPath = Join-Path $DeployDirectory (Split-Path -Path $SourcePath -Leaf)
    Copy-Item -LiteralPath $SourcePath -Destination $destinationPath -Force
    return $destinationPath
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'nuone-tools.csproj'
if (-not (Test-Path $projectPath)) {
    throw "找不到專案檔：$projectPath"
}

[xml]$projectXml = Get-Content -Path $projectPath
$projectPropertyCache = @{}
$version = Resolve-ExpandedProjectValue -ProjectXml $projectXml -Name 'Version' -Cache $projectPropertyCache
$targetFramework = Resolve-ExpandedProjectValue -ProjectXml $projectXml -Name 'TargetFramework' -Cache $projectPropertyCache

if ([string]::IsNullOrWhiteSpace($version)) {
    throw '無法從 nuone-tools.csproj 讀取 Version。'
}

if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw '無法從 nuone-tools.csproj 讀取 TargetFramework。'
}

$profilePath = Join-Path $repoRoot ("Properties\PublishProfiles\{0}.pubxml" -f $Profile)
if (-not (Test-Path $profilePath)) {
    throw "找不到 publish profile：$profilePath"
}

[xml]$profileXml = Get-Content -Path $profilePath
$runtimeIdentifier = Resolve-ProjectValue -ProjectXml $profileXml -Name 'RuntimeIdentifier'
$profilePublishDir = Resolve-ProjectValue -ProjectXml $profileXml -Name 'PublishDir'

if ([string]::IsNullOrWhiteSpace($runtimeIdentifier)) {
    throw "無法從 $Profile 讀取 RuntimeIdentifier。"
}

$resolvedPublishDir = $PublishDir
if ([string]::IsNullOrWhiteSpace($resolvedPublishDir)) {
    $resolvedPublishDir = $profilePublishDir
}

if ([string]::IsNullOrWhiteSpace($resolvedPublishDir)) {
    throw "無法從 $Profile 讀取 PublishDir。"
}

$resolvedPublishDir = Expand-PublishPath -RawPath $resolvedPublishDir -Tokens @{
    Configuration = $Configuration
    TargetFramework = $targetFramework
    RuntimeIdentifier = $runtimeIdentifier
}

if (-not [System.IO.Path]::IsPathRooted($resolvedPublishDir)) {
    $resolvedPublishDir = Join-Path $repoRoot $resolvedPublishDir
}

$resolvedPublishDir = [System.IO.Path]::GetFullPath($resolvedPublishDir)
if (-not (Test-Path $resolvedPublishDir)) {
    throw "Publish 目錄不存在：$resolvedPublishDir`n請先 publish，再執行這支腳本。"
}

$exePath = Join-Path $resolvedPublishDir 'nuone-tools.exe'
if (-not (Test-Path $exePath)) {
    Write-Warning "在 publish 目錄內找不到 nuone-tools.exe：$exePath"
}

$resolvedOutputDir = $OutputDir
if (-not [System.IO.Path]::IsPathRooted($resolvedOutputDir)) {
    $resolvedOutputDir = Join-Path $repoRoot $resolvedOutputDir
}

$resolvedOutputDir = [System.IO.Path]::GetFullPath($resolvedOutputDir)
New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

$archiveBaseName = if ([string]::IsNullOrWhiteSpace($ZipName)) {
    "nuone-tools-$version-$runtimeIdentifier"
} else {
    $ZipName
}

$zipPath = Join-Path $resolvedOutputDir ($archiveBaseName + '.zip')
$stagingRoot = Join-Path $resolvedOutputDir '.staging'
$stagingDir = Join-Path $stagingRoot $archiveBaseName
$zipTool = 'Compress-Archive'

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Copy-Item -Path (Join-Path $resolvedPublishDir '*') -Destination $stagingDir -Recurse -Force

$sevenZipExe = Get-7ZipExecutable
if (-not [string]::IsNullOrWhiteSpace($sevenZipExe)) {
    $zipTool = '7-Zip'
    $sevenZipArguments = @(
        'a',
        '-tzip',
        '-mx=9',
        $zipPath,
        '.\*'
    )

    Push-Location -LiteralPath $stagingDir
    try {
        & $sevenZipExe @sevenZipArguments | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "7-Zip 壓縮失敗，exit code=$LASTEXITCODE"
        }
    } finally {
        Pop-Location
    }
} else {
    Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
}

Remove-Item -LiteralPath $stagingDir -Recurse -Force

$manifestPath = Join-Path $resolvedOutputDir 'manifest.json'
New-UpdateManifest `
    -Version $version `
    -RuntimeIdentifier $runtimeIdentifier `
    -ZipPath $zipPath `
    -ManifestPath $manifestPath `
    -ManifestBaseUrl $ManifestBaseUrl `
    -Channel $Channel `
    -ReleaseNotes $ReleaseNotes `
    -MinSupportedVersion $MinSupportedVersion `
    -Mandatory:$Mandatory

$deployedZipPath = Publish-ReleaseFile `
    -SourcePath $zipPath `
    -DeployDirectory $ManifestDeployDir

$deployedManifestPath = Publish-ReleaseFile `
    -SourcePath $manifestPath `
    -DeployDirectory $ManifestDeployDir

Write-Host "完成打包"
Write-Host "Version      : $version"
Write-Host "Runtime      : $runtimeIdentifier"
Write-Host "PublishDir   : $resolvedPublishDir"
Write-Host "ZipTool      : $zipTool"
Write-Host "Zip          : $zipPath"
Write-Host "Manifest     : $manifestPath"
if (-not [string]::IsNullOrWhiteSpace($deployedZipPath)) {
    Write-Host "ZipCdn       : $deployedZipPath"
}
if (-not [string]::IsNullOrWhiteSpace($deployedManifestPath)) {
    Write-Host "ManifestCdn  : $deployedManifestPath"
}
