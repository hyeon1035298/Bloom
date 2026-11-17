# Install Bloom from PowerShell:
#
#   irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex
#
# It downloads the latest release's setup (BloomLauncher-win-Setup.exe, the same file as on the Releases page) into
# your temp folder and runs it without its window. The setup installs the .NET 10 Desktop Runtime too when the PC
# lacks it (Windows asks for permission for that part) and puts Bloom in %LOCALAPPDATA%\BloomLauncher; then Bloom
# starts, always without admin rights (through explorer.exe), even when this window is elevated. Nothing else happens.
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue' # Windows PowerShell 5.1 downloads far slower while it draws a progress bar
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $url = 'https://github.com/hyeon1035298/Bloom/releases/latest/download/BloomLauncher-win-Setup.exe'
    $setup = Join-Path $env:TEMP 'BloomLauncher-win-Setup.exe'
    $exe = Join-Path $env:LOCALAPPDATA 'BloomLauncher\current\Bloom.exe'

    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($admin) { Write-Host 'Note: this window runs as administrator; Bloom is started without admin rights.' }
    Write-Host 'Downloading Bloom...'
    Invoke-WebRequest -Uri $url -OutFile $setup -UseBasicParsing
    Write-Host 'Installing...'
    $run = Start-Process -FilePath $setup -ArgumentList '--silent' -Wait -PassThru
    Remove-Item -LiteralPath $setup -ErrorAction SilentlyContinue
    if ($run.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $exe)) { throw "The setup ended with code $($run.ExitCode)." }

    # the setup may start Bloom itself; start it only when it isn't running
    Start-Sleep -Seconds 2
    if (-not (Get-Process -Name Bloom -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })) { Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList "`"$exe`"" } # explorer starts it unelevated
    Write-Host 'Done. Press the Win key to open Bloom.'
}
