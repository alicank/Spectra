// Spectra API Host
// Backlog item #56: Implement REST API host
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Spectra API - not yet implemented");

app.Run();