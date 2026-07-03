USE [QuickSpeedTest];
GO

SELECT
    BatchName,
    COUNT(*) AS TotalProcesses,
    MIN(StartTime) AS EarliestStart,
    MAX(EndTime) AS LatestEnd,
    (MAX(EndTime) - MIN(StartTime)) AS TotalDurationMS,
    CASE
        WHEN (MAX(EndTime) - MIN(StartTime)) = 0 THEN 0
        ELSE COUNT(*) / ((MAX(EndTime) - MIN(StartTime)) / 1000.0)
    END AS ProcessesPerSecond
FROM [dbo].[SpeedTestResults]
GROUP BY BatchName
ORDER BY BatchName;
