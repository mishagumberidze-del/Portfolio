SET NOCOUNT ON;

/* Overall KPI snapshot. Sales/GMV excludes canceled and unavailable orders. */
SELECT
    CONVERT(decimal(14,2),SUM(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN MerchandiseValue ELSE 0 END)) AS SalesGMV,
    COUNT(*) AS Orders,
    COUNT(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN 1 END) AS SalesOrders,
    COUNT(DISTINCT CustomerKey) AS UniqueCustomers,
    CONVERT(decimal(12,2),SUM(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN MerchandiseValue ELSE 0 END)
        / NULLIF(COUNT(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN 1 END),0)) AS AOV,
    CONVERT(decimal(12,3),SUM(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN ItemQuantity ELSE 0 END)*1.0
        / NULLIF(COUNT(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN 1 END),0)) AS ItemsPerOrder,
    CONVERT(decimal(12,4),(SELECT COUNT(*)*1.0/NULLIF((SELECT COUNT(*) FROM core.Customer),0) FROM core.Customer WHERE CustomerType='Repeat')) AS LifetimeRepeatCustomerRate,
    CONVERT(decimal(12,4),SUM(CASE WHEN OrderStatus='delivered' THEN 1.0 ELSE 0 END)/COUNT(*)) AS DeliveryRate,
    CONVERT(decimal(12,4),SUM(CASE WHEN OrderStatus='canceled' THEN 1.0 ELSE 0 END)/COUNT(*)) AS CancellationRate,
    CONVERT(decimal(12,2),AVG(CASE WHEN OrderStatus='delivered' THEN DeliveryDays END)) AS AverageDeliveryDays,
    CONVERT(decimal(12,4),AVG(CASE WHEN OrderStatus='delivered' AND DeliveryPerformance<>'Not Delivered' THEN CASE WHEN DeliveryPerformance='On Time' THEN 1.0 ELSE 0 END END)) AS OnTimeDeliveryRate,
    CONVERT(decimal(12,2),(SELECT AVG(CONVERT(decimal(12,4),ReviewScore)) FROM core.Review)) AS AverageReviewScore,
    CONVERT(decimal(12,4),SUM(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN FreightValue ELSE 0 END)
        / NULLIF(SUM(CASE WHEN OrderStatus NOT IN ('canceled','unavailable') THEN MerchandiseValue ELSE 0 END),0)) AS FreightRatio
FROM core.[Order];

SELECT TOP (10) CategoryNameEnglish,SUM(SalesGMV) AS SalesGMV,SUM(Units) AS Units,
       SUM(Orders) AS ProductOrderOccurrences,SUM(Customers) AS ProductCustomerOccurrences,
       SUM(SalesGMV)/NULLIF(SUM(Units),0) AS AverageSellingPrice,
       AVG(AverageReviewScore) AS AverageProductReviewScore
FROM rpt.vProductPerformance
GROUP BY CategoryNameEnglish ORDER BY SalesGMV DESC;

SELECT TOP (10) StateCode,SUM(SalesGMV) AS SalesGMV,SUM(Orders) AS Orders,SUM(Customers) AS LocationCustomerCounts,
       AVG(AverageDeliveryDays) AS AverageDeliveryDays,AVG(OnTimeRate) AS AverageOnTimeRate
FROM rpt.vGeographicPerformance GROUP BY StateCode ORDER BY SalesGMV DESC;

SELECT DeliveryPerformance,COUNT(*) AS Orders,
       AVG(DeliveryDays) AS AverageDeliveryDays,AVG(CONVERT(decimal(12,4),AverageReviewScore)) AS AverageReviewScore
FROM rpt.vDeliveryExperience
WHERE OrderStatus='delivered'
GROUP BY DeliveryPerformance ORDER BY Orders DESC;

SELECT TOP (10) YearMonth,SUM(SalesGMV) AS SalesGMV,SUM(Orders) AS Orders
FROM rpt.vExecutiveDaily GROUP BY YearMonth,YearMonthSort ORDER BY SalesGMV DESC;

SELECT MonthsSinceFirstPurchase,
       SUM(ActiveCustomers) AS ActiveCustomers,SUM(CohortCustomers) AS CohortCustomers,
       CONVERT(decimal(12,4),SUM(ActiveCustomers)*1.0/NULLIF(SUM(CohortCustomers),0)) AS WeightedRetentionRate
FROM rpt.vCohortRetention WHERE MonthsSinceFirstPurchase BETWEEN 0 AND 6
GROUP BY MonthsSinceFirstPurchase ORDER BY MonthsSinceFirstPurchase;

WITH RankedSeller AS (
    SELECT SalesGMV,ROW_NUMBER() OVER (ORDER BY SalesGMV DESC) rn FROM rpt.vSellerPerformance
)
SELECT CONVERT(decimal(12,4),SUM(CASE WHEN rn<=10 THEN SalesGMV ELSE 0 END)/NULLIF(SUM(SalesGMV),0)) AS Top10SellerSalesShare
FROM RankedSeller;
