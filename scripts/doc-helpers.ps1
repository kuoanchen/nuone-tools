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

function Resolve-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,
        [Parameter(Mandatory = $true)]
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
        return Get-ProjectProperty -ProjectXml $ProjectXml -Name $Name
    }

    $value = Get-ProjectProperty -ProjectXml $ProjectXml -Name $Name
    while ($value -match '\$\(([^)]+)\)') {
        $referencedName = $matches[1]
        $referencedValue = Resolve-ProjectProperty -ProjectXml $ProjectXml -Name $referencedName -Cache $Cache -Stack $Stack
        $value = $value.Replace('$(' + $referencedName + ')', $referencedValue)
    }

    $null = $Stack.Remove($Name)
    $Cache[$Name] = $value
    return $value
}

function Convert-AssemblyVersionToDisplayVersion {
    param(
        [string]$AssemblyVersion
    )

    if ([string]::IsNullOrWhiteSpace($AssemblyVersion)) {
        return ''
    }

    $parts = $AssemblyVersion.Split('.')
    if ($parts.Length -ne 4) {
        return $AssemblyVersion
    }

    $major = 0
    $year = 0
    $month = 0
    $revision = 0
    if (-not [int]::TryParse($parts[0], [ref]$major)) {
        return $AssemblyVersion
    }

    if (-not [int]::TryParse($parts[1], [ref]$year)) {
        return $AssemblyVersion
    }

    if (-not [int]::TryParse($parts[2], [ref]$month)) {
        return $AssemblyVersion
    }

    if (-not [int]::TryParse($parts[3], [ref]$revision)) {
        return $AssemblyVersion
    }

    return '{0}.{1}{2:00}.{3}' -f $major, $year, $month, $revision
}

function Get-ProjectMetadata {
    $projectXml = Get-ProjectXml
    $repoRoot = Get-RepoRoot
    $originUrl = ''
    $cache = @{}

    try {
        $originUrl = (git -C $repoRoot remote get-url origin 2>$null | Select-Object -First 1)
    } catch {
        $originUrl = ''
    }

    $assemblyVersion = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'AssemblyVersion' -Cache $cache
    $version = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'Version' -Cache $cache
    if ([string]::IsNullOrWhiteSpace($version) -or $version -eq $assemblyVersion) {
        $version = Convert-AssemblyVersionToDisplayVersion -AssemblyVersion $assemblyVersion
    }

    return [ordered]@{
        Name                 = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'RootNamespace' -Cache $cache
        Version              = $version
        AssemblyVersion      = $assemblyVersion
        FileVersion          = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'FileVersion' -Cache $cache
        InformationalVersion = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'InformationalVersion' -Cache $cache
        TargetFramework      = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'TargetFramework' -Cache $cache
        Platforms            = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'Platforms' -Cache $cache
        RuntimeIdentifiers   = Resolve-ProjectProperty -ProjectXml $projectXml -Name 'RuntimeIdentifiers' -Cache $cache
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
        $tag = git -C $repoRoot tag --sort=-creatordate | Select-Object -First 1
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
