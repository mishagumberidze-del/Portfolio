using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpotifyTaste.Application;
using SpotifyTaste.Infrastructure.Analytics;
using SpotifyTaste.Infrastructure.Import;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Preferences;
using SpotifyTaste.Infrastructure.ReccoBeats;
using SpotifyTaste.Infrastructure.Recommendations;
using SpotifyTaste.Infrastructure.Spotify;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
// Never log OAuth callback query strings or HTTP token exchange details.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<SpotifyTasteDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("SpotifyTaste")));
builder.Services.Configure<PreferenceModelOptions>(builder.Configuration.GetSection("PreferenceModel"));
builder.Services.AddSingleton<SpotifyExportReader>();
builder.Services.AddScoped<ISpotifyImportService, SpotifyImportService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<StoryCache>();
builder.Services.AddScoped<IStoryService, StoryService>();
builder.Services.AddScoped<IDiagnosticsService, DiagnosticsService>();
builder.Services.AddScoped<IPreferenceModelService, PreferenceModelService>();
builder.Services.AddScoped<ITrackFeatureProvider, ReccoBeatsTrackFeatureProvider>();
builder.Services.AddScoped<ICandidateTrackProvider, ReccoBeatsCandidateTrackProvider>();
builder.Services.AddScoped<ISongScoringService, SongScoringService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddSingleton<RecommendationWorkGate>();
builder.Services.AddScoped<FeedbackService>();
builder.Services.Configure<SpotifyConnectionOptions>(builder.Configuration.GetSection("Spotify"));
builder.Services.AddHttpClient("Spotify", client => client.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(sp => new SpotifyConnection(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Spotify"), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SpotifyConnectionOptions>>()));
builder.Services.AddSingleton<SpotifyLibraryWorkGate>();
builder.Services.AddScoped<ISpotifyLibraryClient>(sp => new SpotifyLibraryClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Spotify"), sp.GetRequiredService<SpotifyConnection>()));
var projectDirectory = new DirectoryInfo(builder.Environment.ContentRootPath);
while (projectDirectory.Parent is not null && !File.Exists(Path.Combine(projectDirectory.FullName, "SpotifyTasteSolution.sln"))) projectDirectory = projectDirectory.Parent;
if (!File.Exists(Path.Combine(projectDirectory.FullName, "SpotifyTasteSolution.sln"))) throw new InvalidOperationException("Run this local prototype from inside its project directory.");
builder.Services.AddSingleton(new SpotifyLibraryArchive(Path.Combine(projectDirectory.FullName, ".run/library-snapshots")));
builder.Services.AddScoped<SpotifyLibraryImportService>();
builder.Services.AddHttpClient<IReccoBeatsClient, ReccoBeatsClient>(client =>
{
    client.BaseAddress = new Uri("https://api.reccobeats.com/");
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyTastePrototype/1.0");
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:5174").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (!builder.Configuration.GetValue<bool>("Maintenance:EnableWrites") &&
        !ReadOnlyPolicy.Allows(context.Request.Method, context.Request.Path))
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { message = "Read-only edition. Imports, OAuth, feedback, enrichment, scoring and regeneration are disabled." });
        return;
    }
    await next();
});

if (args.Length == 2 && args[0] == "--export-personal-snapshot")
{
    await using var scope = app.Services.CreateAsyncScope();
    var services = scope.ServiceProvider;
    await PersonalSnapshotExporter.ExportAsync(services.GetRequiredService<SpotifyTasteDbContext>(),
        (AnalyticsService)services.GetRequiredService<IAnalyticsService>(),
        (StoryService)services.GetRequiredService<IStoryService>(),
        (DiagnosticsService)services.GetRequiredService<IDiagnosticsService>(),
        services.GetRequiredService<FeedbackService>(), args[1]);
    return;
}

if (args.Length >= 2 && args[0].Equals("--import", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var importer = scope.ServiceProvider.GetRequiredService<ISpotifyImportService>();
    var report = await importer.ImportAsync(args[1]);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Length >= 1 && args[0].Equals("--rebuild-model", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var model = scope.ServiceProvider.GetRequiredService<IPreferenceModelService>();
    var result = await model.RebuildAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Length >= 1 && args[0].Equals("--enrich", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var provider = scope.ServiceProvider.GetRequiredService<ITrackFeatureProvider>();
    var limit = args.Length >= 2 && int.TryParse(args[1], out var parsedLimit) ? parsedLimit : 100;
    Console.WriteLine($"Enriched {await provider.EnrichMissingAsync(limit)} track(s).");
    return;
}

if (args.Length >= 1 && args[0].Equals("--enrich-all", StringComparison.OrdinalIgnoreCase))
{
    var batchSize = ParseClampedInt(args, 1, defaultValue: 100, minimum: 1, maximum: 1000);
    var delaySeconds = ParseClampedInt(args, 2, defaultValue: 5, minimum: 1, maximum: 300);
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        Console.WriteLine(
            $"Enriching all remaining historical tracks in batches of {batchSize}, " +
            $"with at least {delaySeconds} second(s) between batches. Press Ctrl+C to stop after the current request.");
        var result = await TrackEnrichmentRunLoop.RunAsync(
            async cancellationToken =>
            {
                // A fresh scope keeps each SQL transaction and EF change tracker bounded.
                // EnrichBatchAsync commits before returning, so every batch is resumable.
                await using var scope = app.Services.CreateAsyncScope();
                var provider = scope.ServiceProvider.GetRequiredService<ITrackFeatureProvider>();
                return await provider.EnrichBatchAsync(batchSize, historicalTracksOnly: true, cancellationToken);
            },
            TimeSpan.FromSeconds(delaySeconds),
            Console.Out,
            cancellation.Token);
        Console.WriteLine(
            $"Enrichment stopped ({result.StopReason}). Cached {result.Completed} feature row(s), " +
            $"marked {result.NotFound} not found, and left {result.RemainingEligible} eligible historical track(s).");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        Console.WriteLine("Enrichment cancelled. Every batch completed before cancellation remains stored in SQL Server.");
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
    return;
}

if (args.Length >= 2 && args[0].Equals("--score", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var scorer = scope.ServiceProvider.GetRequiredService<ISongScoringService>();
    var result = await scorer.ScoreAsync(args[1]);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapControllers();

app.Run();

static int ParseClampedInt(string[] arguments, int index, int defaultValue, int minimum, int maximum) =>
    arguments.Length > index && int.TryParse(arguments[index], out var parsed)
        ? Math.Clamp(parsed, minimum, maximum)
        : defaultValue;

public partial class Program;

file sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
}
