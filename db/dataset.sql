/*
  AskMyData Dataset_Script (Requirements 1.6, 4.1, 14.1, 14.3, 14.4, 14.5, 14.6)

  Run with:  sqlcmd -b -C -i db/dataset.sql -v READONLY_PASSWORD="<password>"

  - Idempotent: objects are created behind existence guards; data is loaded only when
    dbo.DatasetVersion has no row for the current version (the row is inserted last,
    inside the same transaction as the data).
  - Deterministic: all pseudo-random values come from an LCG
      state = (state * 1103515245 + 12345) % 2147483648, seed 20240601, bigint arithmetic.
  - Failure: every step runs in TRY/CATCH, prints "FAILED at step: <step> - <message>"
    and re-throws, so `sqlcmd -b` exits non-zero.
  - READONLY_PASSWORD is substituted textually by sqlcmd; it must not contain a single quote.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ---------------------------------------------------------------------------
-- Step 1: database
-- ---------------------------------------------------------------------------
DECLARE @step nvarchar(200) = N'create database AskMyData';
BEGIN TRY
    IF DB_ID(N'AskMyData') IS NULL
        CREATE DATABASE AskMyData;
END TRY
BEGIN CATCH
    PRINT CONCAT('FAILED at step: ', @step, ' - ', ERROR_MESSAGE());
    THROW;
END CATCH
GO

USE AskMyData;
GO

-- ---------------------------------------------------------------------------
-- Step 2: tables
-- ---------------------------------------------------------------------------
DECLARE @step nvarchar(200) = N'create tables';
BEGIN TRY
    SET @step = N'create table dbo.Customers';
    IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
        CREATE TABLE dbo.Customers (
            CustomerId   int           NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
            CustomerName nvarchar(100) NOT NULL,
            Segment      nvarchar(20)  NOT NULL,
            Country      nvarchar(50)  NOT NULL,
            City         nvarchar(50)  NOT NULL,
            CreatedDate  date          NOT NULL
        );

    SET @step = N'create table dbo.Products';
    IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
        CREATE TABLE dbo.Products (
            ProductId    int            NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
            ProductName  nvarchar(100)  NOT NULL,
            Category     nvarchar(50)   NOT NULL,
            Subcategory  nvarchar(50)   NOT NULL,
            ListPrice    decimal(10,2)  NOT NULL,
            StandardCost decimal(10,2)  NOT NULL
        );

    SET @step = N'create table dbo.Stores';
    IF OBJECT_ID(N'dbo.Stores', N'U') IS NULL
        CREATE TABLE dbo.Stores (
            StoreId   int           NOT NULL CONSTRAINT PK_Stores PRIMARY KEY,
            StoreName nvarchar(100) NOT NULL,
            Region    nvarchar(30)  NOT NULL,
            Country   nvarchar(50)  NOT NULL
        );

    SET @step = N'create table dbo.Orders';
    IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
        CREATE TABLE dbo.Orders (
            OrderId    int          NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
            CustomerId int          NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers (CustomerId),
            StoreId    int          NOT NULL CONSTRAINT FK_Orders_Stores REFERENCES dbo.Stores (StoreId),
            OrderDate  date         NOT NULL,
            Channel    nvarchar(20) NOT NULL,
            Status     nvarchar(20) NOT NULL
        );

    SET @step = N'create table dbo.OrderLines';
    IF OBJECT_ID(N'dbo.OrderLines', N'U') IS NULL
        CREATE TABLE dbo.OrderLines (
            OrderLineId int           NOT NULL CONSTRAINT PK_OrderLines PRIMARY KEY,
            OrderId     int           NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders (OrderId),
            ProductId   int           NOT NULL CONSTRAINT FK_OrderLines_Products REFERENCES dbo.Products (ProductId),
            Quantity    int           NOT NULL,
            UnitPrice   decimal(10,2) NOT NULL,
            Discount    decimal(4,3)  NOT NULL,
            UnitCost    decimal(10,2) NOT NULL
        );

    SET @step = N'create table dbo.Calendar';
    IF OBJECT_ID(N'dbo.Calendar', N'U') IS NULL
        CREATE TABLE dbo.Calendar (
            DateKey   int          NOT NULL CONSTRAINT PK_Calendar PRIMARY KEY,
            [Date]    date         NOT NULL,
            [Year]    int          NOT NULL,
            [Quarter] int          NOT NULL,
            [Month]   int          NOT NULL,
            MonthName nvarchar(10) NOT NULL
        );

    SET @step = N'create table dbo.Employees';
    IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
        CREATE TABLE dbo.Employees (
            EmployeeId int           NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
            FullName   nvarchar(100) NOT NULL,
            Salary     decimal(10,2) NOT NULL
        );

    SET @step = N'create table dbo.DatasetVersion';
    IF OBJECT_ID(N'dbo.DatasetVersion', N'U') IS NULL
        CREATE TABLE dbo.DatasetVersion (
            [Version] int       NOT NULL CONSTRAINT PK_DatasetVersion PRIMARY KEY,
            LoadedAt  datetime2 NOT NULL
        );
END TRY
BEGIN CATCH
    PRINT CONCAT('FAILED at step: ', @step, ' - ', ERROR_MESSAGE());
    THROW;
END CATCH
GO

-- ---------------------------------------------------------------------------
-- Step 3: deterministic data load (one transaction, version row inserted last)
--
-- Draw map into #Rng (each value is 0..32767, taken from the high bits of the LCG state):
--   customers  idx = (CustomerId-1)*5 + k            k = 1..5        (1..1000)
--   products   idx = 1000 + (ProductId-1)*3 + k      k = 1..3        (1001..1180)
--   employees  idx = 2000 + EmployeeId               (2001..2010)
--   orders     idx = 3000 + (OrderId-1)*6 + k        k = 1..6        (3001..33000)
--   lines      idx = 33000 + (OrderId-1)*16 + (n-1)*4 + k  n = 1..4, k = 1..4 (33001..113000)
-- ---------------------------------------------------------------------------
DECLARE @step nvarchar(200) = N'check dataset version';
DECLARE @version int = 1;
BEGIN TRY
    IF NOT EXISTS (SELECT 1 FROM dbo.DatasetVersion WHERE [Version] = @version)
    BEGIN
        BEGIN TRANSACTION;

        SET @step = N'generate LCG sequence';
        CREATE TABLE #Rng (n int NOT NULL PRIMARY KEY, v int NOT NULL);
        DECLARE @state bigint = 20240601;
        DECLARE @i int = 1;
        WHILE @i <= 113100
        BEGIN
            SET @state = (@state * 1103515245 + 12345) % 2147483648;
            INSERT INTO #Rng (n, v) VALUES (@i, CAST(@state / 65536 AS int));
            SET @i += 1;
        END;

        SET @step = N'load dbo.Customers';
        INSERT INTO dbo.Customers (CustomerId, CustomerName, Segment, Country, City, CreatedDate)
        SELECT c.n,
               CONCAT(N'Customer ', RIGHT(CONCAT(N'000', c.n), 3)),
               CHOOSE(1 + r1.v % 3, N'Consumer', N'Corporate', N'Small Business'),
               CHOOSE(1 + r2.v % 8, N'United States', N'United States', N'Canada', N'United Kingdom',
                                    N'Germany', N'France', N'Australia', N'Japan'),
               CHOOSE(1 + r2.v % 8, N'Seattle', N'Austin', N'Toronto', N'London',
                                    N'Berlin', N'Paris', N'Sydney', N'Tokyo'),
               DATEADD(DAY, r3.v % 365, CAST('2022-01-01' AS date))
        FROM (SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) c
        JOIN #Rng r1 ON r1.n = (c.n - 1) * 5 + 1
        JOIN #Rng r2 ON r2.n = (c.n - 1) * 5 + 2
        JOIN #Rng r3 ON r3.n = (c.n - 1) * 5 + 3;

        SET @step = N'load dbo.Products';
        INSERT INTO dbo.Products (ProductId, ProductName, Category, Subcategory, ListPrice, StandardCost)
        SELECT p.n,
               CONCAT(sub.Subcategory, N' ', RIGHT(CONCAT(N'00', p.n), 2)),
               cat.Category,
               sub.Subcategory,
               CAST(10 + r1.v % 190 + 0.99 AS decimal(10,2)),
               CAST((10 + r1.v % 190 + 0.99) * (40 + r2.v % 30) / 100.0 AS decimal(10,2))
        FROM (SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects) p
        CROSS APPLY (SELECT (p.n - 1) % 5 AS ci) x
        JOIN #Rng r1 ON r1.n = 1000 + (p.n - 1) * 3 + 1
        JOIN #Rng r2 ON r2.n = 1000 + (p.n - 1) * 3 + 2
        JOIN #Rng r3 ON r3.n = 1000 + (p.n - 1) * 3 + 3
        CROSS APPLY (SELECT CHOOSE(x.ci + 1, N'Electronics', N'Home', N'Apparel', N'Sports', N'Grocery') AS Category) cat
        CROSS APPLY (SELECT CHOOSE(x.ci * 2 + 1 + r3.v % 2,
                                   N'Laptops', N'Audio',
                                   N'Kitchen', N'Furniture',
                                   N'Shirts', N'Shoes',
                                   N'Outdoor', N'Fitness',
                                   N'Snacks', N'Beverages') AS Subcategory) sub;

        SET @step = N'load dbo.Stores';
        INSERT INTO dbo.Stores (StoreId, StoreName, Region, Country)
        VALUES (1, N'Seattle Flagship', N'West',  N'United States'),
               (2, N'Austin Central',   N'South', N'United States'),
               (3, N'New York Midtown', N'East',  N'United States'),
               (4, N'Chicago Loop',     N'North', N'United States'),
               (5, N'Toronto Downtown', N'East',  N'Canada'),
               (6, N'London Oxford St', N'North', N'United Kingdom'),
               (7, N'Berlin Mitte',     N'East',  N'Germany'),
               (8, N'Sydney Harbour',   N'West',  N'Australia');

        SET @step = N'load dbo.Employees';
        INSERT INTO dbo.Employees (EmployeeId, FullName, Salary)
        SELECT e.n,
               CONCAT(N'Employee ', RIGHT(CONCAT(N'00', e.n), 2)),
               CAST(40000 + r.v % 60000 AS decimal(10,2))
        FROM (SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects) e
        JOIN #Rng r ON r.n = 2000 + e.n;

        SET @step = N'load dbo.Calendar';
        ;WITH d AS (
            SELECT CAST('2023-01-01' AS date) AS dt
            UNION ALL
            SELECT DATEADD(DAY, 1, dt) FROM d WHERE dt < '2024-12-31'
        )
        INSERT INTO dbo.Calendar (DateKey, [Date], [Year], [Quarter], [Month], MonthName)
        SELECT YEAR(dt) * 10000 + MONTH(dt) * 100 + DAY(dt),
               dt, YEAR(dt), DATEPART(QUARTER, dt), MONTH(dt),
               CHOOSE(MONTH(dt), N'January', N'February', N'March', N'April', N'May', N'June',
                                 N'July', N'August', N'September', N'October', N'November', N'December')
        FROM d
        OPTION (MAXRECURSION 800);

        SET @step = N'load dbo.Orders';
        INSERT INTO dbo.Orders (OrderId, CustomerId, StoreId, OrderDate, Channel, Status)
        SELECT o.n,
               1 + r1.v % 200,
               1 + r2.v % 8,
               DATEADD(DAY, r3.v % 731, CAST('2023-01-01' AS date)),
               CHOOSE(1 + r4.v % 3, N'Online', N'InStore', N'Phone'),
               CASE WHEN r5.v % 100 < 90 THEN N'Completed'
                    WHEN r5.v % 100 < 96 THEN N'Shipped'
                    ELSE N'Cancelled' END
        FROM (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) o
        JOIN #Rng r1 ON r1.n = 3000 + (o.n - 1) * 6 + 1
        JOIN #Rng r2 ON r2.n = 3000 + (o.n - 1) * 6 + 2
        JOIN #Rng r3 ON r3.n = 3000 + (o.n - 1) * 6 + 3
        JOIN #Rng r4 ON r4.n = 3000 + (o.n - 1) * 6 + 4
        JOIN #Rng r5 ON r5.n = 3000 + (o.n - 1) * 6 + 5;

        SET @step = N'load dbo.OrderLines';
        INSERT INTO dbo.OrderLines (OrderLineId, OrderId, ProductId, Quantity, UnitPrice, Discount, UnitCost)
        SELECT ROW_NUMBER() OVER (ORDER BY l.OrderId, l.n),
               l.OrderId,
               pr.ProductId,
               1 + r2.v % 5,
               pr.ListPrice,
               CAST(CHOOSE(1 + r3.v % 5, 0.000, 0.050, 0.100, 0.150, 0.200) AS decimal(4,3)),
               pr.StandardCost
        FROM (
            SELECT o.OrderId, k.n
            FROM dbo.Orders o
            JOIN #Rng rc ON rc.n = 3000 + (o.OrderId - 1) * 6 + 6
            CROSS JOIN (VALUES (1), (2), (3), (4)) k(n)
            WHERE k.n <= CASE WHEN rc.v % 100 < 20 THEN 1
                              WHEN rc.v % 100 < 55 THEN 2
                              WHEN rc.v % 100 < 85 THEN 3
                              ELSE 4 END
        ) l
        JOIN #Rng r1 ON r1.n = 33000 + (l.OrderId - 1) * 16 + (l.n - 1) * 4 + 1
        JOIN #Rng r2 ON r2.n = 33000 + (l.OrderId - 1) * 16 + (l.n - 1) * 4 + 2
        JOIN #Rng r3 ON r3.n = 33000 + (l.OrderId - 1) * 16 + (l.n - 1) * 4 + 3
        JOIN dbo.Products pr ON pr.ProductId = 1 + r1.v % 60;

        SET @step = N'record dataset version';
        INSERT INTO dbo.DatasetVersion ([Version], LoadedAt) VALUES (@version, SYSUTCDATETIME());

        COMMIT TRANSACTION;
        DROP TABLE #Rng;
        PRINT 'Dataset loaded.';
    END
    ELSE
        PRINT 'Dataset already loaded; data left unchanged.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('FAILED at step: ', @step, ' - ', ERROR_MESSAGE());
    THROW;
END CATCH
GO

-- ---------------------------------------------------------------------------
-- Step 4: Read_Only_User (SELECT on Allowlist objects only)
-- ---------------------------------------------------------------------------
DECLARE @step nvarchar(200) = N'create login askmydata_reader';
DECLARE @sql nvarchar(max);
BEGIN TRY
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'askmydata_reader')
    BEGIN
        SET @sql = N'CREATE LOGIN [askmydata_reader] WITH PASSWORD = N''$(READONLY_PASSWORD)'', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END;

    SET @step = N'create user askmydata_reader';
    IF DATABASE_PRINCIPAL_ID(N'askmydata_reader') IS NULL
        CREATE USER [askmydata_reader] FOR LOGIN [askmydata_reader];

    SET @step = N'grant SELECT on Allowlist objects';
    GRANT SELECT ON OBJECT::dbo.Customers  TO [askmydata_reader];
    GRANT SELECT ON OBJECT::dbo.Products   TO [askmydata_reader];
    GRANT SELECT ON OBJECT::dbo.Stores     TO [askmydata_reader];
    GRANT SELECT ON OBJECT::dbo.Orders     TO [askmydata_reader];
    GRANT SELECT ON OBJECT::dbo.OrderLines TO [askmydata_reader];
    GRANT SELECT ON OBJECT::dbo.Calendar   TO [askmydata_reader];
END TRY
BEGIN CATCH
    PRINT CONCAT('FAILED at step: ', @step, ' - ', ERROR_MESSAGE());
    THROW;
END CATCH
GO
