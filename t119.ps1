Set-Location c:\dev\askmydata
git add tests/AskMyData.IntegrationTests/Api/AskApiFactory.cs tests/AskMyData.IntegrationTests/Api/AskApiTests.cs *> c:\dev\askmydata\c119.log
git commit -m "test(api): end-to-end api integration tests with stub provider" *>> c:\dev\askmydata\c119.log
git log --oneline -2 *>> c:\dev\askmydata\c119.log
dotnet test tests\AskMyData.IntegrationTests --no-build --filter FullyQualifiedName~AskApiStartupTests *> c:\dev\askmydata\t119s.log
