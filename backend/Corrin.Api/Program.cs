using System.Reflection;
using Corrin.Api.Data;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using DbUp;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<CorrinDbContext>(o =>
    o.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));

builder.Services.AddScoped<IReferenceRepository, ReferenceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<ISiteRepository, SiteRepository>();
builder.Services.AddScoped<IAssetRepository, AssetRepository>();
builder.Services.AddScoped<IEngineerRepository, EngineerRepository>();
builder.Services.AddScoped<IShiftRepository, ShiftRepository>();
builder.Services.AddScoped<IPartRepository, PartRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IImportRepository, ImportRepository>();

builder.Services.AddScoped<IReferenceService, ReferenceService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ISiteService, SiteService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IEngineerService, EngineerService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IJobRequestImportService, JobRequestImportService>();

var upgrader = DeployChanges.To
    .MySqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.EndsWith(".sql"))
    .WithVariablesDisabled()
    .LogToConsole()
    .Build();
var upgrade = upgrader.PerformUpgrade();
if (!upgrade.Successful)
{
    Console.Error.WriteLine(upgrade.Error);
    return 1;
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
return 0;

public partial class Program { }
