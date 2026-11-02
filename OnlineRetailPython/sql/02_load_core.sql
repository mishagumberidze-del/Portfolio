SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE FROM core.InvoiceLine;
DELETE FROM core.Invoice;
DELETE FROM core.Date;
DELETE FROM core.Product;
DELETE FROM core.Customer;
DELETE FROM core.Country;
DBCC CHECKIDENT ('core.InvoiceLine', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Invoice', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Product', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Country', RESEED, 0) WITH NO_INFOMSGS;
INSERT core.Country(CountryName) SELECT DISTINCT Country FROM stg.RetailLine;
INSERT core.Customer(CustomerId,SourceCustomerId)
SELECT DISTINCT CONVERT(int,CustomerID),CustomerID FROM stg.RetailLine WHERE CustomerID<>'';
WITH DescriptionCounts AS (
 SELECT StockCode,Description,COUNT(*) AS Frequency,
 ROW_NUMBER() OVER(PARTITION BY StockCode ORDER BY COUNT(*) DESC,Description) AS DescriptionRank
 FROM stg.RetailLine WHERE Description<>'' GROUP BY StockCode,Description
), Codes AS (SELECT DISTINCT StockCode FROM stg.RetailLine)
INSERT core.Product(StockCode,ProductDescription)
SELECT c.StockCode,d.Description FROM Codes c
LEFT JOIN DescriptionCounts d ON d.StockCode=c.StockCode AND d.DescriptionRank=1;
WITH Dates AS (
 SELECT DISTINCT CONVERT(date,CONVERT(datetime2(0),InvoiceDate,1)) AS CalendarDate FROM stg.RetailLine
)
INSERT core.Date(DateKey,CalendarDate,CalendarYear,CalendarMonth,MonthName,QuarterNumber)
SELECT YEAR(CalendarDate)*10000+MONTH(CalendarDate)*100+DAY(CalendarDate),CalendarDate,
 YEAR(CalendarDate),MONTH(CalendarDate),DATENAME(month,CalendarDate),DATEPART(quarter,CalendarDate)
FROM Dates;
WITH InvoiceGroups AS (
 SELECT Invoice,MIN(CONVERT(datetime2(0),InvoiceDate,1)) InvoiceTimestamp,
 MAX(NULLIF(CustomerID,'')) CustomerID,MAX(Country) Country,
 CONVERT(bit,CASE WHEN COUNT(DISTINCT InvoiceDate)>1 THEN 1 ELSE 0 END) TimestampConflict
 FROM stg.RetailLine GROUP BY Invoice
)
INSERT core.Invoice(SourceInvoice,InvoiceTimestamp,CustomerId,CountryId,IsCancellation,TimestampConflict)
SELECT x.Invoice,x.InvoiceTimestamp,TRY_CONVERT(int,x.CustomerID),c.CountryId,
 CONVERT(bit,CASE WHEN x.Invoice LIKE 'C%' THEN 1 ELSE 0 END),x.TimestampConflict
FROM InvoiceGroups x JOIN core.Country c ON x.Country=c.CountryName;
INSERT core.InvoiceLine(SourceRow,InvoiceId,ProductId,DateKey,LineTimestamp,SourceDescription,Quantity,UnitPrice)
SELECT s.SourceRow,i.InvoiceId,p.ProductId,d.DateKey,CONVERT(datetime2(0),s.InvoiceDate,1),
 NULLIF(s.Description,''),CONVERT(int,s.Quantity),CONVERT(decimal(12,4),s.Price)
FROM stg.RetailLine s
JOIN core.Invoice i ON s.Invoice=i.SourceInvoice
JOIN core.Product p ON s.StockCode=p.StockCode
JOIN core.Date d ON d.CalendarDate=CONVERT(date,CONVERT(datetime2(0),s.InvoiceDate,1));
IF (SELECT COUNT(*) FROM core.InvoiceLine)<>(SELECT COUNT(*) FROM stg.RetailLine)
 THROW 50001, 'Retail line count did not reconcile', 1;
COMMIT;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Invoice_Customer' AND object_id=OBJECT_ID('core.Invoice'))
 CREATE INDEX IX_Invoice_Customer ON core.Invoice(CustomerId) INCLUDE(InvoiceTimestamp,IsCancellation);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_InvoiceLine_Date' AND object_id=OBJECT_ID('core.InvoiceLine'))
 CREATE INDEX IX_InvoiceLine_Date ON core.InvoiceLine(DateKey) INCLUDE(Quantity,UnitPrice,InvoiceId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_InvoiceLine_Invoice' AND object_id=OBJECT_ID('core.InvoiceLine'))
 CREATE INDEX IX_InvoiceLine_Invoice ON core.InvoiceLine(InvoiceId) INCLUDE(Quantity,UnitPrice);
GO
