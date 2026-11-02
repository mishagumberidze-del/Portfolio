SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE FROM core.Booking;
DELETE FROM core.Date;
DELETE FROM core.MarketSegment;
DELETE FROM core.Hotel;
DBCC CHECKIDENT ('core.Booking', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.MarketSegment', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Hotel', RESEED, 0) WITH NO_INFOMSGS;
INSERT core.Hotel(HotelName) SELECT DISTINCT hotel FROM stg.Booking;
INSERT core.MarketSegment(SegmentName) SELECT DISTINCT market_segment FROM stg.Booking;
WITH SourceDates AS (
 SELECT DISTINCT TRY_CONVERT(date,CONCAT(arrival_date_day_of_month,' ',arrival_date_month,' ',arrival_date_year),106) AS ArrivalDate
 FROM stg.Booking
)
INSERT core.Date(DateKey,CalendarDate,CalendarYear,CalendarMonth,MonthName,QuarterNumber)
SELECT YEAR(ArrivalDate)*10000+MONTH(ArrivalDate)*100+DAY(ArrivalDate),ArrivalDate,
 YEAR(ArrivalDate),MONTH(ArrivalDate),DATENAME(month,ArrivalDate),DATEPART(quarter,ArrivalDate)
FROM SourceDates WHERE ArrivalDate IS NOT NULL;
IF (SELECT COUNT(*) FROM core.Date) <>
   (SELECT COUNT(DISTINCT CONCAT(arrival_date_year,'-',arrival_date_month,'-',arrival_date_day_of_month)) FROM stg.Booking)
 THROW 50001, 'Unparseable hotel arrival date', 1;
INSERT core.Booking(SourceRow,HotelId,MarketSegmentId,ArrivalDateKey,IsCanceled,LeadTime,
 StayNights,WeekendNights,WeekNights,Adults,Children,Babies,CountryCode,Meal,DistributionChannel,
 IsRepeatedGuest,PreviousCancellations,PreviousBookingsNotCanceled,ReservedRoomType,
 AssignedRoomType,BookingChanges,DepositType,AgentCode,CompanyCode,DaysInWaitingList,
 CustomerType,ADR,RequiredParkingSpaces,SpecialRequests,ReservationStatus,ReservationStatusDate)
SELECT s.source_row,h.HotelId,m.MarketSegmentId,
 d.DateKey,CONVERT(bit,s.is_canceled),CONVERT(int,s.lead_time),
 CONVERT(int,s.stays_in_weekend_nights)+CONVERT(int,s.stays_in_week_nights),
 CONVERT(smallint,s.stays_in_weekend_nights),CONVERT(smallint,s.stays_in_week_nights),
 CONVERT(smallint,s.adults),TRY_CONVERT(smallint,NULLIF(s.children,'')),CONVERT(smallint,s.babies),
 NULLIF(s.country,''),s.meal,s.distribution_channel,CONVERT(bit,s.is_repeated_guest),
 CONVERT(smallint,s.previous_cancellations),CONVERT(smallint,s.previous_bookings_not_canceled),
 s.reserved_room_type,s.assigned_room_type,CONVERT(smallint,s.booking_changes),s.deposit_type,
 NULLIF(s.agent,''),NULLIF(s.company,''),CONVERT(smallint,s.days_in_waiting_list),
 s.customer_type,CONVERT(decimal(12,2),s.adr),CONVERT(smallint,s.required_car_parking_spaces),
 CONVERT(smallint,s.total_of_special_requests),s.reservation_status,CONVERT(date,s.reservation_status_date,23)
FROM stg.Booking s
JOIN core.Hotel h ON h.HotelName=s.hotel
JOIN core.MarketSegment m ON m.SegmentName=s.market_segment
JOIN core.Date d ON d.CalendarDate=TRY_CONVERT(date,CONCAT(s.arrival_date_day_of_month,' ',s.arrival_date_month,' ',s.arrival_date_year),106);
IF (SELECT COUNT(*) FROM core.Booking)<>(SELECT COUNT(*) FROM stg.Booking)
 THROW 50002, 'Hotel row count did not reconcile', 1;
COMMIT;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Booking_ArrivalHotel' AND object_id=OBJECT_ID('core.Booking'))
 CREATE INDEX IX_Booking_ArrivalHotel ON core.Booking(ArrivalDateKey,HotelId) INCLUDE(IsCanceled,ADR,StayNights);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Booking_Segment' AND object_id=OBJECT_ID('core.Booking'))
 CREATE INDEX IX_Booking_Segment ON core.Booking(MarketSegmentId) INCLUDE(IsCanceled,ADR,StayNights);
GO
