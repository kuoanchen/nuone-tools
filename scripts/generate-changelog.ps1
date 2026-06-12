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
$previousTag = Get-PreviousVersionTag
$commits = Get-GitCommits -SinceRef $previousTag

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

$content = ($lines -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine
Set-Content -Path $resolvedOutputPath -Value $content -Encoding UTF8

Write-Host "已產生 CHANGELOG：$resolvedOutputPath"
