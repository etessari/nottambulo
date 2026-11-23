# Builds Nottambulo.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw ".NET Framework 4 csc.exe not found." }

$src      = Join-Path $PSScriptRoot 'src\Nottambulo.cs'
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
$out      = Join-Path $PSScriptRoot 'Nottambulo.exe'
$tmp      = Join-Path $env:TEMP 'Nottambulo-build'
New-Item -ItemType Directory -Force $tmp | Out-Null

$common = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
            "/win32manifest:$manifest", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')

# 1) staging build, only used to render the .exe icon
& $csc @common "/out:$tmp\stage.exe" $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$ico = Join-Path $tmp 'app.ico'
Start-Process -FilePath "$tmp\stage.exe" -ArgumentList '--export-icon', "`"$ico`"" -Wait -NoNewWindow

# 2) final build with the icon embedded
& $csc @common "/win32icon:$ico" "/out:$out" $src
if ($LASTEXITCODE -ne 0) { throw "Build failed (if the app is running, quit it from its tray menu first)" }

Write-Host "Built $out"
