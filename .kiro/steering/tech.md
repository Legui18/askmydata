# Tech stack
- .NET 8, ASP.NET Core Web API, EF Core (write side) + Dapper (read-only query execution)
- SQL Server 2022 in Docker, sample dataset: AdventureWorks (or Contoso)
- Frontend: Vue 3 + TypeScript + Chart.js
- Tests: xUnit, FluentAssertions, Testcontainers for SQL Server
- CI: GitHub Actions. Containerized with Docker Compose.
- LLM access only through the ILlmClient port (Gemini adapter first).