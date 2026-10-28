using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.BackgroundJobs.Data;
using XpresspaymentTest.BackgroundJobs.Jobs;
using XpresspaymentTest.BackgroundJobs.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("JobsDb");

builder.Services.AddDbContext<JobsDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailSender, FakeEmailSender>();
builder.Services.AddSingleton<StatementGenerator>();
builder.Services.AddScoped<StatementJob>();
builder.Services.AddScoped<DailyStatementReportJob>();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        SchemaName = "hangfire",
        PrepareSchemaIfNecessary = true,
        QueuePollInterval = TimeSpan.FromSeconds(5)
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
    options.Queues = ["default"];
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Hangfire creates its own tables but not the database, so run migrations first
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Dashboard is local-only by default (LocalRequestsOnlyAuthorizationFilter).
// Outside dev it would sit behind the same auth as the rest of the back office.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Xpress Test - Jobs"
});

app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<DailyStatementReportJob>(
    DailyStatementReportJob.Id,
    job => job.Run(CancellationToken.None),
    "0 7 * * *", // 7am Lagos time
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos") });

app.MapControllers();

app.Run();
