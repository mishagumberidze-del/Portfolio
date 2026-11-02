USE [master];
GO

IF DB_ID(N'Portfolio_Olist') IS NULL
BEGIN
    PRINT 'Creating database Portfolio_Olist';
    CREATE DATABASE [Portfolio_Olist];
END
ELSE
    PRINT 'Database Portfolio_Olist already exists';
GO

ALTER DATABASE [Portfolio_Olist] SET RECOVERY SIMPLE;
GO

