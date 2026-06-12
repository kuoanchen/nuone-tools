param(
    [string]$TemplatePath = '',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'doc-helpers.ps1')

$repoRoot = Get-RepoRoot
$metadata = Get-ProjectMetadata

$resolvedTemplatePath = if ([string]::IsNullOrWhiteSpace($TemplatePath)) {
    Join-Path $repoRoot 'README.template.md'
} elseif ([System.IO.Path]::IsPathRooted($TemplatePath)) {
    $TemplatePath
} else {
    Join-Path $repoRoot $TemplatePath
}

if (-not (Test-Path $resolvedTemplatePath)) {
    throw "找不到 README 範本：$resolvedTemplatePath"
}

$resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $repoRoot 'README.md'
} elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}

$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$changelogContent = if (Test-Path $changelogPath) {
    ((Get-Content -Path $changelogPath -Raw).Trim() -replace '^# Changelog\r?\n\r?\n', '')
} else {
    '尚未產生 CHANGELOG.md，請先執行 scripts\generate-changelog.ps1。'
}

$template = Get-Content -Path $resolvedTemplatePath -Raw
$tokens = @{
    ProjectName           = 'Nuone Tools'
    RootNamespace         = $metadata.Name
    Version               = $metadata.Version
    AssemblyVersion       = $metadata.AssemblyVersion
    FileVersion           = $metadata.FileVersion
    InformationalVersion  = $metadata.InformationalVersion
    TargetFramework       = $metadata.TargetFramework
    Platforms             = $metadata.Platforms
    RuntimeIdentifiers    = $metadata.RuntimeIdentifiers
    RepositoryUrl         = $metadata.RepositoryUrl
    ChangelogContent      = $changelogContent
}

$content = Replace-TemplateTokens -Template $template -Tokens $tokens
Set-Content -Path $resolvedOutputPath -Value $content -Encoding UTF8

Write-Host "已產生 README：$resolvedOutputPath"
