param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$fixtures = Join-Path $PSScriptRoot 'native-suite'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath $output)) { New-Item -ItemType Directory -Path $output | Out-Null }
if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Use an empty native-test output directory.' }

# Discover the already installed compiler and SDK; never bootstrap/download one.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Installed Visual Studio compiler locator not found.' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'An installed x64 MSVC compiler is required.' }
$toolset = Get-ChildItem -LiteralPath (Join-Path $vs 'VC/Tools/MSVC') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um/Windows.h') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$toolset -or !$sdkVersion) { throw 'Installed MSVC/Windows SDK not found.' }
$cl = Join-Path $toolset.FullName 'bin/Hostx64/x64/cl.exe'
$rc = Join-Path $sdkRoot "bin/$($sdkVersion.Name)/x64/rc.exe"
$includes = @(
    "/I$($toolset.FullName)/include",
    "/I$sdkRoot/Include/$($sdkVersion.Name)/ucrt",
    "/I$sdkRoot/Include/$($sdkVersion.Name)/shared",
    "/I$sdkRoot/Include/$($sdkVersion.Name)/um",
    "/I$sourceRoot/es-app/src"
)
$libraries = @(
    "/LIBPATH:$($toolset.FullName)/lib/x64",
    "/LIBPATH:$sdkRoot/Lib/$($sdkVersion.Name)/ucrt/x64",
    "/LIBPATH:$sdkRoot/Lib/$($sdkVersion.Name)/um/x64",
    'bcrypt.lib', 'advapi32.lib', 'shell32.lib', 'ole32.lib'
)
$compilerFlags = @('/nologo', '/std:c++17', '/EHsc', '/W4', '/WX', '/O2', '/MT', '/DWIN32') + $includes
function Build-Native([string]$Name, [string[]]$Sources, [string[]]$Extra = @()) {
    & $cl @compilerFlags @Extra @Sources "/Fo$output/" "/Fe$output/$Name" /link @libraries
    if ($LASTEXITCODE -ne 0) { throw "Native compilation failed: $Name" }
}
function Assert-Exit([string]$Executable, [int]$Expected, [string]$Argument = '') {
    $options = @{ FilePath=$Executable; WorkingDirectory=$output; WindowStyle='Hidden'; PassThru=$true }
    if ($Argument) { $options.ArgumentList = $Argument }
    $process = Start-Process @options
    if (!$process.WaitForExit(45000)) { $process.Kill(); throw 'Native test timed out.' }
    if ($process.ExitCode -ne $Expected) { throw "Native test expected $Expected, got $($process.ExitCode): $Executable $Argument" }
}

Build-Native 'mock-helper.exe' @((Join-Path $fixtures 'mock-helper.cpp'))
Build-Native 'TurboRama.Suite.Access.exe' @((Join-Path $fixtures 'stale-helper.cpp'))
$mock = Join-Path $output 'mock-helper.exe'
$hash = (Get-FileHash -LiteralPath $mock -Algorithm SHA256).Hash
$pin = Join-Path $output 'pin.h'
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($pin, '#define TURBORAMA_SUITE_HELPER_SHA256 "' + $hash + '"', $encoding)
$sources = @((Join-Path $fixtures 'main.cpp'), (Join-Path $sourceRoot 'es-app/src/SuiteAccessGate.cpp'))
$resource = Join-Path $output 'helper.rc'
$compiledResource = Join-Path $output 'helper.res'
[IO.File]::WriteAllText($resource, '31001 RCDATA "' + $mock.Replace('\', '/') + '"', $encoding)
& $rc /nologo "/fo$compiledResource" $resource
if ($LASTEXITCODE -ne 0) { throw 'Resource compilation failed.' }
Build-Native 'emulationstation.exe' ($sources + $compiledResource) @("/FI$pin")
$rootsBefore = @(Get-ChildItem -LiteralPath $env:LOCALAPPDATA -Directory -Filter 'TurboRama.Suite.Access.*' | Select-Object -ExpandProperty FullName)
Assert-Exit (Join-Path $output 'emulationstation.exe') 0
Assert-Exit (Join-Path $output 'emulationstation.exe') 0 '--suite-access-integrity-self-test'
Assert-Exit (Join-Path $output 'emulationstation.exe') 21 '--suite-access-probe-identity'

# Compile an EXE with the stale payload embedded but the original trusted hash.
# A valid adjacent mock must not become a fallback when the resource is invalid.
$invalidResource = Join-Path $output 'invalid.rc'
$invalidCompiledResource = Join-Path $output 'invalid.res'
$stale = Join-Path $output 'TurboRama.Suite.Access.exe'
[IO.File]::WriteAllText($invalidResource, '31001 RCDATA "' + $stale.Replace('\', '/') + '"', $encoding)
& $rc /nologo "/fo$invalidCompiledResource" $invalidResource
if ($LASTEXITCODE -ne 0) { throw 'Invalid-fixture resource compilation failed.' }
Build-Native 'invalid-resource.exe' ($sources + $invalidCompiledResource) @("/FI$pin")
Build-Native 'missing-resource.exe' $sources @("/FI$pin")
Copy-Item -LiteralPath $mock -Destination $stale -Force
Assert-Exit (Join-Path $output 'invalid-resource.exe') 0 '--expect-integrity-failure'
Assert-Exit (Join-Path $output 'missing-resource.exe') 0 '--expect-integrity-failure'
$leakedRoots = @(Get-ChildItem -LiteralPath $env:LOCALAPPDATA -Directory -Filter 'TurboRama.Suite.Access.*' |
    Where-Object { $_.FullName -notin $rootsBefore })
if ($leakedRoots.Count -ne 0) { throw 'Native test left a private extraction directory behind.' }
'SUITE_NATIVE_TESTS=OK (embedded/absent/altered, adjacent ignored, private ACL, file lock, sanitized environment, probe, pipes, revocation, cleanup)'
