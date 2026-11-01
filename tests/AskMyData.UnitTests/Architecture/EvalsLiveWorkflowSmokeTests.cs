using FluentAssertions;
using YamlDotNet.Serialization;

namespace AskMyData.UnitTests.Architecture;

/// <summary>
/// Smoke tests over .github/workflows/evals-live.yml.
/// Validates: Requirements 6.15, 6.17
/// </summary>
public class EvalsLiveWorkflowSmokeTests
{
    private static readonly Dictionary<object, object> Workflow = Load();

    [Fact]
    public void Triggers_are_only_workflow_dispatch_and_schedule()
    {
        Triggers().Should().BeEquivalentTo(new[] { "workflow_dispatch", "schedule" });
    }

    [Fact]
    public void Has_no_pull_request_trigger()
    {
        Triggers().Should().NotContain(t => t.StartsWith("pull_request"));
    }

    [Fact]
    public void Upload_artifact_step_runs_always()
    {
        var upload = Steps().Where(s =>
            s.TryGetValue("uses", out var u) && u.ToString()!.StartsWith("actions/upload-artifact")).ToList();

        upload.Should().NotBeEmpty();
        upload.Should().OnlyContain(s => s.ContainsKey("if") && s["if"].ToString()!.Trim() == "always()");
    }

    private static List<string> Triggers()
    {
        // YamlDotNet keeps the key "on" as a string (no YAML 1.1 boolean coercion).
        Workflow.Should().ContainKey("on");
        var on = Workflow["on"];
        return on switch
        {
            Dictionary<object, object> d => d.Keys.Select(k => k.ToString()!).ToList(),
            List<object> l => l.Select(x => x.ToString()!).ToList(),
            _ => new List<string> { on?.ToString() ?? string.Empty },
        };
    }

    private static List<Dictionary<string, object>> Steps()
    {
        var jobs = (Dictionary<object, object>)Workflow["jobs"];
        return jobs.Values
            .Cast<Dictionary<object, object>>()
            .SelectMany(j => ((List<object>)j["steps"]).Cast<Dictionary<object, object>>())
            .Select(s => s.ToDictionary(kv => kv.Key.ToString()!, kv => kv.Value))
            .ToList();
    }

    private static Dictionary<object, object> Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.sln").Length == 0)
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing the .sln) must be locatable");
        var path = Path.Combine(dir!.FullName, ".github", "workflows", "evals-live.yml");
        var yaml = File.ReadAllText(path);
        return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml);
    }
}
