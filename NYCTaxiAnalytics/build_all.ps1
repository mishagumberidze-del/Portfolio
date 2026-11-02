[CmdletBinding()]
param(
 [string]$PythonPath='python',
 [string]$RawDir='',
 [string]$ServerInstance='.\SQLEXPRESS',
 [string]$SqlCmdPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
 [string]$BcpPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
$prepare=Join-Path $PSScriptRoot 'etl\prepare_analytics.py'
if($RawDir){& $PythonPath $prepare --raw-dir $RawDir}else{& $PythonPath $prepare}
if($LASTEXITCODE -ne 0){throw 'Parquet preparation failed'}
& (Join-Path $PSScriptRoot 'etl\load_sql.ps1') -ServerInstance $ServerInstance -SqlCmdPath $SqlCmdPath -BcpPath $BcpPath
& (Join-Path $PSScriptRoot 'etl\export_dashboard.ps1') -ServerInstance $ServerInstance -BcpPath $BcpPath
& $PythonPath (Join-Path $PSScriptRoot 'etl\build_dashboard.py')
if($LASTEXITCODE -ne 0){throw 'Dashboard build failed'}
Write-Host 'Open site/index.html. SQL reporting exports and source counts have reconciled.'
