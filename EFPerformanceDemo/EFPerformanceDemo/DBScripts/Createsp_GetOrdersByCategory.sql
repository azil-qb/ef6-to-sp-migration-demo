-- Set-based replacement for the N+1 "GetOrdersNPlusOneAsync" repository method.
-- Preserves V1 output shape: Id, CustomerName, ItemCount, TotalAmount
-- Preserves V1 semantics:
--   - TOP 2000 orders for the category
--   - orders with zero OrderItems are still included (ItemCount=0, TotalAmount=0)
--     -> requires LEFT JOIN OrderItems, not INNER JOIN
--   - every order is assumed to have a valid Customer (non-nullable FK)
CREATE OR ALTER PROCEDURE dbo.sp_GetOrdersByCategory
    @CategoryId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        SELECT TOP (2000)
            o.Id,
            c.FullName AS CustomerName,
            COUNT(oi.Id) AS ItemCount,
            ISNULL(SUM(oi.UnitPrice * oi.Quantity), 0) AS TotalAmount
        FROM dbo.Orders o
        INNER JOIN dbo.Customers c ON c.Id = o.CustomerId
        LEFT JOIN dbo.OrderItems oi ON oi.OrderId = o.Id
        WHERE o.CategoryId = @CategoryId
        GROUP BY o.Id, c.FullName
        ORDER BY o.Id;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END;
