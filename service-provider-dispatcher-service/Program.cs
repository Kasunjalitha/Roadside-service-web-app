using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using service_provider_dispatcher_service.consumers;
using service_provider_dispatcher_service.data;
using service_provider_dispatcher_service.dbcontext;
using service_provider_dispatcher_service.services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<ServiceDispatcher>();

var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort = Environment.GetEnvironmentVariable("DB_PORT");
var dbUser = Environment.GetEnvironmentVariable("DB_USER");
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
var dbName = Environment.GetEnvironmentVariable("DB_NAME");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}"
    )
);

builder.Services.AddMassTransit(config =>
{
    config.SetKebabCaseEndpointNameFormatter();
    config.AddConsumers(typeof(Program).Assembly);
    config.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri("amqp://localhost:5672"), h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var pendingMigrations = dbContext.Database.GetPendingMigrations();
    if (pendingMigrations.Any())
    {
        Console.WriteLine("Applying pending migrations...");
        dbContext.Database.Migrate();
        Console.WriteLine("Migrations applied successfully.");
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapGet("/api/serviceprovider/health", () => Results.Ok("healthy"));

app.MapGet("api/serviceprovider/", async (AppDbContext dbContext) =>
{
    var spList = await dbContext.ServiceProviders.ToListAsync();

    var result = spList.Select(i => new ServiceProviderDto()
    {
        Name = i.Name,
        Email = i.Email,
        Latitude = i.Latitude,
        Longitude = i.Longitude,
        Phone = i.Phone,
        Website = i.Website,
    });
    
    return Results.Ok(result);
});

app.MapPost("/api/serviceprovider", async (
    AppDbContext dbContext, 
    [FromBody]service_provider_dispatcher_service.data.ServiceProviderDto sp) =>
{
    var serviceProvider = new service_provider_dispatcher_service.data.ServiceProvider()
    {
        Id = Guid.NewGuid(),
        Name = sp.Name,
        Email = sp.Email,
        Phone = sp.Phone,
        Website = sp.Website,
        Latitude = sp.Latitude,
        Longitude = sp.Longitude,
    };
    
    dbContext.ServiceProviders.Add(serviceProvider);
    await dbContext.SaveChangesAsync();
    return Results.Created();
});

app.MapGet("/api/serviceprovider/assignments", async (AppDbContext dbContext) =>
{
    try
    {
        var assignments = await dbContext.Assignments.ToListAsync();
        return Results.Ok(assignments);
    }
    catch (Exception e)
    {
        return Results.Problem();
    }
});

app.Run();