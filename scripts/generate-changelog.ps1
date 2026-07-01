param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'doc-helpers.ps1')

$repoRoot = Get-RepoRoot
$metadata = Get-ProjectMetadata
$resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $repoRoot 'CHANGELOG.md'
} elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}

$currentDate = Get-Date -Format 'yyyy-MM-dd'
$previousVersion = Get-PreviousReleasedVersion -CurrentVersion $metadata.Version
$previousBoundaryRef = Resolve-ReleaseBoundaryRef -Version $previousVersion
$commits = Get-GitCommits -SinceRef $previousBoundaryRef

function Get-ChangelogBody {
    param(
        [string]$Content
    )

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return ''
    }

    return (($Content -replace '^\uFEFF', '').Trim() -replace '^# Changelog\r?\n\r?\n', '').Trim()
}

function Remove-VersionSection {
    param(
        [string]$Body,
        [string]$Version
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return ''
    }

    $pattern = '(?ms)^## ' + [regex]::Escape($Version) + ' \([^)]+\)\r?\n.*?(?=^## |\z)'
    return ([regex]::Replace($Body, $pattern, '')).Trim()
}

function Remove-UnresolvedVersionSections {
    param(
        [string]$Body
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return ''
    }

    return ([regex]::Replace($Body, '(?ms)^## \$\([^)]+\) \([^)]+\)\r?\n.*?(?=^## |\z)', '')).Trim()
}

function Get-GitFileContent {
    param(
        [string]$RepositoryRoot,
        [string]$RepositoryRelativePath
    )

    try {
        return (& git -C $RepositoryRoot show ("HEAD:" + $RepositoryRelativePath) 2>$null | Out-String)
    } catch {
        return ''
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Changelog')
$lines.Add('')
$heading = '## {0} ({1})' -f $metadata.Version, $currentDate
$lines.Add($heading)
$lines.Add('')

if ($commits.Count -eq 0) {
    $lines.Add('* 尚無可用的提交紀錄。')
} else {
    $grouped = $commits | Group-Object -Property Group -AsHashTable -AsString
    foreach ($groupName in Get-OrderedCommitGroups) {
        if (-not $grouped.ContainsKey($groupName)) {
            continue
        }

        $lines.Add('### ' + $groupName)
        $lines.Add('')
        foreach ($commit in $grouped[$groupName]) {
            $lines.Add((Render-MarkdownListFromCommit -Commit $commit -RepositoryUrl $metadata.RepositoryUrl))
        }
        $lines.Add('')
    }
}

$currentSection = (($lines | Select-Object -Skip 2) -join [Environment]::NewLine).Trim()
$workingTreeBody = if (Test-Path $resolvedOutputPath) {
    Get-ChangelogBody -Content (Get-Content -Path $resolvedOutputPath -Raw)
} else {
    ''
}
$trackedBody = Get-ChangelogBody -Content (Get-GitFileContent -RepositoryRoot $repoRoot -RepositoryRelativePath 'CHANGELOG.md')
$historyCandidates = @(
    Remove-UnresolvedVersionSections -Body (Remove-VersionSection -Body $workingTreeBody -Version $metadata.Version)
    Remove-UnresolvedVersionSections -Body (Remove-VersionSection -Body $trackedBody -Version $metadata.Version)
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$historyBody = ($historyCandidates | Sort-Object Length -Descending | Select-Object -First 1)

$content = '# Changelog' + [Environment]::NewLine + [Environment]::NewLine + $currentSection
if (-not [string]::IsNullOrWhiteSpace($historyBody)) {
    $content += [Environment]::NewLine + [Environment]::NewLine + $historyBody.Trim()
}

$content = $content.TrimEnd() + [Environment]::NewLine
Set-Content -Path $resolvedOutputPath -Value $content -Encoding UTF8

Write-Host "已產生 CHANGELOG：$resolvedOutputPath"
