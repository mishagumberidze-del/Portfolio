[CmdletBinding()]
param(
 [string]$ServerInstance='.\SQLEXPRESS',
 [string]$SqlCmdPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
 [string]$BcpPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& $SqlCmdPath -S $ServerInstance -E -b -i (Join-Path $root 'sql\00_database.sql')
if($LASTEXITCODE -ne 0){throw 'Database setup failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_NYCTaxi -i (Join-Path $root 'sql\01_model.sql')
if($LASTEXITCODE -ne 0){throw 'Model setup failed'}
$tables=@{SourceFile='source';Zone='zones';TimeProfile='time';ZoneProfile='zone';RouteProfile='route';BandProfile='band';Quality='quality'}
foreach($table in $tables.Keys){
 & $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_NYCTaxi -Q "TRUNCATE TABLE stg.$table"
 if($LASTEXITCODE -ne 0){throw "Staging reset failed: $table"}
 $file=Join-Path $root "data\processed\$($tables[$table]).tsv"
 & $BcpPath "Portfolio_NYCTaxi.stg.$table" in $file -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a -b 50000 -a 32768 | Select-Object -Last 4
 if($LASTEXITCODE -ne 0){throw "Staging load failed: $table"}
}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_NYCTaxi -i (Join-Path $root 'sql\02_load.sql')
if($LASTEXITCODE -ne 0){throw 'Core load or reconciliation failed'}
