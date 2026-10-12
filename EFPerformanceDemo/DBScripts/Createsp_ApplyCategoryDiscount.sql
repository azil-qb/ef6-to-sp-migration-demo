CREATE OR ALTER PROCEDURE dbo.sp_ApplyCategoryDiscount
    @CategoryId INT,
    @DiscountPercent DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- 1. Mass update UnitPrice in OrderItems based on Category & Unprocessed status
        UPDATE oi
        SET oi.UnitPrice = oi.UnitPrice - (oi.UnitPrice * (@DiscountPercent / 100.0))
        FROM OrderItems oi
        INNER JOIN Orders o ON oi.OrderId = o.Id
        WHERE o.CategoryId = @CategoryId AND o.IsProcessed = 0;

        -- 2. Mass update StockQuantity in Inventories
        UPDATE inv
        SET inv.StockQuantity = inv.StockQuantity - sub.TotalQty
        FROM Inventories inv
        INNER JOIN (
            SELECT oi.InventoryId, SUM(oi.Quantity) AS TotalQty
            FROM OrderItems oi
            INNER JOIN Orders o ON oi.OrderId = o.Id
            WHERE o.CategoryId = @CategoryId AND o.IsProcessed = 0
            GROUP BY oi.InventoryId
        ) sub ON inv.Id = sub.InventoryId;

        -- 3. Mass update RewardPoints for Customers
        UPDATE c
        SET c.RewardPoints = c.RewardPoints + sub.PointsEarned
        FROM Customers c
        INNER JOIN (
            SELECT o.CustomerId, CAST(SUM(oi.UnitPrice * oi.Quantity) / 10 AS INT) AS PointsEarned
            FROM Orders o
            INNER JOIN OrderItems oi ON o.Id = oi.OrderId
            WHERE o.CategoryId = @CategoryId AND o.IsProcessed = 0
            GROUP BY o.CustomerId
        ) sub ON c.Id = sub.CustomerId;

        -- 4. Bulk insert Audit Logs for all processed orders
        INSERT INTO OrderAuditLogs (OrderId, LogMessage, ProcessedAt)
        SELECT 
            o.Id,
            CONCAT('Processed discount of ', @DiscountPercent, '% on Category ', @CategoryId, '.'),
            SYSUTCDATETIME()
        FROM Orders o
        WHERE o.CategoryId = @CategoryId AND o.IsProcessed = 0;

        -- 5. Mark all targeted orders as Processed
        UPDATE Orders
        SET IsProcessed = 1
        WHERE CategoryId = @CategoryId AND IsProcessed = 0;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;