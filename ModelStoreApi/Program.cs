using ModelStoreApi;
using ModelStoreApi.Dtos;
using ModelStoreApi.Hubs;
using ModelStoreApi.MongoDB;
using ModelStoreApi.Serialization;
using ModelStoreApi.Services;

var builder = WebApplication.CreateBuilder(args);
var jsonNamingPolicy = new CustomNamingPolicy();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = jsonNamingPolicy;
    });
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<MongoModelStoreSettings>(builder.Configuration.GetSection("ModelStore"));
builder.Services.AddSingleton<IModelStore, MongoModelStore>();
builder.Services.AddSingleton<SubscriptionTracker<SeriesKey>>();
builder.Services.AddSingleton<SubscriptionTracker<string>>();
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = jsonNamingPolicy;
    });
builder.Services.AddHostedService<ModelMonitor>();
builder.Services.AddHostedService<JobMonitor>();

var allowOrigins = builder.Configuration.GetSection("AllowOrigins").Get<string[]>();
var useCors = allowOrigins is { Length: > 0 };
if (useCors)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowReactApp",
            policy =>
            {
                policy.WithOrigins(allowOrigins!)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (useCors)
    app.UseCors("AllowReactApp");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.MapHub<ModelHub>("/ModelHub");
app.MapHub<JobHub>("/JobHub");

app.Run();
