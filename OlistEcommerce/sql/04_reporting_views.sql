SET NOCOUNT ON;
GO

CREATE OR ALTER VIEW rpt.vOrderAnalytics
AS
WITH CommercialSequence AS (
    SELECT OrderKey,
           ROW_NUMBER() OVER (PARTITION BY CustomerKey ORDER BY PurchaseDateTime,OrderId) AS PurchaseSequence,
           LAG(PurchaseDateTime) OVER (PARTITION BY CustomerKey ORDER BY PurchaseDateTime,OrderId) AS PriorPurchaseDateTime,
           MIN(PurchaseDateTime) OVER (PARTITION BY CustomerKey) AS FirstPurchaseDateTime
    FROM core.[Order] WHERE OrderStatus NOT IN ('canceled','unavailable')
), ReviewByOrder AS (
    SELECT OrderKey, AVG(CONVERT(decimal(9,4),ReviewScore)) AS AverageReviewScore,
           SUM(CONVERT(bigint,ReviewScore)) AS ReviewScoreSum, COUNT_BIG(*) AS ReviewCount,
           MAX(CASE WHEN CommentMessage IS NOT NULL THEN 1 ELSE 0 END) AS HasReviewComment
    FROM core.Review GROUP BY OrderKey
)
SELECT o.OrderKey, o.OrderId, o.OlistCustomerId, o.CustomerKey, o.CustomerGeographyKey, o.PurchaseDateKey,
       o.OrderStatus, o.PurchaseDateTime, o.ApprovedDateTime, o.DeliveredCarrierDateTime,
       o.DeliveredCustomerDateTime, o.EstimatedDeliveryDate, o.DeliveryDays, o.DelayDays,
       o.DeliveryPerformance, s.PurchaseSequence,
       DATEDIFF(day,s.PriorPurchaseDateTime,o.PurchaseDateTime) AS DaysSincePriorPurchase,
       CASE WHEN s.PurchaseSequence IS NULL THEN 'Not commercial' WHEN s.PurchaseSequence=1 THEN 'First purchase' ELSE 'Repeat purchase' END AS CustomerOrderType,
       DATEFROMPARTS(YEAR(s.FirstPurchaseDateTime),MONTH(s.FirstPurchaseDateTime),1) AS CohortMonth, o.MerchandiseValue, o.FreightValue,
       o.GrossOrderValue, o.ItemQuantity, o.FreightRatio, o.PaymentValue,
       CONVERT(bit,CASE WHEN o.OrderStatus NOT IN ('canceled','unavailable') THEN 1 ELSE 0 END) AS IsSalesOrder,
       CONVERT(bit,CASE WHEN o.OrderStatus='delivered' THEN 1 ELSE 0 END) AS IsDeliveredOrder,
       CONVERT(bit,CASE WHEN o.OrderStatus='canceled' THEN 1 ELSE 0 END) AS IsCancelledOrder,
       r.AverageReviewScore, r.ReviewScoreSum, r.ReviewCount, COALESCE(r.HasReviewComment,0) AS HasReviewComment
FROM core.[Order] o
LEFT JOIN CommercialSequence s ON s.OrderKey=o.OrderKey
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
       AVG(CASE WHEN o.IsDeliveredOrder=1 AND o.DeliveryPerformance IN ('On Time','Late') THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeDeliveryRate,
       SUM(o.ReviewScoreSum)*1.0/NULLIF(SUM(o.ReviewCount),0) AS AverageReviewScore
FROM rpt.vOrderAnalytics o
JOIN core.[Date] d ON d.DateKey=o.PurchaseDateKey
GROUP BY d.[Date], d.DateKey, d.CalendarYear, d.CalendarQuarter, d.YearMonth, d.YearMonthSort;
GO

CREATE OR ALTER VIEW rpt.vCustomer360
AS
WITH ReferenceDate AS (
    SELECT MAX(CONVERT(date,PurchaseDateTime)) AS MaxPurchaseDate FROM core.[Order]
), CommercialOrders AS (
    SELECT CustomerKey,PurchaseDateTime,
           LAG(PurchaseDateTime) OVER (PARTITION BY CustomerKey ORDER BY PurchaseDateTime,OrderId) AS PriorPurchaseDateTime
    FROM core.[Order] WHERE OrderStatus NOT IN ('canceled','unavailable')
), CommercialStats AS (
    SELECT CustomerKey,MIN(PurchaseDateTime) AS FirstPurchaseDateTime,MAX(PurchaseDateTime) AS LastPurchaseDateTime,
           COUNT(*) AS PurchaseCount,
           AVG(DATEDIFF(minute,PriorPurchaseDateTime,PurchaseDateTime)/1440.0) AS AverageDaysBetweenPurchases
    FROM CommercialOrders GROUP BY CustomerKey
), CustomerValue AS (
    SELECT c.CustomerKey,
           DATEDIFF(day,MAX(CASE WHEN o.OrderStatus='delivered' THEN CONVERT(date,o.PurchaseDateTime) END),(SELECT MaxPurchaseDate FROM ReferenceDate)) AS RecencyDays,
           COUNT(DISTINCT CASE WHEN o.OrderStatus='delivered' THEN o.OrderKey END) AS CompletedPurchaseCount,
           SUM(CASE WHEN o.OrderStatus='delivered' THEN o.MerchandiseValue ELSE 0 END) AS CustomerSales,
           SUM(CASE WHEN o.OrderStatus='delivered' THEN o.FreightValue ELSE 0 END) AS CustomerFreight,
           AVG(CASE WHEN o.OrderStatus='delivered' THEN o.DeliveryDays END) AS AverageDeliveryDays,
           AVG(CASE WHEN o.OrderStatus='delivered' AND o.DeliveryPerformance IN ('On Time','Late') THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeRate,
           SUM(r.ReviewScoreSum)*1.0/NULLIF(SUM(r.ReviewCount),0) AS AverageReviewScore
    FROM core.Customer c
    LEFT JOIN core.[Order] o ON o.CustomerKey=c.CustomerKey
    LEFT JOIN (
        SELECT OrderKey,SUM(CONVERT(bigint,ReviewScore)) AS ReviewScoreSum, COUNT_BIG(*) AS ReviewCount
        FROM core.Review GROUP BY OrderKey
    ) r ON r.OrderKey=o.OrderKey
    GROUP BY c.CustomerKey
)
SELECT c.CustomerKey, c.CustomerUniqueId, c.HomeGeographyKey, g.City, g.StateCode,
       p.FirstPurchaseDateTime, p.LastPurchaseDateTime,
       DATEFROMPARTS(YEAR(p.FirstPurchaseDateTime),MONTH(p.FirstPurchaseDateTime),1) AS CohortMonth,
       COALESCE(p.PurchaseCount,0) AS PurchaseCount, p.AverageDaysBetweenPurchases,
       CASE WHEN p.PurchaseCount IS NULL THEN 'No commercial purchase' WHEN p.PurchaseCount=1 THEN 'One purchase' ELSE 'Repeat' END AS CustomerType,
       s.RecencyDays, s.CompletedPurchaseCount, s.CustomerSales, s.CustomerFreight,
       s.AverageDeliveryDays, s.OnTimeRate, s.AverageReviewScore,
       CASE WHEN s.CompletedPurchaseCount=0 THEN 'No completed purchases'
            WHEN s.CompletedPurchaseCount=1 THEN '1 completed purchase'
            WHEN s.CompletedPurchaseCount=2 THEN '2 completed purchases'
            ELSE '3+ completed purchases' END AS CompletedPurchaseGroup
FROM core.Customer c
JOIN CustomerValue s ON s.CustomerKey=c.CustomerKey
LEFT JOIN CommercialStats p ON p.CustomerKey=c.CustomerKey
LEFT JOIN core.Geography g ON g.GeographyKey=c.HomeGeographyKey;
GO

CREATE OR ALTER VIEW rpt.vCohortRetention
AS
WITH FirstCommercialPurchase AS (
    SELECT CustomerKey,DATEFROMPARTS(YEAR(MIN(PurchaseDateTime)),MONTH(MIN(PurchaseDateTime)),1) AS CohortMonth
    FROM core.[Order] WHERE OrderStatus NOT IN ('canceled','unavailable') GROUP BY CustomerKey
), Activity AS (
    SELECT DISTINCT o.CustomerKey, c.CohortMonth,
           DATEFROMPARTS(YEAR(o.PurchaseDateTime),MONTH(o.PurchaseDateTime),1) AS ActivityMonth,
           DATEDIFF(month,c.CohortMonth,DATEFROMPARTS(YEAR(o.PurchaseDateTime),MONTH(o.PurchaseDateTime),1)) AS MonthsSinceFirstPurchase
    FROM core.[Order] o
    JOIN FirstCommercialPurchase c ON c.CustomerKey=o.CustomerKey
    WHERE o.OrderStatus NOT IN ('canceled','unavailable')
), CohortSize AS (
    SELECT CohortMonth,COUNT(DISTINCT CustomerKey) AS CohortCustomers
    FROM Activity WHERE MonthsSinceFirstPurchase=0 GROUP BY CohortMonth
), ObservedBounds AS (
    SELECT DATEADD(month,1,DATEFROMPARTS(YEAR(MIN(PurchaseDateTime)),MONTH(MIN(PurchaseDateTime)),1)) AS FirstFullMonth,
           DATEFROMPARTS(YEAR(MAX(PurchaseDateTime)),MONTH(MAX(PurchaseDateTime)),1) AS LastObservedMonth
    FROM core.[Order] WHERE OrderStatus NOT IN ('canceled','unavailable')
), MonthCalendar AS (
    SELECT DISTINCT DATEFROMPARTS(CalendarYear,MonthNumber,1) AS ActivityMonth
    FROM core.[Date]
), EligibleGrid AS (
    SELECT c.CohortMonth,m.ActivityMonth,
           DATEDIFF(month,c.CohortMonth,m.ActivityMonth) AS MonthsSinceFirstPurchase,
           c.CohortCustomers
    FROM CohortSize c
    JOIN MonthCalendar m ON m.ActivityMonth>=c.CohortMonth
    CROSS JOIN ObservedBounds b
    WHERE c.CohortMonth>=b.FirstFullMonth AND m.ActivityMonth<b.LastObservedMonth
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
WITH ProductSales AS (
SELECT i.ProductKey,
       COUNT_BIG(*) AS Units,COUNT(DISTINCT i.OrderKey) AS Orders,
       COUNT(DISTINCT o.CustomerKey) AS Customers,
       SUM(i.ItemPrice) AS SalesGMV,SUM(i.FreightValue) AS FreightCost,
       AVG(i.ItemPrice) AS AverageSellingPrice
FROM core.OrderItem i
JOIN core.[Order] o ON o.OrderKey=i.OrderKey
WHERE o.OrderStatus NOT IN ('canceled','unavailable')
GROUP BY i.ProductKey
), ProductReviews AS (
    SELECT p.ProductKey, AVG(CONVERT(decimal(9,4),r.ReviewScore)) AS AverageReviewScore
    FROM (SELECT DISTINCT ProductKey,OrderKey FROM core.OrderItem) p
    JOIN core.[Order] o ON o.OrderKey=p.OrderKey
    JOIN core.Review r ON r.OrderKey=p.OrderKey
    WHERE o.OrderStatus NOT IN ('canceled','unavailable')
    GROUP BY p.ProductKey
)
SELECT p.ProductKey,p.ProductId,p.CategoryNameEnglish,p.CategoryNamePortuguese,
       s.Units,s.Orders,s.Customers,s.SalesGMV,s.FreightCost,s.AverageSellingPrice,r.AverageReviewScore
FROM ProductSales s JOIN core.Product p ON p.ProductKey=s.ProductKey
LEFT JOIN ProductReviews r ON r.ProductKey=s.ProductKey;
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
WITH SellerSales AS (
    SELECT i.SellerKey,COUNT_BIG(*) AS Units,COUNT(DISTINCT i.OrderKey) AS Orders,
           COUNT(DISTINCT o.CustomerKey) AS Customers,SUM(i.ItemPrice) AS SalesGMV,SUM(i.FreightValue) AS FreightCost
    FROM core.OrderItem i JOIN core.[Order] o ON o.OrderKey=i.OrderKey
    WHERE o.OrderStatus NOT IN ('canceled','unavailable') GROUP BY i.SellerKey
), SellerExperience AS (
    SELECT so.SellerKey,
           AVG(CASE WHEN o.OrderStatus='delivered' THEN o.DeliveryDays END) AS AverageDeliveryDays,
           AVG(CASE WHEN o.OrderStatus='delivered' AND o.DeliveryPerformance IN ('On Time','Late') THEN CASE WHEN o.DeliveryPerformance='On Time' THEN 1.0 ELSE 0 END END) AS OnTimeRate,
           SUM(r.ReviewScoreSum)*1.0/NULLIF(SUM(r.ReviewCount),0) AS AverageReviewScore
    FROM (SELECT DISTINCT SellerKey,OrderKey FROM core.OrderItem) so
    JOIN core.[Order] o ON o.OrderKey=so.OrderKey
    LEFT JOIN (SELECT OrderKey,SUM(CONVERT(bigint,ReviewScore)) AS ReviewScoreSum,COUNT_BIG(*) AS ReviewCount FROM core.Review GROUP BY OrderKey) r ON r.OrderKey=o.OrderKey
    WHERE o.OrderStatus NOT IN ('canceled','unavailable')
    GROUP BY so.SellerKey
)
SELECT s.SellerKey,s.SellerId,g.City,g.StateCode,g.Latitude,g.Longitude,
       COALESCE(v.Units,0) AS Units,COALESCE(v.Orders,0) AS Orders,COALESCE(v.Customers,0) AS Customers,
       COALESCE(v.SalesGMV,0) AS SalesGMV,COALESCE(v.FreightCost,0) AS FreightCost,
       e.AverageDeliveryDays,e.OnTimeRate,e.AverageReviewScore
FROM core.Seller s
LEFT JOIN core.Geography g ON g.GeographyKey=s.GeographyKey
LEFT JOIN SellerSales v ON v.SellerKey=s.SellerKey
LEFT JOIN SellerExperience e ON e.SellerKey=s.SellerKey;
GO

CREATE OR ALTER VIEW rpt.vGeographicPerformance
AS
SELECT g.GeographyKey,g.ZipCodePrefix,g.City,g.StateCode,g.Latitude,g.Longitude,
       COUNT_BIG(*) AS Orders,COUNT(DISTINCT o.CustomerKey) AS Customers,
       SUM(CASE WHEN o.OrderStatus NOT IN ('canceled','unavailable') THEN o.MerchandiseValue ELSE 0 END) AS SalesGMV,
       AVG(CASE WHEN o.OrderStatus='delivered' THEN o.DeliveryDays END) AS AverageDeliveryDays,
       AVG(CASE WHEN o.OrderStatus='delivered' AND o.DeliveryPerformance IN ('On Time','Late') THEN CONVERT(decimal(9,4),CASE WHEN o.DeliveryPerformance='On Time' THEN 1 ELSE 0 END) END) AS OnTimeRate,
       SUM(r.ReviewScoreSum)*1.0/NULLIF(SUM(r.ReviewCount),0) AS AverageReviewScore
FROM core.Geography g
JOIN core.[Order] o ON o.CustomerGeographyKey=g.GeographyKey
LEFT JOIN (SELECT OrderKey,SUM(CONVERT(bigint,ReviewScore)) AS ReviewScoreSum,COUNT_BIG(*) AS ReviewCount FROM core.Review GROUP BY OrderKey) r ON r.OrderKey=o.OrderKey
GROUP BY g.GeographyKey,g.ZipCodePrefix,g.City,g.StateCode,g.Latitude,g.Longitude;
GO
