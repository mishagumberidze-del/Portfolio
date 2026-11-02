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
if($LASTEXITCODE -ne 0){throw 'Retail source validation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -i (Join-Path $root 'sql\00_create_database.sql')
if($LASTEXITCODE -ne 0){throw 'Database creation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_OnlineRetail -i (Join-Path $root 'sql\01_model.sql')
if($LASTEXITCODE -ne 0){throw 'Schema creation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_OnlineRetail -Q 'TRUNCATE TABLE stg.RetailLine'
if($LASTEXITCODE -ne 0){throw 'Staging reset failed'}
& $BcpPath Portfolio_OnlineRetail.stg.RetailLine in (Join-Path $root 'data\processed\retail_staging.tsv') -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a -b 10000 -a 32768 | Select-Object -Last 5
if($LASTEXITCODE -ne 0){throw 'Staging import failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_OnlineRetail -i (Join-Path $root 'sql\02_load_core.sql')
if($LASTEXITCODE -ne 0){throw 'Core transformation failed'}
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_OnlineRetail -i (Join-Path $root 'sql\03_validate.sql')
if($LASTEXITCODE -ne 0){throw 'Source reconciliation failed'}
$m=Get-Content (Join-Path $root 'data\processed\manifest.json') -Raw | ConvertFrom-Json
$q="SET NOCOUNT ON; INSERT etl.LoadAudit(SourceSha256,SourceRows,CoreRows,DuplicateRows,MissingCustomerRows,Status) SELECT '$($m.sha256)',$($m.rows),COUNT(*),$($m.exact_duplicate_rows),$($m.missing_customer_rows),'SUCCEEDED' FROM core.InvoiceLine; SELECT TOP 1 * FROM etl.LoadAudit ORDER BY LoadId DESC;"
& $SqlCmdPath -S $ServerInstance -E -b -d Portfolio_OnlineRetail -Q $q
if($LASTEXITCODE -ne 0){throw 'Audit insert failed'}
