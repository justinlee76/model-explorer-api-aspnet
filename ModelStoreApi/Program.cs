using ModelStoreApi;
using ModelStoreApi.Dtos;
using ModelStoreApi.Hubs;
using ModelStoreApi.MongoDB;
using ModelStoreApi.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<MongoModelStoreSettings>(builder.Configuration.GetSection("ModelStore"));
builder.Services.AddSingleton<IModelStore, MongoModelStore>();
builder.Services.AddSingleton<SubscriptionTracker<SeriesKey>>();
builder.Services.AddSingleton<SubscriptionTracker<string>>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<ModelMonitor>();
builder.Services.AddHostedService<JobMonitor>();
var allowOrigins = builder.Configuration.GetSection("AllowOrigins").Get<string[]>();
if (allowOrigins != null)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowReactApp",
            policy =>
            {
                policy.WithOrigins(allowOrigins)   // React dev server
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials(); // only if using cookies/auth
            });
    });
}


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowReactApp");

app.UseAuthorization();

app.MapControllers();

app.MapHub<ModelDataHub>("/ModelDataHub");
app.MapHub<JobHub>("/JobHub");

app.Run();
