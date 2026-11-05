[CmdletBinding()]
param(
    [string]$ServerInstance = '.\SQLEXPRESS',
    [string]$Database = 'Portfolio_Olist',
    [string]$BcpPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exportDir = Join-Path $projectRoot 'data\processed\powerbi'
New-Item -ItemType Directory -Force -Path $exportDir | Out-Null

if (-not (Test-Path -LiteralPath $BcpPath)) {
    throw "BCP executable not found: $BcpPath"
}

$exports = @(
    @{ File='date.tsv'; Query="SELECT DateKey,[Date],CalendarYear,CalendarQuarter,QuarterLabel,MonthNumber,MonthName,YearMonth,YearMonthSort,WeekOfYear,DayOfMonth,DayName,IsWeekend FROM $Database.core.[Date] ORDER BY DateKey" },
    @{ File='customer.tsv'; Query="SELECT CustomerKey,CustomerUniqueId,HomeGeographyKey,City,StateCode,FirstPurchaseDateTime,LastPurchaseDateTime,CohortMonth,PurchaseCount,AverageDaysBetweenPurchases,CustomerType,RecencyDays,CompletedPurchaseCount,CustomerSales,CustomerFreight,AverageDeliveryDays,OnTimeRate,AverageReviewScore,CompletedPurchaseGroup FROM $Database.rpt.vCustomer360 ORDER BY CustomerKey" },
    @{ File='geography.tsv'; Query="SELECT GeographyKey,ZipCodePrefix,City,StateCode,Latitude,Longitude,SourcePointCount FROM $Database.core.Geography ORDER BY GeographyKey" },
    @{ File='order.tsv'; Query="SELECT OrderKey,OrderId,OlistCustomerId,CustomerKey,CustomerGeographyKey,PurchaseDateKey,OrderStatus,PurchaseDateTime,ApprovedDateTime,DeliveredCarrierDateTime,DeliveredCustomerDateTime,EstimatedDeliveryDate,DeliveryDays,DelayDays,DeliveryPerformance,PurchaseSequence,DaysSincePriorPurchase,CustomerOrderType,CohortMonth,MerchandiseValue,FreightValue,GrossOrderValue,ItemQuantity,FreightRatio,PaymentValue FROM $Database.rpt.vOrderAnalytics ORDER BY OrderKey" },
    @{ File='order_item.tsv'; Query="SELECT OrderItemKey,OrderKey,OrderItemNumber,ProductKey,SellerKey,ShippingLimitDateTime,ItemPrice,FreightValue,ItemGrossValue,FreightRatio FROM $Database.core.OrderItem ORDER BY OrderItemKey" },
    @{ File='product.tsv'; Query="SELECT ProductKey,ProductId,CategoryNamePortuguese,CategoryNameEnglish,ProductNameLength,ProductDescriptionLength,ProductPhotoQuantity,ProductWeightGrams,ProductLengthCm,ProductHeightCm,ProductWidthCm FROM $Database.core.Product ORDER BY ProductKey" },
    @{ File='seller.tsv'; Query="SELECT SellerKey,SellerId,GeographyKey FROM $Database.core.Seller ORDER BY SellerKey" },
    @{ File='payment.tsv'; Query="SELECT PaymentKey,OrderKey,PaymentSequence,PaymentType,Installments,PaymentValue FROM $Database.core.Payment ORDER BY PaymentKey" },
    # Free-text review comments are intentionally excluded from the BI extract because embedded tabs/newlines are not analytical fields.
    @{ File='review.tsv'; Query="SELECT ReviewKey,ReviewId,OrderKey,ReviewScore,CASE WHEN ReviewScore<=2 THEN '1-2 stars' WHEN ReviewScore=3 THEN '3 stars' ELSE '4-5 stars' END,ReviewCreationDate,ReviewAnswerDateTime FROM $Database.core.Review ORDER BY ReviewKey" },
    @{ File='cohort_retention.tsv'; Query="SELECT CohortMonth,ActivityMonth,MonthsSinceFirstPurchase,ActiveCustomers,CohortCustomers,RetentionRate FROM $Database.rpt.vCohortRetention ORDER BY CohortMonth,MonthsSinceFirstPurchase" }
)

$manifest = @()
foreach ($export in $exports) {
    $target = Join-Path $exportDir $export.File
    & $BcpPath $export.Query queryout $target -S $ServerInstance -T -c -C 65001 -t "\t" -r 0x0a
    if ($LASTEXITCODE -ne 0) {
        throw "BCP export failed for $($export.File)"
    }

    $rows = ([System.Linq.Enumerable]::Count([IO.File]::ReadLines($target)))
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    $item = Get-Item -LiteralPath $target
    $manifest += [pscustomobject]@{
        file = $export.File
        rows = $rows
        bytes = $item.Length
        sha256 = $hash
        source = $export.Query
    }
}

[pscustomobject]@{
    database = $Database
    server = $ServerInstance
    generated_at_utc = [DateTime]::UtcNow.ToString('o')
    exports = $manifest
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $exportDir 'manifest.json') -Encoding UTF8

Write-Host "Created $($exports.Count) SQL-produced Power BI extracts in $exportDir"
