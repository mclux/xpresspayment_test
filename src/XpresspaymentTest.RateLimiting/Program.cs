using XpresspaymentTest.RateLimiting.Throttling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SlidingWindowRateLimiter>();
builder.Services.AddHostedService<RateLimitCleanupService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RateLimitingMiddleware>();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
