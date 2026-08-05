#Requires -Version 5.1
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-BracedBlock([string]$Text, [string]$Anchor) {
    $anchorIndex = $Text.IndexOf($Anchor, [StringComparison]::Ordinal)
    Assert ($anchorIndex -ge 0) "Bloco ausente: $Anchor"
    $openIndex = $Text.IndexOf('{', $anchorIndex)
    Assert ($openIndex -ge 0) "Abertura do bloco ausente: $Anchor"

    $depth = 0
    for ($index = $openIndex; $index -lt $Text.Length; $index++) {
        if ($Text[$index] -eq '{') { $depth++ }
        elseif ($Text[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($anchorIndex, $index - $anchorIndex + 1)
            }
        }
    }
    throw "Fechamento do bloco ausente: $Anchor"
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fileDataPath = Join-Path $projectRoot 'es-app\src\FileData.cpp'
$platformPath = Join-Path $projectRoot 'es-core\src\utils\Platform.cpp'
$fileData = [IO.File]::ReadAllText($fileDataPath)
$platform = [IO.File]::ReadAllText($platformPath)

$launchStart = $fileData.IndexOf('bool FileData::launchGame(', [StringComparison]::Ordinal)
$launchEnd = $fileData.IndexOf('bool FileData::hasContentFiles()', $launchStart, [StringComparison]::Ordinal)
Assert ($launchStart -ge 0 -and $launchEnd -gt $launchStart) 'Nao foi possivel isolar FileData::launchGame.'
$launch = $fileData.Substring($launchStart, $launchEnd - $launchStart)
$supervised = Get-BracedBlock $launch 'if (creditEnabled)'

Assert ($launch.Contains('const bool creditEnabled = credits.isEnabled();')) 'O modo de credito nao foi congelado antes do launch.'
Assert ($launch.Contains('if (creditEnabled && !credits.hasCredit())')) 'O bloqueio sem saldo nao esta restrito ao modo de credito ativo.'
Assert ($supervised.Contains('process.pollCallback =')) 'O modo de credito ativo perdeu o callback de supervisao.'
Assert ($supervised.Contains('process.killProcessTreeOnCallbackFalse = true;')) 'O modo de credito ativo perdeu a terminacao fail-closed.'
Assert ($supervised.Contains('credits.beginGameSession();')) 'A sessao de credito nao inicia no primeiro poll supervisionado.'
Assert (([regex]::Matches($launch, [regex]::Escape('credits.beginGameSession();'))).Count -eq 1) 'Existe inicio de sessao fora do unico caminho supervisionado.'
Assert ($launch.Contains('if (sessionStarted == nullptr || !*sessionStarted)')) 'Falha de launch pode encerrar/cobrar uma sessao que nunca iniciou.'

$startEvent = $launch.IndexOf('Scripting::fireEvent("game-start"', [StringComparison]::Ordinal)
$runCall = $launch.IndexOf('process.run()', [StringComparison]::Ordinal)
$endEvent = $launch.IndexOf('Scripting::fireEvent("game-end"', [StringComparison]::Ordinal)
Assert ($startEvent -ge 0 -and $startEvent -lt $runCall -and $runCall -lt $endEvent) 'A ordem game-start > launch > game-end foi alterada.'

$controlledGate = $platform.IndexOf('if (pollCallback)', [StringComparison]::Ordinal)
$shellExecute = $platform.IndexOf('ShellExecuteExW(&lpExecInfo)', [StringComparison]::Ordinal)
Assert ($controlledGate -ge 0 -and $shellExecute -gt $controlledGate) 'O ShellExecute historico nao esta depois do desvio supervisionado.'
$controlled = $platform.Substring($controlledGate, $shellExecute - $controlledGate)
foreach ($required in @(
    'CreateJobObjectW',
    'CreateProcessW',
    'AssignProcessToJobObject',
    'ResumeThread',
    'TerminateJobObject'
)) {
    Assert ($controlled.Contains($required)) "Supervisao fail-closed incompleta: $required"
}

Write-Host 'OK: compatibilidade ShellExecute sem credito e supervisao fail-closed com credito validadas.'
