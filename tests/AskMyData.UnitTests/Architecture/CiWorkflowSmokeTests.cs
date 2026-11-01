using FluentAssertions;
using YamlDotNet.Serialization;

namespace AskMyData.UnitTests.Architecture;

/// <summary>
/// Smoke tests over .github/workflows/ci.yml.
/// Validates: Requirements 6.7, 6.15, 6.17
/// </summary>
public class CiWorkflowSmokeTests
{
    private static readonly Dictionary<object, object> Workflow = Load();

    [Fact]
    public void Triggers_include_pull_request()
    {
        // YamlDotNet keeps the key "on" as a string (no YAML 1.1 boolean coercion).
        Workflow.Should().ContainKey("on");
        var on = Workflow["on"];
        var triggers = on switch
        {
            Dictionary<object, object> d => d.Keys.Select(k => k.ToString()!).ToList(),
            List<object> l => l.Select(x => x.ToString()!).ToList(),
            _ => new List<string> { on?.ToString() ?? string.Empty },
        };
        triggers.Should().Contain("pull_request");
    }

    [Fact]
    public void Eval_step_uses_stub_mode()
    {
        Steps().Should().Contain(s => RunOf(s).Contains("--mode stub"));
    }

    [Fact]
    public void No_step_uses_live_mode()
    {
        Steps().Should().NotContain(s => RunOf(s).Contains("--mode live"));
    }

    [Fact]
    public void Upload_artifact_step_runs_always()
    {
        var upload = Steps().Where(s =>
            s.TryGetValue("uses", out var u) && u.ToString()!.StartsWith("actions/upload-artifact")).ToList();

        upload.Should().NotBeEmpty();
        upload.Should().OnlyContain(s => s.ContainsKey("if") && s["if"].ToString()!.Trim() == "always()");
    }

    private static string RunOf(Dictionary<string, object> step) =>
        step.TryGetValue("run", out var r) ? r.ToString()! : string.Empty;

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
        var path = Path.Combine(dir!.FullName, ".github", "workflows", "ci.yml");
        var yaml = File.ReadAllText(path);
        return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml);
    }
}
