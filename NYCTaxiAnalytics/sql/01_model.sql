IF SCHEMA_ID('stg') IS NULL EXEC('CREATE SCHEMA stg');
IF SCHEMA_ID('core') IS NULL EXEC('CREATE SCHEMA core');
IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt');
IF SCHEMA_ID('etl') IS NULL EXEC('CREATE SCHEMA etl');
GO
IF OBJECT_ID('stg.SourceFile') IS NULL CREATE TABLE stg.SourceFile(
 FileId int,ServiceId int,PartitionMonth int,FileName varchar(80),Sha256 char(64),SourceRows bigint,InYearRows bigint);
IF OBJECT_ID('stg.Zone') IS NULL CREATE TABLE stg.Zone(ZoneId int,BoroughName nvarchar(40),ZoneName nvarchar(100),ServiceZone nvarchar(40));
IF OBJECT_ID('stg.TimeProfile') IS NULL CREATE TABLE stg.TimeProfile(
 FileId int,MonthKey int,Weekday int,Hour int,TripCount bigint,
 DurationCount bigint,DurationSeconds float,DistanceCount bigint,DistanceMiles float,
 SpeedCount bigint,SpeedMiles float,SpeedSeconds float,FareCount bigint,FareAmount float);
IF OBJECT_ID('stg.ZoneProfile') IS NULL CREATE TABLE stg.ZoneProfile(
 FileId int,MonthKey int,ZoneId int,Role varchar(2),TripCount bigint);
IF OBJECT_ID('stg.RouteProfile') IS NULL CREATE TABLE stg.RouteProfile(
 FileId int,MonthKey int,PickupZoneId int,DropoffZoneId int,TripCount bigint);
IF OBJECT_ID('stg.BandProfile') IS NULL CREATE TABLE stg.BandProfile(
 FileId int,MonthKey int,Metric varchar(12),Band varchar(24),TripCount bigint);
IF OBJECT_ID('stg.Quality') IS NULL CREATE TABLE stg.Quality(FileId int,CheckName varchar(60),FailedRows bigint);
GO
IF OBJECT_ID('core.Service') IS NULL CREATE TABLE core.Service(ServiceId int PRIMARY KEY,ServiceName varchar(40) UNIQUE NOT NULL);
IF OBJECT_ID('core.Month') IS NULL CREATE TABLE core.Month(MonthKey int PRIMARY KEY,MonthStart date NOT NULL,MonthLabel char(7) NOT NULL);
IF OBJECT_ID('core.Zone') IS NULL CREATE TABLE core.Zone(ZoneId int PRIMARY KEY,BoroughName nvarchar(40),ZoneName nvarchar(100),ServiceZone nvarchar(40));
IF OBJECT_ID('core.SourceFile') IS NULL CREATE TABLE core.SourceFile(
 FileId int PRIMARY KEY,ServiceId int NOT NULL REFERENCES core.Service(ServiceId),
 PartitionMonth int NOT NULL REFERENCES core.Month(MonthKey),FileName varchar(80) UNIQUE NOT NULL,
 Sha256 char(64) NOT NULL,SourceRows bigint NOT NULL,InYearRows bigint NOT NULL);
IF OBJECT_ID('core.TimeProfile') IS NULL CREATE TABLE core.TimeProfile(
 FileId int REFERENCES core.SourceFile(FileId),MonthKey int REFERENCES core.Month(MonthKey),Weekday tinyint,Hour tinyint,
 TripCount bigint NOT NULL,DurationCount bigint NOT NULL,DurationSeconds float NOT NULL,
 DistanceCount bigint NOT NULL,DistanceMiles float NOT NULL,SpeedCount bigint NOT NULL,
 SpeedMiles float NOT NULL,SpeedSeconds float NOT NULL,FareCount bigint NOT NULL,FareAmount float NOT NULL,
 CONSTRAINT PK_TimeProfile PRIMARY KEY(FileId,MonthKey,Weekday,Hour));
IF OBJECT_ID('core.ZoneProfile') IS NULL CREATE TABLE core.ZoneProfile(
 FileId int REFERENCES core.SourceFile(FileId),MonthKey int REFERENCES core.Month(MonthKey),ZoneId int REFERENCES core.Zone(ZoneId),
 Role varchar(2),TripCount bigint NOT NULL,CONSTRAINT PK_ZoneProfile PRIMARY KEY(FileId,MonthKey,ZoneId,Role));
IF OBJECT_ID('core.RouteProfile') IS NULL CREATE TABLE core.RouteProfile(
 FileId int REFERENCES core.SourceFile(FileId),MonthKey int REFERENCES core.Month(MonthKey),
 PickupZoneId int REFERENCES core.Zone(ZoneId),DropoffZoneId int REFERENCES core.Zone(ZoneId),TripCount bigint NOT NULL,
 CONSTRAINT PK_RouteProfile PRIMARY KEY(FileId,MonthKey,PickupZoneId,DropoffZoneId));
IF OBJECT_ID('core.BandProfile') IS NULL CREATE TABLE core.BandProfile(
 FileId int REFERENCES core.SourceFile(FileId),MonthKey int REFERENCES core.Month(MonthKey),Metric varchar(12),Band varchar(24),
 TripCount bigint NOT NULL,CONSTRAINT PK_BandProfile PRIMARY KEY(FileId,MonthKey,Metric,Band));
IF OBJECT_ID('core.Quality') IS NULL CREATE TABLE core.Quality(
 FileId int REFERENCES core.SourceFile(FileId),CheckName varchar(60),FailedRows bigint NOT NULL,PRIMARY KEY(FileId,CheckName));
IF OBJECT_ID('etl.LoadAudit') IS NULL CREATE TABLE etl.LoadAudit(
 LoadId int IDENTITY PRIMARY KEY,LoadedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 SourceFiles int NOT NULL,SourceRows bigint NOT NULL,InYearRows bigint NOT NULL,
 TimeCells bigint NOT NULL,ZoneCells bigint NOT NULL,RouteCells bigint NOT NULL,Status varchar(20) NOT NULL);
GO
CREATE OR ALTER VIEW rpt.TimeAnalysis AS
SELECT s.ServiceId,s.ServiceName,t.MonthKey,t.Weekday,t.Hour,
 SUM(t.TripCount) TripCount,SUM(t.DurationCount) DurationCount,SUM(t.DurationSeconds) DurationSeconds,
 SUM(t.DistanceCount) DistanceCount,SUM(t.DistanceMiles) DistanceMiles,
 SUM(t.SpeedCount) SpeedCount,SUM(t.SpeedMiles) SpeedMiles,SUM(t.SpeedSeconds) SpeedSeconds,
 SUM(t.FareCount) FareCount,SUM(t.FareAmount) FareAmount
FROM core.TimeProfile t JOIN core.SourceFile f ON t.FileId=f.FileId JOIN core.Service s ON f.ServiceId=s.ServiceId
GROUP BY s.ServiceId,s.ServiceName,t.MonthKey,t.Weekday,t.Hour;
GO
CREATE OR ALTER VIEW rpt.ZoneAnalysis AS
SELECT f.ServiceId,z.MonthKey,z.ZoneId,z.Role,SUM(z.TripCount) TripCount
FROM core.ZoneProfile z JOIN core.SourceFile f ON z.FileId=f.FileId
GROUP BY f.ServiceId,z.MonthKey,z.ZoneId,z.Role;
GO
CREATE OR ALTER VIEW rpt.BandAnalysis AS
SELECT f.ServiceId,b.MonthKey,b.Metric,b.Band,SUM(b.TripCount) TripCount
FROM core.BandProfile b JOIN core.SourceFile f ON b.FileId=f.FileId
GROUP BY f.ServiceId,b.MonthKey,b.Metric,b.Band;
GO
CREATE OR ALTER VIEW rpt.QualityAnalysis AS
SELECT f.ServiceId,f.PartitionMonth AS MonthKey,q.CheckName,SUM(q.FailedRows) FailedRows
FROM core.Quality q JOIN core.SourceFile f ON q.FileId=f.FileId GROUP BY f.ServiceId,f.PartitionMonth,q.CheckName;
GO
