# Detailed V2 Report Implementation Pattern

Use this reference after Gate 2 approval. The main `SKILL.md` controls the workflow and approval gates; this file provides the detailed repository-pattern material for contracts, provider, generator, feature flag, DI, and tests.

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

