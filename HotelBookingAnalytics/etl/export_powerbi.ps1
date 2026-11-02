[CmdletBinding()]
param(
 [string]$ServerInstance = '.\SQLEXPRESS',
 [string]$BcpPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\bcp.exe'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'data\processed\powerbi'
New-Item -ItemType Directory -Force $out | Out-Null
$q=@'
SELECT CAST(BookingId AS varchar(20)),CAST(ArrivalDateKey AS varchar(20)),HotelName,SegmentName,
 CustomerType,DistributionChannel,CAST(IsCanceled AS varchar(1)),CAST(LeadTime AS varchar(20)),
 CAST(StayNights AS varchar(20)),CAST(ADR AS varchar(30)),CAST(RealizedRoomValue AS varchar(30)),
 CAST(GrossRoomValue AS varchar(30)),CAST(RoomChanged AS varchar(1)),COALESCE(CountryCode,''),
 CAST(IsRepeatedGuest AS varchar(1)),DepositType,ReservationStatus,CAST(DataQualityFlag AS varchar(1)),
 CAST(BookingChanges AS varchar(20)),CAST(SpecialRequests AS varchar(20)),LeadTimeBand,StayBand,
 ReservedRoomType,AssignedRoomType
FROM Portfolio_HotelBookings.rpt.BookingAnalysis ORDER BY BookingId
'@
& $BcpPath $q queryout (Join-Path $out 'booking.tsv') -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a | Select-Object -Last 4
if($LASTEXITCODE -ne 0){throw 'Booking export failed'}
$q=@'
SELECT CAST(DateKey AS varchar(20)),CONVERT(char(10),CalendarDate,23),
 CAST(CalendarYear AS varchar(10)),CAST(CalendarMonth AS varchar(10)),MonthName,
 CAST(QuarterNumber AS varchar(10)),CONVERT(char(7),CalendarDate,126),
 CAST(CalendarYear*100+CalendarMonth AS varchar(10))
FROM Portfolio_HotelBookings.core.Date ORDER BY DateKey
'@
& $BcpPath $q queryout (Join-Path $out 'date.tsv') -S $ServerInstance -T -c -C 65001 -t "`t" -r 0x0a | Select-Object -Last 4
if($LASTEXITCODE -ne 0){throw 'Date export failed'}
