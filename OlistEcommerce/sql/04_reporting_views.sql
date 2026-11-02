SET NOCOUNT ON;
GO

CREATE OR ALTER VIEW rpt.vOrderAnalytics
AS
WITH ReviewByOrder AS (
    SELECT OrderKey, AVG(CONVERT(decimal(9,4),ReviewScore)) AS AverageReviewScore,
           MAX(CASE WHEN CommentMessage IS NOT NULL THEN 1 ELSE 0 END) AS HasReviewComment
    FROM core.Review GROUP BY OrderKey
)
SELECT o.OrderKey, o.OrderId, o.CustomerKey, o.CustomerGeographyKey, o.PurchaseDateKey,
       o.OrderStatus, o.PurchaseDateTime, o.ApprovedDateTime, o.DeliveredCarrierDateTime,
       o.DeliveredCustomerDateTime, o.EstimatedDeliveryDate, o.DeliveryDays, o.DelayDays,
       o.DeliveryPerformance, o.PurchaseSequence, o.DaysSincePriorPurchase,
       o.CustomerOrderType, o.CohortMonth, o.MerchandiseValue, o.FreightValue,
       o.GrossOrderValue, o.ItemQuantity, o.FreightRatio, o.PaymentValue,
       CONVERT(bit,CASE WHEN o.OrderStatus NOT IN ('canceled','unavailable') THEN 1 ELSE 0 END) AS IsSalesOrder,
       CONVERT(bit,CASE WHEN o.OrderStatus='delivered' THEN 1 ELSE 0 END) AS IsDeliveredOrder,
       CONVERT(bit,CASE WHEN o.OrderStatus='canceled' THEN 1 ELSE 0 END) AS IsCancelledOrder,
       r.AverageReviewScore, COALESCE(r.HasReviewComment,0) AS HasReviewComment
FROM core.[Order] o
LEFT JOIN ReviewByOrder r ON r.OrderKey=o.OrderKey;
GO

CREATE OR ALTER VIEW rpt.vExecutiveDaily
AS
SELECT d.[Date], d.DateKey, d.CalendarYear, d.CalendarQuarter, d.YearMonth, d.YearMonthSort,
       COUNT_BIG(*) AS Orders,
       COUNT(DISTINCT o.CustomerKey) AS UniqueCustomers,
       SUM(CASE WHEN o.IsSalesOrder=1 THEN o.MerchandiseValue ELSE 0 END) AS SalesGMV,
       SUM(CASE WHEN o.IsSalesOrder=1 THEN o.FreightValue ELSE 0 END) AS FreightCost,
       SUM(CASE WHEN o.IsSalesOrder=1 THEN o.ItemQuantity ELSE 0 END) AS Items,
       SUM(CONVERT(bigint,o.IsDeliveredOrder)) AS DeliveredOrders,
       SUM(CONVERT(bigint,o.IsCancelledOrder)) AS CancelledOrders,
       AVG(CASE WHEN o.IsDeliveredOrder=1 THEN o.DeliveryDays END) AS AverageDeliveryDays,
       AVG(CASE WHEN o.IsDeliveredOrder=1 THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeDeliveryRate,
       AVG(o.AverageReviewScore) AS AverageReviewScore
FROM rpt.vOrderAnalytics o
JOIN core.[Date] d ON d.DateKey=o.PurchaseDateKey
GROUP BY d.[Date], d.DateKey, d.CalendarYear, d.CalendarQuarter, d.YearMonth, d.YearMonthSort;
GO

CREATE OR ALTER VIEW rpt.vCustomer360
AS
WITH ReferenceDate AS (
    SELECT MAX(CONVERT(date,PurchaseDateTime)) AS MaxPurchaseDate FROM core.[Order]
), CustomerValue AS (
    SELECT c.CustomerKey,
           DATEDIFF(day,MAX(CASE WHEN o.OrderStatus='delivered' THEN CONVERT(date,o.PurchaseDateTime) END),(SELECT MaxPurchaseDate FROM ReferenceDate)) AS RecencyDays,
           COUNT(DISTINCT CASE WHEN o.OrderStatus='delivered' THEN o.OrderKey END) AS CompletedPurchaseCount,
           SUM(CASE WHEN o.OrderStatus='delivered' THEN o.MerchandiseValue ELSE 0 END) AS CustomerSales,
           SUM(CASE WHEN o.OrderStatus='delivered' THEN o.FreightValue ELSE 0 END) AS CustomerFreight,
           AVG(CASE WHEN o.OrderStatus='delivered' THEN o.DeliveryDays END) AS AverageDeliveryDays,
           AVG(CASE WHEN o.OrderStatus='delivered' THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeRate,
           AVG(r.AverageReviewScore) AS AverageReviewScore
    FROM core.Customer c
    LEFT JOIN core.[Order] o ON o.CustomerKey=c.CustomerKey
    LEFT JOIN (
        SELECT OrderKey,AVG(CONVERT(decimal(9,4),ReviewScore)) AS AverageReviewScore
        FROM core.Review GROUP BY OrderKey
    ) r ON r.OrderKey=o.OrderKey
    GROUP BY c.CustomerKey
), Scored AS (
    SELECT v.*,
           CASE WHEN v.RecencyDays IS NULL THEN NULL ELSE 6-NTILE(5) OVER (ORDER BY v.RecencyDays) END AS RScore,
           CASE WHEN v.CompletedPurchaseCount=0 THEN NULL
                WHEN v.CompletedPurchaseCount=1 THEN 1
                WHEN v.CompletedPurchaseCount=2 THEN 2
                WHEN v.CompletedPurchaseCount=3 THEN 3
                WHEN v.CompletedPurchaseCount=4 THEN 4 ELSE 5 END AS FScore,
           CASE WHEN v.CompletedPurchaseCount=0 THEN NULL ELSE NTILE(5) OVER (ORDER BY v.CustomerSales) END AS MScore
    FROM CustomerValue v
)
SELECT c.CustomerKey, c.CustomerUniqueId, c.HomeGeographyKey, g.City, g.StateCode,
       c.FirstPurchaseDateTime, c.LastPurchaseDateTime, c.CohortMonth, c.PurchaseCount,
       c.CustomerPurchaseFrequency, c.AverageDaysBetweenPurchases, c.CustomerType,
       s.RecencyDays, s.CompletedPurchaseCount, s.CustomerSales, s.CustomerFreight,
       s.AverageDeliveryDays, s.OnTimeRate, s.AverageReviewScore,
       s.RScore, s.FScore, s.MScore,
       CASE WHEN s.CompletedPurchaseCount=0 THEN 'No Completed Purchase'
            WHEN s.RScore>=4 AND s.FScore>=3 AND s.MScore>=4 THEN 'Champions'
            WHEN s.FScore>=2 AND s.RScore>=3 THEN 'Loyal Customers'
            WHEN s.RScore>=4 AND s.FScore=1 AND s.MScore>=3 THEN 'Potential Loyalists'
            WHEN s.RScore=5 AND s.FScore=1 THEN 'New Customers'
            WHEN s.RScore<=2 AND s.FScore>=2 THEN 'At Risk'
            WHEN s.RScore<=2 AND s.FScore=1 THEN 'Hibernating'
            ELSE 'Needs Attention' END AS RfmSegment
FROM core.Customer c
JOIN Scored s ON s.CustomerKey=c.CustomerKey
LEFT JOIN core.Geography g ON g.GeographyKey=c.HomeGeographyKey;
GO

CREATE OR ALTER VIEW rpt.vCohortRetention
AS
WITH Activity AS (
    SELECT DISTINCT o.CustomerKey, o.CohortMonth,
           DATEFROMPARTS(YEAR(o.PurchaseDateTime),MONTH(o.PurchaseDateTime),1) AS ActivityMonth,
           DATEDIFF(month,o.CohortMonth,DATEFROMPARTS(YEAR(o.PurchaseDateTime),MONTH(o.PurchaseDateTime),1)) AS MonthsSinceFirstPurchase
    FROM core.[Order] o
    WHERE o.OrderStatus NOT IN ('canceled','unavailable')
), CohortSize AS (
    SELECT CohortMonth,COUNT(DISTINCT CustomerKey) AS CohortCustomers
    FROM Activity WHERE MonthsSinceFirstPurchase=0 GROUP BY CohortMonth
), MonthCalendar AS (
    SELECT DISTINCT DATEFROMPARTS(CalendarYear,MonthNumber,1) AS ActivityMonth
    FROM core.[Date]
), EligibleGrid AS (
    SELECT c.CohortMonth,m.ActivityMonth,
           DATEDIFF(month,c.CohortMonth,m.ActivityMonth) AS MonthsSinceFirstPurchase,
           c.CohortCustomers
    FROM CohortSize c
    JOIN MonthCalendar m ON m.ActivityMonth>=c.CohortMonth
    WHERE m.ActivityMonth<=(SELECT MAX(ActivityMonth) FROM Activity)
), ActiveByMonth AS (
    SELECT CohortMonth,ActivityMonth,MonthsSinceFirstPurchase,COUNT(DISTINCT CustomerKey) AS ActiveCustomers
    FROM Activity GROUP BY CohortMonth,ActivityMonth,MonthsSinceFirstPurchase
)
SELECT g.CohortMonth,g.ActivityMonth,g.MonthsSinceFirstPurchase,
       COALESCE(a.ActiveCustomers,0) AS ActiveCustomers,g.CohortCustomers,
       CONVERT(decimal(12,6),COALESCE(a.ActiveCustomers,0)*1.0/NULLIF(g.CohortCustomers,0)) AS RetentionRate
FROM EligibleGrid g
LEFT JOIN ActiveByMonth a ON a.CohortMonth=g.CohortMonth AND a.ActivityMonth=g.ActivityMonth;
GO

CREATE OR ALTER VIEW rpt.vProductPerformance
AS
SELECT p.ProductKey,p.ProductId,p.CategoryNameEnglish,p.CategoryNamePortuguese,
       COUNT_BIG(*) AS Units,COUNT(DISTINCT i.OrderKey) AS Orders,
       COUNT(DISTINCT o.CustomerKey) AS Customers,
       SUM(i.ItemPrice) AS SalesGMV,SUM(i.FreightValue) AS FreightCost,
       AVG(i.ItemPrice) AS AverageSellingPrice,
       AVG(CONVERT(decimal(9,4),r.AverageReviewScore)) AS AverageReviewScore
FROM core.OrderItem i
JOIN core.Product p ON p.ProductKey=i.ProductKey
JOIN core.[Order] o ON o.OrderKey=i.OrderKey
LEFT JOIN (SELECT OrderKey,AVG(CONVERT(decimal(9,4),ReviewScore)) AverageReviewScore FROM core.Review GROUP BY OrderKey) r ON r.OrderKey=o.OrderKey
WHERE o.OrderStatus NOT IN ('canceled','unavailable')
GROUP BY p.ProductKey,p.ProductId,p.CategoryNameEnglish,p.CategoryNamePortuguese;
GO

CREATE OR ALTER VIEW rpt.vDeliveryExperience
AS
SELECT o.OrderKey,o.OrderId,o.PurchaseDateKey,o.CustomerKey,o.CustomerGeographyKey,
       o.OrderStatus,o.DeliveryDays,o.DelayDays,o.DeliveryPerformance,o.FreightValue,o.FreightRatio,
       o.MerchandiseValue,o.GrossOrderValue,o.ItemQuantity,
       r.AverageReviewScore,r.ReviewCount,
       CASE WHEN o.DelayDays IS NULL THEN 'Not delivered'
            WHEN o.DelayDays<=-8 THEN '8+ days early'
            WHEN o.DelayDays<0 THEN '1–7 days early'
            WHEN o.DelayDays=0 THEN 'On promised date'
            WHEN o.DelayDays<=3 THEN '1–3 days late'
            WHEN o.DelayDays<=7 THEN '4–7 days late'
            ELSE '8+ days late' END AS DelayBand
FROM core.[Order] o
LEFT JOIN (
    SELECT OrderKey,AVG(CONVERT(decimal(9,4),ReviewScore)) AverageReviewScore,COUNT(*) ReviewCount
    FROM core.Review GROUP BY OrderKey
) r ON r.OrderKey=o.OrderKey;
GO

CREATE OR ALTER VIEW rpt.vSellerPerformance
AS
SELECT s.SellerKey,s.SellerId,g.City,g.StateCode,g.Latitude,g.Longitude,
       COUNT_BIG(*) AS Units,COUNT(DISTINCT i.OrderKey) AS Orders,
       COUNT(DISTINCT o.CustomerKey) AS Customers,
       SUM(CASE WHEN o.OrderStatus NOT IN ('canceled','unavailable') THEN i.ItemPrice ELSE 0 END) AS SalesGMV,
       SUM(CASE WHEN o.OrderStatus NOT IN ('canceled','unavailable') THEN i.FreightValue ELSE 0 END) AS FreightCost,
       AVG(CASE WHEN o.OrderStatus='delivered' THEN o.DeliveryDays END) AS AverageDeliveryDays,
       AVG(CASE WHEN o.OrderStatus='delivered' THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeRate,
       AVG(r.AverageReviewScore) AS AverageReviewScore
FROM core.Seller s
LEFT JOIN core.Geography g ON g.GeographyKey=s.GeographyKey
LEFT JOIN core.OrderItem i ON i.SellerKey=s.SellerKey
LEFT JOIN core.[Order] o ON o.OrderKey=i.OrderKey
LEFT JOIN (SELECT OrderKey,AVG(CONVERT(decimal(9,4),ReviewScore)) AverageReviewScore FROM core.Review GROUP BY OrderKey) r ON r.OrderKey=o.OrderKey
GROUP BY s.SellerKey,s.SellerId,g.City,g.StateCode,g.Latitude,g.Longitude;
GO

CREATE OR ALTER VIEW rpt.vGeographicPerformance
AS
SELECT g.GeographyKey,g.ZipCodePrefix,g.City,g.StateCode,g.Latitude,g.Longitude,
       COUNT_BIG(*) AS Orders,COUNT(DISTINCT o.CustomerKey) AS Customers,
       SUM(CASE WHEN o.OrderStatus NOT IN ('canceled','unavailable') THEN o.MerchandiseValue ELSE 0 END) AS SalesGMV,
       AVG(CASE WHEN o.OrderStatus='delivered' THEN o.DeliveryDays END) AS AverageDeliveryDays,
       AVG(CASE WHEN o.OrderStatus='delivered' THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeRate,
       AVG(r.AverageReviewScore) AS AverageReviewScore
FROM core.Geography g
JOIN core.[Order] o ON o.CustomerGeographyKey=g.GeographyKey
LEFT JOIN (SELECT OrderKey,AVG(CONVERT(decimal(9,4),ReviewScore)) AverageReviewScore FROM core.Review GROUP BY OrderKey) r ON r.OrderKey=o.OrderKey
GROUP BY g.GeographyKey,g.ZipCodePrefix,g.City,g.StateCode,g.Latitude,g.Longitude;
GO
