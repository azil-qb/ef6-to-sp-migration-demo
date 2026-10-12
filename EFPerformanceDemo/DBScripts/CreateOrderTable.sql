-- 1. Customers Table
CREATE TABLE [dbo].[Customers] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [FullName] NVARCHAR(100) NOT NULL,
    [RewardPoints] INT NOT NULL DEFAULT 0,
    CONSTRAINT [PK_Customers] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- 2. Inventories Table
CREATE TABLE [dbo].[Inventories] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [ProductName] NVARCHAR(200) NOT NULL,
    [StockQuantity] INT NOT NULL,
    CONSTRAINT [PK_Inventories] PRIMARY KEY CLUSTERED ([Id] ASC)
);

-- 3. Orders Table (Updated with CustomerId)
CREATE TABLE [dbo].[Orders] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [CategoryId] INT NOT NULL,
    [IsProcessed] BIT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Orders_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers]([Id])
);

-- 4. OrderItems Table (Updated with InventoryId)
CREATE TABLE [dbo].[OrderItems] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [OrderId] INT NOT NULL,
    [InventoryId] INT NOT NULL,
    [Quantity] INT NOT NULL DEFAULT 1,
    [UnitPrice] DECIMAL(18,2) NOT NULL,
    CONSTRAINT [PK_OrderItems] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_OrderItems_Orders] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Orders]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrderItems_Inventories] FOREIGN KEY ([InventoryId]) REFERENCES [dbo].[Inventories]([Id])
);

-- 5. OrderAuditLogs Table (Heavy log table created during processing)
CREATE TABLE [dbo].[OrderAuditLogs] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [OrderId] INT NOT NULL,
    [LogMessage] NVARCHAR(500) NOT NULL,
    [ProcessedAt] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_OrderAuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
);