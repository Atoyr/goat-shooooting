[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'publish/win-x64'))
$packagesDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'packages'))
$symbolsDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'symbols'))
$projectPath = Join-Path $repositoryRoot 'src/goat-shooooting.SampleGame/goat-shooooting.SampleGame.csproj'

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
    $Version = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
}

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a supported semantic version."
}

function Reset-ArtifactDirectory([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $requiredPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a path outside the artifacts directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Reset-ArtifactDirectory $publishDirectory
    Reset-ArtifactDirectory $packagesDirectory
    Reset-ArtifactDirectory $symbolsDirectory

    Invoke-DotNet @('restore', 'goat-shooooting.sln', '--locked-mode')
    Invoke-DotNet @('format', '--verify-no-changes', '--no-restore')
    Invoke-DotNet @('build', 'goat-shooooting.sln', '-c', 'Release', '--no-restore')
    Invoke-DotNet @('test', 'goat-shooooting.sln', '-c', 'Release', '--no-build', '--no-restore')
    Invoke-DotNet @('run', '--project', 'src/goat-shooooting.Tooling', '-c', 'Release', '--no-build', '--', 'validate', 'games/sample')
    Invoke-DotNet @('run', '--project', 'src/goat-shooooting.Tooling', '-c', 'Release', '--no-build', '--', 'validate', 'games/gauntlet')
    Invoke-DotNet @('restore', $projectPath, '-r', 'win-x64', '--locked-mode')
    Invoke-DotNet @(
        'publish',
        $projectPath,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        "-p:Version=$Version",
        '-o', $publishDirectory
    )

    $executablePath = Join-Path $publishDirectory 'GoatShooooting.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Published executable was not found: $executablePath"
    }

    & $executablePath --smoke-test
    if ($LASTEXITCODE -ne 0) { throw 'Published sample smoke test failed.' }
    & $executablePath --game gauntlet --smoke-test
    if ($LASTEXITCODE -ne 0) { throw 'Published gauntlet smoke test failed.' }

    foreach ($gameId in @('sample', 'gauntlet')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "games/$gameId/game.json") -PathType Leaf)) {
            throw "Published content pack '$gameId' is incomplete."
        }
    }

    $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $publishDirectory 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $publishDirectory 'DOTNET-THIRD-PARTY-NOTICES.txt')

    $createdumpPath = Join-Path $publishDirectory 'createdump.exe'
    if (Test-Path -LiteralPath $createdumpPath) {
        Remove-Item -LiteralPath $createdumpPath -Force
    }

    $symbolFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File -Recurse)
    $symbolArchive = Join-Path $symbolsDirectory "goat-shooooting-$Version-win-x64-symbols.zip"
    if ($symbolFiles.Count -gt 0) {
        Compress-Archive -LiteralPath $symbolFiles.FullName -DestinationPath $symbolArchive -CompressionLevel Optimal
        foreach ($symbolFile in $symbolFiles) {
            Remove-Item -LiteralPath $symbolFile.FullName -Force
        }
    }

    $forbidden = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Where-Object {
        $_.Extension -in @('.pdb', '.cs', '.csproj', '.sln') -or
        ($_.Extension -eq '.exe' -and $_.Name -ne 'GoatShooooting.exe') -or
        $_.Name -in @('editor.html') -or
        $_.FullName -match '[\\/]schemas[\\/]'
    })
    if ($forbidden.Count -gt 0) {
        throw "Development files were found in the publish directory: $($forbidden.FullName -join ', ')"
    }

    $packagePath = Join-Path $packagesDirectory "goat-shooooting-$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $packagePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = "$packagePath.sha256"
    Set-Content -LiteralPath $checksumPath -Value "$hash *$(Split-Path -Leaf $packagePath)" -Encoding ascii

    Write-Host "PUBLISH PASSED: $publishDirectory"
    Write-Host "PACKAGE: $packagePath"
    Write-Host "SHA256: $hash"
}
finally {
    Pop-Location
}
