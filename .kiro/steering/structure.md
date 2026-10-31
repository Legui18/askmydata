# Architecture: hexagonal (ports & adapters)
src/
  AskMyData.Domain/          # entities, value objects, semantic-layer metrics
  AskMyData.Application/     # use cases, ports (ILlmClient, IQueryExecutor, ISqlValidator)
  AskMyData.Infrastructure/  # adapters: Gemini, SQL Server read-only executor, logging
  AskMyData.Api/             # controllers, DI, middleware
tests/
  AskMyData.UnitTests/
  AskMyData.IntegrationTests/
  AskMyData.Evals/           # question -> expected result suite with accuracy report
Rules: Domain depends on nothing. Application depends only on Domain.
Naming: PascalCase, async methods end in Async, no business logic in controllers.