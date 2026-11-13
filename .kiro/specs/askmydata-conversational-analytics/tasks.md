# Implementation Plan: AskMyData Conversational Analytics

## Overview

The plan is built as vertical slices in C# on .NET 8, following design.md. Tasks 1 to 13 deliver a running end-to-end service: `docker compose up` loads the deterministic Dataset into SQL Server 2022, and `POST /api/ask` answers a known question with the Stub provider and no network. Later groups add, in this order, the Eval_Suite in Stub_Mode with CI gating, Demo_Guard, the observability extras, and Live_Mode with the Gemini adapter.

**Commit convention.** Every leaf sub-task (decimal-numbered, for example 4.2) ends with exactly one git commit using a Conventional Commits message, given in the last bullet `Commit: <type>(scope): <message>`. Top-level tasks with sub-tasks get no commit of their own. Checkpoint tasks make no commit unless a fix was needed, in which case they end with one `fix(<scope>): ...` commit.

**Property test convention.** Property tests use FsCheck with xUnit (`FsCheck.Xunit`), run at least 100 iterations (`[Property(MaxTest = 100)]`, validator properties use `MaxTest = 500`), and carry the tag comment `// Feature: askmydata-conversational-analytics, Property N: <title>`. Properties 1 to 9, 12, 13 and 25 are required. All other property tests and other test sub-tasks marked `*` are optional.

## Tasks

### Slice 1: Running end-to-end service (Stub provider, no network)

- [x] 1. Solution skeleton and Kiro steering

  - [x] 1.1 Create the solution structure, project references and test packages

    - Create `AskMyData.sln`, `src/AskMyData.Domain`, `src/AskMyData.Application`, `src/AskMyData.Infrastructure`, `src/AskMyData.Api` (ASP.NET Core Web API), `tests/AskMyData.UnitTests`, `tests/AskMyData.IntegrationTests`, `tests/AskMyData.Evals` (console), all targeting `net8.0`
    - Add `Directory.Build.props` with `Nullable` enabled, `ImplicitUsings` enabled, `TreatWarningsAsErrors` true
    - Project references follow the hexagonal rules: Domain references nothing; Application references Domain plus `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.Options`; Infrastructure references Application and Domain plus `Microsoft.SqlServer.TransactSql.ScriptDom` and `Microsoft.Data.SqlClient`; Api references all three
    - Add `InternalsVisibleTo` from Application to UnitTests and Evals so tests can construct `ValidatedSql` only through the Orchestrator path or test seams
    - Test packages: xUnit, `FsCheck.Xunit`, FluentAssertions, `NetArchTest.Rules`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.Mvc.Testing` (UnitTests and IntegrationTests); `Testcontainers.MsSql` (IntegrationTests and Evals)
    - Verify with `dotnet build` and `dotnet test` (empty test run succeeds)
    - _Requirements: 1.5, 11.3, 11.4_
    - Commit: `chore(repo): scaffold solution, layered projects and test packages`
  - [x] 1.2 Move the steering files into `.kiro/steering`

    - Run `git mv steering/product.md .kiro/steering/product.md`, `git mv steering/tech.md .kiro/steering/tech.md`, `git mv steering/structure.md .kiro/steering/structure.md`, then remove the empty `steering/` folder
    - Use move, not copy, so there is one source of truth that Kiro picks up as steering
    - Commit: `chore(kiro): move steering files into .kiro/steering`
- [x] 2. Domain layer

  - [x] 2.1 Implement the Semantic_Layer types and governed catalog

    - Create `Metric`, `AllowlistEntry`, `AllowlistColumn`, `AllowlistObjectKind`, `SemanticLayer` (case-insensitive `TryGetMetric`, `TryGetObject`, `AllowedTableFunctions`) in `Domain/Semantic`
    - Create `SemanticLayerCatalog.Create(allowedTableFunctions = null)` with the six Metrics (TotalRevenue, UnitsSold, OrderCount, GrossMargin, AverageOrderValue, CustomerCount) and the Allowlist for Customers, Products, Stores, Orders, OrderLines and Calendar exactly as in the design Dataset table; Employees and DatasetVersion are not in the Allowlist
    - Allowed_Table_Function_List defaults to empty
    - _Requirements: 1.1, 1.2, 1.5, 1.6, 3.11, 14.6_
    - Commit: `feat(domain): add semantic layer, metrics and allowlist catalog`
  - [x] 2.2 Implement conversation and result types

    - Create `ConversationId` (32 lowercase hex, `TryParse`), `Turn` in `Domain/Conversations`
    - Create `ReasonCodes` (all 17 constants), `ExecutionErrorCategory` (serialized TIMEOUT / EXECUTION_ERROR), `ChartType` (serialized bar / line / table-only) in `Domain/Results`
    - _Requirements: 2.13, 2.14, 3.22, 12.1_
    - Commit: `feat(domain): add conversation ids, turns, reason codes and chart types`
  - [x] 2.3 Write unit tests for the Semantic_Layer catalog

    - Every Metric has name, description, SQL expression and dependency tables that are Allowlist entries; Allowlist has tables, columns and types; `AllowedTableFunctions` is empty by default; `ConversationId.TryParse` accepts 32-hex and rejects other input
    - _Requirements: 1.1, 1.2, 3.11_
    - Commit: `test(domain): cover semantic layer catalog and conversation id parsing`
- [x] 3. Application ports and contracts

  - [x] 3.1 Define the Application ports, options and value types
    - `Llm/`: `ILlmClient`, `LlmRequest`, `LlmResponse`, `LlmCallType`, `RepairInfo`, `LlmUnavailableException`
    - `Validation/`: `ISqlValidator`, `ValidationResult` (with `MetricName`), `ValidatedSql` with an `internal` constructor
    - `Execution/`: `IQueryExecutor`, `QueryResult`, `ResultColumn`, `ColumnKind`, `ExecutionError`, `QueryBudget`, and a small `OneOf<T1,T2>` struct
    - `Conversations/IConversationStore`, `Observability/IAuditLog` and a minimal `AuditRecord` (correlationId, conversationId, question, sql, validation outcome and Reason_Code, failureReason, latencyMs); full fields are added in task 18.1
    - `Options/AskOptions` with defaults: Request_Deadline 60 s, LLM_Timeout 30 s, command timeout 15 s, Row_Limit 1000, HistoryTurns 10, MaxExplanationLength 500
    - _Requirements: 2.15, 4.2, 4.7, 8.2, 9.6, 11.3, 12.1_
    - Commit: `feat(application): define llm, validator, executor, store and audit ports`
- [x] 4. ScriptDom SQL validator with the fixed rule order

  - [x] 4.1 Implement `ScriptDomSqlValidator` and structural rules 1 to 12

    - `ScriptDomSqlValidator(SemanticLayer)` creates a new `TSql160Parser(initialQuotedIdentifiers: true)` per call and holds no mutable state
    - `IValidationRule` with `Order`, `ValidationContext`; rules run in the design rule-table order and the first failure is returned
    - Rules: 1 PARSE_ERROR (including empty or whitespace), 2 MULTIPLE_STATEMENTS (across `GO` batches), 3 NOT_SELECT_ONLY (non-SELECT, EXEC, system procedures, SELECT INTO), 4 COMMENT_NOT_ALLOWED (token stream), 5 OPENROWSET_NOT_ALLOWED (incl. OPENDATASOURCE), 6 OPENQUERY_NOT_ALLOWED, 7 LINKED_SERVER_NOT_ALLOWED, 8 CROSS_DATABASE_NOT_ALLOWED, 9 SYSTEM_OBJECT_NOT_ALLOWED (`sys`, `INFORMATION_SCHEMA`), 10 TABLE_FUNCTION_NOT_ALLOWED (schema-object and built-in rowset functions such as OPENJSON, OPENXML, STRING_SPLIT not in the list), 11 FUNCTION_NOT_ALLOWED (schema-qualified calls; unqualified built-ins pass), 12 SELECT_STAR (`*` and `t.*`; `COUNT(*)` passes)
    - Document the rule order in an XML doc comment on the validator
    - _Requirements: 3.1, 3.2, 3.3, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11, 3.12, 3.13, 3.14, 3.17, 3.22, 3.23, 3.24_
    - Commit: `feat(validator): add scriptdom validator with structural rules 1-12`
  - [x] 4.2 Implement `ScopeResolver` and rules 13 and 14

    - Build `ScopeModel` over every `QuerySpecification` including CTEs, derived tables, subqueries, UNION branches and APPLY; sources are `BaseTable`, `Cte` and `Derived`; correlated subqueries see outer scopes
    - Rule 13 TABLE_NOT_ALLOWED: `NamedTableReference` that is not an in-scope CTE name, resolved with default schema `dbo`, is not in the Allowlist
    - Rule 14 COLUMN_NOT_ALLOWED: a column reference that does not resolve to an Allowlist column of its base table, a CTE or derived-table output column, or a select-list alias used in ORDER BY
    - _Requirements: 3.4, 3.15, 3.16_
    - Commit: `feat(validator): add scope resolution with table and column allowlist rules`
  - [x] 4.3 Implement `SyntaxTreeNormalizer`, `NormNode` and rule 15

    - `NormNode` with `StructurallyEquals` and `ContainsSubtree`
    - Normalization: lower-case identifiers and keywords, remove quoting, default schema `dbo`, replace aliases with base table names, qualify unique unqualified columns, drop `ParenthesisExpression`, keep literals exact; no reordering
    - Rule 15 METRIC_DEFINITION_MISMATCH: for each declared Metric, case (a) unknown name or case (b) expression tree not a sub-tree; Detail strings as in design; set `ValidationResult.MetricName`; skip when Metric_Usage is empty; no raw-text comparison
    - _Requirements: 3.18, 3.19, 3.20, 3.21_
    - Commit: `feat(validator): add syntax tree normalizer and metric definition check`
  - [x] 4.4 Build FsCheck generators for validator properties

    - In `tests/AskMyData.UnitTests/Generators`: `SafeQueryGen` (small AST over Allowlist columns, FK joins, WHERE, GROUP BY, ORDER BY, built-ins, optional `COUNT(*)`, printed to T-SQL), `RewriteGen`, `ViolationGen` (tagged violation, injection position, expected Reason_Code), `MetricQueryGen`, `MetricMutationGen`
    - Include a sanity unit test that a sample of `SafeQueryGen` output parses with `TSql160Parser`
    - _Requirements: 3.12, 3.15, 3.18_
    - Commit: `test(validator): add fscheck generators for safe queries, rewrites and violations`
  - [x] 4.5 Write property test for validator determinism

    - **Property 1: Validator determinism**
    - **Validates: Requirements 3.13**
    - Commit: `test(validator): property 1 validator determinism`
  - [x] 4.6 Write property test for safe queries under rewrites

    - **Property 2: Safe queries pass under structural rewrites**
    - **Validates: Requirements 3.12, 3.14, 3.15, 3.24**
    - Commit: `test(validator): property 2 safe queries pass under structural rewrites`
  - [x] 4.7 Write property test for injected violations

    - **Property 3: Injected violations are rejected with their Reason_Code**
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.14, 3.15, 3.16, 3.17, 3.23**
    - Commit: `test(validator): property 3 injected violations are rejected with their reason code`
  - [x] 4.8 Write property test for rule ordering

    - **Property 4: First failing rule determines the Reason_Code**
    - **Validates: Requirements 3.22**
    - Commit: `test(validator): property 4 first failing rule determines the reason code`
  - [x] 4.9 Write property test for Metric check normalization invariance

    - **Property 5: Metric check is invariant to normalization-only differences**
    - **Validates: Requirements 3.18, 3.20, 3.21**
    - Commit: `test(validator): property 5 metric check invariant to normalization-only differences`
  - [x] 4.10 Write property test for Metric mismatch detection

    - **Property 6: Metric mismatches are detected and named**
    - **Validates: Requirements 3.19, 3.21**
    - Commit: `test(validator): property 6 metric mismatches are detected and named`
  - [x] 4.11 Write validator example unit tests

    - `SELECT 1; DROP TABLE dbo.Orders`, `EXEC sp_who`, `SELECT ... INTO`, `/* */` and `--` comments, OPENDATASOURCE, `sys.fn_listextendedproperty`, `dbo.fnSecret(x)`, `COUNT(*)` passing, `b*a` Metric rewrite failing with case (b)
    - _Requirements: 3.1, 3.2, 3.3, 3.5, 3.6, 3.17, 3.19, 3.23_
    - Commit: `test(validator): add example tests per reason code`
- [x] 5. Dataset_Script and Docker Compose

  - [x] 5.1 Write `db/dataset.sql`

    - Create the database and the tables Customers, Products, Stores, Orders, OrderLines, Calendar, Employees, DatasetVersion with `IF NOT EXISTS` / `OBJECT_ID` guards
    - Generate data with the LCG `state = (state * 1103515245 + 12345) % 2147483648`, seed `20240601`, `bigint` arithmetic; sizes as in design (200 customers, 60 products, 8 stores, 5,000 orders 2023-01-01 to 2024-12-31, about 12,000 order lines, full calendar)
    - Load data in one transaction only when `DatasetVersion` has no row for the current version, inserting the version row last
    - Wrap each step in `BEGIN TRY / BEGIN CATCH` with `@step`, `PRINT CONCAT('FAILED at step: ', ...)` and `THROW`; `SET XACT_ABORT ON`
    - Create login and user `askmydata_reader` from `$(READONLY_PASSWORD)` if absent and grant `SELECT` only on each Allowlist object
    - _Requirements: 1.6, 4.1, 14.1, 14.3, 14.4, 14.5, 14.6_
    - Commit: `feat(db): add deterministic dataset script with read-only user`
  - [x] 5.2 Add Docker Compose and the Api Dockerfile

    - `src/AskMyData.Api/Dockerfile` (multi-stage .NET 8 build, listens on 8080)
    - `docker-compose.yml`: `sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`, health check `sqlcmd -Q "SELECT 1"`), `db-init` (`mcr.microsoft.com/mssql-tools18`, `depends_on: service_healthy`, `sqlcmd -b -C -i /db/dataset.sql -v READONLY_PASSWORD=...`, `restart: "no"`), `api` (`depends_on: db-init: service_completed_successfully`, port `8080:8080`, `ConnectionStrings__ReadOnly`, `Llm__Provider=Stub` by default, `Llm__Gemini__ApiKey` from `.env`)
    - Add `.env.example` with `SA_PASSWORD`, `READONLY_PASSWORD`, `GEMINI_API_KEY` placeholders; confirm `.env` stays ignored by `.gitignore`
    - Verify with `docker compose config`
    - _Requirements: 14.2, 4.1_
    - Commit: `build(docker): add compose stack with sqlserver, db-init and api`
  - [x] 5.3 Write Dataset_Script integration tests

    - Testcontainers SQL Server 2022 fixture that runs `db/dataset.sql`; a second run leaves per-table checksums unchanged; two fresh containers give identical checksums; a deliberately failing step exits non-zero and prints the step label; every Metric and Allowlist column exists in `INFORMATION_SCHEMA`
    - _Requirements: 1.6, 14.1, 14.3, 14.5, 14.6_
    - Commit: `test(db): cover dataset idempotency, determinism and failure reporting`
- [x] 6. Streaming SQL Server query executor

  - [x] 6.1 Implement `SqlServerQueryExecutor` and `SqlTypeClassifier`

    - `QueryExecutorOptions` (Read_Only_User connection string, Row_Limit default 1000)
    - Factor row reading into `internal static RowReader.ReadAsync(DbDataReader, int rowLimit, CancellationToken)` that reads at most Row_Limit + 1 rows, returns `min(n, L)` rows and the Truncated_Flag; on truncation cancel the command and dispose the reader; never rewrite the SQL
    - `CommandTimeout = max(1, ceil(budget.CommandTimeout.TotalSeconds))`; pass the cancellation token to `ExecuteReaderAsync` and `ReadAsync`
    - `SqlException.Number == -2` gives TIMEOUT; other `SqlException` gives EXECUTION_ERROR and logs number, severity, state and message at Warning inside Infrastructure only; `OperationCanceledException` propagates
    - Column kinds from `GetDataTypeName` per design
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10_
    - Commit: `feat(executor): add streaming sql server executor with row limit and timeouts`
  - [x] 6.2 Write property test for Row_Limit and Truncated_Flag

    - Use a fake `DbDataReader` that counts reads
    - **Property 13: Row_Limit and Truncated_Flag invariants**
    - **Validates: Requirements 4.4, 4.5, 4.6**
    - Commit: `test(executor): property 13 row limit and truncated flag invariants`
  - [x] 6.3 Write executor and Read_Only_User integration tests

    - Testcontainers with `db/dataset.sql`: SELECT on Allowlist objects succeeds; SELECT on `dbo.Employees`, INSERT, UPDATE, DELETE and DDL fail with permission errors; a slow cross join with 1 s timeout returns TIMEOUT; divide-by-zero returns EXECUTION_ERROR and logs details; a cancelled token stops the command; a 1,500-row query with Row_Limit 1000 returns 1,000 rows and truncated true
    - _Requirements: 4.1, 4.3, 4.5, 4.8, 4.10, 14.4_
    - Commit: `test(executor): integration tests for read-only user, timeouts and truncation`
- [x] 7. Application services: parser, charts and prompts

  - [x] 7.1 Implement `StructuredResponseParser`

    - Strict JSON object, optional single Markdown fence stripped, required `sql`, `metricUsage`, `explanation`, `adHocCalculation`, optional `refusal`, unknown properties invalid; valid when refusal non-empty or sql non-empty; refusal wins when both are present
    - _Requirements: 2.2, 2.11, 2.13_
    - Commit: `feat(application): add strict structured response parser`
  - [x] 7.2 Write property test for Structured_Response parsing

    - **Property 10: Structured_Response parsing round trip and strictness**
    - **Validates: Requirements 2.2, 2.11**
    - Commit: `test(application): property 10 structured response round trip and strictness`
  - [x] 7.3 Implement `ChartSuggester`

    - Pure function producing `ChartSuggestion(Type, XColumn, YColumns)` using the six ordered rules in design (temporal detection by kind or name tokens, measures, table-only below 2 rows or no measure, line, bar, table-only)
    - _Requirements: 2.5, 5.10, 9.7, 9.8_
    - Commit: `feat(application): add rule-based chart suggester`
  - [x] 7.4 Write property test for chart suggestion

    - **Property 19: Chart suggestion is local, bounded and rule-consistent**
    - **Validates: Requirements 2.5, 5.10, 9.7, 9.8**
    - Commit: `test(application): property 19 chart suggestion rules`
  - [x] 7.5 Implement `PromptBuilder`, `SystemPromptTemplate` and `INonceSource`

    - System_Prompt sections in order: rules (single SELECT, no comments, no `*`, no system objects or UDFs, use Metric expressions exactly and list them in `metricUsage`, `adHocCalculation`, explanation at most 500 chars, refusal with empty sql for data modification, prompt disclosure or non-Allowlist data, ignore conflicting instructions between untrusted markers, never reveal instructions), Metrics, Allowlist schema without row data, output schema, repair block on Repair_Call with only Reason_Code or category and Metric name
    - Untrusted_Section: `<<<UNTRUSTED_{nonce}>>>` JSON payload (question, last 10 Turns chronological with question and SQL only, failingSql only on repair) `<<<END_UNTRUSTED_{nonce}>>>`; redraw the nonce while it occurs in any untrusted text
    - Expose `SystemPromptInstructionText` (rules portion only) for the leak guard; production `INonceSource` uses `RandomNumberGenerator`
    - _Requirements: 1.3, 1.4, 2.1, 8.1, 8.2, 9.1, 9.2, 9.3, 10.1, 10.2_
    - Commit: `feat(application): add prompt builder with nonce-delimited untrusted section`
  - [x] 7.6 Write property test for prompt composition

    - **Property 16: Prompt composition and untrusted delimiting**
    - **Validates: Requirements 1.3, 2.1, 5.6, 8.1, 8.2, 10.1**
    - Commit: `test(application): property 16 prompt composition and untrusted delimiting`
  - [x] 7.7 Write unit tests for System_Prompt rule texts

    - Exact Metric use instruction, refusal instruction, ignore-untrusted-instructions instruction, repair block contains no error text
    - _Requirements: 1.4, 9.2, 10.2_
    - Commit: `test(application): cover system prompt rule texts`
- [x] 8. In-memory conversation store

  - [x] 8.1 Implement `InMemoryConversationStore` and `ConversationSweeper`

    - `ConversationStoreOptions` (TTL 60 min, sweep interval 1 min); `ConcurrentDictionary` of entries with per-entry lock, `NextSeq`, `LastAccess`, `Removed`; expiry checked inside the lock on every access using `TimeProvider`; load and append reset idle time; load returns last N Turns by ascending sequence; unknown or expired returns `null` / `false`; ids are `Guid.NewGuid().ToString("N")`
    - `ConversationSweeper` background service calls `SweepExpired()`
    - _Requirements: 8.2, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_
    - Commit: `feat(conversations): add in-memory store with sliding ttl and sweeper`
  - [x] 8.2 Write property test for ordering and concurrent appends

    - **Property 23: Conversation store preserves order and concurrent appends**
    - **Validates: Requirements 8.2, 12.4, 12.5**
    - Commit: `test(conversations): property 23 order and concurrent appends`
  - [x] 8.3 Write property test for sliding TTL expiry

    - Store-level part with `FakeTimeProvider`; the Api 404 part is asserted in task 11.9
    - **Property 24: Sliding TTL expiry**
    - **Validates: Requirements 8.3, 12.3, 12.6**
    - Commit: `test(conversations): property 24 sliding ttl expiry`
- [x] 9. Orchestrator (`AskQuestionHandler`)

  - [x] 9.1 Implement `AskQuestionHandler`, `AskOutcome` hierarchy and `UsageSummary`

    - `AskCommand`, closed `AskOutcome` records (Succeeded, ValidationFailed, Refused, GenerationInvalid, LlmUnavailable, ExecutionFailed, DeadlineExceeded, ConversationNotFound, ClientAborted), `UsageSummary` with total tokens and elapsed ms, minimal `UsageAccumulator` summing tokens over all calls
    - Implement design steps 1 to 12: Request_Deadline CTS from `TimeProvider` linked with `requestAborted`; load the last 10 Turns or create a Conversation, unknown or unparsable id returns ConversationNotFound before any LLM call; deadline check before every LLM call; LLM call bounded by `min(LLM_Timeout, remaining)` with cancellation classification (client abort, deadline, LLM_Timeout); refusal ends with REFUSED; invalid output repairs once with GENERATION_INVALID; empty Metric_Usage forces adHocCalculation; validate before anything else; mint `ValidatedSql` only after a pass; execute with `QueryBudget(min(command timeout, remaining))`; execution TIMEOUT or EXECUTION_ERROR repairs once; `attempt` is only 1 or 2
    - Success: chart, explanation truncated to 500 chars without splitting a surrogate pair, ad hoc warning `"This calculation is not a governed Metric."`, append the Turn
    - Write exactly one minimal `AuditRecord` in `finally`, failureReason including DEADLINE_EXCEEDED, empty sql when failing before generation
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9, 2.10, 2.11, 2.12, 2.13, 2.14, 2.15, 2.16, 2.17, 2.18, 4.9, 4.10, 5.6, 5.11, 7.9, 7.10, 7.12, 8.1, 8.3, 8.4, 9.4, 9.5, 9.6, 10.5, 12.7_
    - Commit: `feat(orchestrator): add ask handler with single repair call and request deadline`
  - [x] 9.2 Build orchestrator test doubles and generators

    - Scripted `ILlmClient` spy recording every `LlmRequest` and honoring cancellation with `FakeTimeProvider` delays; scripted `IQueryExecutor` spy recording every `ValidatedSql`; validator spy wrapping `ScriptDomSqlValidator` that records pass results; in-memory audit capture
    - FsCheck generators `LlmScriptGen`, `ExecutorScriptGen` (rows with marker strings, TIMEOUT, EXECUTION_ERROR), `DelayScriptGen`
    - _Requirements: 2.3, 2.8, 2.15_
    - Commit: `test(orchestrator): add scripted llm, executor and validator test doubles`
  - [x] 9.3 Write property test for validator gating

    - **Property 7: No SQL reaches IQueryExecutor without a validator pass**
    - **Validates: Requirements 2.3, 2.4, 6.9, 10.5**
    - Commit: `test(orchestrator): property 7 no sql reaches executor without validator pass`
  - [x] 9.4 Write property test for the repair budget

    - **Property 8: Repair budget is at most one and the last attempt decides the outcome**
    - **Validates: Requirements 2.2, 2.6, 2.7, 2.8, 2.11, 2.14, 9.5**
    - Commit: `test(orchestrator): property 8 repair budget and last-attempt outcome`
  - [x] 9.5 Write property test for refusals

    - **Property 9: Refusal ends the request without validation, execution or repair**
    - **Validates: Requirements 2.11, 2.13, 5.12**
    - Commit: `test(orchestrator): property 9 refusal ends the request`
  - [x] 9.6 Write property test for the Request_Deadline

    - Drive delays with `FakeTimeProvider`; assert per-call bounds, cancellation on expiry, no Repair_Call after expiry, and DEADLINE_EXCEEDED audit failure reason
    - **Property 12: Request_Deadline bounds every step**
    - **Validates: Requirements 2.15, 2.16, 2.17, 2.18, 4.9, 4.10, 7.12**
    - Commit: `test(orchestrator): property 12 request deadline bounds every step`
  - [x] 9.7 Write property test for successful answers

    - **Property 11: Successful answers are transparent and consistent**
    - **Validates: Requirements 2.5, 2.9, 2.10, 2.12, 9.5, 9.6, 12.7**
    - Commit: `test(orchestrator): property 11 successful answers are transparent`
  - [x] 9.8 Write property test for data minimization

    - **Property 17: Data minimization in every LlmRequest**
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
    - Commit: `test(orchestrator): property 17 data minimization in llm requests`
- [x] 10. Stub ILlmClient and provider registry

  - [x] 10.1 Implement `StubLlmClient`, `IStubScriptSource` and `LlmProviderRegistry`

    - `StubLlmClient` returns recorded raw outputs keyed by normalized question text and call index (0 initial, 1 repair); unscripted questions return a fixed refusal; fixed tokens 100 prompt and 50 completion; model name `stub`
    - `FileStubScriptSource` reads a JSON file (`Llm:Stub:ScriptPath`) mapping questions to response arrays; an in-memory source for tests and Evals
    - `LlmProviderRegistry` with case-insensitive `Register` and `Resolve`; unknown name throws `InvalidOperationException` naming the Provider_Name and the registered names
    - Add `src/AskMyData.Api/stub/stub-scripts.json` with the demo questions: "What was total revenue by region in 2024?" (TotalRevenue by `dbo.Stores.Region`, ORDER BY Region, using aliases), a follow-up "Only for the West region", and their Structured_Responses
    - _Requirements: 6.6, 11.1, 11.2, 11.4_
    - Commit: `feat(llm): add stub llm client, script source and provider registry`
  - [x] 10.2 Write property test for unknown provider names

    - **Property 27: Unknown provider names fail startup**
    - **Validates: Requirements 11.2**
    - Commit: `test(llm): property 27 unknown provider names fail startup`
- [x] 11. Api layer

  - [x] 11.1 Implement contracts and `OutcomeHttpMapper`

    - `Mapping/Contracts.cs`: `AskRequestDto` (Required, StringLength 1..1000, whitespace rejected), Question_Response DTO (conversationId, sql, result columns and rows, truncated, chartSuggestion, explanation, metricUsage, warnings, usage), problem-details extensions (reasonCode, detail, attemptedSql, refusal, conversationId, correlationId, usage)
    - Map outcomes per the design Error Handling table: 200, 422 (validation Reason_Code, REFUSED with refusal text, GENERATION_INVALID), 503, 504 (TIMEOUT and DEADLINE_EXCEEDED), 502 (generic message, no SQL Server text), 404 naming the Conversation_Id; ClientAborted writes nothing and logs 499
    - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.8, 5.9, 5.10, 5.12, 5.13, 5.14, 7.10, 8.3_
    - Commit: `feat(api): add response contracts and outcome to http mapping`
  - [x] 11.2 Implement `ResponseLeakGuard`

    - 8-word shingles of the normalized `SystemPromptInstructionText`; scan every response string (explanation, warnings, refusal, error messages, SQL, cell values), replace matches with `[redacted]` and add the leak warning
    - _Requirements: 10.3, 10.4_
    - Commit: `feat(api): add response leak guard for system prompt text`
  - [x] 11.3 Implement `CorrelationIdMiddleware` and minimal `JsonAuditLog`

    - New GUID "N" per request, inbound values ignored, sets `TraceIdentifier`, adds `X-Correlation-Id` in `Response.OnStarting`, opens a logging scope `{CorrelationId}`
    - `JsonAuditLog` in Infrastructure writes the minimal `AuditRecord` as one structured log entry (`event: ask.completed`); JSON console logging with scopes
    - _Requirements: 7.1, 7.2_
    - Commit: `feat(api): add correlation id middleware and json audit log`
  - [x] 11.4 Implement `AskController` and compose `Program.cs`

    - Thin `AskController` (`POST /api/ask`) mapping DTO to `AskCommand` and outcome through the mapper and leak guard; `InvalidModelStateResponseFactory` returns 400 problem details
    - DI: Semantic_Layer, validator, executor, parser, charts, prompt builder, store and sweeper, audit log, handler, `TimeProvider.System`; options bound with `ValidateOnStart` (positive timeouts, Row_Limit >= 1, non-empty connection string, secrets never echoed); registry registers `Stub` and resolves `Llm:Provider` eagerly at startup
    - Middleware order: CorrelationId, exception handler (problem details, no internal text), routing, controllers; `appsettings.json` with `Llm:Provider = Stub` and the stub script path; `public partial class Program` for tests
    - _Requirements: 5.1, 5.3, 5.6, 5.7, 5.11, 7.1, 8.4, 10.3, 11.1, 11.2_
    - Commit: `feat(api): add ask endpoint and composition root`
  - [x] 11.5 Write property test for outcome mapping

    - **Property 14: Outcome to HTTP status mapping**
    - **Validates: Requirements 5.2, 5.4, 5.5, 5.8, 5.9, 5.12, 5.13, 5.14**
    - Commit: `test(api): property 14 outcome to http status mapping`
  - [x] 11.6 Write property test for question length validation

    - **Property 15: Question length validation**
    - **Validates: Requirements 5.3**
    - Commit: `test(api): property 15 question length validation`
  - [x] 11.7 Write property test for prompt leak prevention

    - **Property 18: System_Prompt text never appears in a response**
    - **Validates: Requirements 10.3, 10.4**
    - Commit: `test(api): property 18 system prompt never appears in a response`
  - [x] 11.8 Write property test for correlation ids

    - **Property 22: Correlation_Id on every response and log entry**
    - **Validates: Requirements 7.1**
    - Commit: `test(api): property 22 correlation id on every response and log entry`
  - [x] 11.9 Write Api integration tests with the Stub provider

    - `WebApplicationFactory<Program>` plus Testcontainers Dataset: 200 for the scripted question; zero-row result returns empty rows and table-only; new Conversation id returned when none supplied; multi-turn follow-up; unknown or expired Conversation_Id returns 404 naming it and creates no Conversation; unknown Provider_Name fails host startup with its name
    - _Requirements: 5.1, 5.10, 5.11, 8.3, 8.4, 11.1, 11.2, 12.6_
    - Commit: `test(api): end-to-end api integration tests with stub provider`
- [x] 12. Architecture tests

  - [x] 12.1 Write NetArchTest layer rules
    - Domain depends on no other layer or non-BCL package; Application depends only on Domain and `Microsoft.Extensions.*.Abstractions` and has no `Google.*`, `Microsoft.Data.SqlClient`, ScriptDom or Dapper references; every `ILlmClient` implementation is in Infrastructure; `IConversationStore` is in Application and `InMemoryConversationStore` in Infrastructure; controllers depend only on the handler, mapper, leak guard and DTOs; `ValidatedSql` has no public constructor and `IQueryExecutor.ExecuteAsync` takes `ValidatedSql`
    - _Requirements: 1.5, 2.4, 5.7, 11.3, 11.4, 12.1, 12.2_
    - Commit: `test(architecture): enforce hexagonal layer rules`
- [x] 13. Checkpoint: end-to-end service with the Stub provider

  - Ensure all tests pass, ask the user if questions arise.
  - Copy `.env.example` to `.env` and fill in passwords, then run `docker compose up -d --build`; `docker compose ps -a` shows `sqlserver` healthy, `db-init` exited with code 0, `api` running
  - PowerShell check: `$r = Invoke-WebRequest -Method Post -Uri http://localhost:8080/api/ask -ContentType 'application/json' -Body '{"question":"What was total revenue by region in 2024?"}'; $r.StatusCode; $r.Headers['X-Correlation-Id']; $b = $r.Content | ConvertFrom-Json; $b | ConvertTo-Json -Depth 6`
  - curl equivalent: `curl -i -X POST http://localhost:8080/api/ask -H "Content-Type: application/json" -d '{"question":"What was total revenue by region in 2024?"}'`
  - Expected: HTTP 200; `X-Correlation-Id` is 32 hex chars; body has `conversationId` (32 hex), `sql` equal to the scripted SQL, `result.columns` = `[{name: Region, kind: text}, {name: TotalRevenue, kind: numeric}]`, `result.rows` one row per Dataset region with non-zero revenue, `truncated: false`, `chartSuggestion` = `{type: bar, xColumn: Region, yColumns: [TotalRevenue]}`, `metricUsage: [TotalRevenue]`, `warnings: []`, `usage.totalTokens: 150`, `usage.elapsedMs > 0`
  - Follow-up with `"conversationId": "<id from above>"` and question "Only for the West region" returns 200 with the same `conversationId`; an unknown `conversationId` returns 404; an unscripted question such as "Delete all orders" returns 422 with `reasonCode: REFUSED`
  - No outbound network call is made (Provider_Name is `Stub`)

### Slice 2: Evals in Stub_Mode and CI gating

- [x] 14. Eval_Suite in Stub_Mode

  - [x] 14.1 Implement Eval_Case and Accuracy_Report models and the case loader

    - `EvalCase` (id, category, metric, attack, question, priorTurns, referenceSql, expected rows or blocked with allowedReasonCodes, stub responses), loader for `tests/AskMyData.Evals/cases/*.json` with schema checks
    - Immutable `AccuracyReport`, `ReportTotals`, `CaseResult`, `TokenUsage` records; `System.Text.Json` camelCase, ISO 8601 round-trip timestamps, decimals
    - _Requirements: 6.5, 6.11, 6.18_
    - Commit: `feat(evals): add eval case and accuracy report models`
  - [x] 14.2 Implement `ReportWriter`

    - Writes `report.json` and `report.md` (header with mode, label, timestamp, model, token usage; totals table; per-case table); Stub_Mode label "Pipeline and security check (stub LLM). This is not an LLM accuracy measurement."
    - _Requirements: 6.11, 6.12_
    - Commit: `feat(evals): add json and markdown report writer`
  - [x] 14.3 Write property test for the Accuracy_Report

    - **Property 26: Accuracy_Report JSON round trip and Markdown content**
    - **Validates: Requirements 6.11, 6.18**
    - Commit: `test(evals): property 26 accuracy report round trip and markdown content`
  - [x] 14.4 Implement `EvalRunner`, `CountingQueryExecutor` and the CLI

    - `dotnet run --project tests/AskMyData.Evals -- --mode stub --out artifacts/evals [--connection <cs>]`; composes the real Application and Infrastructure services; Stub_Mode registers only `StubLlmClient` with each case's in-memory script and no `HttpClient`
    - Without `--connection`, start a Testcontainers SQL Server 2022 and run `db/dataset.sql`; connect as the Read_Only_User
    - Multi-turn cases create a Conversation and append prior Turns; rows cases compare columns in order case-insensitively and rows with 2-decimal rounding (order only when `orderSensitive`); blocked cases require an allowed Reason_Code and `ExecutedCount == 0`; crashing cases are recorded as failed
    - Exit code 1 in Stub_Mode when pass percentage is below 100
    - _Requirements: 6.4, 6.5, 6.6, 6.8, 6.9, 6.10, 6.11, 6.12_
    - Commit: `feat(evals): add stub-mode eval runner with execution counting`
  - [x] 14.5 Implement the `generate-expected` command

    - `dotnet run --project tests/AskMyData.Evals -- generate-expected [--connection <cs>] [--check]`: for every non-adversarial case, execute `referenceSql` as the Read_Only_User against the loaded Dataset (Docker Compose connection or Testcontainers) through `SqlServerQueryExecutor`, and write `expected.columns` and `expected.rows` into the case file with stable formatting (decimals rounded to 2 places, invariant culture)
    - Validate each `referenceSql` with `ScriptDomSqlValidator` first and fail on a reject; adversarial cases are skipped
    - `--check` regenerates into memory and exits non-zero with a diff summary when any case file would change
    - _Requirements: 6.5, 14.1, 14.6_
    - Commit: `feat(evals): add generate-expected command with no-diff check`
  - [x] 14.6 Author the Eval_Cases and generate their expected rows

    - At least 30 cases in `tests/AskMyData.Evals/cases/`: at least one per Metric (TotalRevenue, UnitsSold, OrderCount, GrossMargin, AverageOrderValue, CustomerCount), dimension cases, multi-turn cases with `priorTurns`, and at least 10 Adversarial_Cases covering instruction override, stacked statements, comment obfuscation, non-Allowlist table access (`dbo.Employees`), data modification and prompt disclosure
    - Each non-adversarial case has `referenceSql` and a stub Structured_Response whose sql equals it; adversarial cases have stub responses (unsafe SQL or refusal) and `allowedReasonCodes` (REFUSED or the specific validator Reason_Code)
    - Fill expected rows only by running `generate-expected` against the Dataset, never by hand; then `generate-expected --check` reports no diff
    - Run `--mode stub` locally and confirm 100 percent pass with zero executed SQL for adversarial cases
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.9, 10.4, 14.6_
    - Commit: `test(evals): add 30+ eval cases with generated expected rows`
  - [x] 14.7 Write unit tests for the case set

    - At least 30 cases, at least 10 adversarial covering every attack kind, one per Metric, multi-turn present, every file matches the schema, every `referenceSql` references only Dataset tables and columns
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 14.6_
    - Commit: `test(evals): assert eval case set composition`
  - [x] 14.8 Write unit tests for the runner

    - Stub label present; exit code 1 below threshold and 0 at 100 percent; Stub_Mode container has no `GeminiLlmClient` and no `HttpClient`
    - _Requirements: 6.6, 6.8, 6.10, 6.12_
    - Commit: `test(evals): cover stub-mode runner labels, exit codes and container`
  - [x] 14.9 Add `.github/workflows/ci.yml`

    - Triggers `pull_request` and `push` to `main`; `actions/checkout@v4`, `actions/setup-dotnet@v4` (.NET 8); `dotnet restore`; `dotnet build --no-restore -c Release -warnaserror`; unit tests; integration tests; `dotnet run --project tests/AskMyData.Evals -c Release -- generate-expected --check`; `dotnet run --project tests/AskMyData.Evals -c Release -- --mode stub --out artifacts/evals` (non-zero exit fails the job); `actions/upload-artifact@v4` with `if: always()`, name `accuracy-report-stub`, path `artifacts/evals`
    - _Requirements: 6.7, 6.10, 6.17, 14.1_
    - Commit: `ci: add pull request pipeline with stub-mode evals and report artifact`
  - [x] 14.10 Write smoke tests for `ci.yml`

    - Parse the YAML: triggers include `pull_request`, an eval step uses `--mode stub`, no step uses `--mode live`, upload-artifact step uses `if: always()`
    - _Requirements: 6.7, 6.15, 6.17_
    - Commit: `test(ci): smoke test pull request workflow`
- [x] 15. Checkpoint: Evals in Stub_Mode

  - Ensure all tests pass, ask the user if questions arise.
  - `dotnet run --project tests/AskMyData.Evals -- --mode stub --out artifacts/evals` exits 0 with 100 percent, and `generate-expected --check` reports no diff

### Slice 3: Demo_Guard

- [x] 16. Demo_Guard rate limiting

  - [x] 16.1 Implement `DemoGuardOptions` and `DemoRateLimiter`

    - Per-IP sliding 60 s window (default 20), global UTC Daily_Cap (default 500), 0 disables a limit; reasons `PER_IP_LIMIT` and `DAILY_CAP`; `RetryAfter = max(1, ceil(...))`; the IP timestamp is recorded only on admission; clock from `TimeProvider`
    - _Requirements: 13.1, 13.2, 13.3, 13.6_
    - Commit: `feat(demo-guard): add clock-injected per-ip and daily rate limiter`
  - [x] 16.2 Write property test for Demo_Guard

    - Use `RequestTimelineGen` crossing minute and UTC-midnight boundaries on `FakeTimeProvider`
    - **Property 25: Demo_Guard limits and Retry-After**
    - **Validates: Requirements 13.1, 13.2, 13.3, 13.6**
    - Commit: `test(demo-guard): property 25 limits and retry-after`
  - [x] 16.3 Implement `DemoGuardMiddleware` and wire it in `Program.cs`

    - `app.UseWhen` for `/api/ask` after CorrelationId and before routing; 429 problem details with `reason` and `Retry-After`; log client IP, reason and Correlation_Id; client IP from `Connection.RemoteIpAddress`; `UseForwardedHeaders` only when `DemoGuard:TrustedProxies` is configured, with those as `KnownProxies`
    - _Requirements: 13.3, 13.4, 13.5_
    - Commit: `feat(demo-guard): add middleware ahead of the orchestrator`
  - [x] 16.4 Write Demo_Guard integration tests

    - 429 with `Retry-After` and zero LLM and executor calls (spies); rejection log entry contains IP, reason and Correlation_Id; X-Correlation-Id present on 429
    - _Requirements: 13.3, 13.4, 13.5_
    - Commit: `test(demo-guard): integration tests for rejection path`
- [x] 17. Checkpoint: Demo_Guard

  - Ensure all tests pass, ask the user if questions arise.

### Slice 4: Observability extras

- [ ] 18. Audit, cost, usage and health

  - [x] 18.1 Add cost calculation and the full audit record

    - `LlmPricingOptions`, `ModelPrice`, `CostCalculator.Estimate` (0 when the model is not priced); `UsageAccumulator.Add(response, cost)` sums tokens and cost across the initial call and the Repair_Call
    - Extend `AuditRecord` and the handler to fill prompt and completion tokens, estimated cost, executionMs, rowCount, repairCallOccurred, provider and model; add pricing to `appsettings.json`
    - _Requirements: 7.2, 7.3, 7.7, 7.8, 7.10_
    - Commit: `feat(observability): add cost calculation and full audit record fields`
  - [x] 18.2 Add LLM-failure and validator-reject log entries

    - Handler calls `IAuditLog.LlmCallFailed` (Provider_Name, call type, elapsed, Correlation_Id, timedOut) and `IAuditLog.ValidationFailed` (Reason_Code); implement both in `JsonAuditLog`; ensure no result row values, API keys or connection strings are logged
    - _Requirements: 7.4, 7.5, 7.6_
    - Commit: `feat(observability): log llm call failures and validator rejections`
  - [x] 18.3 Write property test for usage and cost

    - **Property 20: Usage and cost are summed across all calls**
    - **Validates: Requirements 7.7, 7.8, 7.10**
    - Commit: `test(observability): property 20 usage and cost summed across calls`
  - [x] 18.4 Write property test for audit records

    - **Property 21: Exactly one complete audit record per request, without sensitive data**
    - **Validates: Requirements 7.2, 7.3, 7.5, 7.6, 7.9**
    - Commit: `test(observability): property 21 one complete audit record without sensitive data`
  - [x] 18.5 Write unit tests for LLM failure logging

    - Provider error and LLM_Timeout on initial and Repair_Call log Provider_Name, call type, elapsed duration and Correlation_Id
    - _Requirements: 7.4_
    - Commit: `test(observability): cover llm failure log fields`
  - [x] 18.6 Implement the health endpoint

    - `SqlServerHealthCheck` runs `SELECT 1` as the Read_Only_User with a 5 s timeout; `app.MapHealthChecks("/health")` with JSON `{"status":..., "sqlServer":...}`, 200 healthy and 503 unhealthy; not behind Demo_Guard
    - _Requirements: 7.11, 13.5_
    - Commit: `feat(api): add sql server health endpoint`
  - [x] 18.7 Write health endpoint integration tests

    - Healthy with the Testcontainers database, 503 with an unreachable connection string, and `/health` is never rate limited by Demo_Guard
    - _Requirements: 7.11, 13.5_
    - Commit: `test(api): integration tests for health endpoint`
- [x] 19. Checkpoint: observability

  - Ensure all tests pass, ask the user if questions arise.

### Slice 5: Live_Mode with Gemini

- [x] 20. Gemini adapter and Live_Mode evals

  - [x] 20.1 Implement `GeminiLlmClient` and register it

    - `LlmOptions`, `GeminiOptions` (model `gemini-2.5-flash`, API key, base URL); typed `HttpClient` calling `v1beta/models/{model}:generateContent` with `systemInstruction`, one user content with the Untrusted_Section, `responseMimeType = application/json` and a `responseSchema`; API key in `x-goog-api-key`; non-success, network errors and empty candidates throw `LlmUnavailableException`; tokens from `usageMetadata`
    - Register `Gemini` in the registry in `Program.cs`; `ValidateOnStart` requires a non-empty API key when the provider is Gemini
    - _Requirements: 11.1, 11.3, 11.4_
    - Commit: `feat(llm): add gemini adapter with structured output`
  - [x] 20.2 Write unit tests for the Gemini adapter

    - Fake `HttpMessageHandler`: request shape (system instruction, response schema, API key header), usage parsing, error status and empty candidates throw `LlmUnavailableException`
    - _Requirements: 11.4_
    - Commit: `test(llm): cover gemini adapter request shape and errors`
  - [x] 20.3 Add Live_Mode to the Eval runner

    - `--mode live` registers `GeminiLlmClient` from configuration; report `mode: live`, `label: LLM accuracy`, `passThreshold: null`, model name and token usage; exit code 0 regardless of pass rate
    - _Requirements: 6.13, 6.14, 6.16_
    - Commit: `feat(evals): add live mode with model and token usage reporting`
  - [x] 20.4 Write unit tests for Live_Mode reporting

    - Live report contains model and token usage and the LLM accuracy label; runner exits 0 below threshold
    - _Requirements: 6.14, 6.16_
    - Commit: `test(evals): cover live mode report fields and exit code`
  - [x] 20.5 Add `.github/workflows/evals-live.yml`

    - Triggers only `workflow_dispatch` and `schedule: cron "0 3 * * *"`; same setup and build; `--mode live --out artifacts/evals` with `Llm__Gemini__ApiKey: ${{ secrets.GEMINI_API_KEY }}` and `continue-on-error: true`; upload `accuracy-report-live` with `if: always()`
    - _Requirements: 6.13, 6.15, 6.16, 6.17_
    - Commit: `ci: add manual and nightly live-mode eval workflow`
  - [x] 20.6 Write smoke tests for `evals-live.yml`

    - Triggers are only `workflow_dispatch` and `schedule`; upload step uses `if: always()`; no `pull_request` trigger
    - _Requirements: 6.15, 6.17_
    - Commit: `test(ci): smoke test live eval workflow`
- [x] 21. Checkpoint: Live_Mode with Gemini

  - Ensure all tests pass, ask the user if questions arise.

### Slice 6: Portfolio polish

- [ ] 22. README and public demo

  - [ ] 22.1 Write the README

    - Sections in order: demo GIF placeholder, one-paragraph pitch, architecture diagram (Mermaid, hexagonal layers and request flow), "how safety works" (prompt isolation, validator, read-only user, row and time limits), eval results table (Stub_Mode pass rate and latest Live_Mode accuracy with model name), quick start (`docker compose up`, sample curl), configuration table, known limitations (Metric sub-tree check is order-sensitive, ad hoc calculations are only flagged, in-memory conversations), and a link to `.kiro/specs`
    - Commit: `docs: add readme with architecture, safety model and eval results`
  - [ ] 22.2 Deploy a public demo

    - Deploy the compose stack (or Api plus managed SQL Server) to a host of choice with Demo_Guard limits enabled and `TrustedProxies` configured; document the URL and the Gemini key handling in the README
    - Commit: `docs: document public demo deployment`
- [ ] 23. Final checkpoint: portfolio

  - Fresh clone, `docker compose up`, README quick start works as written; latest Accuracy_Report linked from the README

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP; required property tests (Properties 1 to 9, 12, 13, 25) have no `*`.
- Each leaf sub-task ends with exactly one Conventional Commits commit, shown in its `Commit:` bullet.
- Each task references requirement clauses for traceability; design.md holds the implementation detail.
- Eval expected rows are always produced by `generate-expected` against the deterministic Dataset; CI runs `generate-expected --check` to prove regeneration yields no diff.
- The end-to-end checkpoint (task 13) uses the Stub provider and needs no network access to any LLM provider.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "3.1", "5.1"] },
    { "id": 3, "tasks": ["4.1", "5.2", "6.1", "7.1", "7.3", "8.1"] },
    { "id": 4, "tasks": ["4.2", "5.3", "6.2", "6.3", "7.2", "7.4", "7.5", "8.2", "8.3"] },
    { "id": 5, "tasks": ["4.3", "7.6", "7.7", "10.1"] },
    { "id": 6, "tasks": ["4.4", "9.1", "10.2"] },
    { "id": 7, "tasks": ["4.5", "4.6", "4.7", "4.8", "4.9", "4.10", "9.2"] },
    { "id": 8, "tasks": ["4.11", "9.3", "9.4", "9.5", "9.6", "9.7", "9.8", "11.1", "11.2", "11.3"] },
    { "id": 9, "tasks": ["11.4"] },
    { "id": 10, "tasks": ["11.5", "11.6", "11.7", "11.8", "11.9", "12.1"] },
    { "id": 11, "tasks": ["14.1"] },
    { "id": 12, "tasks": ["14.2"] },
    { "id": 13, "tasks": ["14.3", "14.4"] },
    { "id": 14, "tasks": ["14.5"] },
    { "id": 15, "tasks": ["14.6"] },
    { "id": 16, "tasks": ["14.7", "14.8", "14.9"] },
    { "id": 17, "tasks": ["14.10", "16.1"] },
    { "id": 18, "tasks": ["16.2", "16.3"] },
    { "id": 19, "tasks": ["16.4", "18.1", "18.6"] },
    { "id": 20, "tasks": ["18.2", "18.3", "18.7"] },
    { "id": 21, "tasks": ["18.4", "18.5", "20.1"] },
    { "id": 22, "tasks": ["20.2", "20.3"] },
    { "id": 23, "tasks": ["20.4", "20.5"] },
    { "id": 24, "tasks": ["20.6"] },
    { "id": 25, "tasks": ["22.1"] },
    { "id": 26, "tasks": ["22.2"] }
  ]
}
```
