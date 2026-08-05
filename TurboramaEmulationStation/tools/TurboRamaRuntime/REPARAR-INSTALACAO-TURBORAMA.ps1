[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InstallRoot = 'D:\emulationstation',
    [string]$LauncherConfig = 'C:\TurboRama\Config\turborama.json',
    [switch]$RemoveMaintenanceLock,
    # Em producao, o instalador passa a pasta payload-expanded protegida onde
    # esta a copia do script que esta sendo executada. Isso impede que uma
    # copia ja instalada em D: seja elevada e usada como codigo de atualizacao.
    [string]$TrustedStaging = '',
    [int]$InstallerProcessId = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Production validates every ancestor to the volume root. The non-elevated
# smoke test runs under a restricted token, so it validates through the
# current user's known LocalAppData anchor instead of attempting to inspect
# the inaccessible profile parent. The exact smoke target is still enforced.
$script:SafetyValidationAnchor = $null

if (-not ('TurboRama.Repair.NativeFileIdentity' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace TurboRama.Repair
{
    public static class NativeFileIdentity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(IntPtr file, out BY_HANDLE_FILE_INFORMATION information);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public static bool TryRead(string path, bool directory, out uint attributes, out uint links)
        {
            attributes = 0;
            links = 0;
            // FILE_READ_ATTRIBUTES evita exigir permissao de listar/ler o
            // conteudo de uma pasta apenas para conferir seu objeto NTFS.
            const uint FileReadAttributes = 0x00000080;
            const uint ShareReadWriteDelete = 0x00000007;
            const uint OpenExisting = 3;
            const uint FileFlagBackupSemantics = 0x02000000;
            const uint FileFlagOpenReparsePoint = 0x00200000;
            IntPtr handle = CreateFile(path, FileReadAttributes, ShareReadWriteDelete, IntPtr.Zero, OpenExisting,
                FileFlagOpenReparsePoint | (directory ? FileFlagBackupSemantics : 0), IntPtr.Zero);
            if (handle == new IntPtr(-1)) return false;
            try
            {
                BY_HANDLE_FILE_INFORMATION information;
                if (!GetFileInformationByHandle(handle, out information)) return false;
                attributes = information.FileAttributes;
                links = information.NumberOfLinks;
                return true;
            }
            finally { CloseHandle(handle); }
        }
    }
}
'@
}

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    if ($full.Length -gt 3) { $full = $full.TrimEnd('\') }
    return $full
}

function Test-PathEquals {
    param([Parameter(Mandatory = $true)][string]$Left, [Parameter(Mandatory = $true)][string]$Right)
    return [string]::Equals((Get-NormalizedPath $Left), (Get-NormalizedPath $Right),
        [StringComparison]::OrdinalIgnoreCase)
}

function Convert-IdentityToSid {
    param([Parameter(Mandatory = $true)]$Identity)

    try {
        return $Identity.Translate([Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        try { return ([Security.Principal.SecurityIdentifier]::new($Identity.ToString())).Value }
        catch { throw "Nao foi possivel resolver a identidade de seguranca '$Identity'." }
    }
}

function Assert-SafeDirectoryChain {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = Get-NormalizedPath $Path
    $anchor = if ($null -ne $script:SafetyValidationAnchor) {
        Get-NormalizedPath $script:SafetyValidationAnchor
    }
    else { $null }
    while ($true) {
        if (-not (Test-Path -LiteralPath $current -PathType Container)) {
            throw "Diretorio obrigatorio nao encontrado: $current"
        }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Redirecionamento/reparse recusado no diretorio: $current"
        }
        [uint32]$attributes = 0
        [uint32]$links = 0
        if ((-not [TurboRama.Repair.NativeFileIdentity]::TryRead($current, $true, [ref]$attributes, [ref]$links)) -or
            (($attributes -band 0x00000400) -ne 0) -or (($attributes -band 0x00000010) -eq 0)) {
            throw "Nao foi possivel validar o diretorio sem redirecionamento: $current"
        }
        if ($null -ne $anchor -and (Test-PathEquals $current $anchor)) { break }
        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent -or [string]::Equals($parent.FullName, $current, [StringComparison]::OrdinalIgnoreCase)) { break }
        $current = Get-NormalizedPath $parent.FullName
    }
}

function Assert-SafeExistingFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = Get-NormalizedPath $Path
    Assert-SafeDirectoryChain (Split-Path -Parent $full)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Arquivo obrigatorio nao encontrado: $full" }
    $item = Get-Item -LiteralPath $full -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Redirecionamento/reparse recusado no arquivo: $full"
    }
    [uint32]$attributes = 0
    [uint32]$links = 0
    if ((-not [TurboRama.Repair.NativeFileIdentity]::TryRead($full, $false, [ref]$attributes, [ref]$links)) -or
        (($attributes -band 0x00000400) -ne 0) -or (($attributes -band 0x00000010) -ne 0)) {
        throw "Nao foi possivel validar o arquivo sem redirecionamento: $full"
    }
    # Nao ha API de alto nivel confiavel para seguir hardlinks em PowerShell.
    # Rejeitar qualquer arquivo com mais de um link e uma defesa pratica contra
    # a troca do JSON/backup por outro objeto local antes de uma escrita elevada.
    if ($links -ne 1) { throw "Arquivo com hardlink recusado: $full" }
    return $full
}

function New-RandomSiblingPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    $full = Get-NormalizedPath $Path
    $directory = Split-Path -Parent $full
    Assert-SafeDirectoryChain $directory
    $leaf = Split-Path -Leaf $full
    for ($attempt = 0; $attempt -lt 16; $attempt++) {
        $candidate = Join-Path $directory ('{0}.{1}.{2}' -f $leaf, $Kind, [Guid]::NewGuid().ToString('N'))
        if (-not [IO.File]::Exists($candidate) -and -not [IO.Directory]::Exists($candidate)) { return $candidate }
    }
    throw "Nao foi possivel reservar um nome temporario aleatorio em: $directory"
}

function Remove-OwnedTemporaryFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.File]::Exists($Path)) { return }
    try {
        # Se o objeto inesperadamente deixou de ser nosso arquivo regular,
        # preservamos a evidencia e nao seguimos link/reparse para apaga-lo.
        Assert-SafeExistingFile $Path | Out-Null
        [IO.File]::Delete($Path)
    }
    catch { }
}

function Copy-FileCreateNew {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $sourceFull = Assert-SafeExistingFile $Source
    $destinationFull = Get-NormalizedPath $Destination
    Assert-SafeDirectoryChain (Split-Path -Parent $destinationFull)
    if ([IO.File]::Exists($destinationFull) -or [IO.Directory]::Exists($destinationFull)) {
        throw "Destino de backup ja existe: $destinationFull"
    }
    $input = $null
    $output = $null
    try {
        $input = [IO.FileStream]::new($sourceFull, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $output = [IO.FileStream]::new($destinationFull, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
            [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        $input.CopyTo($output)
        $output.Flush($true)
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        if ($null -ne $input) { $input.Dispose() }
    }
    Assert-SafeExistingFile $destinationFull | Out-Null
    return $destinationFull
}

function Write-Utf8JsonAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $destination = Get-NormalizedPath $Path
    Assert-SafeDirectoryChain (Split-Path -Parent $destination)
    if ([IO.File]::Exists($destination)) { Assert-SafeExistingFile $destination | Out-Null }
    $temporary = New-RandomSiblingPath $destination 'turborama-write'
    $replacementBackup = $null
    $stream = $null
    try {
        $json = $Value | ConvertTo-Json -Depth 20
        $bytes = ([Text.UTF8Encoding]::new($false)).GetBytes($json + [Environment]::NewLine)
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
            [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null
        Assert-SafeExistingFile $temporary | Out-Null
        if ([IO.File]::Exists($destination)) {
            Assert-SafeExistingFile $destination | Out-Null
            $replacementBackup = New-RandomSiblingPath $destination 'turborama-replaced'
            [IO.File]::Replace($temporary, $destination, $replacementBackup, $true)
        }
        else {
            [IO.File]::Move($temporary, $destination)
        }
        $temporary = $null
        Remove-OwnedTemporaryFile $replacementBackup
        $replacementBackup = $null
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        Remove-OwnedTemporaryFile $temporary
        Remove-OwnedTemporaryFile $replacementBackup
    }
}

function Restore-FileAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Backup,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $backupFull = Assert-SafeExistingFile $Backup
    $destinationFull = Get-NormalizedPath $Destination
    Assert-SafeDirectoryChain (Split-Path -Parent $destinationFull)
    if ([IO.File]::Exists($destinationFull)) { Assert-SafeExistingFile $destinationFull | Out-Null }
    $temporary = New-RandomSiblingPath $destinationFull 'turborama-rollback'
    $replacementBackup = $null
    try {
        Copy-FileCreateNew -Source $backupFull -Destination $temporary | Out-Null
        if ([IO.File]::Exists($destinationFull)) {
            Assert-SafeExistingFile $destinationFull | Out-Null
            $replacementBackup = New-RandomSiblingPath $destinationFull 'turborama-replaced'
            [IO.File]::Replace($temporary, $destinationFull, $replacementBackup, $true)
        }
        else {
            [IO.File]::Move($temporary, $destinationFull)
        }
        $temporary = $null
    }
    finally {
        Remove-OwnedTemporaryFile $temporary
        Remove-OwnedTemporaryFile $replacementBackup
    }
}

function Move-SafeDirectory {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Destination)

    $sourceFull = Get-NormalizedPath $Source
    Assert-SafeDirectoryChain $sourceFull
    $destinationFull = Get-NormalizedPath $Destination
    Assert-SafeDirectoryChain (Split-Path -Parent $destinationFull)
    if ([IO.File]::Exists($destinationFull) -or [IO.Directory]::Exists($destinationFull)) {
        throw "Destino de diretorio ja existe: $destinationFull"
    }
    [IO.Directory]::Move($sourceFull, $destinationFull)
}

function Move-SafeFile {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Destination)

    $sourceFull = Assert-SafeExistingFile $Source
    $destinationFull = Get-NormalizedPath $Destination
    Assert-SafeDirectoryChain (Split-Path -Parent $destinationFull)
    if ([IO.File]::Exists($destinationFull) -or [IO.Directory]::Exists($destinationFull)) {
        throw "Destino de arquivo ja existe: $destinationFull"
    }
    [IO.File]::Move($sourceFull, $destinationFull)
}

function Test-CurrentProcessElevated {
    if ($env:OS -ne 'Windows_NT') { return $false }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-AdminOnlyAcl {
    param([Parameter(Mandatory = $true)][string]$Path)

    $acl = Get-Acl -LiteralPath $Path
    $owner = Convert-IdentityToSid $acl.Owner
    if ($owner -ne 'S-1-5-32-544') { throw "Owner do staging nao e BUILTIN\\Administrators: $Path" }
    $rules = @($acl.Access)
    if ($rules.Count -ne 2) { throw "ACL do staging deve ter somente SYSTEM e Administrators: $Path" }
    $seen = @{}
    foreach ($rule in $rules) {
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) {
            throw "ACL do staging contem regra de negacao/invalida: $Path"
        }
        $sid = Convert-IdentityToSid $rule.IdentityReference
        if ($sid -notin @('S-1-5-18', 'S-1-5-32-544')) { throw "ACL do staging contem identidade inesperada: $Path" }
        if (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl) {
            throw "ACL do staging nao concede controle total ao administrador do pacote: $Path"
        }
        if ($seen.ContainsKey($sid)) { throw "ACL do staging contem regra duplicada: $Path" }
        $seen[$sid] = $true
    }
    if (-not $seen.ContainsKey('S-1-5-18') -or -not $seen.ContainsKey('S-1-5-32-544')) {
        throw "ACL do staging nao contem SYSTEM e Administrators: $Path"
    }
}

function Assert-TrustedStaging {
    param(
        [Parameter(Mandatory = $true)][string]$Staging,
        [Parameter(Mandatory = $true)][int]$ParentInstallerProcessId
    )

    if ($ParentInstallerProcessId -le 0) { throw 'InstallerProcessId confiavel nao foi informado.' }
    $stagingFull = Get-NormalizedPath $Staging
    $scriptFull = Get-NormalizedPath $PSCommandPath
    if (-not (Test-PathEquals (Split-Path -Parent $scriptFull) $stagingFull)) {
        throw 'O reparo elevado so aceita a propria copia dentro do TrustedStaging administrativo.'
    }
    Assert-SafeDirectoryChain $stagingFull
    Assert-SafeExistingFile $scriptFull | Out-Null

    $stageParent = Get-NormalizedPath (Split-Path -Parent $stagingFull)
    $programData = Get-NormalizedPath ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData))
    if (((Split-Path -Leaf $stagingFull) -cne 'payload-expanded') -or
        (-not (Test-PathEquals (Split-Path -Parent $stageParent) $programData)) -or
        ((Split-Path -Leaf $stageParent) -cnotmatch '^TurboRamaInstaller-stage-[0-9a-f]{32}$')) {
        throw 'TrustedStaging nao esta na hierarquia administrativa esperada do instalador.'
    }
    Assert-SafeDirectoryChain $stageParent
    Assert-AdminOnlyAcl $stageParent
    Assert-AdminOnlyAcl $stagingFull
    Assert-AdminOnlyAcl $scriptFull

    $self = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $PID" -ErrorAction Stop
    $installer = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ParentInstallerProcessId" -ErrorAction Stop
    if ($null -eq $self -or $null -eq $installer -or [int]$self.ParentProcessId -ne $ParentInstallerProcessId) {
        throw 'O processo chamador do reparo nao corresponde ao instalador informado.'
    }
    $installerPath = Get-NormalizedPath ([string]$installer.ExecutablePath)
    $expectedInstaller = Join-Path $stageParent 'TurboRamaInstaller.exe'
    if (-not (Test-PathEquals $installerPath $expectedInstaller)) {
        throw 'O processo chamador nao e o TurboRamaInstaller protegido do staging.'
    }
    Assert-SafeExistingFile $installerPath | Out-Null
    Assert-AdminOnlyAcl $installerPath
}

function Test-IsolatedSmokeInstall {
    param([Parameter(Mandatory = $true)][string]$ResolvedInstallRoot)

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) { return $false }
    $expectedRoot = Join-Path $localAppData 'Temp\TurboRama-v25-smoke\install'
    return $env:TURBORAMA_INSTALLER_SILENT_TEST -eq '1' -and
        (Test-PathEquals $ResolvedInstallRoot $expectedRoot)
}

$resolvedInstallRoot = Get-NormalizedPath $InstallRoot
$isIsolatedSmoke = Test-IsolatedSmokeInstall $resolvedInstallRoot
if ($isIsolatedSmoke) {
    $script:SafetyValidationAnchor = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
}
if (-not $WhatIfPreference -and -not $isIsolatedSmoke) {
    if (-not (Test-CurrentProcessElevated)) {
        throw 'O reparo de producao exige o token elevado do TurboRamaInstaller protegido; use somente o instalador integro.'
    }
    Assert-TrustedStaging -Staging $TrustedStaging -ParentInstallerProcessId $InstallerProcessId
}

Assert-SafeDirectoryChain $resolvedInstallRoot
$frontend = Join-Path $resolvedInstallRoot 'emulationstation.exe'
Assert-SafeExistingFile $frontend | Out-Null

$changes = New-Object System.Collections.Generic.List[string]
$runtimeCache = Join-Path $resolvedInstallRoot '.emulationstation\.runtime'
$maintenanceLock = 'C:\TurboRama\State\maintenance.lock'
$launcherBackup = $null
$launcherChanged = $false
$cacheBackup = $null
$cacheMoved = $false
$lockBackup = $null
$lockMoved = $false

try {
    if (Test-Path -LiteralPath $LauncherConfig -PathType Leaf) {
        $launcherPath = Assert-SafeExistingFile $LauncherConfig
        $launcher = [IO.File]::ReadAllText($launcherPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        $frontendProperty = $launcher.PSObject.Properties['frontendExecutable']
        $currentFrontend = if ($null -ne $frontendProperty) { [string]$frontendProperty.Value } else { '' }
        if (-not [string]::Equals($currentFrontend, $frontend, [StringComparison]::OrdinalIgnoreCase)) {
            $launcherBackup = New-RandomSiblingPath $launcherPath 'turborama-backup'
            if ($PSCmdlet.ShouldProcess($launcherPath, "corrigir frontendExecutable e criar backup em $launcherBackup")) {
                Copy-FileCreateNew -Source $launcherPath -Destination $launcherBackup | Out-Null
                if ($null -ne $frontendProperty) {
                    $launcher.frontendExecutable = $frontend
                }
                else {
                    $launcher | Add-Member -NotePropertyName frontendExecutable -NotePropertyValue $frontend
                }
                Write-Utf8JsonAtomic -Path $launcherPath -Value $launcher
                $launcherChanged = $true
            }
            $changes.Add("Launcher: $currentFrontend -> $frontend")
        }
    }
    else {
        Write-Warning "Configuracao do Launcher nao encontrada: $LauncherConfig"
    }

    if ($isIsolatedSmoke -and $env:TURBORAMA_REPAIR_TEST_FAIL_AFTER_LAUNCHER -eq '1') {
        # Proves to the parent smoke test that this deliberate branch, rather
        # than an unrelated repair error, produced the rollback request.
        $controlledFailureMarker = Join-Path $resolvedInstallRoot '.repair-test-after-launcher.marker'
        if ([IO.File]::Exists($controlledFailureMarker) -or [IO.Directory]::Exists($controlledFailureMarker)) {
            throw 'marcador de falha controlada ja existe no smoke test'
        }
        $markerStream = $null
        try {
            $markerBytes = ([Text.UTF8Encoding]::new($false)).GetBytes("reached`n")
            $markerStream = [IO.FileStream]::new($controlledFailureMarker, [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
            $markerStream.Write($markerBytes, 0, $markerBytes.Length)
            $markerStream.Flush($true)
        }
        finally {
            if ($null -ne $markerStream) { $markerStream.Dispose() }
        }
        Assert-SafeExistingFile $controlledFailureMarker | Out-Null
        throw 'falha controlada do autoteste apos atualizar o Launcher'
    }

    if (Test-Path -LiteralPath $runtimeCache -PathType Container) {
        Assert-SafeDirectoryChain $runtimeCache
        $cacheBackup = New-RandomSiblingPath $runtimeCache 'turborama-stale'
        if ($PSCmdlet.ShouldProcess($runtimeCache, "mover cache antigo para $cacheBackup")) {
            Move-SafeDirectory -Source $runtimeCache -Destination $cacheBackup
            $cacheMoved = $true
        }
        $changes.Add("Cache de tema preservado em: $cacheBackup")
    }

    if ($RemoveMaintenanceLock -and (Test-Path -LiteralPath $maintenanceLock -PathType Leaf)) {
        $maintenancePath = Assert-SafeExistingFile $maintenanceLock
        $lockBackup = New-RandomSiblingPath $maintenancePath 'turborama-backup'
        if ($PSCmdlet.ShouldProcess($maintenancePath, "mover bloqueio de manutencao para $lockBackup")) {
            Move-SafeFile -Source $maintenancePath -Destination $lockBackup
            $lockMoved = $true
        }
        $changes.Add("Bloqueio de manutencao preservado em: $lockBackup")
    }
}
catch {
    $originalFailure = $_.Exception.Message
    $rollbackFailures = New-Object System.Collections.Generic.List[string]

    if ($lockMoved -and $null -ne $lockBackup) {
        try {
            if (Test-Path -LiteralPath $maintenanceLock) { throw 'o destino foi recriado durante o rollback' }
            Move-SafeFile -Source $lockBackup -Destination $maintenanceLock
        }
        catch { $rollbackFailures.Add("bloqueio de manutencao: $($_.Exception.Message)") }
    }
    if ($cacheMoved -and $null -ne $cacheBackup) {
        try {
            if (Test-Path -LiteralPath $runtimeCache) { throw 'o destino foi recriado durante o rollback' }
            Move-SafeDirectory -Source $cacheBackup -Destination $runtimeCache
        }
        catch { $rollbackFailures.Add("cache de tema: $($_.Exception.Message)") }
    }
    if ($launcherChanged -and $null -ne $launcherBackup -and (Test-Path -LiteralPath $launcherBackup -PathType Leaf)) {
        try { Restore-FileAtomically -Backup $launcherBackup -Destination $LauncherConfig }
        catch { $rollbackFailures.Add("Launcher: $($_.Exception.Message)") }
    }

    if ($rollbackFailures.Count -gt 0) {
        throw "Reparo falhou: $originalFailure. Rollback incompleto: $($rollbackFailures -join ' | ')"
    }
    throw "Reparo falhou e todas as alteracoes anteriores foram revertidas: $originalFailure"
}

if ($WhatIfPreference) {
    Write-Host 'Simulacao concluida; nenhuma alteracao foi gravada:' -ForegroundColor Yellow
    $changes | ForEach-Object { Write-Host " - $_" }
}
elseif ($changes.Count -eq 0) {
    Write-Host 'Instalacao ja estava alinhada; nenhuma alteracao necessaria.' -ForegroundColor Green
}
else {
    Write-Host 'Reparo concluido:' -ForegroundColor Green
    $changes | ForEach-Object { Write-Host " - $_" }
}

Write-Host 'A pasta .emulationstation\pix e todas as credenciais foram preservadas.' -ForegroundColor Cyan
