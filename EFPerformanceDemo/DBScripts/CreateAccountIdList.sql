IF TYPE_ID(N'dbo.AccountIdList') IS NULL
BEGIN
    EXEC(N'CREATE TYPE dbo.AccountIdList AS TABLE
    (
        Id INT NOT NULL PRIMARY KEY
    );');
END;
GO
