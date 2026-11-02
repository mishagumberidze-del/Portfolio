SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM stg.Booking)<>(SELECT COUNT(*) FROM core.Booking)
 THROW 50001,'Source and core booking counts differ',1;
IF EXISTS(SELECT source_row FROM stg.Booking EXCEPT SELECT SourceRow FROM core.Booking)
 THROW 50002,'Source rows missing from the core',1;
IF (SELECT SUM(CONVERT(int,is_canceled)) FROM stg.Booking)
 <> (SELECT SUM(CONVERT(int,IsCanceled)) FROM core.Booking)
 THROW 50003,'Cancellation counts differ',1;
SELECT COUNT(*) Bookings,SUM(CONVERT(int,IsCanceled)) Cancellations,SUM(RealizedRoomValue) RoomValueProxy FROM core.Booking;
