using ModelStoreApi;
using ModelStoreApi.Hubs;
using ModelStoreApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<ModelStoreSettings>(builder.Configuration.GetSection("ModelStore"));
builder.Services.AddSingleton<ModelStoreClient>();
builder.Services.AddSingleton<SubscriptionTracker<SeriesKey>>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<ModelMonitor>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173")   // React dev server
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials(); // only if using cookies/auth
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.MapHub<ModelDataHub>("/ModelDataHub");
app.UseCors("AllowReactApp");

app.Run();
