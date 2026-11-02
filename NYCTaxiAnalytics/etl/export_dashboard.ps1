[CmdletBinding()]
param(
 [string]$ServerInstance='.\SQLEXPRESS',
 [string]$BcpPath='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'data\processed\dashboard'
New-Item -ItemType Directory -Force $out | Out-Null
$queries=@{
time='SELECT ServiceId,MonthKey,Weekday,Hour,TripCount,DurationCount,CONVERT(decimal(22,3),DurationSeconds),DistanceCount,CONVERT(decimal(22,3),DistanceMiles),SpeedCount,CONVERT(decimal(22,3),SpeedMiles),CONVERT(decimal(22,3),SpeedSeconds),FareCount,CONVERT(decimal(22,3),FareAmount) FROM Portfolio_NYCTaxi.rpt.TimeAnalysis ORDER BY ServiceId,MonthKey,Weekday,Hour';
zone='SELECT ServiceId,MonthKey,ZoneId,Role,TripCount FROM Portfolio_NYCTaxi.rpt.ZoneAnalysis ORDER BY ServiceId,MonthKey,ZoneId,Role';
band='SELECT ServiceId,MonthKey,Metric,Band,TripCount FROM Portfolio_NYCTaxi.rpt.BandAnalysis ORDER BY ServiceId,MonthKey,Metric,Band';
quality='SELECT ServiceId,MonthKey,CheckName,FailedRows FROM Portfolio_NYCTaxi.rpt.QualityAnalysis ORDER BY ServiceId,MonthKey,CheckName';
source='SELECT FileId,ServiceId,PartitionMonth,FileName,Sha256,SourceRows,InYearRows FROM Portfolio_NYCTaxi.core.SourceFile ORDER BY FileId';
zones='SELECT ZoneId,BoroughName,ZoneName,ServiceZone FROM Portfolio_NYCTaxi.core.Zone ORDER BY ZoneId';
routes=@'
WITH Base AS (
 SELECT f.ServiceId,r.MonthKey,r.PickupZoneId,r.DropoffZoneId,r.TripCount
 FROM Portfolio_NYCTaxi.core.RouteProfile r JOIN Portfolio_NYCTaxi.core.SourceFile f ON r.FileId=f.FileId
 WHERE r.PickupZoneId BETWEEN 1 AND 263 AND r.DropoffZoneId BETWEEN 1 AND 263
), Rolled AS (
 SELECT ISNULL(ServiceId,0) ServiceId,ISNULL(MonthKey,0) MonthKey,PickupZoneId,DropoffZoneId,SUM(TripCount) TripCount
 FROM Base GROUP BY GROUPING SETS((ServiceId,MonthKey,PickupZoneId,DropoffZoneId),(ServiceId,PickupZoneId,DropoffZoneId),(MonthKey,PickupZoneId,DropoffZoneId),(PickupZoneId,DropoffZoneId))
), Ranked AS (
 SELECT *,ROW_NUMBER() OVER(PARTITION BY ServiceId,MonthKey ORDER BY TripCount DESC,PickupZoneId,DropoffZoneId) Ranking FROM Rolled
)
SELECT ServiceId,MonthKey,PickupZoneId,DropoffZoneId,TripCount,Ranking FROM Ranked WHERE Ranking<=10 ORDER BY ServiceId,MonthKey,Ranking
'@
}
foreach($key in $queries.Keys){
 & $BcpPath $queries[$key] queryout (Join-Path $out "$key.tsv") -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a | Select-Object -Last 4
 if($LASTEXITCODE -ne 0){throw "SQL export failed: $key"}
}
