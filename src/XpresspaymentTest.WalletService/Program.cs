using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using XpresspaymentTest.WalletService.Auth;
using XpresspaymentTest.WalletService.Data;
using XpresspaymentTest.WalletService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("WalletDb")));

builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.Scheme,
        options => builder.Configuration.GetSection("ApiKeyAuth").Bind(options));
builder.Services.AddAuthorization();

builder.Services.AddScoped<Ledger>();

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Wallet Service", Version = "v1" });
    c.AddSecurityDefinition(ApiKeyAuthenticationOptions.Scheme, new OpenApiSecurityScheme
    {
        Name = ApiKeyAuthenticationOptions.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyAuthenticationOptions.Scheme }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    await db.Database.MigrateAsync();
    await DemoData.EnsureWalletsAsync(db);
}

// Unhandled exceptions come back as ProblemDetails (no stack traces leaking to the caller)
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
