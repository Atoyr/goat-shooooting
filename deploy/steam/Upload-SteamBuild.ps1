[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ $_ -gt 0 })]
    [long]$AppId,

    [Parameter(Mandatory)]
    [ValidateScript({ $_ -gt 0 })]
    [long]$DepotId,

    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$Branch = 'beta',

    [string]$BuildDescription,

    [string]$SteamCmdPath = $env:STEAMCMD_PATH,

    [string]$Username,

    [switch]$Preview,

    [switch]$SkipPublish,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$contentRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'publish/win-x64'))
$steamArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'steam'))
$scriptOutput = [System.IO.Path]::GetFullPath((Join-Path $steamArtifactsRoot "$AppId/scripts"))
$buildOutput = [System.IO.Path]::GetFullPath((Join-Path $steamArtifactsRoot "$AppId/output"))
$appTemplatePath = Join-Path $PSScriptRoot 'app_build.template.vdf'
$depotTemplatePath = Join-Path $PSScriptRoot 'depot_build.template.vdf'
$appScriptPath = Join-Path $scriptOutput "app_build_$AppId.vdf"
$depotScriptPath = Join-Path $scriptOutput "depot_build_$DepotId.vdf"

if ([string]::IsNullOrWhiteSpace($BuildDescription)) {
    [xml]$buildProperties = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
    $version = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
    $BuildDescription = "Goat Shooooting $version"
}

function ConvertTo-VdfValue([string]$Value) {
    return $Value.Replace("`r", ' ').Replace("`n", ' ').Replace('"', '\"')
}

function Expand-VdfTemplate([string]$TemplatePath, [hashtable]$Values) {
    $content = Get-Content -Raw -LiteralPath $TemplatePath
    foreach ($entry in $Values.GetEnumerator()) {
        $token = "{{$($entry.Key)}}"
        $content = $content.Replace($token, (ConvertTo-VdfValue ([string]$entry.Value)))
    }

    $unresolvedTokens = [regex]::Matches($content, '\{\{[A-Z0-9_]+\}\}')
    if ($unresolvedTokens.Count -gt 0) {
        throw "Unresolved SteamPipe template tokens in '$TemplatePath': $($unresolvedTokens.Value -join ', ')"
    }

    return $content
}

function Assert-PublishContent {
    $requiredFiles = @(
        'GoatShooooting.exe',
        'THIRD-PARTY-NOTICES.txt',
        'games/sample/game.json',
        'games/gauntlet/game.json'
    )
    foreach ($relativePath in $requiredFiles) {
        $path = Join-Path $contentRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Steam Depot content is missing '$relativePath'. Run build/Publish-Game.ps1 first."
        }
    }

    $forbidden = @(Get-ChildItem -LiteralPath $contentRoot -File -Recurse | Where-Object {
        $_.Extension -in @('.pdb', '.cs', '.csproj', '.sln', '.vdf') -or
        ($_.Extension -eq '.exe' -and $_.Name -ne 'GoatShooooting.exe') -or
        $_.Name -eq 'editor.html' -or
        $_.FullName -match '[\\/]schemas[\\/]'
    })
    if ($forbidden.Count -gt 0) {
        throw "Files that must not be uploaded were found: $($forbidden.FullName -join ', ')"
    }
}

Push-Location $repositoryRoot
try {
    if (-not $SkipPublish) {
        & (Join-Path $repositoryRoot 'build/Publish-Game.ps1')
        if ($LASTEXITCODE -ne 0) {
            throw "Publish-Game.ps1 failed with exit code $LASTEXITCODE."
        }
    }

    Assert-PublishContent
    New-Item -ItemType Directory -Path $scriptOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null

    $depotValues = @{
        'DEPOT_ID' = $DepotId
        'CONTENT_ROOT' = $contentRoot
    }
    $appValues = @{
        'APP_ID' = $AppId
        'BUILD_DESCRIPTION' = $BuildDescription
        'BUILD_OUTPUT' = $buildOutput
        'CONTENT_ROOT' = $contentRoot
        'BRANCH' = $Branch
        'PREVIEW' = $(if ($Preview) { '1' } else { '0' })
        'DEPOT_ID' = $DepotId
        'DEPOT_SCRIPT' = $depotScriptPath
    }

    Set-Content -LiteralPath $depotScriptPath -Value (Expand-VdfTemplate $depotTemplatePath $depotValues) -Encoding utf8
    Set-Content -LiteralPath $appScriptPath -Value (Expand-VdfTemplate $appTemplatePath $appValues) -Encoding utf8

    Write-Host "SteamPipe scripts prepared in $scriptOutput"
    Write-Host "Content root: $contentRoot"
    Write-Host "Target branch: $Branch"

    if ($DryRun) {
        Write-Host 'DRY RUN PASSED: SteamCMD was not started.'
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Username)) {
            throw 'Username is required for upload. Password and Steam Guard code are entered interactively in SteamCMD.'
        }
        if ([string]::IsNullOrWhiteSpace($SteamCmdPath)) {
            $SteamCmdPath = 'steamcmd.exe'
        }

        $steamCmd = Get-Command $SteamCmdPath -ErrorAction Stop
        & $steamCmd.Source '+login' $Username '+run_app_build' $appScriptPath '+quit'
        if ($LASTEXITCODE -ne 0) {
            throw "SteamCMD failed with exit code $LASTEXITCODE."
        }

        Write-Host "UPLOAD PASSED: app $AppId, depot $DepotId, branch $Branch"
    }
}
finally {
    Pop-Location
}
