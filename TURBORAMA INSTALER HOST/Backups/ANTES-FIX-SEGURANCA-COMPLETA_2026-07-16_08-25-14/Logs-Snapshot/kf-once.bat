schtasks /Create /TN TurboRamaKFOnce /TR "C:\TurboRama\Logs\kf-start.bat" /SC ONCE /ST 00:00 /RL HIGHEST /RU SYSTEM /F
schtasks /Run /TN TurboRamaKFOnce
ping -n 4 127.0.0.1 >nul
schtasks /Delete /TN TurboRamaKFOnce /F
