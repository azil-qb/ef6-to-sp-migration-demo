/*
    Phase 1 database comparison for GetAccountDriftReportAsync.

    Run CreateAccountIdList.sql and Createsps_GetAccountDriftReport.sql first.
    This script compares the two stored procedures and a set-based equivalent of
    the captured EF statement using the same advisor and threshold.
*/

SET NOCOUNT ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

DECLARE @AdvisorFirmId UNIQUEIDENTIFIER = '0e62af5e-c99c-479d-9960-363a0531b485';
DECLARE @DriftThreshold DECIMAL(18, 2) = 0.05;

--DECLARE @AccountIds dbo.AccountIdList;

--INSERT INTO @AccountIds (Id)
--SELECT Id
--FROM dbo.ClientAccounts
--WHERE AdvisorFirmId = @AdvisorFirmId
--  AND IsActive = 1;

--PRINT 'V2 TVP procedure';
--EXEC dbo.sp_GetAccountDriftReportByAccountIds
--    @AccountIds = @AccountIds,
--    @DriftThreshold = @DriftThreshold;

PRINT 'V2 single-advisor procedure';
EXEC dbo.sp_GetAccountDriftReportByAdvisorFirm
    @AdvisorFirmId = @AdvisorFirmId,
    @DriftThreshold = @DriftThreshold;

/* V1 EF baseline: equivalent relational form of the captured EF SQL.
   The generated EF statement repeatedly emits these correlated SUM expressions;
   this form preserves its joins, null behavior, filter, and ordering while making
   the comparison script easier to review in SSMS. */
PRINT 'V1 EF baseline equivalent';
SELECT
    ca.AccountNumber,
    ca.AccountName,
    totals.TotalPortfolioValue,
    cash.CashValue /
        CASE WHEN totals.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
            THEN CONVERT(DECIMAL(38, 4), 1)
            ELSE totals.TotalPortfolioValue END AS CurrentCashWeight,
    mp.TargetCashAllocation AS TargetCashWeight,
    ABS(
        cash.CashValue /
            CASE WHEN totals.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
                THEN CONVERT(DECIMAL(38, 4), 1)
                ELSE totals.TotalPortfolioValue END
        - mp.TargetCashAllocation
    ) AS AbsoluteDrift,
    ca.LastRebalanceTime AS LastRebalanceDate
FROM dbo.ClientAccounts AS ca
LEFT JOIN dbo.ModelPortfolios AS mp
    ON ca.ModelPortfolio_Id = mp.Id
CROSS APPLY
(
    SELECT SUM(h.Quantity * h.CurrentPrice) AS TotalPortfolioValue
    FROM dbo.Holdings AS h
    WHERE h.ClientAccountId = ca.Id
) AS totals
CROSS APPLY
(
    SELECT SUM(h.Quantity * h.CurrentPrice) AS CashValue
    FROM dbo.Holdings AS h
    WHERE h.ClientAccountId = ca.Id
      AND h.AssetClass = N'CASH'
) AS cash
WHERE ca.AdvisorFirmId = @AdvisorFirmId
  AND ca.IsActive = 1
  AND ABS(
        cash.CashValue /
            CASE WHEN totals.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
                THEN CONVERT(DECIMAL(38, 4), 1)
                ELSE totals.TotalPortfolioValue END
        - mp.TargetCashAllocation
      ) >= @DriftThreshold
ORDER BY AbsoluteDrift DESC;

PRINT 'V1 EF from logFile';

DECLARE @p__linq__0 UNIQUEIDENTIFIER = '0e62af5e-c99c-479d-9960-363a0531b485';
DECLARE @p__linq__1 DECIMAL(2,2) = 0.05;
SELECT 
    [Project8].[Id] AS [Id], 
    [Project8].[AccountNumber] AS [AccountNumber], 
    [Project8].[AccountName] AS [AccountName], 
    [Project8].[C3] AS [C1], 
    [Project8].[C1] AS [C2], 
    [Project8].[TargetCashAllocation] AS [TargetCashAllocation], 
    [Project8].[C2] AS [C3], 
    [Project8].[LastRebalanceTime] AS [LastRebalanceTime]
    FROM ( SELECT 
        [Project7].[Id] AS [Id], 
        [Project7].[AccountNumber] AS [AccountNumber], 
        [Project7].[AccountName] AS [AccountName], 
        [Project7].[LastRebalanceTime] AS [LastRebalanceTime], 
        [Project7].[TargetCashAllocation] AS [TargetCashAllocation], 
        [Project7].[C2] / (CASE WHEN (cast(0 as decimal(18)) = [Project7].[C3]) THEN cast(1 as decimal(18)) ELSE [Project7].[C4] END) AS [C1], 
        ABS(([Project7].[C5] / (CASE WHEN (cast(0 as decimal(18)) = [Project7].[C6]) THEN cast(1 as decimal(18)) ELSE [Project7].[C7] END)) - [Project7].[TargetCashAllocation]) AS [C2], 
        [Project7].[C1] AS [C3]
        FROM ( SELECT 
            [Project6].[Id] AS [Id], 
            [Project6].[AccountNumber] AS [AccountNumber], 
            [Project6].[AccountName] AS [AccountName], 
            [Project6].[LastRebalanceTime] AS [LastRebalanceTime], 
            [Project6].[TargetCashAllocation] AS [TargetCashAllocation], 
            [Project6].[C1] AS [C1], 
            [Project6].[C2] AS [C2], 
            [Project6].[C3] AS [C3], 
            [Project6].[C4] AS [C4], 
            [Project6].[C5] AS [C5], 
            [Project6].[C6] AS [C6], 
            (SELECT 
                SUM([Filter8].[A1_0]) AS [A1]
                FROM ( SELECT 
                    [Extent9].[Quantity] * [Extent9].[CurrentPrice] AS [A1_0]
                    FROM [dbo].[Holdings] AS [Extent9]
                    WHERE [Project6].[Id] = [Extent9].[ClientAccountId]
                )  AS [Filter8]) AS [C7]
            FROM ( SELECT 
                [Project5].[Id] AS [Id], 
                [Project5].[AccountNumber] AS [AccountNumber], 
                [Project5].[AccountName] AS [AccountName], [Project5].[LastRebalanceTime] AS [LastRebalanceTime], 
                [Project5].[TargetCashAllocation] AS [TargetCashAllocation],  [Project5].[C1] AS [C1],  [Project5].[C2] AS [C2], 
                [Project5].[C3] AS [C3],  [Project5].[C4] AS [C4],  [Project5].[C5] AS [C5], 
                (SELECT 
                    SUM([Filter7].[A1_0]) AS [A1] FROM ( SELECT 
                        [Extent8].[Quantity] * [Extent8].[CurrentPrice] AS [A1_0]
                        FROM [dbo].[Holdings] AS [Extent8]
                        WHERE [Project5].[Id] = [Extent8].[ClientAccountId] )  AS [Filter7]) AS [C6]
                FROM ( SELECT 
                    [Project4].[Id] AS [Id], [Project4].[AccountNumber] AS [AccountNumber], 
                    [Project4].[AccountName] AS [AccountName], 
                    [Project4].[LastRebalanceTime] AS [LastRebalanceTime], 
                    [Project4].[TargetCashAllocation] AS [TargetCashAllocation],  [Project4].[C1] AS [C1],  [Project4].[C2] AS [C2],  [Project4].[C3] AS [C3],  [Project4].[C4] AS [C4], 
                    (SELECT 
                        SUM([Filter6].[A1_0]) AS [A1]
                        FROM ( SELECT 
                            [Extent7].[Quantity] * [Extent7].[CurrentPrice] AS [A1_0]
                            FROM [dbo].[Holdings] AS [Extent7]
                            WHERE ([Project4].[Id] = [Extent7].[ClientAccountId]) AND (N'CASH' = [Extent7].[AssetClass])
                        )  AS [Filter6]) AS [C5]
                    FROM ( SELECT 
                        [Project3].[Id] AS [Id],   [Project3].[AccountNumber] AS [AccountNumber],   [Project3].[AccountName] AS [AccountName],  [Project3].[LastRebalanceTime] AS [LastRebalanceTime], [Project3].[TargetCashAllocation] AS [TargetCashAllocation], [Project3].[C1] AS [C1], [Project3].[C2] AS [C2],  [Project3].[C3] AS [C3], 
                        (SELECT 
                            SUM([Filter5].[A1_0]) AS [A1]
                            FROM ( SELECT 
                                [Extent6].[Quantity] * [Extent6].[CurrentPrice] AS [A1_0]
                                FROM [dbo].[Holdings] AS [Extent6]
                                WHERE [Project3].[Id] = [Extent6].[ClientAccountId]
                            )  AS [Filter5]) AS [C4]
                        FROM ( SELECT 
                            [Project2].[Id] AS [Id],  [Project2].[AccountNumber] AS [AccountNumber], [Project2].[AccountName] AS [AccountName],  [Project2].[LastRebalanceTime] AS [LastRebalanceTime],  [Project2].[TargetCashAllocation] AS [TargetCashAllocation],  [Project2].[C1] AS [C1],  [Project2].[C2] AS [C2], 
                            (SELECT 
                                SUM([Filter4].[A1_0]) AS [A1]
                                FROM ( SELECT 
                                    [Extent5].[Quantity] * [Extent5].[CurrentPrice] AS [A1_0]
                                    FROM [dbo].[Holdings] AS [Extent5]
                                    WHERE [Project2].[Id] = [Extent5].[ClientAccountId]
                                )  AS [Filter4]) AS [C3]
                            FROM ( SELECT 
                                [Project1].[Id] AS [Id], 
                                [Project1].[AccountNumber] AS [AccountNumber], 
                                [Project1].[AccountName] AS [AccountName], 
                                [Project1].[LastRebalanceTime] AS [LastRebalanceTime], 
                                [Project1].[TargetCashAllocation] AS [TargetCashAllocation], 
                                [Project1].[C1] AS [C1], 
                                (SELECT 
                                    SUM([Filter3].[A1_0]) AS [A1]
                                    FROM ( SELECT 
                                        [Extent4].[Quantity] * [Extent4].[CurrentPrice] AS [A1_0]
                                        FROM [dbo].[Holdings] AS [Extent4]
                                        WHERE ([Project1].[Id] = [Extent4].[ClientAccountId]) AND (N'CASH' = [Extent4].[AssetClass])
                                    )  AS [Filter3]) AS [C2] FROM ( SELECT 
                                    [Extent1].[Id] AS [Id], 
                                    [Extent1].[AccountNumber] AS [AccountNumber], 
                                    [Extent1].[AccountName] AS [AccountName], 
                                    [Extent1].[LastRebalanceTime] AS [LastRebalanceTime], 
                                    [Extent2].[TargetCashAllocation] AS [TargetCashAllocation], 
                                    (SELECT  SUM([Filter2].[A1_0]) AS [A1]
                                        FROM ( SELECT 
                                            [Extent3].[Quantity] * [Extent3].[CurrentPrice] AS [A1_0]
                                            FROM [dbo].[Holdings] AS [Extent3]
                                            WHERE [Extent1].[Id] = [Extent3].[ClientAccountId]
                                        )  AS [Filter2]) AS [C1] FROM  [dbo].[ClientAccounts] AS [Extent1]
                                    LEFT OUTER JOIN [dbo].[ModelPortfolios] AS [Extent2] ON [Extent1].[ModelPortfolio_Id] = [Extent2].[Id]
                                    WHERE ([Extent1].[AdvisorFirmId] = @p__linq__0) AND ([Extent1].[IsActive] = 1)
                                )  AS [Project1] )  AS [Project2] )  AS [Project3] )  AS [Project4] )  AS [Project5]  )  AS [Project6]
        )  AS [Project7] WHERE (ABS(([Project7].[C5] / (CASE WHEN (cast(0 as decimal(18)) = [Project7].[C6]) THEN cast(1 as decimal(18)) ELSE [Project7].[C7] END)) - [Project7].[TargetCashAllocation])) >= @p__linq__1
    )  AS [Project8] ORDER BY [Project8].[C2] DESC
/* Deterministic row-count check. */
PRINT 'Row-count comparison';
DECLARE @ExpectedCount INT;
DECLARE @ActualCount INT;

SELECT @ExpectedCount = COUNT(*)
FROM dbo.ClientAccounts AS ca
LEFT JOIN dbo.ModelPortfolios AS mp ON ca.ModelPortfolio_Id = mp.Id
CROSS APPLY
(
    SELECT SUM(h.Quantity * h.CurrentPrice) AS TotalPortfolioValue
    FROM dbo.Holdings AS h WHERE h.ClientAccountId = ca.Id
) AS totals
CROSS APPLY
(
    SELECT SUM(h.Quantity * h.CurrentPrice) AS CashValue
    FROM dbo.Holdings AS h
    WHERE h.ClientAccountId = ca.Id AND h.AssetClass = N'CASH'
) AS cash
WHERE ca.AdvisorFirmId = @AdvisorFirmId
  AND ca.IsActive = 1
  AND ABS(cash.CashValue /
      CASE WHEN totals.TotalPortfolioValue = 0 THEN 1 ELSE totals.TotalPortfolioValue END
      - mp.TargetCashAllocation) >= @DriftThreshold;

SELECT @ActualCount = COUNT(*)
FROM
(
    SELECT ca.Id
    FROM dbo.ClientAccounts AS ca
    LEFT JOIN dbo.ModelPortfolios AS mp ON ca.ModelPortfolio_Id = mp.Id
    LEFT JOIN dbo.Holdings AS h ON h.ClientAccountId = ca.Id
    WHERE ca.AdvisorFirmId = @AdvisorFirmId
      AND ca.IsActive = 1
    GROUP BY ca.Id, mp.TargetCashAllocation
    HAVING ABS(
        SUM(CASE WHEN h.AssetClass = N'CASH' THEN h.Quantity * h.CurrentPrice END) /
        CASE WHEN SUM(h.Quantity * h.CurrentPrice) = 0
            THEN 1 ELSE SUM(h.Quantity * h.CurrentPrice) END
        - mp.TargetCashAllocation
    ) >= @DriftThreshold
) AS rows_to_return;

SELECT @ExpectedCount AS ExpectedRowCount, @ActualCount AS V2EquivalentRowCount;

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
