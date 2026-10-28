using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XpresspaymentTest.PaymentService.Clients;
using XpresspaymentTest.PaymentService.Data;
using XpresspaymentTest.PaymentService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PaymentDb")));

builder.Services.Configure<WalletServiceOptions>(builder.Configuration.GetSection("WalletService"));

builder.Services
    .AddHttpClient<WalletClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<WalletServiceOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    })
    // Retries POSTs too, which is only OK because the wallet treats the reference as an idempotency key
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(300);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    });

builder.Services.AddScoped<PaymentProcessor>();

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
