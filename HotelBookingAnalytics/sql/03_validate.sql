SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM stg.Booking)<>(SELECT COUNT(*) FROM core.Booking)
 THROW 50001,'Source and core booking counts differ',1;
IF EXISTS(SELECT source_row FROM stg.Booking EXCEPT SELECT SourceRow FROM core.Booking)
 THROW 50002,'Source rows missing from the core',1;
IF (SELECT SUM(CONVERT(int,is_canceled)) FROM stg.Booking)
 <> (SELECT SUM(CONVERT(int,IsCanceled)) FROM core.Booking)
 THROW 50003,'Cancellation counts differ',1;
SELECT COUNT(*) Bookings,SUM(CONVERT(int,IsCanceled)) Cancellations FROM core.Booking;
-- Validate the reporting population independently from the raw derived value column.
IF (SELECT SUM(CASE WHEN CONVERT(int,is_canceled)=0 AND CONVERT(decimal(12,2),adr)>=0
    AND CONVERT(int,stays_in_week_nights)+CONVERT(int,stays_in_weekend_nights)>0
    THEN CONVERT(decimal(12,2),adr)*(CONVERT(int,stays_in_week_nights)+CONVERT(int,stays_in_weekend_nights)) ELSE 0 END)
    FROM stg.Booking) <> (SELECT SUM(RoomValueProxy) FROM rpt.MonthlyPerformance)
 THROW 50004,'Eligible source room value and reporting value differ',1;
SELECT SUM(RoomValueProxy) AS EligibleRoomValueProxy FROM rpt.MonthlyPerformance;
