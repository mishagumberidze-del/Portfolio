SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM stg.RetailLine)<>(SELECT COUNT(*) FROM core.InvoiceLine)
 THROW 50001,'Source and core line counts differ',1;
IF EXISTS(SELECT SourceRow FROM stg.RetailLine EXCEPT SELECT SourceRow FROM core.InvoiceLine)
 THROW 50002,'Source rows missing from the core',1;
IF (SELECT SUM(CONVERT(decimal(18,4),Price)*CONVERT(int,Quantity)) FROM stg.RetailLine)
 <> (SELECT SUM(LineAmount) FROM core.InvoiceLine)
 THROW 50003,'Source and core amounts differ: check numeric precision',1;
IF (SELECT COUNT(*) FROM stg.RetailLine WHERE CONVERT(decimal(18,4),Price)=0)
 <> (SELECT COUNT(*) FROM core.InvoiceLine WHERE UnitPrice=0)
 THROW 50004,'Zero-price classifications differ',1;
SELECT COUNT(*) AS CoreLines,SUM(LineAmount) AS AllSourceLineValue FROM core.InvoiceLine;
SELECT SUM(GrossSales) GrossSales,SUM(ReturnValue) ReturnsAndAdjustments,SUM(NetSales) NetSales FROM rpt.LineAnalysis;
