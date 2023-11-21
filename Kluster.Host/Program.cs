using Kluster.Host;

var builder = WebApplication.CreateBuilder(args);
builder.Services.RegisterApplicationServices(builder.Environment);

var app = builder.Build();
app.ConfigureApplication();
app.Run();

// This is used for the integration tests.
public partial class Program;