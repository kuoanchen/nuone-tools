param(
    [ValidateSet('all', 'changelog', 'readme')]
    [string]$Target = 'all'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

switch ($Target) {
    'changelog' {
        & (Join-Path $PSScriptRoot 'generate-changelog.ps1')
        break
    }
    'readme' {
        & (Join-Path $PSScriptRoot 'generate-readme.ps1')
        break
    }
    default {
        & (Join-Path $PSScriptRoot 'generate-changelog.ps1')
        & (Join-Path $PSScriptRoot 'generate-readme.ps1')
        break
    }
}
