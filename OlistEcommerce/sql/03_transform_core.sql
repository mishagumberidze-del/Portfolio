SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LoadBatchId int = $(LoadBatchId);

BEGIN TRY
    BEGIN TRANSACTION;

    /* Invalid records are retained as auditable rejection metadata. */
    INSERT etl.RejectedRecord (LoadBatchId, SourceTable, SourceKey, ReasonCode, ReasonDetail)
    SELECT @LoadBatchId, 'stg.Orders', order_id, 'INVALID_ORDER',
           'Missing/invalid order id, customer id, or purchase timestamp'
    FROM stg.Orders
    WHERE NULLIF(order_id,'') IS NULL
       OR NULLIF(customer_id,'') IS NULL
       OR TRY_CONVERT(datetime2(0), NULLIF(order_purchase_timestamp,'')) IS NULL;

    INSERT etl.RejectedRecord (LoadBatchId, SourceTable, SourceKey, ReasonCode, ReasonDetail)
    SELECT @LoadBatchId, 'stg.OrderItems', CONCAT(order_id, ':', order_item_id), 'INVALID_ORDER_ITEM',
           'Invalid key, price, freight, product, seller, or orphan order'
    FROM stg.OrderItems i
    WHERE NULLIF(i.order_id,'') IS NULL
       OR TRY_CONVERT(smallint, NULLIF(i.order_item_id,'')) IS NULL
       OR NULLIF(i.product_id,'') IS NULL
       OR NULLIF(i.seller_id,'') IS NULL
       OR TRY_CONVERT(decimal(12,2), NULLIF(i.price,'')) IS NULL
       OR TRY_CONVERT(decimal(12,2), NULLIF(i.freight_value,'')) IS NULL
       OR NOT EXISTS (SELECT 1 FROM stg.Orders o WHERE o.order_id=i.order_id);

    INSERT etl.RejectedRecord (LoadBatchId, SourceTable, SourceKey, ReasonCode, ReasonDetail)
    SELECT @LoadBatchId, 'stg.Payments', CONCAT(order_id, ':', payment_sequential), 'INVALID_PAYMENT',
           'Invalid key/value or orphan order'
    FROM stg.Payments p
    WHERE NULLIF(p.order_id,'') IS NULL
       OR TRY_CONVERT(smallint, NULLIF(p.payment_sequential,'')) IS NULL
       OR TRY_CONVERT(decimal(12,2), NULLIF(p.payment_value,'')) IS NULL
       OR NOT EXISTS (SELECT 1 FROM stg.Orders o WHERE o.order_id=p.order_id);

    INSERT etl.RejectedRecord (LoadBatchId, SourceTable, SourceKey, ReasonCode, ReasonDetail)
    SELECT @LoadBatchId, 'stg.Reviews', CONCAT(review_id, ':', order_id), 'INVALID_REVIEW',
           'Invalid review key/score or orphan order'
    FROM stg.Reviews r
    WHERE NULLIF(r.review_id,'') IS NULL
       OR NULLIF(r.order_id,'') IS NULL
       OR TRY_CONVERT(tinyint, NULLIF(r.review_score,'')) NOT BETWEEN 1 AND 5
       OR NOT EXISTS (SELECT 1 FROM stg.Orders o WHERE o.order_id=r.order_id);

    /* Rebuild all analytical tables atomically: reruns cannot duplicate rows. */
    DELETE FROM core.Review;
    DELETE FROM core.Payment;
    DELETE FROM core.OrderItem;
    DELETE FROM core.[Order];
    DELETE FROM core.Customer;
    DELETE FROM core.Seller;
    DELETE FROM core.Product;
    DELETE FROM core.[Date];
    DELETE FROM core.Geography;

    DBCC CHECKIDENT ('core.Review', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.Payment', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.OrderItem', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.[Order]', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.Customer', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.Seller', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.Product', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('core.Geography', RESEED, 0) WITH NO_INFOMSGS;

    ;WITH ValidGeo AS (
        SELECT
            TRY_CONVERT(int, geolocation_zip_code_prefix) AS ZipCodePrefix,
            TRY_CONVERT(decimal(10,7), geolocation_lat) AS Latitude,
            TRY_CONVERT(decimal(10,7), geolocation_lng) AS Longitude,
            NULLIF(TRIM(geolocation_city),'') AS City,
            UPPER(NULLIF(TRIM(geolocation_state),'')) AS StateCode
        FROM stg.Geolocation
        WHERE TRY_CONVERT(int, geolocation_zip_code_prefix) IS NOT NULL
          AND TRY_CONVERT(decimal(10,7), geolocation_lat) BETWEEN -90 AND 90
          AND TRY_CONVERT(decimal(10,7), geolocation_lng) BETWEEN -180 AND 180
    ), LocationFrequency AS (
        SELECT ZipCodePrefix, City, StateCode, COUNT_BIG(*) AS LocationCount
        FROM ValidGeo
        GROUP BY ZipCodePrefix, City, StateCode
    ), RankedLocation AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY ZipCodePrefix ORDER BY LocationCount DESC, StateCode, City) AS rn
        FROM LocationFrequency
    ), GeoAverage AS (
        SELECT ZipCodePrefix, AVG(Latitude) AS Latitude, AVG(Longitude) AS Longitude, COUNT(*) AS PointCount
        FROM ValidGeo
        GROUP BY ZipCodePrefix
    )
    INSERT core.Geography (ZipCodePrefix, City, StateCode, Latitude, Longitude, SourcePointCount)
    SELECT a.ZipCodePrefix, r.City, r.StateCode, a.Latitude, a.Longitude, a.PointCount
    FROM GeoAverage a
    JOIN RankedLocation r ON r.ZipCodePrefix=a.ZipCodePrefix AND r.rn=1;

    ;WITH SourceLocation AS (
        SELECT TRY_CONVERT(int, customer_zip_code_prefix) AS ZipCodePrefix,
               NULLIF(TRIM(customer_city),'') AS City,
               UPPER(NULLIF(TRIM(customer_state),'')) AS StateCode
        FROM stg.Customers
        UNION ALL
        SELECT TRY_CONVERT(int, seller_zip_code_prefix), NULLIF(TRIM(seller_city),''), UPPER(NULLIF(TRIM(seller_state),''))
        FROM stg.Sellers
    ), Ranked AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY ZipCodePrefix ORDER BY StateCode, City) AS rn
        FROM SourceLocation
        WHERE ZipCodePrefix IS NOT NULL
    )
    INSERT core.Geography (ZipCodePrefix, City, StateCode, Latitude, Longitude, SourcePointCount)
    SELECT s.ZipCodePrefix, s.City, s.StateCode, NULL, NULL, 0
    FROM Ranked s
    WHERE s.rn=1 AND NOT EXISTS (SELECT 1 FROM core.Geography g WHERE g.ZipCodePrefix=s.ZipCodePrefix);

    DECLARE @StartDate date = (
        SELECT MIN(CONVERT(date, TRY_CONVERT(datetime2(0), order_purchase_timestamp))) FROM stg.Orders
        WHERE TRY_CONVERT(datetime2(0), order_purchase_timestamp) IS NOT NULL
    );
    DECLARE @EndDate date = (
        SELECT MAX(d) FROM (
            SELECT MAX(CONVERT(date, TRY_CONVERT(datetime2(0), order_purchase_timestamp))) d FROM stg.Orders
            UNION ALL SELECT MAX(TRY_CONVERT(date, NULLIF(order_delivered_customer_date,''))) FROM stg.Orders
            UNION ALL SELECT MAX(TRY_CONVERT(date, NULLIF(order_estimated_delivery_date,''))) FROM stg.Orders
        ) dates
    );

    ;WITH DateSeries AS (
        SELECT @StartDate AS d
        UNION ALL
        SELECT DATEADD(day,1,d) FROM DateSeries WHERE d < @EndDate
    )
    INSERT core.[Date] (DateKey, [Date], CalendarYear, CalendarQuarter, QuarterLabel, MonthNumber, MonthName, YearMonth, YearMonthSort, WeekOfYear, DayOfMonth, DayName, IsWeekend)
    SELECT CONVERT(int, CONVERT(char(8), d, 112)), d, YEAR(d), DATEPART(quarter,d),
           CONCAT(YEAR(d), '-Q', DATEPART(quarter,d)), MONTH(d), DATENAME(month,d),
           CONVERT(char(7),d,126), YEAR(d)*100+MONTH(d), DATEPART(iso_week,d), DAY(d), DATENAME(weekday,d),
           CASE WHEN DATEDIFF(day,'19000101',d)%7 IN (5,6) THEN 1 ELSE 0 END
    FROM DateSeries OPTION (MAXRECURSION 0);

    INSERT core.Product (
        ProductId, CategoryNamePortuguese, CategoryNameEnglish, ProductNameLength,
        ProductDescriptionLength, ProductPhotoQuantity, ProductWeightGrams,
        ProductLengthCm, ProductHeightCm, ProductWidthCm
    )
    SELECT CONVERT(char(32), p.product_id), NULLIF(p.product_category_name,''),
           COALESCE(NULLIF(REPLACE(t.product_category_name_english,'_',' '),''), 'Unknown'),
           TRY_CONVERT(smallint,NULLIF(p.product_name_lenght,'')),
           TRY_CONVERT(int,NULLIF(p.product_description_lenght,'')),
           TRY_CONVERT(smallint,NULLIF(p.product_photos_qty,'')),
           TRY_CONVERT(decimal(12,2),NULLIF(p.product_weight_g,'')),
           TRY_CONVERT(decimal(10,2),NULLIF(p.product_length_cm,'')),
           TRY_CONVERT(decimal(10,2),NULLIF(p.product_height_cm,'')),
           TRY_CONVERT(decimal(10,2),NULLIF(p.product_width_cm,''))
    FROM stg.Products p
    LEFT JOIN stg.CategoryTranslation t ON t.product_category_name=p.product_category_name
    WHERE NULLIF(p.product_id,'') IS NOT NULL;

    INSERT core.Seller (SellerId, GeographyKey)
    SELECT CONVERT(char(32),s.seller_id), g.GeographyKey
    FROM stg.Sellers s
    LEFT JOIN core.Geography g ON g.ZipCodePrefix=TRY_CONVERT(int,s.seller_zip_code_prefix)
    WHERE NULLIF(s.seller_id,'') IS NOT NULL;

    ;WITH ValidCustomerOrders AS (
        SELECT c.customer_unique_id, c.customer_zip_code_prefix,
               TRY_CONVERT(datetime2(0),o.order_purchase_timestamp) AS PurchaseDateTime,
               LAG(TRY_CONVERT(datetime2(0),o.order_purchase_timestamp)) OVER (
                   PARTITION BY c.customer_unique_id ORDER BY TRY_CONVERT(datetime2(0),o.order_purchase_timestamp), o.order_id
               ) AS PriorPurchaseDateTime,
               ROW_NUMBER() OVER (
                   PARTITION BY c.customer_unique_id ORDER BY TRY_CONVERT(datetime2(0),o.order_purchase_timestamp) DESC, o.order_id DESC
               ) AS LatestRow
        FROM stg.Customers c
        JOIN stg.Orders o ON o.customer_id=c.customer_id
        WHERE NULLIF(c.customer_unique_id,'') IS NOT NULL
          AND TRY_CONVERT(datetime2(0),o.order_purchase_timestamp) IS NOT NULL
    ), CustomerStats AS (
        SELECT customer_unique_id, MIN(PurchaseDateTime) AS FirstPurchaseDateTime,
               MAX(PurchaseDateTime) AS LastPurchaseDateTime, COUNT(*) AS PurchaseCount,
               CONVERT(decimal(12,2), AVG(CONVERT(decimal(12,2), CASE WHEN PriorPurchaseDateTime IS NULL THEN NULL ELSE DATEDIFF(minute,PriorPurchaseDateTime,PurchaseDateTime)/1440.0 END))) AS AverageDaysBetweenPurchases,
               MAX(CASE WHEN LatestRow=1 THEN TRY_CONVERT(int,customer_zip_code_prefix) END) AS HomeZipCodePrefix
        FROM ValidCustomerOrders
        GROUP BY customer_unique_id
    )
    INSERT core.Customer (CustomerUniqueId, HomeGeographyKey, FirstPurchaseDateTime, LastPurchaseDateTime, CohortMonth, PurchaseCount, CustomerPurchaseFrequency, AverageDaysBetweenPurchases, CustomerType)
    SELECT CONVERT(char(32),s.customer_unique_id), g.GeographyKey, s.FirstPurchaseDateTime, s.LastPurchaseDateTime,
           DATEFROMPARTS(YEAR(s.FirstPurchaseDateTime),MONTH(s.FirstPurchaseDateTime),1), s.PurchaseCount,
           CONVERT(decimal(12,4), s.PurchaseCount * 1.0 / NULLIF(DATEDIFF(month,s.FirstPurchaseDateTime,s.LastPurchaseDateTime)+1,0)),
           s.AverageDaysBetweenPurchases, CASE WHEN s.PurchaseCount>1 THEN 'Repeat' ELSE 'New' END
    FROM CustomerStats s
    LEFT JOIN core.Geography g ON g.ZipCodePrefix=s.HomeZipCodePrefix;

    ;WITH BaseOrders AS (
        SELECT o.order_id, o.customer_id, c.customer_unique_id,
               TRY_CONVERT(int,c.customer_zip_code_prefix) AS CustomerZipCodePrefix,
               LOWER(TRIM(o.order_status)) AS OrderStatus,
               TRY_CONVERT(datetime2(0),o.order_purchase_timestamp) AS PurchaseDateTime,
               TRY_CONVERT(datetime2(0),NULLIF(o.order_approved_at,'')) AS ApprovedDateTime,
               TRY_CONVERT(datetime2(0),NULLIF(o.order_delivered_carrier_date,'')) AS DeliveredCarrierDateTime,
               TRY_CONVERT(datetime2(0),NULLIF(o.order_delivered_customer_date,'')) AS DeliveredCustomerDateTime,
               TRY_CONVERT(date,NULLIF(o.order_estimated_delivery_date,'')) AS EstimatedDeliveryDate
        FROM stg.Orders o
        JOIN stg.Customers c ON c.customer_id=o.customer_id
        WHERE NULLIF(o.order_id,'') IS NOT NULL
          AND TRY_CONVERT(datetime2(0),o.order_purchase_timestamp) IS NOT NULL
    ), Sequenced AS (
        SELECT b.*,
               ROW_NUMBER() OVER (PARTITION BY b.customer_unique_id ORDER BY b.PurchaseDateTime,b.order_id) AS PurchaseSequence,
               LAG(b.PurchaseDateTime) OVER (PARTITION BY b.customer_unique_id ORDER BY b.PurchaseDateTime,b.order_id) AS PriorPurchaseDateTime,
               MIN(b.PurchaseDateTime) OVER (PARTITION BY b.customer_unique_id) AS FirstPurchaseDateTime
        FROM BaseOrders b
    ), ItemAgg AS (
        SELECT order_id, SUM(TRY_CONVERT(decimal(14,2),price)) AS MerchandiseValue,
               SUM(TRY_CONVERT(decimal(14,2),freight_value)) AS FreightValue, COUNT(*) AS ItemQuantity
        FROM stg.OrderItems
        WHERE TRY_CONVERT(decimal(14,2),price) IS NOT NULL AND TRY_CONVERT(decimal(14,2),freight_value) IS NOT NULL
        GROUP BY order_id
    ), PaymentAgg AS (
        SELECT order_id, SUM(TRY_CONVERT(decimal(14,2),payment_value)) AS PaymentValue
        FROM stg.Payments WHERE TRY_CONVERT(decimal(14,2),payment_value) IS NOT NULL GROUP BY order_id
    )
    INSERT core.[Order] (
        OrderId, OlistCustomerId, CustomerKey, CustomerGeographyKey, PurchaseDateKey,
        OrderStatus, PurchaseDateTime, ApprovedDateTime, DeliveredCarrierDateTime,
        DeliveredCustomerDateTime, EstimatedDeliveryDate, DeliveryDays, DelayDays,
        DeliveryPerformance, PurchaseSequence, DaysSincePriorPurchase, CustomerOrderType,
        CohortMonth, MerchandiseValue, FreightValue, GrossOrderValue, ItemQuantity,
        FreightRatio, PaymentValue
    )
    SELECT CONVERT(char(32),s.order_id), CONVERT(char(32),s.customer_id), c.CustomerKey, g.GeographyKey,
           CONVERT(int,CONVERT(char(8),CONVERT(date,s.PurchaseDateTime),112)), s.OrderStatus,
           s.PurchaseDateTime, s.ApprovedDateTime, s.DeliveredCarrierDateTime,
           s.DeliveredCustomerDateTime, s.EstimatedDeliveryDate,
           CONVERT(decimal(10,2),CASE WHEN s.DeliveredCustomerDateTime IS NULL THEN NULL ELSE DATEDIFF(minute,s.PurchaseDateTime,s.DeliveredCustomerDateTime)/1440.0 END),
           CASE WHEN s.DeliveredCustomerDateTime IS NULL OR s.EstimatedDeliveryDate IS NULL THEN NULL ELSE DATEDIFF(day,s.EstimatedDeliveryDate,CONVERT(date,s.DeliveredCustomerDateTime)) END,
           CASE WHEN s.DeliveredCustomerDateTime IS NULL OR s.EstimatedDeliveryDate IS NULL THEN 'Not Delivered'
                WHEN CONVERT(date,s.DeliveredCustomerDateTime)<=s.EstimatedDeliveryDate THEN 'On Time' ELSE 'Late' END,
           s.PurchaseSequence,
           CASE WHEN s.PriorPurchaseDateTime IS NULL THEN NULL ELSE DATEDIFF(day,s.PriorPurchaseDateTime,s.PurchaseDateTime) END,
           CASE WHEN s.PurchaseSequence=1 THEN 'New' ELSE 'Repeat' END,
           DATEFROMPARTS(YEAR(s.FirstPurchaseDateTime),MONTH(s.FirstPurchaseDateTime),1),
           COALESCE(i.MerchandiseValue,0), COALESCE(i.FreightValue,0), COALESCE(i.MerchandiseValue,0)+COALESCE(i.FreightValue,0), COALESCE(i.ItemQuantity,0),
           CONVERT(decimal(12,6),CASE WHEN COALESCE(i.MerchandiseValue,0)=0 THEN NULL ELSE i.FreightValue/i.MerchandiseValue END),
           COALESCE(p.PaymentValue,0)
    FROM Sequenced s
    JOIN core.Customer c ON c.CustomerUniqueId=s.customer_unique_id
    LEFT JOIN core.Geography g ON g.ZipCodePrefix=s.CustomerZipCodePrefix
    LEFT JOIN ItemAgg i ON i.order_id=s.order_id
    LEFT JOIN PaymentAgg p ON p.order_id=s.order_id;

    INSERT core.OrderItem (OrderKey, OrderItemNumber, ProductKey, SellerKey, ShippingLimitDateTime, ItemPrice, FreightValue)
    SELECT o.OrderKey, TRY_CONVERT(smallint,i.order_item_id), p.ProductKey, s.SellerKey,
           TRY_CONVERT(datetime2(0),NULLIF(i.shipping_limit_date,'')),
           TRY_CONVERT(decimal(12,2),i.price), TRY_CONVERT(decimal(12,2),i.freight_value)
    FROM stg.OrderItems i
    JOIN core.[Order] o ON o.OrderId=i.order_id
    JOIN core.Product p ON p.ProductId=i.product_id
    JOIN core.Seller s ON s.SellerId=i.seller_id
    WHERE TRY_CONVERT(smallint,i.order_item_id) IS NOT NULL
      AND TRY_CONVERT(decimal(12,2),i.price) IS NOT NULL
      AND TRY_CONVERT(decimal(12,2),i.freight_value) IS NOT NULL;

    INSERT core.Payment (OrderKey, PaymentSequence, PaymentType, Installments, PaymentValue)
    SELECT o.OrderKey, TRY_CONVERT(smallint,p.payment_sequential), COALESCE(NULLIF(LOWER(TRIM(p.payment_type)),''),'unknown'),
           TRY_CONVERT(smallint,NULLIF(p.payment_installments,'')), TRY_CONVERT(decimal(12,2),p.payment_value)
    FROM stg.Payments p
    JOIN core.[Order] o ON o.OrderId=p.order_id
    WHERE TRY_CONVERT(smallint,p.payment_sequential) IS NOT NULL
      AND TRY_CONVERT(decimal(12,2),p.payment_value) IS NOT NULL;

    INSERT core.Review (ReviewId, OrderKey, ReviewScore, ReviewScoreGroup, CommentTitle, CommentMessage, ReviewCreationDate, ReviewAnswerDateTime)
    SELECT CONVERT(char(32),r.review_id), o.OrderKey, TRY_CONVERT(tinyint,r.review_score),
           CASE WHEN TRY_CONVERT(tinyint,r.review_score)<=2 THEN 'Detractor'
                WHEN TRY_CONVERT(tinyint,r.review_score)=3 THEN 'Neutral' ELSE 'Promoter' END,
           NULLIF(r.review_comment_title,''), NULLIF(r.review_comment_message,''),
           TRY_CONVERT(date,NULLIF(r.review_creation_date,'')), TRY_CONVERT(datetime2(0),NULLIF(r.review_answer_timestamp,''))
    FROM stg.Reviews r
    JOIN core.[Order] o ON o.OrderId=r.order_id
    WHERE TRY_CONVERT(tinyint,r.review_score) BETWEEN 1 AND 5
      AND NULLIF(r.review_id,'') IS NOT NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
