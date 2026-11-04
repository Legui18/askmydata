Set-Location c:\dev\askmydata
$log = 'c:\dev\askmydata\final146.tmp'
"START" | Set-Content $log
dotnet run --project tests/AskMyData.Evals -- generate-expected --check 2>&1 | Out-String | Add-Content $log
$check = $LASTEXITCODE
"CHECK_EXIT=$check" | Add-Content $log
dotnet run --project tests/AskMyData.Evals -- --mode stub --out tmp_out_146 2>&1 | Out-String | Add-Content $log
$stub = $LASTEXITCODE
"STUB_EXIT=$stub" | Add-Content $log
git log --oneline -12 2>&1 | Out-String | Add-Content $log
if ($check -ne 0 -or $stub -ne 0) { "SKIP_COMMIT" | Add-Content $log; exit 1 }
Get-ChildItem tests\AskMyData.Evals\Cases -Filter *.json | ForEach-Object {
    $hash = (git hash-object -w $_.FullName).Trim()
    git update-index --add --cacheinfo "100644,$hash,tests/AskMyData.Evals/cases/$($_.Name)" 2>&1 | Out-String | Add-Content $log
}
"STAGED:" | Add-Content $log
git diff --cached --name-only 2>&1 | Out-String | Add-Content $log
git commit -m "test(evals): add 30+ eval cases with generated expected rows" 2>&1 | Out-String | Add-Content $log
"COMMIT_EXIT=$LASTEXITCODE" | Add-Content $log
git show --stat --oneline HEAD 2>&1 | Out-String | Add-Content $log
"DONE" | Add-Content $log
