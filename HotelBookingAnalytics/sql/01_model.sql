IF SCHEMA_ID('stg') IS NULL EXEC('CREATE SCHEMA stg');
IF SCHEMA_ID('core') IS NULL EXEC('CREATE SCHEMA core');
IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt');
IF SCHEMA_ID('etl') IS NULL EXEC('CREATE SCHEMA etl');
GO
IF OBJECT_ID('stg.Booking','U') IS NULL
CREATE TABLE stg.Booking (
 source_row int NOT NULL, hotel nvarchar(40), is_canceled nvarchar(10), lead_time nvarchar(20),
 arrival_date_year nvarchar(10), arrival_date_month nvarchar(20), arrival_date_week_number nvarchar(10),
 arrival_date_day_of_month nvarchar(10), stays_in_weekend_nights nvarchar(10), stays_in_week_nights nvarchar(10),
 adults nvarchar(10), children nvarchar(10), babies nvarchar(10), meal nvarchar(30), country nvarchar(20),
 market_segment nvarchar(50), distribution_channel nvarchar(50), is_repeated_guest nvarchar(10),
 previous_cancellations nvarchar(20), previous_bookings_not_canceled nvarchar(20),
 reserved_room_type nvarchar(20), assigned_room_type nvarchar(20), booking_changes nvarchar(20),
 deposit_type nvarchar(50), agent nvarchar(20), company nvarchar(20), days_in_waiting_list nvarchar(20),
 customer_type nvarchar(40), adr nvarchar(30), required_car_parking_spaces nvarchar(10),
 total_of_special_requests nvarchar(10), reservation_status nvarchar(30), reservation_status_date nvarchar(30)
);
GO
IF OBJECT_ID('etl.LoadAudit','U') IS NULL CREATE TABLE etl.LoadAudit (
 LoadId int IDENTITY PRIMARY KEY, LoadedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 SourceSha256 char(64) NOT NULL, SourceRows int NOT NULL, CoreRows int NOT NULL,
 FlaggedRows int NOT NULL, DuplicateRows int NOT NULL, Status varchar(20) NOT NULL
);
GO
IF OBJECT_ID('core.Hotel','U') IS NULL CREATE TABLE core.Hotel (
 HotelId tinyint IDENTITY PRIMARY KEY, HotelName nvarchar(40) NOT NULL UNIQUE
);
IF OBJECT_ID('core.MarketSegment','U') IS NULL CREATE TABLE core.MarketSegment (
 MarketSegmentId smallint IDENTITY PRIMARY KEY, SegmentName nvarchar(50) NOT NULL UNIQUE
);
IF OBJECT_ID('core.Date','U') IS NULL CREATE TABLE core.Date (
 DateKey int PRIMARY KEY, CalendarDate date NOT NULL UNIQUE, CalendarYear smallint NOT NULL,
 CalendarMonth tinyint NOT NULL, MonthName nvarchar(12) NOT NULL, QuarterNumber tinyint NOT NULL
);
IF OBJECT_ID('core.Booking','U') IS NULL CREATE TABLE core.Booking (
 BookingId int IDENTITY PRIMARY KEY, SourceRow int NOT NULL UNIQUE,
 HotelId tinyint NOT NULL REFERENCES core.Hotel(HotelId),
 MarketSegmentId smallint NOT NULL REFERENCES core.MarketSegment(MarketSegmentId),
 ArrivalDateKey int NOT NULL REFERENCES core.Date(DateKey),
 IsCanceled bit NOT NULL, LeadTime int NOT NULL, StayNights int NOT NULL,
 WeekendNights smallint NOT NULL, WeekNights smallint NOT NULL,
 Adults smallint NOT NULL, Children smallint NULL, Babies smallint NOT NULL,
 CountryCode nvarchar(10) NULL, Meal nvarchar(30) NOT NULL, DistributionChannel nvarchar(50) NOT NULL,
 IsRepeatedGuest bit NOT NULL, PreviousCancellations smallint NOT NULL,
 PreviousBookingsNotCanceled smallint NOT NULL, ReservedRoomType nvarchar(20) NOT NULL,
 AssignedRoomType nvarchar(20) NOT NULL, BookingChanges smallint NOT NULL,
 DepositType nvarchar(50) NOT NULL, AgentCode nvarchar(20) NULL, CompanyCode nvarchar(20) NULL,
 DaysInWaitingList smallint NOT NULL, CustomerType nvarchar(40) NOT NULL,
 ADR decimal(12,2) NOT NULL, RequiredParkingSpaces smallint NOT NULL,
 SpecialRequests smallint NOT NULL, ReservationStatus nvarchar(30) NOT NULL,
 ReservationStatusDate date NOT NULL,
 RoomChanged AS CONVERT(bit, CASE WHEN ReservedRoomType <> AssignedRoomType THEN 1 ELSE 0 END),
 GrossRoomValue AS CONVERT(decimal(16,2), ADR * StayNights),
 RealizedRoomValue AS CONVERT(decimal(16,2), CASE WHEN IsCanceled = 0 THEN ADR * StayNights ELSE 0 END),
 DataQualityFlag AS CONVERT(bit, CASE WHEN StayNights = 0 OR Adults + ISNULL(Children,0) + Babies = 0 OR ADR < 0 THEN 1 ELSE 0 END)
);
GO
CREATE OR ALTER VIEW rpt.BookingAnalysis AS
SELECT b.*, d.CalendarDate AS ArrivalDate, d.CalendarYear, d.CalendarMonth, d.MonthName,
 h.HotelName, s.SegmentName,
 CASE WHEN b.LeadTime < 30 THEN '0-29' WHEN b.LeadTime < 90 THEN '30-89'
      WHEN b.LeadTime < 180 THEN '90-179' ELSE '180+' END AS LeadTimeBand,
 CASE WHEN b.StayNights = 0 THEN '0' WHEN b.StayNights <= 3 THEN '1-3'
      WHEN b.StayNights <= 7 THEN '4-7' ELSE '8+' END AS StayBand
FROM core.Booking b
JOIN core.Date d ON b.ArrivalDateKey=d.DateKey
JOIN core.Hotel h ON b.HotelId=h.HotelId
JOIN core.MarketSegment s ON b.MarketSegmentId=s.MarketSegmentId;
GO
CREATE OR ALTER VIEW rpt.MonthlyPerformance AS
SELECT DATEFROMPARTS(CalendarYear,CalendarMonth,1) AS MonthStart, HotelName,
 COUNT(*) Bookings, SUM(CONVERT(int,IsCanceled)) Cancellations,
 CAST(AVG(CONVERT(float,IsCanceled)) AS decimal(8,4)) CancellationRate,
 SUM(RealizedRoomValue) RealizedRoomValue, SUM(GrossRoomValue) GrossRoomValue,
 CAST(AVG(CASE WHEN IsCanceled=0 AND StayNights>0 AND ADR>=0 THEN ADR END) AS decimal(12,2)) RealizedADR
FROM rpt.BookingAnalysis GROUP BY CalendarYear,CalendarMonth,HotelName;
GO
CREATE OR ALTER VIEW rpt.SegmentPerformance AS
SELECT HotelName, SegmentName, CustomerType, DistributionChannel,
 COUNT(*) Bookings, SUM(CONVERT(int,IsCanceled)) Cancellations,
 SUM(RealizedRoomValue) RealizedRoomValue,
 CAST(AVG(CASE WHEN IsCanceled=0 AND StayNights>0 AND ADR>=0 THEN ADR END) AS decimal(12,2)) RealizedADR
FROM rpt.BookingAnalysis GROUP BY HotelName,SegmentName,CustomerType,DistributionChannel;
GO
