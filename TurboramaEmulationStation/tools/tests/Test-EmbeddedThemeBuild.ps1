#Requires -Version 5.1
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$packer = Join-Path $projectRoot 'tools\Pack-EmbeddedTheme.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('turborama-theme-test-' + [Guid]::NewGuid().ToString('N'))
$theme = Join-Path $testRoot 'theme'
$first = Join-Path $testRoot 'first.bin'
$second = Join-Path $testRoot 'second.bin'
$decoded = Join-Path $testRoot 'decoded.zip'
$extracted = Join-Path $testRoot 'extracted'
$runtimeSource = Join-Path $projectRoot 'es-core\src\EmbeddedTheme.cpp'

try {
    [IO.Directory]::CreateDirectory((Join-Path $theme 'nested')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $theme 'theme.xml'), '<theme><formatVersion>4</formatVersion></theme>', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $theme 'nested\value.txt'), 'TurboRama deterministic payload', [Text.UTF8Encoding]::new($false))

    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $packer -Source $theme -Output $first | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Primeiro empacotamento falhou: $LASTEXITCODE" }

    # Source timestamps must not affect the payload identity or bytes.
    (Get-Item -LiteralPath (Join-Path $theme 'theme.xml')).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-10)
    (Get-Item -LiteralPath (Join-Path $theme 'nested\value.txt')).LastWriteTimeUtc = [DateTime]::UtcNow
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $packer -Source $theme -Output $second | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Segundo empacotamento falhou: $LASTEXITCODE" }

    $firstHash = (Get-FileHash -LiteralPath $first -Algorithm SHA256).Hash
    $secondHash = (Get-FileHash -LiteralPath $second -Algorithm SHA256).Hash
    Assert ($firstHash -eq $secondHash) 'O empacotador nao produziu bytes deterministas.'

    $payload = [IO.File]::ReadAllBytes($first)
    $headerLength = 42
    Assert ($payload.Length -gt $headerLength) 'Payload gerado esta vazio.'
    $header = [Text.Encoding]::ASCII.GetString($payload, 0, $headerLength)
    Assert ($header -match '^TRTHEME1:[0-9a-f]{32}\n$') 'Cabecalho de identidade do payload esta invalido.'

    $key = [byte[]](0xB3,0x57,0x9E,0x24,0xC8,0x6A,0x11,0xFD,0x45,0x8B,0xD2,0x37,0xE9,0x02,0xAC,0x71)
    $zip = [byte[]]::new($payload.Length - $headerLength)
    for ($index = 0; $index -lt $zip.Length; $index++) {
        $zip[$index] = $payload[$headerLength + $index] -bxor $key[$index % $key.Length]
    }
    [IO.File]::WriteAllBytes($decoded, $zip)
    $md5 = [BitConverter]::ToString([Security.Cryptography.MD5]::Create().ComputeHash($zip)).Replace('-','').ToLowerInvariant()
    Assert ($header.Substring(9, 32) -eq $md5) 'Identidade do cabecalho nao corresponde ao ZIP descriptografado.'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($decoded, $extracted)
    Assert (Test-Path -LiteralPath (Join-Path $extracted 'theme.xml') -PathType Leaf) 'theme.xml nao foi preservado no payload.'
	Assert (([IO.File]::ReadAllText((Join-Path $extracted 'nested\value.txt'))) -eq 'TurboRama deterministic payload') 'Conteudo extraido divergiu da origem.'

	# Guardrail da primeira inicializacao: estes arquivos sao consultados antes
	# de serem criados, portanto a confirmacao posterior nunca pode usar o
	# resultado negativo memorizado pelo FileSystemCache.
	$runtimeText = [IO.File]::ReadAllText($runtimeSource)
	foreach ($requiredCheck in @(
		'exists(tempZip, false)',
		'exists(targetPath + "/theme.xml", false)',
		'exists(cachedPath + "/theme.xml", false)',
		'exists(markerPath, false)',
		'exists(extractPath + "/theme.xml", false)'
	)) {
		Assert ($runtimeText.Contains($requiredCheck)) "EmbeddedTheme voltou a usar cache obsoleto em: $requiredCheck"
	}

	Write-Host "OK: payload deterministico, cabecalho/hash, extracao e guardas de cache validados ($firstHash)."
}
finally {
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        [IO.Directory]::Delete($testRoot, $true)
    }
}
