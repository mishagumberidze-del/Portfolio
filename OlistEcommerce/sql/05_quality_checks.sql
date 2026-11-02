SET NOCOUNT ON;

DECLARE @LoadBatchId int = $(LoadBatchId);

INSERT etl.DataQualityResult (LoadBatchId,CheckName,Severity,FailedRows)
SELECT @LoadBatchId,'Core order count equals valid staging orders','ERROR',
       ABS((SELECT COUNT_BIG(*) FROM core.[Order])-(SELECT COUNT_BIG(*) FROM stg.Orders WHERE NULLIF(order_id,'') IS NOT NULL AND TRY_CONVERT(datetime2(0),order_purchase_timestamp) IS NOT NULL))
UNION ALL
SELECT @LoadBatchId,'Customer grain equals distinct customer_unique_id','ERROR',
       ABS((SELECT COUNT_BIG(*) FROM core.Customer)-(SELECT COUNT_BIG(DISTINCT c.customer_unique_id) FROM stg.Customers c JOIN stg.Orders o ON o.customer_id=c.customer_id WHERE NULLIF(c.customer_unique_id,'') IS NOT NULL AND TRY_CONVERT(datetime2(0),o.order_purchase_timestamp) IS NOT NULL))
UNION ALL
SELECT @LoadBatchId,'Duplicate natural order IDs','ERROR',COUNT_BIG(*)
FROM (SELECT OrderId FROM core.[Order] GROUP BY OrderId HAVING COUNT(*)>1) d
UNION ALL
SELECT @LoadBatchId,'Duplicate order item business keys','ERROR',COUNT_BIG(*)
FROM (SELECT OrderKey,OrderItemNumber FROM core.OrderItem GROUP BY OrderKey,OrderItemNumber HAVING COUNT(*)>1) d
UNION ALL
SELECT @LoadBatchId,'Negative item prices or freight','ERROR',COUNT_BIG(*) FROM core.OrderItem WHERE ItemPrice<0 OR FreightValue<0
UNION ALL
SELECT @LoadBatchId,'Delivered orders missing delivery timestamp','WARNING',COUNT_BIG(*) FROM core.[Order] WHERE OrderStatus='delivered' AND DeliveredCustomerDateTime IS NULL
UNION ALL
SELECT @LoadBatchId,'Order payment differs from gross order value by more than 0.05','WARNING',COUNT_BIG(*)
FROM core.[Order] WHERE PaymentValue>0 AND ABS(PaymentValue-GrossOrderValue)>0.05
UNION ALL
SELECT @LoadBatchId,'Products with unknown English category','WARNING',COUNT_BIG(*) FROM core.Product WHERE CategoryNameEnglish='Unknown'
UNION ALL
SELECT @LoadBatchId,'Geographies without coordinates','WARNING',COUNT_BIG(*) FROM core.Geography WHERE Latitude IS NULL OR Longitude IS NULL
UNION ALL
SELECT @LoadBatchId,'Rejected records in current load','WARNING',COUNT_BIG(*) FROM etl.RejectedRecord WHERE LoadBatchId=@LoadBatchId;
