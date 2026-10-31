# Product
AskMyData: a conversational analytics copilot. Business users ask questions in
natural language; the system generates safe, read-only SQL over SQL Server,
executes it, and returns a table, a chart suggestion, and a short explanation.

Principles:
- Safety first: the LLM never gets write access to anything.
- Every answer shows the SQL that produced it (transparency).
- Metrics come from a semantic layer, never invented by the LLM.