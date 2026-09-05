[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'publish')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location $PSScriptRoot
try {
    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne '10.0.400') {
        throw 'The approved .NET SDK 10.0.400 is required. CI installs it; no local installation is needed.'
    }
    & dotnet run --project (Join-Path $PSScriptRoot 'tests/Verifier.csproj') --configuration Release -p:SuiteCompatibilityTargetFramework=net10.0-windows
    if ($LASTEXITCODE -ne 0) { throw 'Suite cryptographic/session verifier failed.' }
    & dotnet publish (Join-Path $PSScriptRoot 'TurboRama.Suite.Access.csproj') --configuration Release --runtime win-x64 --self-contained true --output $OutputDirectory -p:SuiteCompatibilityTargetFramework=net10.0-windows
    if ($LASTEXITCODE -ne 0) { throw 'Suite access helper publish failed.' }
    $helperPath = Join-Path $OutputDirectory 'TurboRama.Suite.Access.exe'
    if (!(Test-Path -LiteralPath $helperPath -PathType Leaf)) { throw 'Published helper was not found.' }
    $helperHash = (Get-FileHash -LiteralPath $helperPath -Algorithm SHA256).Hash.ToLowerInvariant()
    # This is a generated build artifact consumed by the native CMake hash pin.
    [IO.File]::WriteAllText($helperPath + '.sha256', $helperHash + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Host ('Published suite access helper; SHA256: ' + $helperHash)
}
finally { Pop-Location }
