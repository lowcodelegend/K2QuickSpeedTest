CREATE DATABASE [QuickSpeedTest];
GO

USE [QuickSpeedTest];
GO

CREATE TABLE [dbo].[SpeedTestResults](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [StartTime] [bigint] NOT NULL,
    [BatchName] [nvarchar](100) NOT NULL,
    [EndTime] [bigint] NOT NULL,
PRIMARY KEY CLUSTERED
(
    [Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];
GO

SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE [dbo].[sp_InsertSpeedTestResult]
    @StartTime BIGINT,
    @BatchName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CurrentEpochMS BIGINT;
    SET @CurrentEpochMS = DATEDIFF_BIG(MILLISECOND, '1970-01-01', SYSUTCDATETIME());

    INSERT INTO [dbo].[SpeedTestResults] ([StartTime], [BatchName], [EndTime])
    VALUES (@StartTime, @BatchName, @CurrentEpochMS);

    SELECT 1;
END;
GO
