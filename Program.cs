using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<DirectoryChangeDetectorApi.Services.ISnapshotStateStore, DirectoryChangeDetectorApi.Services.JsonSnapshotStateStore>();
builder.Services.AddSingleton<DirectoryChangeDetectorApi.Services.IDirectoryAnalysisService, DirectoryChangeDetectorApi.Services.DirectoryAnalysisService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();
