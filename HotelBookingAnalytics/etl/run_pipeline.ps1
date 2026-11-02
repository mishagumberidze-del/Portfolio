[CmdletBinding()]
param(
 [string]$ServerInstance = '.\SQLEXPRESS',
 [string]$PythonPath = 'python',
 [string]$SqlCmdPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
 [string]$BcpPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& $PythonPath (Join-Path $PSScriptRoot 'prepare.py')
if($LASTEXITCODE -ne 0){throw 'Hotel source validation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -i (Join-Path $root 'sql\00_create_database.sql')
if($LASTEXITCODE -ne 0){throw 'Database creation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_HotelBookings -i (Join-Path $root 'sql\01_model.sql')
if($LASTEXITCODE -ne 0){throw 'Schema creation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_HotelBookings -Q 'TRUNCATE TABLE stg.Booking'
if($LASTEXITCODE -ne 0){throw 'Staging reset failed'}
$file=Join-Path $root 'data\processed\hotel_staging.tsv'
& $BcpPath Portfolio_HotelBookings.stg.Booking in $file -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a
if($LASTEXITCODE -ne 0){throw 'Staging import failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_HotelBookings -i (Join-Path $root 'sql\02_load_core.sql')
if($LASTEXITCODE -ne 0){throw 'Core transformation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_HotelBookings -i (Join-Path $root 'sql\03_validate.sql')
if($LASTEXITCODE -ne 0){throw 'Source reconciliation failed'}
$m=Get-Content (Join-Path $root 'data\processed\manifest.json') -Raw | ConvertFrom-Json
$hash=$m.sha256
$q="SET NOCOUNT ON; INSERT etl.LoadAudit(SourceSha256,SourceRows,CoreRows,FlaggedRows,DuplicateRows,Status) SELECT '$hash',$($m.rows),COUNT(*),SUM(CONVERT(int,DataQualityFlag)),$($m.exact_duplicate_rows),'SUCCEEDED' FROM core.Booking; SELECT TOP 1 * FROM etl.LoadAudit ORDER BY LoadId DESC;"
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_HotelBookings -Q $q
if($LASTEXITCODE -ne 0){throw 'Audit insert failed'}
