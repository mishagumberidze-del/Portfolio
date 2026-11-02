SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'stg') EXEC('CREATE SCHEMA stg');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'core') EXEC('CREATE SCHEMA core');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'rpt') EXEC('CREATE SCHEMA rpt');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'etl') EXEC('CREATE SCHEMA etl');
GO

IF OBJECT_ID('etl.LoadBatch', 'U') IS NULL
CREATE TABLE etl.LoadBatch (
    LoadBatchId     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoadBatch PRIMARY KEY,
    StartedAtUtc    datetime2(0) NOT NULL,
    CompletedAtUtc  datetime2(0) NULL,
    Status          varchar(20) NOT NULL,
    SourceRoot      nvarchar(500) NOT NULL,
    PipelineVersion varchar(30) NOT NULL,
    SourceRows      bigint NULL,
    StagedRows      bigint NULL,
    RejectedRows    bigint NULL,
    ErrorMessage    nvarchar(2000) NULL
);
GO

IF OBJECT_ID('etl.FileAudit', 'U') IS NULL
CREATE TABLE etl.FileAudit (
    FileAuditId  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FileAudit PRIMARY KEY,
    LoadBatchId  int NOT NULL CONSTRAINT FK_FileAudit_LoadBatch REFERENCES etl.LoadBatch(LoadBatchId),
    SourceFile   nvarchar(260) NOT NULL,
    SourceSha256 char(64) NOT NULL,
    SourceRows   bigint NOT NULL,
    StagedRows   bigint NOT NULL,
    LoadedAtUtc  datetime2(0) NOT NULL CONSTRAINT DF_FileAudit_LoadedAt DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('etl.RejectedRecord', 'U') IS NULL
CREATE TABLE etl.RejectedRecord (
    RejectedRecordId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RejectedRecord PRIMARY KEY,
    LoadBatchId      int NOT NULL CONSTRAINT FK_RejectedRecord_LoadBatch REFERENCES etl.LoadBatch(LoadBatchId),
    SourceTable      sysname NOT NULL,
    SourceKey        nvarchar(200) NULL,
    ReasonCode       varchar(80) NOT NULL,
    ReasonDetail     nvarchar(1000) NULL,
    RejectedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_RejectedRecord_At DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('etl.DataQualityResult', 'U') IS NULL
CREATE TABLE etl.DataQualityResult (
    DataQualityResultId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataQualityResult PRIMARY KEY,
    LoadBatchId         int NOT NULL CONSTRAINT FK_DQ_LoadBatch REFERENCES etl.LoadBatch(LoadBatchId),
    CheckName           nvarchar(200) NOT NULL,
    Severity            varchar(10) NOT NULL,
    FailedRows          bigint NOT NULL,
    CheckStatus         AS (CASE WHEN FailedRows = 0 THEN 'PASS' ELSE 'FAIL' END),
    CheckedAtUtc        datetime2(0) NOT NULL CONSTRAINT DF_DQ_CheckedAt DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('stg.Customers', 'U') IS NULL
CREATE TABLE stg.Customers (
    customer_id nvarchar(40) NULL,
    customer_unique_id nvarchar(40) NULL,
    customer_zip_code_prefix nvarchar(20) NULL,
    customer_city nvarchar(200) NULL,
    customer_state nvarchar(10) NULL
);
IF OBJECT_ID('stg.Geolocation', 'U') IS NULL
CREATE TABLE stg.Geolocation (
    geolocation_zip_code_prefix nvarchar(20) NULL,
    geolocation_lat nvarchar(50) NULL,
    geolocation_lng nvarchar(50) NULL,
    geolocation_city nvarchar(200) NULL,
    geolocation_state nvarchar(10) NULL
);
IF OBJECT_ID('stg.OrderItems', 'U') IS NULL
CREATE TABLE stg.OrderItems (
    order_id nvarchar(40) NULL,
    order_item_id nvarchar(20) NULL,
    product_id nvarchar(40) NULL,
    seller_id nvarchar(40) NULL,
    shipping_limit_date nvarchar(40) NULL,
    price nvarchar(40) NULL,
    freight_value nvarchar(40) NULL
);
IF OBJECT_ID('stg.Payments', 'U') IS NULL
CREATE TABLE stg.Payments (
    order_id nvarchar(40) NULL,
    payment_sequential nvarchar(20) NULL,
    payment_type nvarchar(40) NULL,
    payment_installments nvarchar(20) NULL,
    payment_value nvarchar(40) NULL
);
IF OBJECT_ID('stg.Reviews', 'U') IS NULL
CREATE TABLE stg.Reviews (
    review_id nvarchar(40) NULL,
    order_id nvarchar(40) NULL,
    review_score nvarchar(20) NULL,
    review_comment_title nvarchar(1000) NULL,
    review_comment_message nvarchar(max) NULL,
    review_creation_date nvarchar(40) NULL,
    review_answer_timestamp nvarchar(40) NULL
);
IF OBJECT_ID('stg.Orders', 'U') IS NULL
CREATE TABLE stg.Orders (
    order_id nvarchar(40) NULL,
    customer_id nvarchar(40) NULL,
    order_status nvarchar(40) NULL,
    order_purchase_timestamp nvarchar(40) NULL,
    order_approved_at nvarchar(40) NULL,
    order_delivered_carrier_date nvarchar(40) NULL,
    order_delivered_customer_date nvarchar(40) NULL,
    order_estimated_delivery_date nvarchar(40) NULL
);
IF OBJECT_ID('stg.Products', 'U') IS NULL
CREATE TABLE stg.Products (
    product_id nvarchar(40) NULL,
    product_category_name nvarchar(200) NULL,
    product_name_lenght nvarchar(20) NULL,
    product_description_lenght nvarchar(20) NULL,
    product_photos_qty nvarchar(20) NULL,
    product_weight_g nvarchar(30) NULL,
    product_length_cm nvarchar(30) NULL,
    product_height_cm nvarchar(30) NULL,
    product_width_cm nvarchar(30) NULL
);
IF OBJECT_ID('stg.Sellers', 'U') IS NULL
CREATE TABLE stg.Sellers (
    seller_id nvarchar(40) NULL,
    seller_zip_code_prefix nvarchar(20) NULL,
    seller_city nvarchar(200) NULL,
    seller_state nvarchar(10) NULL
);
IF OBJECT_ID('stg.CategoryTranslation', 'U') IS NULL
CREATE TABLE stg.CategoryTranslation (
    product_category_name nvarchar(200) NULL,
    product_category_name_english nvarchar(200) NULL
);
GO

IF OBJECT_ID('core.Geography', 'U') IS NULL
CREATE TABLE core.Geography (
    GeographyKey int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Geography PRIMARY KEY,
    ZipCodePrefix int NOT NULL,
    City nvarchar(200) NULL,
    StateCode char(2) NULL,
    Latitude decimal(10,7) NULL,
    Longitude decimal(10,7) NULL,
    SourcePointCount int NOT NULL CONSTRAINT DF_Geography_PointCount DEFAULT 0,
    CONSTRAINT UQ_Geography_Zip UNIQUE (ZipCodePrefix),
    CONSTRAINT CK_Geography_Latitude CHECK (Latitude IS NULL OR Latitude BETWEEN -90 AND 90),
    CONSTRAINT CK_Geography_Longitude CHECK (Longitude IS NULL OR Longitude BETWEEN -180 AND 180)
);
GO

IF OBJECT_ID('core.Customer', 'U') IS NULL
CREATE TABLE core.Customer (
    CustomerKey int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    CustomerUniqueId char(32) NOT NULL,
    HomeGeographyKey int NULL CONSTRAINT FK_Customer_Geography REFERENCES core.Geography(GeographyKey),
    FirstPurchaseDateTime datetime2(0) NULL,
    LastPurchaseDateTime datetime2(0) NULL,
    CohortMonth date NULL,
    PurchaseCount int NOT NULL CONSTRAINT DF_Customer_PurchaseCount DEFAULT 0,
    CustomerPurchaseFrequency decimal(12,4) NULL,
    AverageDaysBetweenPurchases decimal(12,2) NULL,
    CustomerType varchar(10) NOT NULL,
    CONSTRAINT UQ_Customer_UniqueId UNIQUE (CustomerUniqueId),
    CONSTRAINT CK_Customer_Type CHECK (CustomerType IN ('New','Repeat'))
);
GO

IF OBJECT_ID('core.Product', 'U') IS NULL
CREATE TABLE core.Product (
    ProductKey int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Product PRIMARY KEY,
    ProductId char(32) NOT NULL,
    CategoryNamePortuguese nvarchar(200) NULL,
    CategoryNameEnglish nvarchar(200) NOT NULL,
    ProductNameLength smallint NULL,
    ProductDescriptionLength int NULL,
    ProductPhotoQuantity smallint NULL,
    ProductWeightGrams decimal(12,2) NULL,
    ProductLengthCm decimal(10,2) NULL,
    ProductHeightCm decimal(10,2) NULL,
    ProductWidthCm decimal(10,2) NULL,
    CONSTRAINT UQ_Product_ProductId UNIQUE (ProductId)
);
GO

IF OBJECT_ID('core.Seller', 'U') IS NULL
CREATE TABLE core.Seller (
    SellerKey int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Seller PRIMARY KEY,
    SellerId char(32) NOT NULL,
    GeographyKey int NULL CONSTRAINT FK_Seller_Geography REFERENCES core.Geography(GeographyKey),
    CONSTRAINT UQ_Seller_SellerId UNIQUE (SellerId)
);
GO

IF OBJECT_ID('core.Date', 'U') IS NULL
CREATE TABLE core.[Date] (
    DateKey int NOT NULL CONSTRAINT PK_Date PRIMARY KEY,
    [Date] date NOT NULL CONSTRAINT UQ_Date_Date UNIQUE,
    CalendarYear smallint NOT NULL,
    CalendarQuarter tinyint NOT NULL,
    QuarterLabel char(7) NOT NULL,
    MonthNumber tinyint NOT NULL,
    MonthName varchar(12) NOT NULL,
    YearMonth char(7) NOT NULL,
    YearMonthSort int NOT NULL,
    WeekOfYear tinyint NOT NULL,
    DayOfMonth tinyint NOT NULL,
    DayName varchar(12) NOT NULL,
    IsWeekend bit NOT NULL
);
GO

IF OBJECT_ID('core.[Order]', 'U') IS NULL
CREATE TABLE core.[Order] (
    OrderKey int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Order PRIMARY KEY,
    OrderId char(32) NOT NULL,
    OlistCustomerId char(32) NOT NULL,
    CustomerKey int NOT NULL CONSTRAINT FK_Order_Customer REFERENCES core.Customer(CustomerKey),
    CustomerGeographyKey int NULL CONSTRAINT FK_Order_Geography REFERENCES core.Geography(GeographyKey),
    PurchaseDateKey int NOT NULL CONSTRAINT FK_Order_Date REFERENCES core.[Date](DateKey),
    OrderStatus varchar(30) NOT NULL,
    PurchaseDateTime datetime2(0) NOT NULL,
    ApprovedDateTime datetime2(0) NULL,
    DeliveredCarrierDateTime datetime2(0) NULL,
    DeliveredCustomerDateTime datetime2(0) NULL,
    EstimatedDeliveryDate date NULL,
    DeliveryDays decimal(10,2) NULL,
    DelayDays int NULL,
    DeliveryPerformance varchar(20) NOT NULL,
    PurchaseSequence int NOT NULL,
    DaysSincePriorPurchase int NULL,
    CustomerOrderType varchar(10) NOT NULL,
    CohortMonth date NOT NULL,
    MerchandiseValue decimal(14,2) NOT NULL,
    FreightValue decimal(14,2) NOT NULL,
    GrossOrderValue decimal(14,2) NOT NULL,
    ItemQuantity int NOT NULL,
    FreightRatio decimal(12,6) NULL,
    PaymentValue decimal(14,2) NOT NULL,
    CONSTRAINT UQ_Order_OrderId UNIQUE (OrderId),
    CONSTRAINT CK_Order_CustomerOrderType CHECK (CustomerOrderType IN ('New','Repeat')),
    CONSTRAINT CK_Order_DeliveryPerformance CHECK (DeliveryPerformance IN ('On Time','Late','Not Delivered'))
);
GO

IF OBJECT_ID('core.OrderItem', 'U') IS NULL
CREATE TABLE core.OrderItem (
    OrderItemKey bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderItem PRIMARY KEY,
    OrderKey int NOT NULL CONSTRAINT FK_OrderItem_Order REFERENCES core.[Order](OrderKey),
    OrderItemNumber smallint NOT NULL,
    ProductKey int NOT NULL CONSTRAINT FK_OrderItem_Product REFERENCES core.Product(ProductKey),
    SellerKey int NOT NULL CONSTRAINT FK_OrderItem_Seller REFERENCES core.Seller(SellerKey),
    ShippingLimitDateTime datetime2(0) NULL,
    ItemPrice decimal(12,2) NOT NULL,
    FreightValue decimal(12,2) NOT NULL,
    ItemGrossValue AS (CONVERT(decimal(13,2), ItemPrice + FreightValue)) PERSISTED,
    FreightRatio AS (CONVERT(decimal(12,6), CASE WHEN ItemPrice = 0 THEN NULL ELSE FreightValue / ItemPrice END)) PERSISTED,
    CONSTRAINT UQ_OrderItem_OrderNumber UNIQUE (OrderKey, OrderItemNumber)
);
GO

IF OBJECT_ID('core.Payment', 'U') IS NULL
CREATE TABLE core.Payment (
    PaymentKey bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payment PRIMARY KEY,
    OrderKey int NOT NULL CONSTRAINT FK_Payment_Order REFERENCES core.[Order](OrderKey),
    PaymentSequence smallint NOT NULL,
    PaymentType varchar(40) NOT NULL,
    Installments smallint NULL,
    PaymentValue decimal(12,2) NOT NULL,
    CONSTRAINT UQ_Payment_OrderSequence UNIQUE (OrderKey, PaymentSequence)
);
GO

IF OBJECT_ID('core.Review', 'U') IS NULL
CREATE TABLE core.Review (
    ReviewKey bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Review PRIMARY KEY,
    ReviewId char(32) NOT NULL,
    OrderKey int NOT NULL CONSTRAINT FK_Review_Order REFERENCES core.[Order](OrderKey),
    ReviewScore tinyint NOT NULL,
    ReviewScoreGroup varchar(20) NOT NULL,
    CommentTitle nvarchar(1000) NULL,
    CommentMessage nvarchar(max) NULL,
    ReviewCreationDate date NULL,
    ReviewAnswerDateTime datetime2(0) NULL,
    CONSTRAINT UQ_Review_ReviewOrder UNIQUE (ReviewId, OrderKey),
    CONSTRAINT CK_Review_Score CHECK (ReviewScore BETWEEN 1 AND 5),
    CONSTRAINT CK_Review_Group CHECK (ReviewScoreGroup IN ('Detractor','Neutral','Promoter'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('core.[Order]') AND name='IX_Order_CustomerPurchase')
    CREATE INDEX IX_Order_CustomerPurchase ON core.[Order](CustomerKey, PurchaseDateTime) INCLUDE (OrderStatus, MerchandiseValue, FreightValue, PurchaseSequence);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('core.[Order]') AND name='IX_Order_PurchaseDate')
    CREATE INDEX IX_Order_PurchaseDate ON core.[Order](PurchaseDateKey) INCLUDE (OrderStatus, MerchandiseValue, GrossOrderValue, ItemQuantity);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('core.OrderItem') AND name='IX_OrderItem_Product')
    CREATE INDEX IX_OrderItem_Product ON core.OrderItem(ProductKey) INCLUDE (OrderKey, SellerKey, ItemPrice, FreightValue);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('core.OrderItem') AND name='IX_OrderItem_Seller')
    CREATE INDEX IX_OrderItem_Seller ON core.OrderItem(SellerKey) INCLUDE (OrderKey, ProductKey, ItemPrice, FreightValue);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('core.Review') AND name='IX_Review_Order')
    CREATE INDEX IX_Review_Order ON core.Review(OrderKey) INCLUDE (ReviewScore, ReviewScoreGroup);
GO
