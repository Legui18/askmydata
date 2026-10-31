var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();

/// <summary>Entry point marker so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
