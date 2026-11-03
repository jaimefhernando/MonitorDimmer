# Builds a release: the installer (Shadeplay-win-Setup.exe), a portable zip and the update
# packages, all in releases\. Packages from earlier versions in that folder are used to make
# small delta updates, so keep the folder between releases.
#
#   .\scripts\release.ps1 -Version 0.2.3            build only
#   .\scripts\release.ps1 -Version 0.2.3 -Publish   build and publish a GitHub release
#
# -Publish uses the GitHub CLI login (gh auth login); installed copies update from that release.
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'
$releases = Join-Path $root 'releases'
$repoUrl = 'https://github.com/jaimefhernando/MonitorDimmer'

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# vpk is a local tool, found through .config\dotnet-tools.json from the project folder
Push-Location $root
try {
    # Not single-file: separate files let updates download only what changed
    dotnet publish (Join-Path $root 'Shadeplay.csproj') -c Release -r win-x64 --self-contained `
        -p:Version=$Version -p:DebugType=None -o $publishDir -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    dotnet tool restore | Out-Null
    dotnet vpk pack `
        --packId Shadeplay `
        --packVersion $Version `
        --packTitle 'Shadeplay' `
        --runtime win-x64 `
        --packDir $publishDir `
        --mainExe Shadeplay.exe `
        --icon (Join-Path $root 'assets\app.ico') `
        --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

    if ($Publish) {
        $token = gh auth token
        if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Not logged in to GitHub; run: gh auth login" }
        dotnet vpk upload github `
            --repoUrl $repoUrl `
            --token $token `
            --publish `
            --releaseName "Shadeplay $Version" `
            --tag "v$Version" `
            --outputDir $releases
        if ($LASTEXITCODE -ne 0) { throw "Uploading the release failed" }
        Write-Host "Published: $repoUrl/releases/tag/v$Version"
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Release $Version is in $releases"
Get-ChildItem $releases | Sort-Object LastWriteTime |
    Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize | Out-String | Write-Host
