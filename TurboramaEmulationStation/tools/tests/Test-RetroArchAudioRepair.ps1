$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('audio-repair-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$script = Join-Path $PSScriptRoot '..\Repair-RetroArchAudio.ps1'
$path = Join-Path $root 'retroarch.cfg'
$inputText = "# caff$([char]233)`r`naudio_driver = `"wasapi`"`r`naudio_wasapi_exclusive_mode = `"true`"`r`naudio_mute_enable = `"true`"`r`ninput_audio_mute = `"o`"`r`naudio_volume = `"-3.0`"`r`nserver_setting = `"unchanged`"`r`n"
$bytes = [Text.Encoding]::UTF8.GetPreamble() + [Text.Encoding]::UTF8.GetBytes($inputText)
[IO.File]::WriteAllBytes($path, $bytes)
& $script -ConfigPath $path | Out-Null
$expected = $inputText.Replace('audio_wasapi_exclusive_mode = "true"', 'audio_wasapi_exclusive_mode = "false"').Replace('audio_mute_enable = "true"', 'audio_mute_enable = "false"').Replace('input_audio_mute = "o"', 'input_audio_mute = "nul"')
$expectedBytes = [Text.Encoding]::UTF8.GetPreamble() + [Text.Encoding]::UTF8.GetBytes($expected)
if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToBase64String($expectedBytes)) { throw 'Unexpected settings/encoding change' }
$backup = @(Get-ChildItem -LiteralPath $root -Filter '*.audio-backup-*')
if ($backup.Count -ne 1 -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($backup[0].FullName)) -cne [Convert]::ToBase64String($bytes)) { throw 'Backup mismatch' }
& $script -ConfigPath $path | Out-Null
if (@(Get-ChildItem -LiteralPath $root -Filter '*.audio-backup-*').Count -ne 1) { throw 'Not idempotent' }
[IO.File]::WriteAllText($path, 'input_audio_mute = "f9"')
& $script -ConfigPath $path | Out-Null
if ([IO.File]::ReadAllText($path) -cne 'input_audio_mute = "f9"') { throw 'Custom key changed' }
Write-Host 'RETROARCH_AUDIO_REPAIR_TEST=OK (exact bytes, backup, idempotence, custom hotkey)'
