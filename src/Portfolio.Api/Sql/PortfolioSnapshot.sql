-- Canonical snapshot calculation, used by the HTTP API and executable directly in SSMS.
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
    DECLARE @LockResult int, @Resource nvarchar(255) = N'account:' + @AccountId;
    EXEC @LockResult = sys.sp_getapplock @Resource=@Resource, @LockMode='Shared',
        @LockOwner='Transaction', @LockTimeout=10000;
    IF @LockResult < 0
    BEGIN
        ROLLBACK;
        THROW 51000, 'Could not acquire snapshot lock; retry the request.', 1;
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
        SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE -Quantity END)
            OVER (PARTITION BY InstrumentId ORDER BY Step ROWS UNBOUNDED PRECEDING) AS RunningQuantity
    INTO #Ordered FROM Effective
    OPTION (RECOMPILE);
    COMMIT;

    IF EXISTS (SELECT 1 FROM #Ordered WHERE RunningQuantity < 0)
        THROW 51001, 'The selected historical ledger contains a short position.', 1;

    CREATE UNIQUE CLUSTERED INDEX IX_Ordered ON #Ordered (InstrumentId, Step);

    -- Relational recurrence across each instrument's ordered events. BUY increases carrying
    -- cost; SELL removes cost at the pre-sale moving average, never at sale proceeds.
    ;WITH Ledger AS
    (
        SELECT InstrumentId, Step, RunningQuantity,
            CAST(Quantity * Price AS decimal(28,8)) AS CostBasis
        FROM #Ordered WHERE Step = 1
        UNION ALL
        SELECT o.InstrumentId, o.Step, o.RunningQuantity,
            CAST(CASE WHEN o.Side = 'BUY' THEN l.CostBasis + CAST(o.Quantity * o.Price AS decimal(28,8))
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
