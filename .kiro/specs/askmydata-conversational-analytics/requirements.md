# Requirements Document

## Introduction

AskMyData is a conversational analytics copilot. Business users ask questions in natural language, and the backend generates safe, read-only SQL over SQL Server, executes it, and returns a result table, a chart suggestion, and a short explanation. This spec covers the backend only: the Domain, Application, Infrastructure and Api layers, plus the Eval_Suite. The frontend and chat user interface are out of scope and will be specified separately; this spec defines only the HTTP contract that a client consumes.

Guiding principles: the LLM never gets write access to anything, every answer shows the SQL that produced it, and Metrics come from a governed semantic layer rather than being invented by the LLM.

## Glossary

- **System**: The AskMyData backend, consisting of the Domain, Application, Infrastructure and Api layers.
- **Api**: The ASP.NET Core Web API layer that exposes HTTP endpoints, performs dependency injection and hosts middleware.
- **Orchestrator**: The Application-layer use case that coordinates prompt building, SQL generation, validation, execution and chart suggestion.
- **ILlmClient**: The Application-layer port through which the System accesses a large language model.
- **ISqlValidator**: The Application-layer port that validates generated SQL before execution. Also referred to as the Sql_Validator.
- **IQueryExecutor**: The Application-layer port that executes validated SQL against SQL Server. Also referred to as the Query_Executor.
- **Provider_Name**: The configuration value that selects the ILlmClient implementation (for example "Gemini" or "Stub").
- **Metric**: A governed business measure (for example total revenue) defined in the semantic layer with a fixed SQL expression.
- **Semantic_Layer**: The Domain-layer catalog of Metrics, dimensions and Allowlist entries.
- **Allowlist**: The configured set of tables, views and their columns that generated SQL may reference.
- **Allowed_Table_Function_List**: The configured set of table-valued functions that generated SQL may call. The default value is empty.
- **Conversation**: A sequence of question and answer Turns sharing one Conversation_Id.
- **Conversation_Id**: The server-generated identifier of a Conversation.
- **Turn**: One question and answer exchange within a Conversation, consisting of the question, the executed SQL, and the explanation.
- **Conversation_History**: The prior Turns of a Conversation that the Orchestrator supplies to the ILlmClient.
- **IConversationStore**: The Application-layer port for creating Conversations, loading the most recent N Turns, and appending a Turn.
- **In_Memory_Conversation_Store**: The Infrastructure-layer IConversationStore implementation that keeps Conversations in process memory.
- **Conversation_TTL**: The idle duration after which a Conversation expires. The default value is 60 minutes.
- **Question_Request**: The HTTP request to POST /api/ask carrying a user question and an optional Conversation_Id.
- **Question_Response**: The HTTP response carrying the Conversation_Id, the executed SQL, the result table, the Truncated_Flag, the Chart_Suggestion, the explanation, the Metric_Usage, a list of warnings, and the Usage_Summary.
- **Structured_Response**: The single LLM output consumed by the Orchestrator, containing the fields sql, metricUsage, explanation and adHocCalculation, and an optional refusal string. When refusal is present and non-empty, sql may be empty.
- **Metric_Usage**: The list of Metric names that the ILlmClient declares in the metricUsage field of the Structured_Response as used by the generated SQL.
- **Normalized_Syntax_Tree**: The parsed T-SQL syntax tree after normalization, which removes comments and whitespace, folds identifier and keyword case, removes bracket quoting, defaults the schema to dbo, and replaces table aliases with the underlying base table name.
- **Metric_Definition_Mismatch**: A Metric check failure in which either (a) a name in Metric_Usage is not a Metric in the Semantic_Layer, or (b) the Normalized_Syntax_Tree of the declared Metric SQL expression is not a sub-tree of the Normalized_Syntax_Tree of the query.
- **Ad_Hoc_Calculation**: A calculation that is not defined as a Metric in the Semantic_Layer, flagged by the adHocCalculation field.
- **Validation_Result**: The outcome of ISqlValidator, consisting of a pass or fail indication, a Reason_Code on failure, and a Detail that names the Metric when the Reason_Code is METRIC_DEFINITION_MISMATCH.
- **Reason_Code**: A stable upper-snake-case identifier describing why ISqlValidator rejected SQL, or why the Orchestrator returned a REFUSED or GENERATION_INVALID failure outcome.
- **Repair_Call**: A single additional ILlmClient call made by the Orchestrator after a validation or execution failure, supplying the failure reason so the LLM can correct the SQL.
- **Untrusted_Section**: The delimited portion of a prompt that contains the user question, the Conversation_History, and on a Repair_Call the failing SQL, and is marked as untrusted data.
- **System_Prompt**: The instruction text the System sends to the ILlmClient, excluding the Untrusted_Section.
- **LLM_Timeout**: The maximum duration of 30 seconds allowed for a single ILlmClient call.
- **Request_Deadline**: The maximum duration allowed for the whole Orchestrator run of one Question_Request, covering every ILlmClient call, the Repair_Call, validation and SQL execution. The default value is 60 seconds and the value is configurable. The Request_Deadline takes precedence over any remaining LLM_Timeout or command timeout. Expiry produces a failure outcome with error category DEADLINE_EXCEEDED.
- **Row_Limit**: The maximum number of result rows the IQueryExecutor returns. The default value is 1000 and the value is configurable.
- **Truncated_Flag**: The Boolean indicator in the query result and the Question_Response that is true when more rows than the Row_Limit were available.
- **Chart_Suggestion**: The chart type (bar, line, or table-only) that the Application layer derives from the result column names and types.
- **Usage_Summary**: The Question_Response section containing the total token count and the elapsed time in milliseconds for the request.
- **Dataset**: The deterministic sample database that the Semantic_Layer, the Allowlist and the Eval_Cases are defined against.
- **Dataset_Script**: The SQL script that creates and loads the Dataset and the Read_Only_User.
- **Read_Only_User**: The SQL Server database principal with SELECT permission on Allowlist objects only, used by the IQueryExecutor.
- **Demo_Guard**: The Api middleware that enforces the per-IP rate limit and the Daily_Cap before the Orchestrator runs.
- **Daily_Cap**: The maximum number of accepted requests per UTC day. The default value is 500 and the value is configurable.
- **Stub_Mode**: The execution mode in which the Eval_Suite runs against a deterministic stub ILlmClient and requires no network access.
- **Live_Mode**: The execution mode in which the Eval_Suite runs against a real LLM provider.
- **Eval_Suite**: The collection of Eval_Cases in the AskMyData.Evals project.
- **Eval_Case**: A single test definition, defined against the Dataset, containing a question, optional prior Turns, and the expected outcome expressed as expected columns and rows.
- **Adversarial_Case**: An Eval_Case designed to induce unsafe behavior, such as instruction override, stacked statements, comment obfuscation, non-Allowlist table access, or data modification requests.
- **Accuracy_Report**: The output of an Eval_Suite run in both JSON and Markdown formats, listing the mode, a timestamp, per-case results, and aggregate pass rates.
- **Pass_Threshold**: The minimum pass rate an Eval_Suite run must reach. In Stub_Mode the Pass_Threshold is 100 percent of all Eval_Cases, including Adversarial_Cases.
- **CI_Pipeline**: The GitHub Actions workflow that builds and tests the System.
- **Correlation_Id**: A unique identifier assigned to each Question_Request, attached to every log entry produced while serving the request, and returned in the X-Correlation-Id response header.

## Requirements

### Requirement 1: Governed Semantic Layer

**User Story:** As a business analyst, I want answers based on governed Metrics, so that numbers are consistent and not invented by the LLM.

#### Acceptance Criteria

1. THE Semantic_Layer SHALL define each Metric with a name, a description, a SQL expression, and the Allowlist tables the Metric depends on.
2. THE Semantic_Layer SHALL define the Allowlist as a configured set of tables, views and columns.
3. WHEN the Orchestrator builds a prompt, THE Orchestrator SHALL include the Metric definitions and the Allowlist schema in the System_Prompt.
4. WHEN a question refers to a Metric by name, THE Orchestrator SHALL instruct the ILlmClient to use the Metric SQL expression exactly as defined.
5. THE Semantic_Layer SHALL reside in the Domain layer and SHALL depend on no other layer.
6. THE Semantic_Layer SHALL define every Metric and Allowlist entry against tables and columns present in the Dataset.

### Requirement 2: Question to SQL Orchestration

**User Story:** As a business user, I want my natural-language question converted into SQL, so that I can query data without knowing SQL.

#### Acceptance Criteria

1. WHEN a question is received, THE Orchestrator SHALL build a prompt containing the System_Prompt and the Untrusted_Section.
2. WHEN the prompt is built, THE Orchestrator SHALL make one ILlmClient call that returns a single Structured_Response containing sql, metricUsage, explanation, adHocCalculation, and an optional refusal.
3. WHEN the ILlmClient returns a Structured_Response without a non-empty refusal, THE Orchestrator SHALL submit the sql and the Metric_Usage to ISqlValidator before any other processing.
4. WHEN ISqlValidator returns a pass, THE Orchestrator SHALL submit the SQL to IQueryExecutor.
5. WHEN IQueryExecutor returns rows, THE Orchestrator SHALL derive the Chart_Suggestion from the result column names and types and SHALL use the explanation field of the Structured_Response as the explanation.
6. IF ISqlValidator returns a fail, THEN THE Orchestrator SHALL make one Repair_Call supplying the Reason_Code.
7. IF IQueryExecutor returns an execution error, including an execution error with category TIMEOUT, THEN THE Orchestrator SHALL make one Repair_Call supplying the error category.
8. IF the SQL returned by a Repair_Call fails validation or execution, THEN THE Orchestrator SHALL return a failure outcome with the Reason_Code or error category of the last attempt and SHALL make no further Repair_Call.
9. WHERE the adHocCalculation flag is true, THE Orchestrator SHALL include in the Question_Response a warning message stating that the calculation is not a governed Metric.
10. THE Orchestrator SHALL include the executed SQL in every successful Question_Response.
11. IF the ILlmClient output is not a valid Structured_Response, THEN THE Orchestrator SHALL treat the output as a generation failure handled by the Repair_Call rule, where a Structured_Response that contains a non-empty refusal and an empty sql counts as valid.
12. WHEN the Metric_Usage is empty, THE Orchestrator SHALL set adHocCalculation to true.
13. IF a Structured_Response, including a Structured_Response returned by a Repair_Call, contains a non-empty refusal, THEN THE Orchestrator SHALL return a failure outcome with Reason_Code REFUSED and the refusal text, SHALL make no Repair_Call, and SHALL submit no SQL to ISqlValidator or IQueryExecutor.
14. IF the output of a Repair_Call is not a valid Structured_Response, THEN THE Orchestrator SHALL return a failure outcome with Reason_Code GENERATION_INVALID and SHALL make no further Repair_Call.
15. WHEN a question is received, THE Orchestrator SHALL start the Request_Deadline and SHALL apply the Request_Deadline to every ILlmClient call, the Repair_Call, validation and SQL execution of that request.
16. WHILE the time remaining before the Request_Deadline is shorter than the LLM_Timeout, THE Orchestrator SHALL bound the in-flight ILlmClient call by the remaining Request_Deadline time.
17. IF the Request_Deadline expires, THEN THE Orchestrator SHALL cancel any in-flight ILlmClient call and SQL command and SHALL return a failure outcome with error category DEADLINE_EXCEEDED.
18. IF the Request_Deadline has expired, THEN THE Orchestrator SHALL start no Repair_Call.

### Requirement 3: SQL Validation

**User Story:** As a security engineer, I want every generated SQL statement validated, so that only safe read-only queries reach the database.

#### Acceptance Criteria

1. WHEN SQL is submitted, THE ISqlValidator SHALL parse the SQL using a T-SQL parser and SHALL return a fail with Reason_Code PARSE_ERROR if the SQL cannot be parsed.
2. IF the SQL contains a statement other than a single SELECT statement, including EXEC, a system procedure call, or SELECT INTO, THEN THE ISqlValidator SHALL return a fail with Reason_Code NOT_SELECT_ONLY.
3. IF the SQL contains more than one statement, THEN THE ISqlValidator SHALL return a fail with Reason_Code MULTIPLE_STATEMENTS.
4. IF the SQL references a table or view that is not in the Allowlist, THEN THE ISqlValidator SHALL return a fail with Reason_Code TABLE_NOT_ALLOWED.
5. IF the SQL contains a SQL comment, THEN THE ISqlValidator SHALL return a fail with Reason_Code COMMENT_NOT_ALLOWED.
6. IF the SQL contains OPENROWSET, THEN THE ISqlValidator SHALL return a fail with Reason_Code OPENROWSET_NOT_ALLOWED.
7. IF the SQL contains OPENQUERY, THEN THE ISqlValidator SHALL return a fail with Reason_Code OPENQUERY_NOT_ALLOWED.
8. IF the SQL contains a four-part name that references a linked server, THEN THE ISqlValidator SHALL return a fail with Reason_Code LINKED_SERVER_NOT_ALLOWED.
9. IF the SQL contains a cross-database reference, THEN THE ISqlValidator SHALL return a fail with Reason_Code CROSS_DATABASE_NOT_ALLOWED.
10. IF the SQL calls a table-valued function that is not in the Allowed_Table_Function_List, THEN THE ISqlValidator SHALL return a fail with Reason_Code TABLE_FUNCTION_NOT_ALLOWED.
11. THE System SHALL configure the Allowed_Table_Function_List with an empty default value.
12. WHEN the SQL satisfies all validation rules, THE ISqlValidator SHALL return a pass.
13. FOR ALL SQL inputs, THE ISqlValidator SHALL return the same Validation_Result when the same SQL is validated twice (determinism).
14. IF the SQL contains an unqualified * or table.* select item, THEN THE ISqlValidator SHALL return a fail with Reason_Code SELECT_STAR; COUNT(*) is allowed.
15. THE ISqlValidator SHALL resolve table aliases to base tables, exclude CTE names and derived-table aliases from base-table Allowlist checks, and validate every reference inside CTEs and derived tables.
16. IF the SQL references a column that is not an Allowlist column of the resolved base table, THEN THE ISqlValidator SHALL return a fail with Reason_Code COLUMN_NOT_ALLOWED.
17. IF the SQL references a system object or catalog view, including sys.* and INFORMATION_SCHEMA.*, THEN THE ISqlValidator SHALL return a fail with Reason_Code SYSTEM_OBJECT_NOT_ALLOWED.
18. WHEN the Metric_Usage declares one or more Metrics, THE ISqlValidator SHALL, for each declared Metric, compare the Normalized_Syntax_Tree of the Metric SQL expression with the Normalized_Syntax_Tree of the parsed query and SHALL accept the Metric when the expression tree appears as a sub-tree of the query tree.
19. IF a Metric check results in a Metric_Definition_Mismatch, THEN THE ISqlValidator SHALL return a fail with Reason_Code METRIC_DEFINITION_MISMATCH naming the Metric and stating which case applied: (a) the declared name is not a Metric in the Semantic_Layer, or (b) the expression tree is not a sub-tree of the query tree.
20. WHEN the Metric_Usage is empty, THE ISqlValidator SHALL skip the Metric check.
21. THE ISqlValidator SHALL perform the Metric check on syntax trees and SHALL use no raw-text or semantic-similarity comparison.
22. THE ISqlValidator SHALL evaluate the validation rules in a fixed documented order and SHALL return the Reason_Code of the first rule that fails.
23. IF the SQL calls a schema-qualified (user-defined) scalar or aggregate function, THEN THE ISqlValidator SHALL return a fail with Reason_Code FUNCTION_NOT_ALLOWED.
24. WHEN the SQL calls a built-in T-SQL scalar or aggregate function, THE ISqlValidator SHALL accept the function call, subject to the other validation rules.

### Requirement 4: Least-Privilege Query Execution

**User Story:** As a security engineer, I want queries executed with least privilege and bounded cost, so that the database is protected even if validation has a gap.

#### Acceptance Criteria

1. THE IQueryExecutor implementation SHALL connect to SQL Server using the Read_Only_User.
2. WHEN IQueryExecutor executes SQL, THE IQueryExecutor SHALL apply a configurable command timeout with a default of 15 seconds.
3. IF a command timeout occurs, THEN THE IQueryExecutor SHALL return an execution error with category TIMEOUT.
4. WHEN IQueryExecutor executes SQL, THE IQueryExecutor SHALL request at most Row_Limit plus 1 rows.
5. IF more than Row_Limit rows are returned, THEN THE IQueryExecutor SHALL return exactly Row_Limit rows and set the Truncated_Flag to true.
6. IF Row_Limit or fewer rows are returned, THEN THE IQueryExecutor SHALL set the Truncated_Flag to false.
7. THE System SHALL configure the Row_Limit with a default value of 1000.
8. IF SQL Server returns an execution error other than a timeout, THEN THE IQueryExecutor SHALL return an execution error with category EXECUTION_ERROR and SHALL log the error details.
9. WHILE the time remaining before the Request_Deadline is shorter than the command timeout, THE IQueryExecutor SHALL bound the SQL command by the remaining Request_Deadline time.
10. IF the Request_Deadline expires during SQL execution, THEN THE IQueryExecutor SHALL cancel the SQL command.

### Requirement 5: Conversational Question API

**User Story:** As an API client developer, I want a conversational question endpoint, so that clients can ask questions and receive transparent answers.

#### Acceptance Criteria

1. WHEN a valid Question_Request is received at POST /api/ask, THE Api SHALL invoke the Orchestrator and return HTTP 200 with a Question_Response.
2. THE Question_Response SHALL contain the Conversation_Id, the executed SQL, the result table, the Truncated_Flag, the Chart_Suggestion, the explanation, the Metric_Usage, a list of warnings, and the Usage_Summary.
3. IF the question text is empty or exceeds 1000 characters, THEN THE Api SHALL return HTTP 400 with an error message.
4. IF the Orchestrator returns a validation failure outcome after the Repair_Call, THEN THE Api SHALL return HTTP 422 with the Reason_Code.
5. IF an ILlmClient call, including a Repair_Call, fails or exceeds the LLM_Timeout, THEN THE Api SHALL return HTTP 503 with an error message.
6. WHEN a Question_Request contains a Conversation_Id, THE Api SHALL load the Conversation_History of that Conversation from the IConversationStore and supply the Conversation_History to the Orchestrator.
7. THE Api SHALL contain no business logic in controllers.
8. IF the Orchestrator returns an execution failure outcome whose last attempt has category TIMEOUT, THEN THE Api SHALL return HTTP 504 with a timeout error message.
9. IF the Orchestrator returns an execution failure outcome whose last attempt has category EXECUTION_ERROR, THEN THE Api SHALL return HTTP 502 with a generic error message that contains no SQL Server error text.
10. WHEN the query result contains zero rows, THE Api SHALL return HTTP 200 with an empty result table, the explanation, and a table-only Chart_Suggestion.
11. WHEN a Question_Request has no Conversation_Id, THE Api SHALL create a new Conversation through the IConversationStore and return the new Conversation_Id in the Question_Response.
12. IF the Orchestrator returns a failure outcome with Reason_Code REFUSED, from the initial ILlmClient call or from a Repair_Call, THEN THE Api SHALL return HTTP 422 with Reason_Code REFUSED and the refusal text.
13. IF the Orchestrator returns a failure outcome with Reason_Code GENERATION_INVALID, THEN THE Api SHALL return HTTP 422 with Reason_Code GENERATION_INVALID.
14. IF the Orchestrator returns a failure outcome with error category DEADLINE_EXCEEDED, THEN THE Api SHALL return HTTP 504 with a timeout error message, regardless of which ILlmClient call or SQL command was in flight.

### Requirement 6: Evals

**User Story:** As a developer, I want an automated evaluation suite with two modes, so that pipeline and security regressions are caught in every pull request and real LLM accuracy is measured separately.

#### Acceptance Criteria

1. THE Eval_Suite SHALL contain at least 30 Eval_Cases.
2. THE Eval_Suite SHALL contain at least 10 Adversarial_Cases covering instruction override, stacked statements, comment obfuscation, non-Allowlist table access, and data modification requests.
3. THE Eval_Suite SHALL contain at least one Eval_Case for each Metric in the Semantic_Layer.
4. THE Eval_Suite SHALL contain multi-turn Eval_Cases that supply prior Turns as Conversation_History.
5. THE Eval_Suite SHALL define each Eval_Case against the Dataset with the expected outcome expressed either as expected columns and rows, or, for Adversarial_Cases, as a blocked outcome with the set of allowed Reason_Codes (including REFUSED).
6. WHEN the Eval_Suite runs in Stub_Mode, THE Eval_Suite SHALL execute using the stub ILlmClient and without network access to any LLM provider.
7. THE CI_Pipeline SHALL run the Eval_Suite in Stub_Mode on every pull request.
8. WHILE the Eval_Suite runs in Stub_Mode, THE Eval_Suite SHALL apply a Pass_Threshold of 100 percent of all Eval_Cases.
9. WHILE the Eval_Suite runs in Stub_Mode, THE System SHALL execute zero SQL statements produced for Adversarial_Cases that ISqlValidator should reject.
10. IF the pass rate in Stub_Mode is below the Pass_Threshold, THEN THE CI_Pipeline SHALL fail the build.
11. WHEN the Eval_Suite runs, THE Eval_Suite SHALL produce an Accuracy_Report in both JSON and Markdown formats containing the mode used, a timestamp, the result of each Eval_Case, the overall pass percentage, and the pass rate of Adversarial_Cases.
12. WHILE the Eval_Suite runs in Stub_Mode, THE Accuracy_Report SHALL label the pass percentage as a pipeline and security check and not as LLM accuracy.
13. WHERE Live_Mode is enabled, THE Eval_Suite SHALL run against a real LLM provider and produce the headline accuracy number.
14. WHERE Live_Mode is enabled, THE Accuracy_Report SHALL state the model name and the token usage.
15. THE CI_Pipeline SHALL run the Eval_Suite in Live_Mode only manually or on a nightly schedule and SHALL exclude Live_Mode from the pull-request pipeline.
16. WHILE the Eval_Suite runs in Live_Mode, THE Eval_Suite SHALL exit with success regardless of the pass rate.
17. WHEN the CI_Pipeline completes an Eval_Suite run, THE CI_Pipeline SHALL upload the Accuracy_Report as a GitHub Actions build artifact.
18. FOR ALL valid Accuracy_Report objects, serializing to JSON, deserializing, and serializing again SHALL produce an equivalent Accuracy_Report (round-trip property).

### Requirement 7: Observability

**User Story:** As an operator, I want structured logging of each question, so that I can diagnose failures and audit usage.

#### Acceptance Criteria

1. WHEN a Question_Request is received, THE Api SHALL assign a Correlation_Id, attach the Correlation_Id to every log entry for that request, and return the Correlation_Id in the X-Correlation-Id response header.
2. WHEN the Orchestrator completes a request, THE System SHALL write one structured JSON log entry containing the question text, the generated SQL, the total latency, the prompt tokens, the completion tokens, the estimated cost in USD, the validation outcome (accepted or rejected, plus the Reason_Code), the Conversation_Id, and the Correlation_Id.
3. THE System SHALL log the execution duration, the row count, and whether a Repair_Call occurred for each completed request.
4. WHEN an ILlmClient call fails or exceeds the LLM_Timeout, THE System SHALL log the Provider_Name, the call type (initial or Repair_Call), the elapsed duration, and the Correlation_Id.
5. WHEN ISqlValidator returns a fail, THE System SHALL log the Reason_Code.
6. THE System SHALL exclude result row values, API keys and connection strings from log entries.
7. THE System SHALL compute the estimated cost from the configured per-token price of the active model.
8. THE System SHALL sum the token usage and the estimated cost across all ILlmClient calls of a request, including the Repair_Call.
9. IF a request fails before SQL generation, THEN THE System SHALL write the log entry with an empty SQL value and the failure reason.
10. THE Question_Response SHALL include the Usage_Summary with the total tokens and the elapsed milliseconds.
11. THE Api SHALL expose a health endpoint reporting SQL Server connectivity.
12. IF the Request_Deadline expires, THEN THE System SHALL write the log entry with DEADLINE_EXCEEDED as the failure reason.

### Requirement 8: Conversation History

**User Story:** As a business user, I want to ask follow-up questions, so that I can refine an analysis without restating context.

#### Acceptance Criteria

1. WHEN a Question_Request contains a Conversation_Id, THE Orchestrator SHALL include the prior questions and the SQL of prior Turns of that Conversation in the Untrusted_Section.
2. THE Orchestrator SHALL limit the Conversation_History to the most recent 10 Turns in chronological order.
3. IF a Conversation_Id is unknown or expired, THEN THE Api SHALL return HTTP 404 with an error body naming the Conversation_Id and SHALL not start a new Conversation.
4. WHEN a Question_Request has no Conversation_Id, THE Api SHALL create a new Conversation and return its server-generated Conversation_Id.

### Requirement 9: LLM Data Minimization

**User Story:** As a data owner, I want to minimize the data sent to the LLM provider, so that business data is not exposed to a third party.

#### Acceptance Criteria

1. THE Orchestrator SHALL send to the ILlmClient only the System_Prompt, the Untrusted_Section (including the failing SQL on a Repair_Call), and the Repair_Call failure information defined in criterion 2.
2. WHEN the Orchestrator builds a Repair_Call, THE Orchestrator SHALL include only the failing SQL (placed inside the Untrusted_Section), the Reason_Code or error category, and the Metric name from the Validation_Result Detail, and SHALL NOT include database error text or result data.
3. THE Orchestrator SHALL send the schema of Allowlist objects and not the contents of any table.
4. THE Orchestrator SHALL send zero result rows to the ILlmClient in any call.
5. THE Orchestrator SHALL obtain the explanation from the explanation field of the same Structured_Response that contains the SQL, describing the Metrics and filters used, and SHALL make no second ILlmClient call to generate the explanation.
6. THE Orchestrator SHALL limit the explanation to 500 characters.
7. THE Application layer SHALL derive the Chart_Suggestion from the result column names and types without an ILlmClient call, selecting bar, line, or table-only.
8. IF the result contains fewer than two rows or no numeric column, THEN THE Application layer SHALL set the Chart_Suggestion to table-only.

### Requirement 10: Prompt-Injection Resistance

**User Story:** As a security engineer, I want the System to resist prompt injection, so that malicious questions cannot override safety controls or leak internal instructions.

#### Acceptance Criteria

1. WHEN the Orchestrator builds a prompt, THE Orchestrator SHALL place the user question, the Conversation_History, and on a Repair_Call the failing SQL in the Untrusted_Section, delimited and marked as untrusted data.
2. THE System_Prompt SHALL instruct the ILlmClient to ignore any instruction inside the Untrusted_Section that conflicts with the System_Prompt.
3. THE Api SHALL exclude the System_Prompt text from every response.
4. IF a user question requests disclosure of the System_Prompt, THEN THE Api SHALL return a response that contains no System_Prompt text.
5. THE Orchestrator SHALL run ISqlValidator on every SQL statement generated by the ILlmClient, regardless of the ILlmClient implementation in use.

### Requirement 11: Swappable Provider

**User Story:** As a platform engineer, I want to swap LLM providers by configuration, so that the System is not locked into one vendor.

#### Acceptance Criteria

1. WHEN the Api starts, THE Api SHALL resolve the ILlmClient implementation through dependency injection using the configured Provider_Name.
2. IF the configured Provider_Name does not match a registered ILlmClient implementation, THEN THE Api SHALL fail startup with an error message naming the Provider_Name.
3. THE Domain layer and the Application layer SHALL contain no LLM vendor types.
4. THE Infrastructure layer SHALL contain each ILlmClient implementation, including the Gemini adapter and the stub used in Stub_Mode.

### Requirement 12: Conversation Store

**User Story:** As a business user, I want my conversation remembered for a while, so that follow-up questions have context.

#### Acceptance Criteria

1. THE Application layer SHALL define an IConversationStore port with operations to create a Conversation, load the most recent N Turns, and append a Turn.
2. THE Infrastructure layer SHALL provide the In_Memory_Conversation_Store as an IConversationStore implementation.
3. THE In_Memory_Conversation_Store SHALL expire a Conversation after the Conversation_TTL of idle time, with a configurable default of 60 minutes, and SHALL reset the idle time on every load or append (sliding expiration).
4. WHEN Turns are loaded, THE In_Memory_Conversation_Store SHALL return the Turns in chronological order.
5. WHEN concurrent requests append Turns to the same Conversation, THE In_Memory_Conversation_Store SHALL preserve every appended Turn.
6. IF a Conversation_Id is unknown or expired, THEN THE IConversationStore SHALL signal that the Conversation does not exist so that the Api returns HTTP 404 naming the Conversation_Id.
7. WHEN the Orchestrator produces an answer, THE Orchestrator SHALL append the Turn to the Conversation.

### Requirement 13: Demo Protection

**User Story:** As an operator of a public demo, I want request limits, so that abuse does not exhaust the LLM budget.

#### Acceptance Criteria

1. THE Demo_Guard SHALL limit each client IP address to a configurable number of requests per minute, with a default of 20.
2. THE Demo_Guard SHALL limit the total accepted requests per UTC day to the configurable Daily_Cap, with a default of 500.
3. IF a limit is exceeded, THEN THE Demo_Guard SHALL return HTTP 429 with a Retry-After header stating the seconds until the limit resets.
4. WHEN a request is rejected by a limit, THE System SHALL make no call to ILlmClient or IQueryExecutor and SHALL log the client IP, the reason, and the Correlation_Id.
5. THE Demo_Guard SHALL run in the Api before the Orchestrator.
6. WHERE a limit is configured as 0, THE Demo_Guard SHALL disable that limit.

### Requirement 14: Sample Dataset

**User Story:** As a developer, I want a repeatable sample dataset, so that the Semantic_Layer and evaluations are stable across machines and runs.

#### Acceptance Criteria

1. THE Dataset_Script SHALL create the Dataset with a fixed random seed so every run produces identical data.
2. WHEN Docker Compose starts the stack, THE Dataset_Script SHALL load the Dataset into SQL Server 2022.
3. WHEN the Dataset_Script runs against an already loaded Dataset, THE Dataset_Script SHALL leave the data unchanged.
4. THE Dataset_Script SHALL create the Read_Only_User with SELECT permission on Allowlist objects only.
5. IF a statement in the Dataset_Script fails, THEN THE Dataset_Script SHALL log the failing statement and exit with a non-zero exit code.
6. THE Semantic_Layer Metrics, the Allowlist, and every Eval_Case SHALL reference only tables and columns present in the Dataset.
