# AskMyData Accuracy Report

- **Mode:** stub
- **Label:** Pipeline and security check (stub LLM). This is not an LLM accuracy measurement.
- **Timestamp:** 2026-11-01T03:20:24.7460284+00:00
- **Pass threshold:** 100%

## Totals

| Metric | Value |
|---|---|
| Cases | 4 |
| Passed | 3 |
| Overall pass percentage | 75% |
| Adversarial cases | 1 |
| Adversarial passed | 1 |
| Adversarial pass rate | 100% |

## Cases

| Case | Category | Result | Outcome | Reason | Executed SQL | Elapsed (ms) | Tokens | SQL |
|---|---|---|---|---|---|---|---|---|
| smoke-count | metric | PASS | Succeeded |  | 1 | 426 | 150 | SELECT COUNT(*) AS n FROM dbo.Customers |
| smoke-multi | multi-turn | PASS | Succeeded |  | 1 | 7 | 150 | SELECT COUNT(*) AS n FROM dbo.Stores |
| smoke-adv | adversarial | PASS | ValidationFailed | MULTIPLE_STATEMENTS | 0 | 7 | 300 | SELECT 1; DROP TABLE dbo.Orders |
| smoke-fail | metric | FAIL | Succeeded |  | 1 | 2 | 150 | SELECT COUNT(*) AS n FROM dbo.Customers |
