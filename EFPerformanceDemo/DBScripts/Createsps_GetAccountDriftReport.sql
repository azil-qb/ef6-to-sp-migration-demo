CREATE OR ALTER PROCEDURE dbo.sp_GetAccountDriftReportByAccountIds
    @AccountIds dbo.AccountIdList READONLY,
    @DriftThreshold DECIMAL(18, 2)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    BEGIN TRY
        CREATE TABLE #AccountIds
        (
            Id INT NOT NULL PRIMARY KEY
        );

        INSERT INTO #AccountIds (Id)
        SELECT Id
        FROM @AccountIds;

        ;WITH PortfolioValues AS
        (
            SELECT
                h.ClientAccountId,
                SUM(h.Quantity * h.CurrentPrice) AS TotalPortfolioValue,
                SUM(CASE WHEN h.AssetClass = N'CASH'
                    THEN h.Quantity * h.CurrentPrice END) AS CashValue
            FROM dbo.Holdings AS h
            INNER JOIN #AccountIds AS ids
                ON ids.Id = h.ClientAccountId
            GROUP BY h.ClientAccountId
        ),
        ReportRows AS
        (
            SELECT
                ca.AccountNumber,
                ca.AccountName,
                pv.TotalPortfolioValue,
                pv.CashValue /
                    CASE WHEN pv.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
                        THEN CONVERT(DECIMAL(38, 4), 1)
                        ELSE pv.TotalPortfolioValue END AS CurrentCashWeight,
                mp.TargetCashAllocation AS TargetCashWeight,
                ABS(
                    pv.CashValue /
                        CASE WHEN pv.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
                            THEN CONVERT(DECIMAL(38, 4), 1)
                            ELSE pv.TotalPortfolioValue END
                    - mp.TargetCashAllocation
                ) AS AbsoluteDrift,
                ca.LastRebalanceTime AS LastRebalanceDate
            FROM dbo.ClientAccounts AS ca
            INNER JOIN #AccountIds AS ids
                ON ids.Id = ca.Id
            LEFT JOIN dbo.ModelPortfolios AS mp
                ON mp.Id = ca.ModelPortfolio_Id
            LEFT JOIN PortfolioValues AS pv
                ON pv.ClientAccountId = ca.Id
            WHERE ca.IsActive = 1
        )
        SELECT
            AccountNumber,
            AccountName,
            TotalPortfolioValue,
            CurrentCashWeight,
            TargetCashWeight,
            AbsoluteDrift,
            LastRebalanceDate
        FROM ReportRows
        WHERE AbsoluteDrift >= @DriftThreshold
        ORDER BY AbsoluteDrift DESC;

        DROP TABLE IF EXISTS #AccountIds;
    END TRY
    BEGIN CATCH
        DROP TABLE IF EXISTS #AccountIds;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAccountDriftReportByAdvisorFirm
    @AdvisorFirmId UNIQUEIDENTIFIER,
    @DriftThreshold DECIMAL(18, 2)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    BEGIN TRY
        ;WITH PortfolioValues AS
        (
            SELECT
                h.ClientAccountId,
                SUM(h.Quantity * h.CurrentPrice) AS TotalPortfolioValue,
                SUM(CASE WHEN h.AssetClass = N'CASH'
                    THEN h.Quantity * h.CurrentPrice END) AS CashValue
            FROM dbo.Holdings AS h
            INNER JOIN dbo.ClientAccounts AS ca
                ON ca.Id = h.ClientAccountId
            WHERE ca.AdvisorFirmId = @AdvisorFirmId
              AND ca.IsActive = 1
            GROUP BY h.ClientAccountId
        ),
        ReportRows AS
        (
            SELECT
                ca.Id,
                ca.AccountNumber,
                ca.AccountName,
                pv.TotalPortfolioValue,
                pv.CashValue /
                    CASE WHEN pv.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
                        THEN CONVERT(DECIMAL(38, 4), 1)
                        ELSE pv.TotalPortfolioValue END AS CurrentCashWeight,
                mp.TargetCashAllocation AS TargetCashWeight,
                ABS(
                    pv.CashValue /
                        CASE WHEN pv.TotalPortfolioValue = CONVERT(DECIMAL(38, 4), 0)
                            THEN CONVERT(DECIMAL(38, 4), 1)
                            ELSE pv.TotalPortfolioValue END
                    - mp.TargetCashAllocation
                ) AS AbsoluteDrift,
                ca.LastRebalanceTime AS LastRebalanceDate
            FROM dbo.ClientAccounts AS ca
            LEFT JOIN dbo.ModelPortfolios AS mp
                ON mp.Id = ca.ModelPortfolio_Id
            LEFT JOIN PortfolioValues AS pv
                ON pv.ClientAccountId = ca.Id
            WHERE ca.AdvisorFirmId = @AdvisorFirmId
              AND ca.IsActive = 1
        )
        SELECT
            Id,
            AccountNumber,
            AccountName,
            TotalPortfolioValue,
            CurrentCashWeight,
            TargetCashWeight,
            AbsoluteDrift,
            LastRebalanceDate
        FROM ReportRows
        WHERE AbsoluteDrift >= @DriftThreshold
        ORDER BY AbsoluteDrift DESC;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH;
END;
GO

