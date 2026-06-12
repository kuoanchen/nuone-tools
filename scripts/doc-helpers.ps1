Set-StrictMode -Version Latest

function Get-RepoRoot {
    return Split-Path -Parent $PSScriptRoot
}

function Get-ProjectPath {
    $projectPath = Join-Path (Get-RepoRoot) 'nuone-tools.csproj'
    if (-not (Test-Path $projectPath)) {
        throw "找不到專案檔：$projectPath"
    }

    return $projectPath
}

function Get-ProjectXml {
    [xml](Get-Content -Path (Get-ProjectPath))
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    foreach ($group in @($ProjectXml.Project.PropertyGroup)) {
        $value = $group.$Name
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            return [string]$value
        }
    }

    return ''
}

function Get-ProjectMetadata {
    $projectXml = Get-ProjectXml
    $repoRoot = Get-RepoRoot
    $originUrl = ''

    try {
        $originUrl = (git -C $repoRoot remote get-url origin 2>$null | Select-Object -First 1)
    } catch {
        $originUrl = ''
    }

    return [ordered]@{
        Name                 = Get-ProjectProperty -ProjectXml $projectXml -Name 'RootNamespace'
        Version              = Get-ProjectProperty -ProjectXml $projectXml -Name 'Version'
        AssemblyVersion      = Get-ProjectProperty -ProjectXml $projectXml -Name 'AssemblyVersion'
        FileVersion          = Get-ProjectProperty -ProjectXml $projectXml -Name 'FileVersion'
        InformationalVersion = Get-ProjectProperty -ProjectXml $projectXml -Name 'InformationalVersion'
        TargetFramework      = Get-ProjectProperty -ProjectXml $projectXml -Name 'TargetFramework'
        Platforms            = Get-ProjectProperty -ProjectXml $projectXml -Name 'Platforms'
        RuntimeIdentifiers   = Get-ProjectProperty -ProjectXml $projectXml -Name 'RuntimeIdentifiers'
        RepositoryUrl        = Convert-GitRemoteToHttps -RemoteUrl $originUrl
    }
}

function Convert-GitRemoteToHttps {
    param(
        [string]$RemoteUrl
    )

    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
        return ''
    }

    $normalized = $RemoteUrl.Trim()

    if ($normalized -match '^https?://') {
        return $normalized -replace '\.git$', ''
    }

    if ($normalized -match '^git@([^:]+):(.+)$') {
        $remoteHost = $matches[1]
        $remotePath = $matches[2]
        if ($remoteHost -match '^[^.]+\.github\.com$') {
            $remoteHost = 'github.com'
        }

        return ('https://{0}/{1}' -f $remoteHost, $remotePath) -replace '\.git$', ''
    }

    return $normalized
}

function Get-GitCommits {
    param(
        [string]$SinceRef = ''
    )

    $repoRoot = Get-RepoRoot
    $format = '%H%x1f%h%x1f%s%x1f%b%x1e'
    $arguments = @('-C', $repoRoot, 'log', '--date=short', "--pretty=format:$format")
    if (-not [string]::IsNullOrWhiteSpace($SinceRef)) {
        $arguments += "$SinceRef..HEAD"
    }

    $raw = & git @arguments
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @()
    }

    $records = $raw -split [char]0x1e
    $commits = New-Object System.Collections.Generic.List[object]

    foreach ($record in $records) {
        if ([string]::IsNullOrWhiteSpace($record)) {
            continue
        }

        $parts = $record.Trim() -split [char]0x1f
        if ($parts.Length -lt 3) {
            continue
        }

        $subject = $parts[2].Trim()
        $body = if ($parts.Length -ge 4) { $parts[3].Trim() } else { '' }
        $parsed = Parse-ConventionalCommit -Message $subject

        $commits.Add([pscustomobject]@{
            Hash      = $parts[0].Trim()
            ShortHash = $parts[1].Trim()
            Subject   = $subject
            Body      = $body
            Type      = $parsed.Type
            Scope     = $parsed.Scope
            Title     = $parsed.Title
            Group     = $parsed.Group
        })
    }

    return $commits
}

function Parse-ConventionalCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Message -match '^(?<type>[a-zA-Z0-9_-]+)(\((?<scope>[^)]+)\))?(?<breaking>!)?:\s*(?<title>.+)$') {
        $type = $matches['type'].ToLowerInvariant()
        $scope = $matches['scope']
        $title = $matches['title'].Trim()
        return [pscustomobject]@{
            Type  = $type
            Scope = $scope
            Title = $title
            Group = Get-CommitGroupTitle -Type $type
        }
    }

    return [pscustomobject]@{
        Type  = 'other'
        Scope = ''
        Title = $Message.Trim()
        Group = Get-CommitGroupTitle -Type 'other'
    }
}

function Get-CommitGroupTitle {
    param(
        [string]$Type
    )

    switch ($Type) {
        'feat' { return '新功能' }
        'fix' { return '修復' }
        'refactor' { return '重構' }
        'perf' { return '效能' }
        'docs' { return '文件' }
        'style' { return '樣式' }
        'test' { return '測試' }
        'build' { return '建置' }
        'ci' { return 'CI' }
        'chore' { return '維護' }
        default { return '其他' }
    }
}

function Get-OrderedCommitGroups {
    return @('新功能', '修復', '重構', '效能', '樣式', '文件', '測試', '建置', 'CI', '維護', '其他')
}

function Get-PreviousVersionTag {
    $repoRoot = Get-RepoRoot
    try {
        $tag = git -C $repoRoot tag --sort=-creatordate | Select-Object -Skip 1 -First 1
        if ($null -eq $tag) {
            return ''
        }

        return [string]$tag
    } catch {
        return ''
    }
}

function Render-MarkdownListFromCommit {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Commit,
        [string]$RepositoryUrl = ''
    )

    $scopePrefix = if ([string]::IsNullOrWhiteSpace($Commit.Scope)) { '' } else { '**' + $Commit.Scope + ':** ' }
    $commitLink = if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
        $Commit.ShortHash
    } else {
        '[{0}]({1}/commit/{2})' -f $Commit.ShortHash, $RepositoryUrl, $Commit.Hash
    }

    $line = '* {0}{1} ({2})' -f $scopePrefix, $Commit.Title, $commitLink
    $bodyLines = @()
    if (-not [string]::IsNullOrWhiteSpace($Commit.Body)) {
        $bodyLines = @(
            $Commit.Body -split '\r?\n' |
                ForEach-Object { ($_ -replace '^\s*[-*]\s*', '').Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
    }

    if ($bodyLines.Count -eq 0) {
        return $line
    }

    return ($line + [Environment]::NewLine + (($bodyLines | ForEach-Object { '  - ' + $_ }) -join [Environment]::NewLine))
}

function Replace-TemplateTokens {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Template,
        [Parameter(Mandatory = $true)]
        [hashtable]$Tokens
    )

    $content = $Template
    foreach ($key in $Tokens.Keys) {
        $content = $content.Replace('{{' + $key + '}}', [string]$Tokens[$key])
    }

    return $content
}
