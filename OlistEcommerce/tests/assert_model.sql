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
IF EXISTS (SELECT 1 FROM rpt.vCustomer360 WHERE CompletedPurchaseCount=1 AND RfmSegment IN ('Champions','Loyal Customers','At Risk'))
    THROW 51006, 'RFM logic labels a one-time completed purchaser as a repeat-oriented segment.', 1;
IF EXISTS (SELECT 1 FROM rpt.vCohortRetention WHERE MonthsSinceFirstPurchase=0 AND RetentionRate<>1)
    THROW 51007, 'Cohort Month 0 retention is not 100%.', 1;

SELECT 'PASS' AS TestStatus,
       (SELECT COUNT(*) FROM core.Customer) AS Customers,
       (SELECT COUNT(*) FROM core.[Order]) AS Orders,
       (SELECT COUNT(*) FROM core.OrderItem) AS OrderItems,
       (SELECT COUNT(*) FROM etl.RejectedRecord WHERE LoadBatchId=(SELECT MAX(LoadBatchId) FROM etl.LoadBatch)) AS LatestBatchRejectedRows;

