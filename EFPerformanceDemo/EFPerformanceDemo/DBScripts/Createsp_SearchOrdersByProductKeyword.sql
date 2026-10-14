-- Set-based replacement for the cartesian-explosion "search-cartesian-explosion-ef" endpoint.
-- Preserves V1 output shape: Id, CustomerName, CalculatedTotal, ItemNames
-- Preserves V1 filter semantics: OrderItems.Any(i => i.Inventory.ProductName.Contains(keyword))
--   -> pre-filter to distinct matching OrderIds, then aggregate without duplicating rows.
CREATE OR ALTER PROCEDURE dbo.sp_SearchOrdersByProductKeyword
    @Keyword NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        -- Materialize matching OrderIds once (indexed temp table) so the
        -- aggregation join below does not repeatedly rescan OrderItems/Inventories
        -- for keyword matching.
        SELECT DISTINCT oi.OrderId
        INTO #MatchingOrderIds
        FROM dbo.OrderItems oi
        INNER JOIN dbo.Inventories inv ON inv.Id = oi.InventoryId
        WHERE inv.ProductName LIKE '%' + @Keyword + '%';

        CREATE UNIQUE CLUSTERED INDEX IX_MatchingOrderIds_OrderId
            ON #MatchingOrderIds (OrderId);

        -- Single aggregation pass: SUM/STRING_AGG computed set-based in SQL,
        -- one row per matching order returned to the caller (no cartesian
        -- duplication, no full entity graph materialization).
        SELECT
            o.Id,
            c.FullName AS CustomerName,
            SUM(oi.UnitPrice * oi.Quantity) AS CalculatedTotal,
            STRING_AGG(inv.ProductName, ', ') AS ItemNames
        FROM dbo.Orders o
        INNER JOIN #MatchingOrderIds m ON m.OrderId = o.Id
        INNER JOIN dbo.Customers c ON c.Id = o.CustomerId
        INNER JOIN dbo.OrderItems oi ON oi.OrderId = o.Id
        INNER JOIN dbo.Inventories inv ON inv.Id = oi.InventoryId
        GROUP BY o.Id, c.FullName;

        DROP TABLE IF EXISTS #MatchingOrderIds;
    END TRY
    BEGIN CATCH
        DROP TABLE IF EXISTS #MatchingOrderIds;
        THROW;
    END CATCH
END;
