[CmdletBinding()]
param(
 [string]$PythonPath='python',
 [string]$ServerInstance='.\SQLEXPRESS',
 [string]$SqlCmdPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
 [string]$BcpPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'etl\run_pipeline.ps1') -PythonPath $PythonPath -ServerInstance $ServerInstance -SqlCmdPath $SqlCmdPath -BcpPath $BcpPath
& (Join-Path $PSScriptRoot 'etl\export_powerbi.ps1') -ServerInstance $ServerInstance -BcpPath $BcpPath
& $PythonPath (Join-Path $PSScriptRoot 'powerbi\build_pbip.py')
if($LASTEXITCODE -ne 0){throw 'PBIP generation failed'}
Write-Host 'Open powerbi/HotelBookingPBIP/HotelBooking.pbip, refresh and save in Power BI Desktop.'
