IF SCHEMA_ID('stg') IS NULL EXEC('CREATE SCHEMA stg');
IF SCHEMA_ID('core') IS NULL EXEC('CREATE SCHEMA core');
IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt');
IF SCHEMA_ID('etl') IS NULL EXEC('CREATE SCHEMA etl');
GO
IF OBJECT_ID('stg.RetailLine','U') IS NULL CREATE TABLE stg.RetailLine (
 SourceRow int NOT NULL, Invoice nvarchar(30), StockCode nvarchar(30), Description nvarchar(500),
 Quantity nvarchar(30), InvoiceDate nvarchar(30), Price nvarchar(30), CustomerID nvarchar(30), Country nvarchar(80)
);
IF OBJECT_ID('etl.LoadAudit','U') IS NULL CREATE TABLE etl.LoadAudit (
 LoadId int IDENTITY PRIMARY KEY, LoadedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 SourceSha256 char(64) NOT NULL, SourceRows int NOT NULL, CoreRows int NOT NULL,
 DuplicateRows int NOT NULL, MissingCustomerRows int NOT NULL, Status varchar(20) NOT NULL
);
IF OBJECT_ID('core.Country','U') IS NULL CREATE TABLE core.Country (
 CountryId smallint IDENTITY PRIMARY KEY, CountryName nvarchar(80) NOT NULL UNIQUE
);
IF OBJECT_ID('core.Customer','U') IS NULL CREATE TABLE core.Customer (
 CustomerId int PRIMARY KEY, SourceCustomerId nvarchar(30) NOT NULL UNIQUE
);
IF OBJECT_ID('core.Product','U') IS NULL CREATE TABLE core.Product (
 ProductId int IDENTITY PRIMARY KEY, StockCode nvarchar(30) NOT NULL UNIQUE,
 ProductDescription nvarchar(500) NULL
);
IF OBJECT_ID('core.Date','U') IS NULL CREATE TABLE core.Date (
 DateKey int PRIMARY KEY, CalendarDate date NOT NULL UNIQUE, CalendarYear smallint NOT NULL,
 CalendarMonth tinyint NOT NULL, MonthName nvarchar(12) NOT NULL, QuarterNumber tinyint NOT NULL
);
IF OBJECT_ID('core.Invoice','U') IS NULL CREATE TABLE core.Invoice (
 InvoiceId int IDENTITY PRIMARY KEY, SourceInvoice nvarchar(30) NOT NULL UNIQUE,
 InvoiceTimestamp datetime2(0) NOT NULL, CustomerId int NULL REFERENCES core.Customer(CustomerId),
 CountryId smallint NOT NULL REFERENCES core.Country(CountryId), IsCancellation bit NOT NULL,
 TimestampConflict bit NOT NULL
);
IF OBJECT_ID('core.InvoiceLine','U') IS NULL CREATE TABLE core.InvoiceLine (
 InvoiceLineId int IDENTITY PRIMARY KEY, SourceRow int NOT NULL UNIQUE,
 InvoiceId int NOT NULL REFERENCES core.Invoice(InvoiceId),
 ProductId int NOT NULL REFERENCES core.Product(ProductId),
 DateKey int NOT NULL REFERENCES core.Date(DateKey),
 LineTimestamp datetime2(0) NOT NULL, SourceDescription nvarchar(500) NULL,
 Quantity int NOT NULL, UnitPrice decimal(12,4) NOT NULL,
 LineAmount AS CONVERT(decimal(18,4), Quantity*UnitPrice),
 LineClass AS CONVERT(varchar(20), CASE
   WHEN UnitPrice < 0 THEN 'NEGATIVE_PRICE'
   WHEN UnitPrice = 0 THEN 'ZERO_PRICE'
   WHEN Quantity < 0 THEN 'RETURN_OR_ADJUST'
   ELSE 'SALE' END)
);
GO
-- Upgrade earlier local builds that rounded four 0.001 prices to zero.
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('core.InvoiceLine') AND name='UnitPrice' AND scale=2)
BEGIN
 DROP INDEX IF EXISTS IX_InvoiceLine_Date ON core.InvoiceLine;
 DROP INDEX IF EXISTS IX_InvoiceLine_Invoice ON core.InvoiceLine;
 ALTER TABLE core.InvoiceLine DROP COLUMN LineAmount,LineClass;
 ALTER TABLE core.InvoiceLine ALTER COLUMN UnitPrice decimal(12,4) NOT NULL;
 ALTER TABLE core.InvoiceLine ADD LineAmount AS CONVERT(decimal(18,4),Quantity*UnitPrice),
 LineClass AS CONVERT(varchar(20), CASE WHEN UnitPrice<0 THEN 'NEGATIVE_PRICE'
 WHEN UnitPrice=0 THEN 'ZERO_PRICE' WHEN Quantity<0 THEN 'RETURN_OR_ADJUST' ELSE 'SALE' END);
END;
GO
CREATE OR ALTER VIEW rpt.LineAnalysis AS
SELECT l.*, i.SourceInvoice,i.CustomerId,i.CountryId,i.IsCancellation,i.TimestampConflict,
 p.StockCode,p.ProductDescription,c.CountryName,d.CalendarDate,d.CalendarYear,d.CalendarMonth,
 CASE WHEN l.LineClass='SALE' THEN l.LineAmount ELSE 0 END GrossSales,
 CASE WHEN l.LineClass='RETURN_OR_ADJUST' THEN -l.LineAmount ELSE 0 END ReturnValue,
 CASE WHEN l.LineClass IN ('SALE','RETURN_OR_ADJUST') THEN l.LineAmount ELSE 0 END NetSales
FROM core.InvoiceLine l
JOIN core.Invoice i ON l.InvoiceId=i.InvoiceId
JOIN core.Product p ON l.ProductId=p.ProductId
JOIN core.Country c ON i.CountryId=c.CountryId
JOIN core.Date d ON l.DateKey=d.DateKey;
GO
CREATE OR ALTER VIEW rpt.CustomerValue AS
SELECT i.CustomerId, COUNT(DISTINCT CASE WHEN l.LineClass='SALE' THEN i.InvoiceId END) PurchaseInvoices,
 MIN(CASE WHEN l.LineClass='SALE' THEN l.LineTimestamp END) FirstPurchase,
 MAX(CASE WHEN l.LineClass='SALE' THEN l.LineTimestamp END) LastPurchase,
 SUM(CASE WHEN l.LineClass IN ('SALE','RETURN_OR_ADJUST') THEN l.LineAmount ELSE 0 END) NetSales,
 SUM(CASE WHEN l.LineClass='SALE' THEN l.LineAmount ELSE 0 END) GrossSales,
 SUM(CASE WHEN l.LineClass='RETURN_OR_ADJUST' THEN -l.LineAmount ELSE 0 END) ReturnValue
FROM core.Invoice i JOIN core.InvoiceLine l ON l.InvoiceId=i.InvoiceId
WHERE i.CustomerId IS NOT NULL GROUP BY i.CustomerId;
GO
