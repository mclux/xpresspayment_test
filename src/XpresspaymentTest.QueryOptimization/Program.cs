using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.QueryOptimization.Data;
using XpresspaymentTest.QueryOptimization.Queries;

var builder = WebApplication.CreateBuilder(args);

// Pooling saves allocating a new DbContext per request - worth it for read-heavy reporting endpoints
builder.Services.AddDbContextPool<BankDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BankDb"),
        sql => sql.CommandTimeout(60)));

builder.Services.AddScoped<TransactionQueries>();
builder.Services.AddScoped<DataSeeder>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
