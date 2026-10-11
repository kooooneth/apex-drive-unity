# Builds ApexDrive-Setup.exe from the Windows build.
#   1. Build the game: Unity > Apex Drive > Build Windows (output: Build\Windows).
#   2. Run:  powershell -File installer\build.ps1 -GameDir <path to Build\Windows> -Out <where to put ApexDrive-Setup.exe>
# Needs only what Windows ships with (the .NET Framework C# compiler).
param(
  [Parameter(Mandatory = $true)] [string] $GameDir,
  [string] $Out = "ApexDrive-Setup.exe"
)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$work = Join-Path $env:TEMP ("apex-setup-" + [guid]::NewGuid().ToString("N"))
$stage = Join-Path $work "game"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Get-ChildItem $GameDir | Where-Object { $_.Name -notlike "*DoNotShip*" } | ForEach-Object { Copy-Item -Recurse -Force $_.FullName $stage }
$payload = Join-Path $work "payload.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $payload -CompressionLevel Optimal -Force
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$icon = Join-Path $here "apexdrive.ico"
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { "" }
& $csc /nologo /target:winexe "/out:$Out" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$payload,payload.zip" $iconArg (Join-Path $here "Setup.cs")
Get-Item $Out | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
