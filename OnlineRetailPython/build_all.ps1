[CmdletBinding()]
param(
 [string]$PythonPath='python',
 [string]$ServerInstance='.\SQLEXPRESS',
 [string]$SqlCmdPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
 [string]$BcpPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'etl\run_pipeline.ps1') -PythonPath $PythonPath -ServerInstance $ServerInstance -SqlCmdPath $SqlCmdPath -BcpPath $BcpPath
& (Join-Path $PSScriptRoot 'etl\export_report.ps1') -ServerInstance $ServerInstance -BcpPath $BcpPath
& $PythonPath (Join-Path $PSScriptRoot 'report\build_report.py')
if($LASTEXITCODE -ne 0){throw 'HTML report generation failed'}
Write-Host 'Open report/online_retail_analysis.html in a browser.'
