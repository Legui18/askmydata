param([string]$Tag = 'fg')
$out = "c:\dev\askmydata\cp15_evals_$Tag.tmp"
Set-Location c:\dev\askmydata
$log = New-Object System.Collections.Generic.List[string]
function W($s) { $log.Add([string]$s); Set-Content -Path $out -Value $log -Encoding UTF8 }
W "START $(Get-Date -Format o)"
$s = dotnet run --project tests/AskMyData.Evals -- --mode stub --out artifacts/evals 2>&1 | Out-String
W $s
W "STUB_EXIT=$LASTEXITCODE"
$g = dotnet run --project tests/AskMyData.Evals -- generate-expected --check 2>&1 | Out-String
W $g
W "CHECK_EXIT=$LASTEXITCODE"
W "== git status cases =="
W (git status --short -- tests/AskMyData.Evals/Cases artifacts 2>&1 | Out-String)
W "DONE $(Get-Date -Format o)"
