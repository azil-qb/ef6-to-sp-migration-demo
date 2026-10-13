# EF Query to Stored Procedure — Memory-Bounded Report Migration

## Purpose

Use this skill to migrate a large, memory-intensive Entity Framework/EF Core report query to a SQL Server stored procedure backed by Dapper and bounded batch streaming.

The primary goal is not merely to replace LINQ with SQL. The implementation must reduce peak memory on both SQL Server and the application by:

- pushing set-based filtering/joining into SQL;
- avoiding full-result materialization;
- sending large ID sets as TVPs;
- materializing TVPs into indexed temporary tables so SQL Server can build statistics;
- processing the workload in bounded account chunks;
- returning multiple related result sets in one database round-trip per batch;
- keeping only one bounded batch in application memory;
- mapping/enriching each batch and yielding it onward;
- avoiding N+1 queries and O(n²) enrichment;
- preserving the existing report's business behavior;
- allowing gradual rollout with a feature flag;
- providing unit tests for mapping, filtering, enrichment, and multi-batch behavior.

This skill is based on the repository's established V2 bulk-report migration pattern.

## Non-negotiable rules

1. **Do not start implementation until repository discovery is complete.**
2. **Do not invent table columns, keys, relationships, types, indexes, filters, or business rules.**
3. **Inspect the existing EF query, related query classes, existing stored procedures, models, providers, DI registration, job engine, feature flags, and tests before changing code.**
4. **Locate every existing V1 stored procedure before designing the V2 procedure.**
5. **Enumerate every table/view/TVP/temp table referenced by the existing SQL.**
6. **Obtain exact table definitions before generating SQL or Dapper models when the schema is not already available.**
7. **Do not use a single giant query execution that materializes the complete report in application memory.**
8. **Do not call the database once per account or once per output line.**
9. **Do not introduce `ToList()` over the complete report. `ToList()` is permitted only for a bounded batch when required for in-place enrichment.**
10. **Do not silently change business semantics while optimizing the data-access implementation.**
11. **Do not remove existing report-specific behavior merely because it is inconvenient to migrate.**
12. **Reuse existing repository helpers, TVP types, connection abstractions, naming conventions, and registration patterns whenever they already exist.**
13. **Use the repository's established synchronous `IEnumerable` streaming model when the report engine's `GetQuery()` is synchronous. Do not introduce `IAsyncEnumerable` solely because it sounds more modern.**
14. **Keep result-set ordering between SQL and Dapper exact and explicit.**
15. **Always include cleanup and exception propagation in the stored procedure.**
16. **The final implementation must be buildable and tested.**

---

## Workflow and mandatory approval gates

This skill is deliberately human-in-the-loop because the client requirement is performance improvement, not merely a code translation.

The workflow is:

```text
Phase 0 — Discovery + V1 baseline
        ↓
Gate 1 — Developer approves discovery/design
        ↓
Phase 1 — Stored procedure + comparison script
        ↓
Gate 2 — Run EF SQL vs stored procedure in SSMS
        ↓
        ├── unacceptable → provide plan/IO/TIME → Copilot iterates SP
        │                                      ↺
        └── acceptable → Developer approves database result
        ↓
Phase 2–8 — Dapper + batching + streaming + tests
        ↓
Gate 3 — End-to-end application performance validation
        ↓
Phase 9–11 — behavioral review + build/PR gate
```

### Approval semantics

"Complete a phase" does not mean "continue automatically."

At every mandatory gate, Copilot must stop and wait for an explicit developer instruction before proceeding.

Valid approval examples include:

- `Proceed`
- `Approved`
- `Continue`
- `Go ahead`

If the developer supplies corrections instead of approval, incorporate them and stop again.

### Performance semantics

There are two performance layers:

1. **Database performance:** compare the actual EF-generated SQL with the new stored procedure using equivalent parameters and representative data.
2. **Application performance:** validate Dapper batching, bounded materialization, streaming, enrichment, and application memory in the complete V2 path.

The SSMS comparison is necessary but cannot prove the full benefit of batching and streaming.


# Phase 1 — Database design and stored procedure implementation

This phase begins only after Gate 1 approval.

Design and create the replacement stored procedure first. Do not implement the Dapper provider, report generator, feature flag, or DI changes yet.

The objective is to create a database implementation that is functionally equivalent to the V1 EF query and is suitable for bounded application-side consumption.


Design the replacement around this flow:

    existing report inputs
        -> build account/security ID sets
        -> split account IDs into bounded chunks
        -> execute one stored procedure per chunk
        -> SQL materializes TVPs into indexed temp tables
        -> SQL produces bounded primary rows
        -> SQL returns related result sets in one round-trip
        -> Dapper reads one batch
        -> map batch rows
        -> filter batch
        -> enrich batch using in-memory lookups
        -> yield report lines
        -> next batch

The critical property is that memory usage is proportional to one batch, not the entire report.

Do not confuse database streaming with application streaming. A single huge result set can still create substantial SQL Server memory pressure and application memory pressure. Use bounded database requests.

## Batch sizing

Use the repository's validated default if one exists. In the reference pattern the default is 7,500 accounts per database round-trip.

Do not blindly increase the batch size. Tune it using actual profiling if necessary.

Keep chunk size as an implementation detail of the SQL provider rather than exposing it in the business-facing provider interface unless the repository already follows a different convention.

---




Create a stored procedure that accepts the report's scalar filters plus TVPs for large ID sets.

Use the repository's existing TVP type, for example:

```sql
@AccountIds IdTable READONLY,
@SecurityIds IdTable READONLY
```

## 1.3 Required SQL initialization

Use:

```sql
SET XACT_ABORT ON;
SET NOCOUNT ON;
```

Use `TRY/CATCH` and rethrow:

```sql
BEGIN TRY
    ...
END TRY
BEGIN CATCH
    THROW;
END CATCH;
```

Do not swallow exceptions.

## 1.4 Materialize TVPs into indexed temp tables

TVPs do not have column statistics. For large joins, immediately copy them into temporary tables with keys/indexes.

Pattern:

```sql
CREATE TABLE #AccountIds
(
    Id INT NOT NULL PRIMARY KEY
);

INSERT INTO #AccountIds (Id)
SELECT Id
FROM @AccountIds;

CREATE TABLE #SecurityIds
(
    Id INT NOT NULL PRIMARY KEY
);

INSERT INTO #SecurityIds (Id)
SELECT Id
FROM @SecurityIds;
```

This is an important query-plan/cardinality technique, not merely a coding preference.

Do this before large joins.

## 1.5 Materialize primary rows once

When several result sets depend on the same primary rows, materialize the primary dataset into a temp table instead of repeatedly rescanning base tables.

Example:

```sql
;WITH Base AS
(
    SELECT
        r.ReportId,
        r.AccountId,
        r.SecurityId
        -- only required columns
    FROM dbo.ReportTable r WITH (NOLOCK)
    INNER JOIN #AccountIds a
        ON r.AccountId = a.Id
    INNER JOIN #SecurityIds s
        ON r.SecurityId = s.Id
    WHERE
        (@StartDate IS NULL OR r.ReportDate >= @StartDate)
        AND (@EndDate IS NULL OR r.ReportDate <= @EndDate)
)
SELECT *
INTO #Rows
FROM Base;

CREATE NONCLUSTERED INDEX IX_Rows_ReportId
    ON #Rows (ReportId);
```

Use the actual schema and required keys from the repository. Do not blindly copy the example.

The primary-row temp table should have an index on the key used by subsequent related result-set joins.

## 1.6 Return multiple result sets deliberately

Return result sets in a fixed documented order.

For example:

1. primary rows;
2. approvals;
3. notes.

Every additional result set must join to the already-materialized primary rows where practical, rather than repeating the original expensive base-table scan.

Example:

```sql
SELECT *
FROM #Rows;

SELECT
    j.ReportId,
    j.Column1
FROM dbo.ReportJoinTable j WITH (NOLOCK)
INNER JOIN #Rows r
    ON j.ReportId = r.ReportId;
```

The C# provider must consume the exact same order with `QueryMultiple`.

## 1.7 Temp-table cleanup

Drop temporary tables before successful completion:

```sql
DROP TABLE IF EXISTS #AccountIds, #SecurityIds, #Rows;
```

Keep cleanup in the `TRY` path and allow SQL Server's session cleanup to handle exceptional termination as appropriate.

---



## 1.1 Create a reproducible database comparison script

Create a SQL test script alongside the stored procedure. It must contain:

- declarations/population of realistic TVP parameters;
- representative scalar parameters;
- the stored-procedure execution;
- the exact EF-generated SQL captured during Phase 0, with equivalent parameters;
- optional result-count checks where practical;
- `SET STATISTICS IO ON`;
- `SET STATISTICS TIME ON`;
- clear comments identifying which execution is V1 EF SQL and which is V2 stored procedure.

The script must make it easy for a developer to run both database operations in SSMS using equivalent parameters.

Do not claim that this script proves end-to-end application performance. It is specifically for database-level comparison.

## 1.2 Functional comparison before performance comparison

Before judging performance, verify that the EF SQL and stored procedure produce equivalent result sets for the representative test case.

Compare:

- row counts;
- key columns;
- important output columns;
- null behavior;
- duplicate behavior;
- related result sets;
- date-boundary behavior.

If exact comparison is impractical for a huge result, provide deterministic aggregate/key-count checks or another repository-appropriate validation method. Do not silently assume equivalence.

### Gate 2 — Stored procedure and database-performance validation

After the stored procedure and comparison script are created, **STOP**.

The developer should run the comparison in SSMS using representative parameters and inspect:

- elapsed time;
- CPU time;
- logical reads;
- physical reads where relevant;
- memory grant;
- spills;
- actual execution plan;
- tempdb behavior where relevant;
- returned row counts/results.

The minimum objective is to establish that the stored procedure is not a significant database-side regression. A material improvement is preferred, but the skill must not promise a fixed percentage because batching and application streaming provide additional improvements later.

If the stored procedure is worse or otherwise unacceptable, do not proceed to application implementation.

Instead, request the relevant evidence, preferably:

- actual execution plan for the EF SQL;
- actual execution plan for the stored procedure;
- `STATISTICS IO/TIME` output;
- parameter values/cardinality;
- observed bottleneck.

Then iterate on the stored procedure.

### Database performance iteration loop

Use this loop until the database result is acceptable:

```text
Run EF SQL and stored procedure
        ↓
Compare correctness
        ↓
Compare database performance
        ↓
Acceptable?
   ┌────┴────┐
   │         │
  YES        NO
   │         │
   ↓         ↓
Gate 3     Analyze plan/
approval   IO/TIME
             ↓
          Modify SP
             ↓
          Re-test
```

When the database result is acceptable, ask for explicit developer approval before moving to Phase 2.

Do not interpret "acceptable" as "must be faster in every metric." Judge the trade-off using the baseline and the migration's stated performance objective. If one metric regresses materially, investigate it rather than hiding it behind an aggregate result.

### Important boundary

SSMS execution validates the database operation only.

It does **not** validate:

- Dapper object materialization;
- bounded application memory;
- batch-to-batch behavior;
- report-line enrichment;
- garbage-collection behavior;
- HTTP/report streaming;
- end-to-end application latency.

Those are validated later in the application-performance gate.

# Phase 2 — Define Dapper contracts

Create a row model representing the primary stored-procedure result set.

Example:

```csharp
public class {Report}Data
{
    public long {Report}Id { get; set; }
    public int AccountId { get; set; }
    public int SecurityId { get; set; }

    // Other properties must exactly match the stored-procedure result columns.
}
```

Dapper maps by column name. Therefore:

- aliases must be intentional;
- names must match the model;
- nullable SQL columns need nullable C# types;
- do not add properties that are not returned;
- do not return unnecessary columns.

Create a batch model containing one collection per result set:

```csharp
public class {Report}Batch
{
    public IEnumerable<{Report}Data> Rows { get; set; }

    // Add one property for each additional result set.
}
```

Only return related result sets that are actually needed by the report.

---

# Phase 3 — Dapper provider
Create an interface similar to:

```csharp
public interface I{Report}Provider
{
    IEnumerable<{Report}Batch> StreamBatched(
        int entId,
        HashSet<int> accountIds,
        HashSet<int> securityIds,
        DateRange dateRange);
}
```

Adjust parameters to the actual report.

## 4.1 Empty-input guards

Do not execute meaningless stored-procedure calls:

```csharp
if (!accountIdSet.Any())
    yield break;

if (!securityIdSet.Any())
    yield break;
```

Only add guards for inputs that are actually required.

## 4.2 Connection lifetime

Open one connection/accessor for the whole `StreamBatched` enumeration rather than reopening it for every chunk.

The reference architecture deliberately keeps one connection across the enumeration because reopening per chunk adds latency.

If the repository's connection abstraction requires a different lifetime, follow that established abstraction.

## 4.3 Chunk the large ID set

Reuse an existing helper such as:

```csharp
EnumerableHelpers.ChunkBy(accountIdSet, DefaultChunkSize)
```

Do not recreate an existing helper.

## 4.4 Build parameters per chunk

Use `DynamicParameters` and the repository's TVP parameter type:

```csharp
var parameters = new DynamicParameters();

parameters.Add(
    "AccountIds",
    new IdTableParam(accountBatch));

parameters.Add(
    "SecurityIds",
    new IdTableParam(securityIdSet));

parameters.Add(
    "StartDate",
    dateRange?.StartDate,
    DbType.DateTime2);

parameters.Add(
    "EndDate",
    dateRange?.EndDate,
    DbType.DateTime2);
```

Use the actual parameter names and types.

## 4.5 One round-trip per batch

Use Dapper `QueryMultiple` when multiple result sets are needed:

```csharp
using var multi = accessor.QueryMultiple(
    "{SP_Name}",
    parameters,
    commandType: CommandType.StoredProcedure,
    commandTimeout: CommandTimeoutSeconds);
```

Then read the result sets in the exact SQL order:

```csharp
yield return new {Report}Batch
{
    Rows = multi.Read<{Report}Data>().ToList()
};
```

Materializing a single bounded batch with `ToList()` is acceptable. Materializing the complete report is not.

---

# Phase 4 — V2 report/generator
Create the V2 report implementation while preserving the existing report contract and behavior.

The V2 class should:

- extend the same base class as V1;
- inject the new provider;
- retain required prerequisite queries/providers;
- preserve report metadata;
- preserve sorting and filtering semantics;
- preserve custom-field behavior;
- preserve date-range semantics;
- preserve report-specific enrichment;
- use `GetQuery()` as a lazy `IEnumerable<BasicReportLine>`;
- process one batch at a time.

## Core pattern

```csharp
public override IEnumerable<BasicReportLine> GetQuery()
{
    var accounts = _acctInfoAv.GetQuery()
        .Cast<AccountInfoAVReportLine>()
        .ToDictionary(a => a.UploadAccountId);

    var securities = _secInfoAv.GetQuery()
        .Cast<SecurityInfoAVReportLine>()
        .ToDictionary(s => s.SecurityId);

    var accountIdSet = new HashSet<int>(
        accounts.Keys.Where(k => k.HasValue).Select(k => k.Value));

    var securityIdSet = new HashSet<int>(
        securities.Keys.Where(k => k.HasValue).Select(k => k.Value));

    var dateRange = ReadDateRange();

    var needsEnrichment = DetermineEnrichmentNeeds();

    foreach (var batch in _provider.StreamBatched(
        this.EntId,
        accountIdSet,
        securityIdSet,
        dateRange))
    {
        IEnumerable<{ReportLine}> batchLines =
            MapRows(batch.Rows, accounts, securities);

        var batchList = batchLines.ToList();

        if (needsEnrichment && batchList.Count > 0)
            EnrichLines(batchList, batch);

        foreach (var line in batchList)
            yield return line;
    }
}
```

The exact implementation must reflect the existing report.

## Do not accidentally reintroduce memory growth

Avoid:

```csharp
var allRows = provider.GetAll(...).ToList();
```

Avoid:

```csharp
var allLines = batches.SelectMany(...).ToList();
```

Avoid storing every generated line in a class-level collection.

Avoid database calls from inside the per-line mapping loop.

The batch is the memory boundary.

---

# Phase 5 — Mapping and enrichment
Keep transformation logic separate from database access.

Use small private methods such as:

```csharp
private static IEnumerable<{ReportLine}> MapRows(...)
```

Mapping should be:

- deterministic;
- side-effect free where possible;
- database-free;
- easy to unit test.

Use dictionaries for repeated lookup:

```csharp
Dictionary<int?, AccountInfoAVReportLine>
Dictionary<int?, SecurityInfoAVReportLine>
```

Do not search a list for every row.

For enrichment, build lookup dictionaries once per batch:

```csharp
var notesByReportId = notes
    .OrderBy(n => n.Date)
    .GroupBy(n => n.ReportId)
    .ToDictionary(g => g.Key, g => g.ToList());
```

Then perform O(1)-style dictionary lookups per line.

The desired complexity is approximately O(n) for a batch, not O(n²).

Any enrichment that can be performed from result sets already returned by the stored procedure should not issue another database call per line.

---

# Phase 6 — Feature flag and rollout
Add a report-specific feature flag.

Example:

```csharp
public const string Use{Report}V2 =
    "Tamarac.Reporting.Use{Report}V2";
```

The existing V1 query should remain available during migration.

The job engine should support:

1. the original report name selecting V1 or V2 through the feature flag;
2. a direct V2 report name where the existing architecture supports it.

Example:

```csharp
case "{OldQuery}":
    query = FeatureFlagManager.IsFeatureEnabled(
        FeatureFlagManager.Use{Report}V2,
        settings.EnterpriseAccountGroups.FirstOrDefault()?.EnterpriseId)
        ? (BulkReportQuery)AutofacHostFactory.Container.Resolve<{Report}V2>()
        : new {OldQuery}();
    break;

case "{OldQuery}V2":
case "{Report}V2":
    query = AutofacHostFactory.Container.Resolve<{Report}V2>();
    break;
```

If the report participates in existing propagation logic such as order-blotter or trader-note behavior, update those guards for V2 as well.

Feature flags are used for gradual rollout and rollback without changing code.

---

# Phase 7 — Dependency injection
Register the provider using the repository's existing lifetime convention.

Typical pattern:

```csharp
builder
    .RegisterType<{Report}Provider>()
    .As<I{Report}Provider>()
    .InstancePerLifetimeScope();

builder.RegisterType<{Report}V2>();
```

Do not duplicate shared provider registrations.

Verify that Autofac can resolve all constructor dependencies.

---

# Phase 8 — Tests
Create unit tests following the repository's existing test framework and conventions.

The reference pattern uses an inner `Testable` subclass to override environment-dependent behavior such as `EntId` and date-range calculation.

Use substitutes for:

- report provider;
- product provider;
- prerequisite queries/providers.

Create helpers such as:

- `BuildQuery()`;
- `MakeRow()`;
- `SetupProvider()`.

## Required test coverage

At minimum verify:

- correct line type;
- empty provider result;
- successful account/security mapping;
- unknown account is skipped or handled exactly as V1;
- unknown security is skipped or handled exactly as V1;
- multiple database batches are streamed;
- report-specific enrichment when enabled;
- report-specific enrichment when disabled;
- relevant feature-flag behavior;
- all important business-rule branches.

The multi-batch test is important because it verifies that the implementation does not accidentally collapse the result into one collection.

---

# Phase 9 — Behavioral equivalence review
Before declaring the migration complete, compare V1 and V2 behavior.

Verify:

- same filters;
- same date-range interpretation;
- same account/security selection;
- same null handling;
- same joins;
- same output fields;
- same aliases;
- same sorting expectations;
- same custom fields;
- same enrichment;
- same conditional business behavior;
- same empty-input semantics;
- same report settings;
- same downstream GCS/export behavior if applicable.

Performance optimization must not become an accidental functional rewrite.

---

# Phase 10 — Application performance validation
Before declaring the migration complete, compare V1 and V2 behavior.

Verify:

- same filters;
- same date-range interpretation;
- same account/security selection;
- same null handling;
- same joins;
- same output fields;
- same aliases;
- same sorting expectations;
- same custom fields;
- same enrichment;
- same conditional business behavior;
- same empty-input semantics;
- same report settings;
- same downstream GCS/export behavior if applicable.

Performance optimization must not become an accidental functional rewrite.

---

The problem being solved is memory pressure, so validate memory behavior explicitly.

Measure or inspect:

- SQL Server memory grants;
- query execution plan;
- logical reads;
- CPU;
- duration;
- tempdb usage where relevant;
- number of database round-trips;
- application working-set/heap behavior;
- peak memory during a large report;
- batch size;
- rows returned per batch;
- whether application memory returns toward baseline between batches.

Confirm that the application no longer materializes the full report.

Confirm that SQL Server is not repeatedly rescanning large base tables for related result sets.

If performance is poor, profile before changing the batch size or adding hints.

Do not add arbitrary `OPTION`, index, or locking hints without evidence and repository conventions.

---



## 10.1 Compare against the same representative scenario

Use the same representative dataset and parameter set used during Gate 2 whenever practical.

Compare:

- V1 EF application execution;
- V2 stored-procedure execution through Dapper;
- end-to-end report duration;
- application peak memory;
- SQL duration;
- number of database calls;
- rows processed;
- batch size;
- CPU/GC behavior where available.

The purpose of this test is different from Gate 2.

Gate 2 asks:

> Does the stored procedure provide an acceptable database implementation compared with the actual EF-generated SQL?

Gate 3 asks:

> Does the complete V2 implementation solve the application-side memory/scalability problem through bounded batching and streaming?

Do not attribute all end-to-end improvement to the stored procedure. Database optimization and bounded application streaming solve different parts of the problem.

## 10.2 Validate bounded memory

For a large report:

- verify that only one bounded batch is materialized at a time;
- verify there is no full-report `ToList()`, `ToArray()`, or equivalent;
- verify enrichment does not retain previous batches;
- verify related result sets are bounded to the current batch;
- verify the application does not accumulate all output lines before returning them;
- record peak application memory where a representative load test is available.

The target is not necessarily a specific absolute memory number. The target is that memory growth is bounded by the batch and processing overhead rather than by total report size.

## 10.3 Validate scalability characteristics

Where practical, run at least two representative sizes:

- a moderate report;
- a large report.

Confirm that increasing the total report size does not cause application memory to grow linearly with the complete result set.

If the report engine is synchronous, preserve the repository's established `IEnumerable` streaming behavior rather than changing the entire execution model merely for this migration.

# Phase 11 — Build and PR gate
Before completion:

- build the solution with no warnings introduced by the migration;
- run all relevant unit tests;
- run integration/database tests when available;
- verify stored procedure deployment/build;
- verify Dapper mappings;
- verify feature-flag resolution;
- verify DI resolution;
- verify the V1 path remains intact;
- review the generated SQL for unnecessary columns and joins.

Use this final checklist:

```text
[ ] V1 EF query fully understood
[ ] Existing V1 SPs located and read
[ ] All referenced tables/views/TVPs identified
[ ] Exact table definitions available and reviewed
[ ] Primary keys and join columns verified
[ ] Nullable SQL columns mapped correctly
[ ] Dapper row model matches SP column names
[ ] Batch model matches SP result-set count/order
[ ] TVPs materialized into indexed temp tables
[ ] Primary rows materialized once when reused
[ ] Appropriate temp-table indexes created
[ ] Result sets returned in deterministic order
[ ] Temp tables cleaned up
[ ] SQL exceptions rethrown with THROW
[ ] Empty input guarded
[ ] Account IDs processed in bounded chunks
[ ] One connection used across StreamBatched enumeration
[ ] QueryMultiple used for related result sets
[ ] Only one bounded batch materialized in application memory
[ ] GetQuery uses lazy yield-based streaming
[ ] No full-report ToList()
[ ] No N+1 database calls
[ ] Enrichment uses per-batch lookup dictionaries
[ ] Enrichment is O(n), not O(n²)
[ ] Existing business behavior preserved
[ ] Feature flag added
[ ] V1/V2 switch implemented
[ ] Direct V2 path implemented where required
[ ] DI registration added
[ ] Mapping tests added
[ ] Unknown-key tests added
[ ] Multi-batch streaming test added
[ ] Enrichment tests added where applicable
[ ] Solution builds successfully
[ ] Tests pass
[ ] Large-report memory/performance behavior validated
```

# Output expectations for Copilot

When asked to perform this migration, do not immediately dump a large implementation.

First report the discovered V1 query, SPs, tables, schema dependencies, existing V2 patterns, and any missing information.

If table definitions are missing and are necessary to safely generate SQL/models, stop and ask for them.

Once the prerequisites are complete, implement the migration in repository order:

1. contracts/models;
2. provider interface;
3. stored procedure;
4. Dapper provider;
5. V2 generator;
6. feature flag;
7. job-engine integration;
8. DI registration;
9. tests;
10. build/test/validation.

When modifying an existing repository, prefer small, reviewable changes and reuse existing patterns rather than introducing a new abstraction solely for this migration.

# Important adaptation rule

The reference report uses `AccountIds`, `SecurityIds`, `DateRange`, `IdTable`, `EnumerableHelpers.ChunkBy`, `IProductProvider`, `AccountInfoAV`, `SecurityInfoAV`, `AVQueryBase`, and `BasicReportLine`.

These are examples of the established architecture, not universal requirements.

If the target repository uses different names or abstractions, map the pattern to the repository's actual types. Do not fabricate these types.

The architectural invariants are:

**bounded input → indexed SQL staging → bounded result batch → one round-trip per batch → Dapper multi-result mapping → in-memory batch enrichment → lazy output streaming.**

# Output expectations for Copilot

When asked to perform this migration, do not immediately dump a large implementation.

First report the discovered V1 query, SPs, tables, schema dependencies, existing V2 patterns, and any missing information.

If table definitions are missing and are necessary to safely generate SQL/models, stop and ask for them.

Once the prerequisites are complete, implement the migration in repository order:

1. contracts/models;
2. provider interface;
3. stored procedure;
4. Dapper provider;
5. V2 generator;
6. feature flag;
7. job-engine integration;
8. DI registration;
9. tests;
10. build/test/validation.

When modifying an existing repository, prefer small, reviewable changes and reuse existing patterns rather than introducing a new abstraction solely for this migration.

# Important adaptation rule

The reference report uses `AccountIds`, `SecurityIds`, `DateRange`, `IdTable`, `EnumerableHelpers.ChunkBy`, `IProductProvider`, `AccountInfoAV`, `SecurityInfoAV`, `AVQueryBase`, and `BasicReportLine`.

These are examples of the established architecture, not universal requirements.

If the target repository uses different names or abstractions, map the pattern to the repository's actual types. Do not fabricate these types.

The architectural invariants are:

**bounded input → indexed SQL staging → bounded result batch → one round-trip per batch → Dapper multi-result mapping → in-memory batch enrichment → lazy output streaming.**

## Final approval rule

Do not declare the migration complete merely because the solution builds.

Completion requires:

1. Gate 1 discovery approval.
2. Gate 2 database-performance validation and approval.
3. End-to-end application-performance validation showing bounded application memory and correct batching/streaming behavior.
4. Behavioral equivalence review.
5. Build and test success.

If any of these are incomplete, report exactly what remains rather than claiming the migration is finished.

