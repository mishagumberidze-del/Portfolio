[CmdletBinding()]
param(
    [string]$ServerInstance = '.\SQLEXPRESS',
    [string]$Database = 'Portfolio_Olist',
    [string]$PythonPath = 'python',
    [string]$SqlCmdPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
    [string]$BcpPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)

$ErrorActionPreference = 'Stop'
$PythonPath = (Get-Command $PythonPath -ErrorAction Stop).Source
$projectRoot = Split-Path -Parent $PSScriptRoot
$rawDir = Join-Path $projectRoot 'data\raw'
$processedDir = Join-Path $projectRoot 'data\processed'
$sqlDir = Join-Path $projectRoot 'sql'
$logDir = Join-Path $projectRoot 'logs'
New-Item -ItemType Directory -Force -Path $processedDir, $logDir | Out-Null

foreach ($required in @($PythonPath, $SqlCmdPath, $BcpPath)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required executable not found: $required" }
}

function Invoke-SqlFile([string]$File, [string]$TargetDatabase = '', [string[]]$Variables = @()) {
    $args = @('-S', $ServerInstance, '-E', '-b', '-i', $File)
    if ($TargetDatabase) { $args += @('-d', $TargetDatabase) }
    if ($Variables.Count -gt 0) { $args += '-v'; $args += $Variables }
    & $SqlCmdPath @args
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for $File with exit code $LASTEXITCODE" }
}

Write-Host '1/7 Validating raw files and preparing deterministic staging files...'
& $PythonPath (Join-Path $PSScriptRoot 'prepare_staging.py') --raw-dir $rawDir --processed-dir $processedDir
if ($LASTEXITCODE -ne 0) { throw 'Python staging preparation failed.' }

Write-Host '2/7 Creating database and schemas if needed...'
Invoke-SqlFile (Join-Path $sqlDir '00_create_database.sql')
Invoke-SqlFile (Join-Path $sqlDir '01_schema.sql') $Database

$sourceRootSql = $rawDir.Replace("'", "''")
$batchQuery = "SET NOCOUNT ON; INSERT etl.LoadBatch (StartedAtUtc, Status, SourceRoot, PipelineVersion) VALUES (SYSUTCDATETIME(), 'RUNNING', N'$sourceRootSql', '1.0.0'); SELECT CONVERT(int, SCOPE_IDENTITY());"
$batchOutput = & $SqlCmdPath -S $ServerInstance -E -b -d $Database -h -1 -W -Q $batchQuery
if ($LASTEXITCODE -ne 0) { throw 'Could not create ETL batch.' }
$batchId = [int](($batchOutput | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1).Trim())

try {
    Write-Host "3/7 Resetting staging for load batch $batchId..."
    Invoke-SqlFile (Join-Path $sqlDir '02_reset_staging.sql') $Database

    $manifest = Get-Content -LiteralPath (Join-Path $processedDir 'manifest.json') -Raw | ConvertFrom-Json
    $loads = @(
        @{Table='Customers'; File='customers.tsv'},
        @{Table='Geolocation'; File='geolocation.tsv'},
        @{Table='OrderItems'; File='order_items.tsv'},
        @{Table='Payments'; File='payments.tsv'},
        @{Table='Reviews'; File='reviews.tsv'},
        @{Table='Orders'; File='orders.tsv'},
        @{Table='Products'; File='products.tsv'},
        @{Table='Sellers'; File='sellers.tsv'},
        @{Table='CategoryTranslation'; File='category_translation.tsv'}
    )

    Write-Host '4/7 Bulk-loading MSSQL staging tables...'
    foreach ($load in $loads) {
        $path = Join-Path $processedDir $load.File
        $errorFile = Join-Path $logDir ("bcp_{0}_{1}.err" -f $batchId, $load.Table)
        $bcpOutput = & $BcpPath "$Database.stg.$($load.Table)" in $path -S $ServerInstance -T -c -C 65001 -t "\t" -r 0x0a -e $errorFile 2>&1
        if ($LASTEXITCODE -ne 0) { throw "BCP failed for $($load.Table). See $errorFile" }
        $copiedLine = $bcpOutput | Where-Object { $_ -match '^\d+ rows copied\.$' } | Select-Object -Last 1
        Write-Host ("  {0}: {1}" -f $load.Table, $copiedLine)

        $entry = $manifest.files | Where-Object { $_.processed_file -eq $load.File }
        $fileNameSql = $entry.source_file.Replace("'", "''")
        $hashSql = $entry.source_sha256.Replace("'", "''")
        $auditQuery = "SET NOCOUNT ON; INSERT etl.FileAudit (LoadBatchId, SourceFile, SourceSha256, SourceRows, StagedRows) VALUES ($batchId, N'$fileNameSql', '$hashSql', $($entry.row_count), $($entry.row_count));"
        & $SqlCmdPath -S $ServerInstance -E -b -d $Database -Q $auditQuery
        if ($LASTEXITCODE -ne 0) { throw "Could not write file audit for $($load.File)" }
    }

    Write-Host '5/7 Building relational core model and reporting views...'
    Invoke-SqlFile (Join-Path $sqlDir '03_transform_core.sql') $Database @("LoadBatchId=$batchId")
    Invoke-SqlFile (Join-Path $sqlDir '04_reporting_views.sql') $Database
    Invoke-SqlFile (Join-Path $sqlDir '05_quality_checks.sql') $Database @("LoadBatchId=$batchId")

    $completeQuery = "SET NOCOUNT ON; UPDATE etl.LoadBatch SET CompletedAtUtc=SYSUTCDATETIME(), Status='SUCCEEDED', SourceRows=(SELECT SUM(SourceRows) FROM etl.FileAudit WHERE LoadBatchId=$batchId), StagedRows=(SELECT SUM(StagedRows) FROM etl.FileAudit WHERE LoadBatchId=$batchId), RejectedRows=(SELECT COUNT(*) FROM etl.RejectedRecord WHERE LoadBatchId=$batchId) WHERE LoadBatchId=$batchId;"
    & $SqlCmdPath -S $ServerInstance -E -b -d $Database -Q $completeQuery
    if ($LASTEXITCODE -ne 0) { throw 'Could not finalize ETL audit.' }
    & $SqlCmdPath -S $ServerInstance -E -d $Database -W -Q "SET NOCOUNT ON; SELECT LoadBatchId,Status,SourceRows,StagedRows,RejectedRows,StartedAtUtc,CompletedAtUtc FROM etl.LoadBatch WHERE LoadBatchId=$batchId; SELECT CheckName,Severity,FailedRows,CheckStatus FROM etl.DataQualityResult WHERE LoadBatchId=$batchId ORDER BY Severity,CheckName;"
    Write-Host "6/7 Load batch $batchId completed successfully."
    Write-Host '7/7 Exporting SQL-produced Power BI extracts...'
    & (Join-Path $PSScriptRoot 'export_powerbi.ps1') -ServerInstance $ServerInstance -Database $Database -BcpPath $BcpPath
    if ($LASTEXITCODE -ne 0) { throw 'Power BI export failed.' }
}
catch {
    $message = $_.Exception.Message.Replace("'", "''")
    & $SqlCmdPath -S $ServerInstance -E -d $Database -Q "SET NOCOUNT ON; UPDATE etl.LoadBatch SET CompletedAtUtc=SYSUTCDATETIME(), Status='FAILED', ErrorMessage=N'$message' WHERE LoadBatchId=$batchId;"
    throw
}
