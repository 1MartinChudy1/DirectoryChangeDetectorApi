var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<DirectoryChangeDetectorApi.Services.ISnapshotStateStore, DirectoryChangeDetectorApi.Services.JsonSnapshotStateStore>();
builder.Services.AddSingleton<DirectoryChangeDetectorApi.Services.IDirectoryAnalysisService, DirectoryChangeDetectorApi.Services.DirectoryAnalysisService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();
