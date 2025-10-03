using Azure.Storage.Blobs;
using ClaimBridge.PayerSim.Engine;
using ClaimBridge.PayerSim.Payers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration).WriteTo.Console());

var scenario = builder.Configuration.GetSection("Sim:Scenario").Get<SimScenario>() ?? new SimScenario();
builder.Services.AddSingleton(scenario);
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration.GetConnectionString("Blob") ?? "UseDevelopmentStorage=true"));
builder.Services.AddSingleton<MemberDirectory>();
builder.Services.AddSingleton<ClaimStore>();
builder.Services.AddSingleton<Adjudicator>();
builder.Services.AddSingleton<AdjudicationFlow>();
builder.Services.AddSingleton<RemittanceWriter>();
builder.Services.AddSingleton<SimScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimScheduler>());
builder.Services.AddHostedService<MemberLoader>();
builder.Services.AddHostedService<PayerBFileProcessor>();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/alive");
app.MapOpenApi();
app.MapGet("/", (MemberDirectory members, ClaimStore store) =>
    new { service = "payer-sim", members = members.Count, claims = store.All.Count(), note = "Synthetic payers for local development only" });
app.MapPayerA();
app.MapPayerB();
app.MapPayerC();
app.MapSimControl();

app.Run();
