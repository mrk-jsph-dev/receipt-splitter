var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Used by the Container Apps health probe and the deploy smoke test.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

// Lets the test project start the app with WebApplicationFactory<Program>.
public partial class Program { }
