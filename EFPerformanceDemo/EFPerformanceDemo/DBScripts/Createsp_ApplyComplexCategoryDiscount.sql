-- Set-based replacement for the N+1 "apply-complex-discount-ef" endpoint.
-- Preserves exact V1 business semantics:
--   - UnitPrice discounted per OrderItem
--   - Inventory.StockQuantity decremented by quantity sold
--   - RewardPoints earned is TRUNCATED PER ORDER (order total / 10), then summed per customer
--     (NOT sum-of-order-totals-then-truncate, which would change the result)
--   - One audit log row per order, with the per-order discounted total in the message
--   - Orders marked as processed
CREATE OR ALTER PROCEDURE dbo.sp_ApplyComplexCategoryDiscount
    @CategoryId INT,
    @DiscountPercent DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Materialize the target orders once so every subsequent step
        -- reuses the same bounded set instead of rescanning Orders repeatedly.
        SELECT o.Id AS OrderId, o.CustomerId
        INTO #TargetOrders
        FROM dbo.Orders o
        WHERE o.CategoryId = @CategoryId AND o.IsProcessed = 0;

        CREATE UNIQUE CLUSTERED INDEX IX_TargetOrders_OrderId ON #TargetOrders (OrderId);

        -- 1. Discount UnitPrice for items belonging to targeted orders
        UPDATE oi
        SET oi.UnitPrice = oi.UnitPrice - (oi.UnitPrice * (@DiscountPercent / 100.0))
        FROM dbo.OrderItems oi
        INNER JOIN #TargetOrders t ON t.OrderId = oi.OrderId;

        -- 2. Per-order discounted total, computed once and reused below.
        SELECT
            oi.OrderId,
            t.CustomerId,
            SUM(oi.UnitPrice * oi.Quantity) AS OrderTotal
        INTO #OrderTotals
        FROM dbo.OrderItems oi
        INNER JOIN #TargetOrders t ON t.OrderId = oi.OrderId
        GROUP BY oi.OrderId, t.CustomerId;

        CREATE UNIQUE CLUSTERED INDEX IX_OrderTotals_OrderId ON #OrderTotals (OrderId);

        -- 3. Decrement inventory stock based on quantities sold in targeted orders.
        UPDATE inv
        SET inv.StockQuantity = inv.StockQuantity - sub.TotalQty
        FROM dbo.Inventories inv
        INNER JOIN (
            SELECT oi.InventoryId, SUM(oi.Quantity) AS TotalQty
            FROM dbo.OrderItems oi
            INNER JOIN #TargetOrders t ON t.OrderId = oi.OrderId
            GROUP BY oi.InventoryId
        ) sub ON inv.Id = sub.InventoryId;

        -- 4. Reward points: truncate PER ORDER first (matches original per-order loop),
        --    then sum the truncated per-order points per customer.
        UPDATE c
        SET c.RewardPoints = c.RewardPoints + sub.PointsEarned
        FROM dbo.Customers c
        INNER JOIN (
            SELECT
                CustomerId,
                SUM(CAST(OrderTotal / 10 AS INT)) AS PointsEarned
            FROM #OrderTotals
            GROUP BY CustomerId
        ) sub ON c.Id = sub.CustomerId;

        -- 5. One audit log row per order, with the per-order discounted total,
        --    matching the original per-order log message exactly.
        INSERT INTO dbo.OrderAuditLogs (OrderId, LogMessage, ProcessedAt)
        SELECT
            ot.OrderId,
            CONCAT('Processed discount of ', @DiscountPercent, '% on Category ', @CategoryId, '. Total: $', FORMAT(ot.OrderTotal, 'F2')),
            SYSUTCDATETIME()
        FROM #OrderTotals ot;

        -- 6. Mark all targeted orders as processed.
        UPDATE o
        SET o.IsProcessed = 1
        FROM dbo.Orders o
        INNER JOIN #TargetOrders t ON t.OrderId = o.Id;

        DROP TABLE IF EXISTS #OrderTotals, #TargetOrders;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
