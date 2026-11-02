IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE TABLE [Instruments] (
        [Isin] nvarchar(12) NOT NULL,
        [Symbol] nvarchar(20) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Instruments] PRIMARY KEY ([Isin])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE TABLE [Prices] (
        [InstrumentId] nvarchar(12) NOT NULL,
        [Date] date NOT NULL,
        [Price] decimal(19,6) NOT NULL,
        CONSTRAINT [PK_Prices] PRIMARY KEY ([InstrumentId], [Date]),
        CONSTRAINT [CK_Prices_Positive] CHECK ([Price] > 0),
        CONSTRAINT [FK_Prices_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Isin]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE TABLE [TradeRevisions] (
        [Id] bigint NOT NULL IDENTITY,
        [ExternalRef] nvarchar(80) NOT NULL,
        [AccountId] nvarchar(64) NOT NULL,
        [InstrumentId] nvarchar(12) NOT NULL,
        [Side] nvarchar(4) NOT NULL,
        [Quantity] decimal(19,6) NOT NULL,
        [Price] decimal(19,6) NOT NULL,
        [TradeDate] date NOT NULL,
        [AsOf] datetimeoffset NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_TradeRevisions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TradeRevisions_Price] CHECK ([Price] > 0 AND [Price] <= 1000000000),
        CONSTRAINT [CK_TradeRevisions_Quantity] CHECK ([Quantity] > 0 AND [Quantity] <= 1000000000),
        CONSTRAINT [CK_TradeRevisions_Side] CHECK ([Side] IN ('BUY', 'SELL')),
        CONSTRAINT [FK_TradeRevisions_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Isin]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE TABLE [IngestionReceipts] (
        [Id] bigint NOT NULL IDENTITY,
        [ExternalRef] nvarchar(80) NOT NULL,
        [AccountId] nvarchar(64) NOT NULL,
        [AsOf] datetimeoffset NOT NULL,
        [Outcome] nvarchar(32) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [TraceId] nvarchar(64) NOT NULL,
        [RevisionId] bigint NULL,
        CONSTRAINT [PK_IngestionReceipts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IngestionReceipts_TradeRevisions_RevisionId] FOREIGN KEY ([RevisionId]) REFERENCES [TradeRevisions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Isin', N'Name', N'Symbol') AND [object_id] = OBJECT_ID(N'[Instruments]'))
        SET IDENTITY_INSERT [Instruments] ON;
    EXEC(N'INSERT INTO [Instruments] ([Isin], [Name], [Symbol])
    VALUES (N''US0378331005'', N''Apple Inc.'', N''AAPL''),
    (N''US5949181045'', N''Microsoft Corporation'', N''MSFT'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Isin', N'Name', N'Symbol') AND [object_id] = OBJECT_ID(N'[Instruments]'))
        SET IDENTITY_INSERT [Instruments] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Date', N'InstrumentId', N'Price') AND [object_id] = OBJECT_ID(N'[Prices]'))
        SET IDENTITY_INSERT [Prices] ON;
    EXEC(N'INSERT INTO [Prices] ([Date], [InstrumentId], [Price])
    VALUES (''2025-03-01'', N''US0378331005'', 186.0),
    (''2025-03-03'', N''US0378331005'', 190.0),
    (''2025-03-01'', N''US5949181045'', 400.0)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Date', N'InstrumentId', N'Price') AND [object_id] = OBJECT_ID(N'[Prices]'))
        SET IDENTITY_INSERT [Prices] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_IngestionReceipts_AccountId_ReceivedAt] ON [IngestionReceipts] ([AccountId], [ReceivedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_IngestionReceipts_RevisionId] ON [IngestionReceipts] ([RevisionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TradeRevisions_AccountId_ExternalRef_AsOf] ON [TradeRevisions] ([AccountId], [ExternalRef], [AsOf]) INCLUDE ([InstrumentId], [Side], [Quantity], [Price], [TradeDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TradeRevisions_ExternalRef_AsOf] ON [TradeRevisions] ([ExternalRef], [AsOf]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TradeRevisions_InstrumentId] ON [TradeRevisions] ([InstrumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075643_InitialSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102075643_InitialSchema', N'8.0.33');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075648_SnapshotProcedure'
)
BEGIN
    EXEC(N'-- Canonical snapshot calculation, used by the HTTP API and executable directly in SSMS.
    -- Identity fields are immutable; choose versions BEFORE filtering their effective trade dates.
    CREATE OR ALTER PROCEDURE dbo.GetPortfolioSnapshot
        @AccountId nvarchar(64),
        @ValuationDate date,
        @AsOf datetimeoffset(7) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        -- Serialize materialization with account writers without holding locks during valuation.
        BEGIN TRANSACTION;
        DECLARE @LockResult int, @Resource nvarchar(255) = N''account:'' + @AccountId;
        EXEC @LockResult = sys.sp_getapplock @Resource=@Resource, @LockMode=''Shared'',
            @LockOwner=''Transaction'', @LockTimeout=10000;
        IF @LockResult < 0
        BEGIN
            ROLLBACK;
            THROW 51000, ''Could not acquire snapshot lock; retry the request.'', 1;
        END;
        ;WITH Versions AS
        (
            SELECT ExternalRef, InstrumentId, Side, Quantity, Price, TradeDate,
                ROW_NUMBER() OVER (PARTITION BY ExternalRef ORDER BY AsOf DESC) AS VersionRank
            FROM dbo.TradeRevisions
            WHERE AccountId = @AccountId AND (@AsOf IS NULL OR AsOf <= @AsOf)
        ), Effective AS
        (
            SELECT *, ROW_NUMBER() OVER
                (PARTITION BY InstrumentId ORDER BY TradeDate, ExternalRef) AS Step
            FROM Versions WHERE VersionRank = 1 AND TradeDate <= @ValuationDate
        )
        SELECT InstrumentId, Side, Quantity, Price, Step,
            SUM(CASE WHEN Side = ''BUY'' THEN Quantity ELSE -Quantity END)
                OVER (PARTITION BY InstrumentId ORDER BY Step ROWS UNBOUNDED PRECEDING) AS RunningQuantity
        INTO #Ordered FROM Effective
        OPTION (RECOMPILE);
        COMMIT;

        IF EXISTS (SELECT 1 FROM #Ordered WHERE RunningQuantity < 0)
            THROW 51001, ''The selected historical ledger contains a short position.'', 1;

        CREATE UNIQUE CLUSTERED INDEX IX_Ordered ON #Ordered (InstrumentId, Step);

        -- Relational recurrence across each instrument''s ordered events. BUY increases carrying
        -- cost; SELL removes cost at the pre-sale moving average, never at sale proceeds.
        ;WITH Ledger AS
        (
            SELECT InstrumentId, Step, RunningQuantity,
                CAST(Quantity * Price AS decimal(28,8)) AS CostBasis
            FROM #Ordered WHERE Step = 1
            UNION ALL
            SELECT o.InstrumentId, o.Step, o.RunningQuantity,
                CAST(CASE WHEN o.Side = ''BUY'' THEN l.CostBasis + CAST(o.Quantity * o.Price AS decimal(28,8))
                    WHEN o.RunningQuantity = 0 THEN 0
                    ELSE l.CostBasis - CAST(o.Quantity *
                        CAST(l.CostBasis / NULLIF(CAST(l.RunningQuantity AS decimal(19,6)), 0) AS decimal(28,12)) AS decimal(28,8))
                    END AS decimal(28,8))
            FROM Ledger l JOIN #Ordered o ON o.InstrumentId = l.InstrumentId AND o.Step = l.Step + 1
        ), LastRows AS
        (
            SELECT *, ROW_NUMBER() OVER (PARTITION BY InstrumentId ORDER BY Step DESC) AS LastRank
            FROM Ledger
        )
        SELECT l.InstrumentId, i.Symbol, CAST(l.RunningQuantity AS decimal(19,6)) AS Quantity,
            CAST(l.CostBasis / NULLIF(CAST(l.RunningQuantity AS decimal(19,6)), 0) AS decimal(19,8)) AS UnitCostUsd,
            l.CostBasis AS CostBasisUsd, p.Price AS MarketPriceUsd, p.Date AS PriceDate,
            CAST(CAST(l.RunningQuantity AS decimal(19,6)) * p.Price AS decimal(28,8)) AS MarketValueUsd,
            CAST(CAST(CAST(l.RunningQuantity AS decimal(19,6)) * p.Price AS decimal(28,8)) - l.CostBasis AS decimal(28,8)) AS UnrealizedPnlUsd
        INTO #Positions
        FROM LastRows l JOIN dbo.Instruments i ON i.Isin = l.InstrumentId
        OUTER APPLY
        (
            SELECT TOP (1) mp.Price, mp.Date FROM dbo.Prices mp
            WHERE mp.InstrumentId = l.InstrumentId AND mp.Date <= @ValuationDate
            ORDER BY mp.Date DESC
        ) p
        WHERE l.LastRank = 1 AND l.RunningQuantity > 0
        OPTION (MAXRECURSION 0);

        SELECT * FROM #Positions ORDER BY InstrumentId;
        SELECT
            CASE WHEN COUNT(*) = COUNT(MarketPriceUsd) THEN COALESCE(SUM(MarketValueUsd), 0) END AS TotalMarketValueUsd,
            CASE WHEN COUNT(*) = COUNT(MarketPriceUsd) THEN COALESCE(SUM(MarketValueUsd), 0) END AS OverallValueUsd,
            COALESCE(SUM(CostBasisUsd), 0) AS TotalCostBasisUsd,
            CASE WHEN COUNT(*) = COUNT(MarketPriceUsd) THEN COALESCE(SUM(UnrealizedPnlUsd), 0) END AS UnrealizedPnlUsd
        FROM #Positions;
    END;
    ');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102075648_SnapshotProcedure'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102075648_SnapshotProcedure', N'8.0.33');
END;
GO

COMMIT;
GO

