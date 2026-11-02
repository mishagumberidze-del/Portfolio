SET XACT_ABORT ON;
SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM stg.SourceFile)<>48 THROW 50001,'Expected 48 verified TLC source files',1;
IF EXISTS(SELECT f.FileId FROM stg.SourceFile f LEFT JOIN stg.TimeProfile t ON f.FileId=t.FileId
 GROUP BY f.FileId,f.InYearRows HAVING ISNULL(SUM(t.TripCount),0)<>f.InYearRows)
 THROW 50002,'Source-file counts do not reconcile to time aggregates',1;
BEGIN TRANSACTION;
DELETE core.Quality;DELETE core.BandProfile;DELETE core.RouteProfile;DELETE core.ZoneProfile;DELETE core.TimeProfile;
DELETE core.SourceFile;DELETE core.Zone;DELETE core.Month;DELETE core.Service;
INSERT core.Service VALUES(1,'Yellow taxi'),(2,'Green taxi'),(3,'FHV'),(4,'High-volume FHV');
INSERT core.Month SELECT DISTINCT PartitionMonth,DATEFROMPARTS(PartitionMonth/100,PartitionMonth%100,1),
 CONCAT(PartitionMonth/100,'-',RIGHT(CONCAT('0',PartitionMonth%100),2)) FROM stg.SourceFile;
INSERT core.Zone SELECT * FROM stg.Zone;
INSERT core.SourceFile SELECT * FROM stg.SourceFile;
INSERT core.TimeProfile SELECT * FROM stg.TimeProfile;
INSERT core.ZoneProfile SELECT * FROM stg.ZoneProfile;
INSERT core.RouteProfile SELECT * FROM stg.RouteProfile;
INSERT core.BandProfile SELECT * FROM stg.BandProfile;
INSERT core.Quality SELECT * FROM stg.Quality;
IF EXISTS(SELECT f.FileId FROM core.SourceFile f LEFT JOIN core.ZoneProfile z ON z.FileId=f.FileId AND z.Role='PU'
 GROUP BY f.FileId,f.InYearRows HAVING ISNULL(SUM(z.TripCount),0)<>f.InYearRows)
 THROW 50003,'Pickup zone counts do not reconcile',1;
IF EXISTS(SELECT f.FileId FROM core.SourceFile f LEFT JOIN core.ZoneProfile z ON z.FileId=f.FileId AND z.Role='DO'
 GROUP BY f.FileId,f.InYearRows HAVING ISNULL(SUM(z.TripCount),0)<>f.InYearRows)
 THROW 50004,'Dropoff zone counts do not reconcile',1;
IF EXISTS(SELECT f.FileId FROM core.SourceFile f LEFT JOIN core.RouteProfile r ON r.FileId=f.FileId
 GROUP BY f.FileId,f.InYearRows HAVING ISNULL(SUM(r.TripCount),0)<>f.InYearRows)
 THROW 50005,'Route counts do not reconcile',1;
COMMIT;
INSERT etl.LoadAudit(SourceFiles,SourceRows,InYearRows,TimeCells,ZoneCells,RouteCells,Status)
SELECT COUNT(*),SUM(SourceRows),SUM(InYearRows),(SELECT COUNT(*) FROM core.TimeProfile),
 (SELECT COUNT(*) FROM core.ZoneProfile),(SELECT COUNT(*) FROM core.RouteProfile),'SUCCEEDED' FROM core.SourceFile;
SELECT TOP 1 * FROM etl.LoadAudit ORDER BY LoadId DESC;
GO
