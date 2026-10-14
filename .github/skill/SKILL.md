---
name: ef-query-to-stored-procedure
description: Migrate memory-intensive EF Core report queries to SQL Server stored procedures with Dapper, bounded batching, and streaming. Use for performance-focused report migrations.
---

# EF Core to Stored Procedure Migration

## Purpose

Migrate an existing EF/EF Core report to a V2 SQL Server stored-procedure implementation while preserving business behavior and improving performance.

Treat performance as two separate objectives:

1. **Database performance:** the stored procedure must be functionally equivalent to the actual EF-generated SQL and must not introduce a significant database-side regression; material improvement is preferred.
2. **Application scalability:** Dapper batching, bounded materialization, and streaming must prevent application memory from growing with the complete report size.

SSMS can validate the first objective, but cannot prove the second.

## Mandatory human-in-the-loop workflow

Do not implement the complete migration in one pass.

```text
Phase 0: Discovery + V1 baseline
        ↓
Gate 1: developer approval
        ↓
Phase 1: stored procedure + comparison script
        ↓
Gate 2: SSMS database-performance validation
        ↓
   ┌───────────────┐
   │ unacceptable? │
   └───────┬───────┘
       yes ↓
 execution plan + IO/TIME
       ↓
 improve SP → retest
       ↺
       no ↓
developer approval
        ↓
Dapper + batching + streaming + tests
        ↓
Gate 3: end-to-end application validation
        ↓
behavioral review + build/PR gate
```

At every gate, **STOP** and wait for explicit developer approval. Valid approvals include `Proceed`, `Approved`, `Continue`, or `Go ahead`.

If the developer supplies corrections instead of approval, incorporate them and stop again.

Never invent missing schema, SQL, measurements, or execution-plan evidence.

---

# Phase 0 — Discovery and V1 baseline

Do not create production SQL or C# implementation.

### 0.1 Discover the V1 implementation

Find and read:

- the V1 EF/EF Core query;
- every related V1 stored procedure;
- provider/repository code;
- report generator and job-engine integration;
- existing V2 reports using Dapper/streaming;
- feature-flag and DI conventions;
- relevant tests.

Read V1 stored procedures completely.

### 0.2 Enumerate database dependencies

Record every referenced:

- table;
- view;
- TVP type;
- temp table;
- CTE source;
- subquery source;
- dynamic-SQL source;
- DML target.

### 0.3 Require exact schema definitions

Before generating replacement SQL or C# contracts, obtain exact definitions for relevant tables/types, including:

- columns and data types;
- nullability;
- primary keys;
- relevant indexes;
- TVP definitions.

If definitions are missing, STOP and request them. Derive SQL joins, filters, and Dapper types from verified definitions rather than assumptions.

### 0.4 Capture the actual EF-generated SQL

Use, in preference order:

1. repository diagnostics/logs;
2. `ToQueryString()` or equivalent;
3. captured SQL from an actual execution;
4. developer-provided SQL.

Do not reconstruct EF SQL from LINQ when actual generated SQL can be obtained.

If actual SQL cannot be obtained, explicitly state that and request it.

### 0.5 Establish the V1 baseline

Use representative parameters/data. Record whatever is available:

- tenant/entity/enterprise;
- account/security inputs;
- date range and filters;
- input/output cardinality;
- elapsed time;
- CPU time;
- logical/physical reads;
- memory grant;
- spills;
- actual execution-plan observations;
- application peak memory.

Never fabricate measurements.

### 0.6 Discovery report

Before Gate 1, produce a concise report containing:

- V1 execution path;
- actual EF SQL;
- V1 stored procedures;
- database dependencies/schema;
- business rules to preserve;
- V1 performance baseline;
- proposed SP inputs/result sets;
- proposed batching strategy;
- database risks;
- application-memory risks;
- open questions/assumptions.

## Gate 1 — Discovery approval

STOP. Do not create the SP or application implementation until the developer explicitly approves the discovery/design.

---

# Phase 1 — Stored procedure and database validation

Begin only after Gate 1 approval.

### 1.1 Create the stored procedure

Preserve V1 business behavior. Follow the repository's established SQL conventions.

For the performance pattern:

- materialize large TVPs into indexed temp tables when appropriate because TVPs lack column statistics;
- materialize the primary result when it enables efficient reuse by related result sets;
- index temp tables appropriately;
- return result sets in a fixed documented order;
- use `SET XACT_ABORT ON`;
- use `SET NOCOUNT ON`;
- use `TRY/CATCH` with `THROW`;
- clean up temp tables;
- avoid speculative hints without evidence.

Read `references/v2-report-pattern.md` for the detailed repository pattern.

### 1.2 Create an SSMS comparison script

Create a script containing:

- realistic TVP/scalar sample parameters;
- the actual EF-generated SQL with equivalent parameters;
- the new stored-procedure execution;
- `SET STATISTICS IO ON`;
- `SET STATISTICS TIME ON`;
- clear labels for V1 EF SQL versus V2 SP.

Make both executions easy to run in SSMS.

### 1.3 Functional equivalence

Before interpreting performance, compare both implementations using the same parameters.

Validate where practical:

- row counts;
- primary/business keys;
- important output columns;
- null behavior;
- duplicates;
- related result sets;
- date boundaries.

For huge results, use deterministic aggregate/key-count/hash checks rather than loading everything into application memory.

## Gate 2 — Database performance validation

STOP and have the developer run the comparison in SSMS.

Compare:

- elapsed time;
- CPU time;
- logical reads;
- physical reads;
- memory grant;
- spills;
- tempdb behavior where relevant;
- actual execution plans;
- result counts/correctness.

The goal is:

> The stored procedure is functionally equivalent and an acceptable database-side replacement for the EF-generated SQL.

A material improvement is preferred. Do not require a fixed percentage because batching and streaming provide additional application-level benefits later.

### If the stored procedure is worse

Do not proceed.

Request, preferably:

- actual plan for EF SQL;
- actual plan for SP;
- `STATISTICS IO`;
- `STATISTICS TIME`;
- parameter values/cardinality;
- observed bottleneck.

Analyze the evidence, modify the SP, and have the developer rerun the comparison.

Repeat until acceptable.

### Gate 2 approval

Once acceptable, STOP and wait for explicit developer approval before implementing Dapper/application code.

---

# Application implementation after Gate 2

Implement only after Gate 2 approval, in this order:

1. Dapper result models/contracts;
2. batch model;
3. provider interface;
4. Dapper provider;
5. V2 generator;
6. mapping/enrichment;
7. feature flag;
8. job-engine integration;
9. DI;
10. tests.

Use `references/v2-report-pattern.md` for detailed patterns.

Mandatory architecture:

- bounded input/account chunks;
- one connection for the streaming operation where supported by the repository;
- `QueryMultiple` for multiple result sets;
- fixed result-set order;
- `ToList()` only for bounded batches when needed;
- no full-report `ToList()`/`ToArray()`;
- no accumulation of all report lines;
- enrichment must use current-batch data/lookups;
- previous batches must become collectible;
- reuse existing chunking helpers;
- preserve the repository's existing sync/async contract unless an architectural change is explicitly approved.

---

# Gate 3 — End-to-end application performance validation

Use the same representative scenario as Gate 2 where practical.

Compare V1 EF versus complete V2 for:

- total report duration;
- SQL duration;
- application peak memory;
- number of database calls;
- rows processed;
- batch size;
- CPU/GC behavior where available.

The question is now:

> Does batching and streaming solve the application-side memory/scalability problem?

For a large report, verify:

- only one bounded batch is materialized at a time;
- no full-result materialization exists;
- enrichment does not retain previous batches;
- related result sets are bounded to the current batch;
- output is not accumulated;
- memory does not grow linearly with total report size.

Where practical, test both a moderate and a large report.

---

# Behavioral equivalence

Verify:

- filters;
- date semantics;
- null handling;
- joins;
- duplicates;
- enrichment;
- account/security filtering;
- report-line mapping;
- V1 feature-flag fallback;
- externally visible behavior.

Performance optimization must not silently change business behavior.

---

# Final completion gate

Do not declare the migration complete until:

- Gate 1 approved;
- schema verified;
- actual EF SQL captured;
- V1 baseline established as far as possible;
- stored procedure created;
- EF SQL vs SP comparison performed;
- Gate 2 accepted;
- any execution-plan iteration completed;
- Dapper provider implemented;
- batching/streaming implemented;
- enrichment is bounded;
- feature flag/DI integrated;
- tests pass;
- Gate 3 validated;
- behavioral equivalence reviewed;
- build succeeds.

Do not claim completion while any required gate remains pending.

## Adaptation rule

Repository-specific names, paths, namespaces, classes, and sample SQL in the reference file are patterns, not universal names. Discover and reuse the actual repository conventions. Do not invent duplicate infrastructure.
