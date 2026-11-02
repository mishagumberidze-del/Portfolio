[CmdletBinding()]
param(
 [string]$ServerInstance = '.\SQLEXPRESS',
 [string]$BcpPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'data\processed'
New-Item -ItemType Directory -Force $out | Out-Null
$q=@'
SELECT CAST(InvoiceLineId AS varchar(20)),SourceInvoice,
 COALESCE(CAST(CustomerId AS varchar(20)),''),StockCode,COALESCE(ProductDescription,''),CountryName,
 CONVERT(char(10),CalendarDate,23),CAST(Quantity AS varchar(20)),
 CAST(UnitPrice AS varchar(30)),LineClass,CAST(NetSales AS varchar(30)),
 CAST(GrossSales AS varchar(30)),CAST(ReturnValue AS varchar(30)),
 CAST(IsCancellation AS varchar(1))
FROM Portfolio_OnlineRetail.rpt.LineAnalysis ORDER BY InvoiceLineId
'@
& $BcpPath $q queryout (Join-Path $out 'report_lines.tsv') -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a | Select-Object -Last 4
if($LASTEXITCODE -ne 0){throw 'Report line export failed'}
$q=@'
SELECT CAST(CustomerId AS varchar(20)),CAST(PurchaseInvoices AS varchar(20)),
 COALESCE(CONVERT(char(10),FirstPurchase,23),''),COALESCE(CONVERT(char(10),LastPurchase,23),''),
 CAST(NetSales AS varchar(30)),CAST(GrossSales AS varchar(30)),CAST(ReturnValue AS varchar(30))
FROM Portfolio_OnlineRetail.rpt.CustomerValue ORDER BY CustomerId
'@
& $BcpPath $q queryout (Join-Path $out 'customer_value.tsv') -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a | Select-Object -Last 4
if($LASTEXITCODE -ne 0){throw 'Customer export failed'}
