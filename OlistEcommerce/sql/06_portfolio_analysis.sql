SET NOCOUNT ON;

/* Commercial value excludes canceled/unavailable orders; denominators use that same population. */
SELECT CONVERT(decimal(14,2),SUM(CASE WHEN IsSalesOrder=1 THEN MerchandiseValue ELSE 0 END)) AS SalesGMV,
    COUNT(*) AS Orders, SUM(CONVERT(int,IsSalesOrder)) AS SalesOrders,
    COUNT(DISTINCT CustomerKey) AS UniqueCustomers,
    COUNT(DISTINCT CASE WHEN IsSalesOrder=1 THEN CustomerKey END) AS SalesCustomers,
    COUNT(DISTINCT CASE WHEN PurchaseSequence>1 THEN CustomerKey END) AS RepeatSalesCustomers,
    CONVERT(decimal(12,4),COUNT(DISTINCT CASE WHEN PurchaseSequence>1 THEN CustomerKey END)*1.0/
        NULLIF(COUNT(DISTINCT CASE WHEN IsSalesOrder=1 THEN CustomerKey END),0)) AS RepeatSalesCustomerRate,
    CONVERT(decimal(12,2),SUM(CASE WHEN IsSalesOrder=1 THEN MerchandiseValue ELSE 0 END)/NULLIF(SUM(CONVERT(int,IsSalesOrder)),0)) AS AOV,
    CONVERT(decimal(12,3),SUM(CASE WHEN IsSalesOrder=1 THEN ItemQuantity ELSE 0 END)*1.0/NULLIF(SUM(CONVERT(int,IsSalesOrder)),0)) AS ItemsPerOrder,
    CONVERT(decimal(12,4),AVG(CONVERT(decimal(12,4),IsDeliveredOrder))) AS DeliveryRate,
    CONVERT(decimal(12,4),AVG(CONVERT(decimal(12,4),IsCancelledOrder))) AS CancellationRate,
    CONVERT(decimal(12,2),AVG(CASE WHEN IsDeliveredOrder=1 THEN DeliveryDays END)) AS AverageDeliveryDays,
    CONVERT(decimal(12,4),AVG(CASE WHEN IsDeliveredOrder=1 AND DeliveryPerformance IN ('On Time','Late') THEN CASE WHEN DeliveryPerformance='On Time' THEN 1.0 ELSE 0 END END)) AS OnTimeDeliveryRate,
    CONVERT(decimal(12,2),SUM(ReviewScoreSum)*1.0/NULLIF(SUM(ReviewCount),0)) AS AverageReviewScore,
    CONVERT(decimal(12,4),SUM(CASE WHEN IsSalesOrder=1 THEN FreightValue ELSE 0 END)/NULLIF(SUM(CASE WHEN IsSalesOrder=1 THEN MerchandiseValue ELSE 0 END),0)) AS FreightRatio
FROM rpt.vOrderAnalytics;

/* Distinct counts are recalculated at category grain, not summed across products. */
SELECT TOP (10) p.CategoryNameEnglish,SUM(i.ItemPrice) AS SalesGMV,COUNT_BIG(*) AS Units,
    COUNT(DISTINCT i.OrderKey) AS Orders,COUNT(DISTINCT o.CustomerKey) AS Customers,
    SUM(i.ItemPrice)/COUNT_BIG(*) AS AverageSellingPrice
FROM core.OrderItem i JOIN core.Product p ON p.ProductKey=i.ProductKey
JOIN core.[Order] o ON o.OrderKey=i.OrderKey
WHERE o.OrderStatus NOT IN ('canceled','unavailable')
GROUP BY p.CategoryNameEnglish ORDER BY SalesGMV DESC;

/* State averages weight eligible orders, not ZIP-code averages. */
SELECT TOP (10) g.StateCode,
    SUM(CASE WHEN o.IsSalesOrder=1 THEN o.MerchandiseValue ELSE 0 END) AS SalesGMV,
    COUNT_BIG(*) AS Orders,COUNT(DISTINCT o.CustomerKey) AS Customers,
    AVG(CASE WHEN o.IsDeliveredOrder=1 THEN o.DeliveryDays END) AS AverageDeliveryDays,
    AVG(CASE WHEN o.IsDeliveredOrder=1 AND o.DeliveryPerformance IN ('On Time','Late') THEN CASE WHEN o.DeliveryPerformance='On Time' THEN 1.0 ELSE 0 END END) AS OnTimeRate
FROM rpt.vOrderAnalytics o JOIN core.Geography g ON g.GeographyKey=o.CustomerGeographyKey
GROUP BY g.StateCode ORDER BY SalesGMV DESC;

SELECT DeliveryPerformance,COUNT(*) AS Orders,AVG(DeliveryDays) AS AverageDeliveryDays,
    SUM(ReviewScoreSum)*1.0/NULLIF(SUM(ReviewCount),0) AS AverageReviewScore
FROM rpt.vOrderAnalytics WHERE IsDeliveredOrder=1
GROUP BY DeliveryPerformance ORDER BY Orders DESC;

SELECT TOP (10) YearMonth,SUM(SalesGMV) AS SalesGMV,SUM(Orders) AS Orders
FROM rpt.vExecutiveDaily GROUP BY YearMonth,YearMonthSort ORDER BY SalesGMV DESC;

/* Complete follow-up months only; each age can have a different eligible cohort base. */
SELECT MonthsSinceFirstPurchase,SUM(ActiveCustomers) AS ActiveCustomers,SUM(CohortCustomers) AS CohortCustomers,
    CONVERT(decimal(12,6),SUM(ActiveCustomers)*1.0/NULLIF(SUM(CohortCustomers),0)) AS WeightedRetentionRate
FROM rpt.vCohortRetention WHERE MonthsSinceFirstPurchase BETWEEN 0 AND 6
GROUP BY MonthsSinceFirstPurchase ORDER BY MonthsSinceFirstPurchase;

WITH RankedSeller AS (
    SELECT SalesGMV,ROW_NUMBER() OVER (ORDER BY SalesGMV DESC,SellerKey) rn FROM rpt.vSellerPerformance
)
SELECT CONVERT(decimal(12,4),SUM(CASE WHEN rn<=10 THEN SalesGMV ELSE 0 END)/NULLIF(SUM(SalesGMV),0)) AS Top10SellerSalesShare
FROM RankedSeller;
