SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> 'Portfolio_Olist' THROW 51000, 'Run assertions in Portfolio_Olist.', 1;
IF EXISTS (SELECT 1 FROM etl.DataQualityResult WHERE Severity='ERROR' AND FailedRows>0)
    THROW 51001, 'One or more ERROR-level data-quality checks failed.', 1;
IF (SELECT COUNT(*) FROM core.[Order]) <> (SELECT COUNT(*) FROM stg.Orders)
    THROW 51002, 'Core order count does not match staging.', 1;
IF (SELECT COUNT(*) FROM core.Customer) <> (SELECT COUNT(DISTINCT customer_unique_id) FROM stg.Customers)
    THROW 51003, 'Customer grain is not customer_unique_id.', 1;
IF EXISTS (SELECT OrderId FROM core.[Order] GROUP BY OrderId HAVING COUNT(*)>1)
    THROW 51004, 'Duplicate order natural keys found.', 1;
IF EXISTS (SELECT OrderKey,OrderItemNumber FROM core.OrderItem GROUP BY OrderKey,OrderItemNumber HAVING COUNT(*)>1)
    THROW 51005, 'Duplicate order-item business keys found.', 1;
-- Materialize the sequence once. Pushing the null/OR assertions through the
-- window-function view can produce repeated nested scans on SQL Express.
SELECT OrderKey,CustomerKey,IsSalesOrder,PurchaseSequence INTO #AuditOrders
FROM rpt.vOrderAnalytics;
IF EXISTS (SELECT 1 FROM rpt.vCustomer360 WHERE CompletedPurchaseGroup <> CASE WHEN CompletedPurchaseCount=0 THEN 'No completed purchases' WHEN CompletedPurchaseCount=1 THEN '1 completed purchase' WHEN CompletedPurchaseCount=2 THEN '2 completed purchases' ELSE '3+ completed purchases' END)
    THROW 51006, 'Completed-purchase group disagrees with its factual purchase count.', 1;
IF EXISTS (SELECT 1 FROM rpt.vCohortRetention WHERE MonthsSinceFirstPurchase=0 AND RetentionRate<>1)
    THROW 51007, 'Cohort Month 0 retention is not 100%.', 1;
IF EXISTS (SELECT 1 FROM #AuditOrders WHERE (IsSalesOrder=0 AND PurchaseSequence IS NOT NULL) OR (IsSalesOrder=1 AND PurchaseSequence IS NULL))
    THROW 51008, 'Purchase sequence includes noncommercial orders or excludes commercial orders.', 1;
IF EXISTS (SELECT CustomerKey FROM #AuditOrders WHERE IsSalesOrder=1 GROUP BY CustomerKey HAVING MIN(PurchaseSequence)<>1 OR MAX(PurchaseSequence)<>COUNT(*))
    THROW 51009, 'Commercial purchase sequence does not reconcile.', 1;
IF EXISTS (SELECT 1 FROM rpt.vCohortRetention WHERE ActiveCustomers>CohortCustomers OR MonthsSinceFirstPurchase<0)
    THROW 51010, 'Invalid retention numerator or cohort age.', 1;
IF EXISTS (SELECT 1 FROM rpt.vCohortRetention WHERE ActivityMonth>=
    (SELECT DATEFROMPARTS(YEAR(MAX(PurchaseDateTime)),MONTH(MAX(PurchaseDateTime)),1) FROM core.[Order] WHERE OrderStatus NOT IN ('canceled','unavailable')))
    THROW 51011, 'Partial final commercial month entered retention.', 1;

/* Independent raw-order reference: seller rates must not give multi-item orders extra weight. */
IF EXISTS (
    SELECT 1 FROM rpt.vSellerPerformance s
    JOIN (
        SELECT so.SellerKey,AVG(CASE WHEN o.OrderStatus='delivered' AND o.DeliveredCustomerDateTime IS NOT NULL AND o.EstimatedDeliveryDate IS NOT NULL
                    THEN CASE WHEN CONVERT(date,o.DeliveredCustomerDateTime)<=o.EstimatedDeliveryDate THEN 1.0 ELSE 0 END END) Rate
        FROM (SELECT DISTINCT SellerKey,OrderKey FROM core.OrderItem) so
        JOIN core.[Order] o ON o.OrderKey=so.OrderKey
        WHERE o.OrderStatus NOT IN ('canceled','unavailable') GROUP BY so.SellerKey
    ) x ON x.SellerKey=s.SellerKey WHERE ABS(s.OnTimeRate-x.Rate)>0.00001
) THROW 51012, 'Seller on-time rate is not weighted by eligible distinct orders.', 1;

SELECT 'PASS' AS TestStatus,
       (SELECT COUNT(*) FROM core.Customer) AS Customers,
       (SELECT COUNT(*) FROM core.[Order]) AS Orders,
       (SELECT COUNT(*) FROM core.OrderItem) AS OrderItems,
       (SELECT COUNT(*) FROM etl.RejectedRecord WHERE LoadBatchId=(SELECT MAX(LoadBatchId) FROM etl.LoadBatch)) AS LatestBatchRejectedRows;

