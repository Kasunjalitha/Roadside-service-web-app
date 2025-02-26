using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using request_service.consumers;
using request_service.DbContext;
using request_service.Models;
using request_service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policyBuilder =>
    {
        policyBuilder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddScoped<IRequestService, RequestService>();

var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort = Environment.GetEnvironmentVariable("DB_PORT");
var dbUser = Environment.GetEnvironmentVariable("DB_USER");
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
var dbName = Environment.GetEnvironmentVariable("DB_NAME");
var mqHost = Environment.GetEnvironmentVariable("MQ_HOST");
var mqPort = Environment.GetEnvironmentVariable("MQ_PORT");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}"
    )
);
builder.Services.AddMassTransit(configure =>
{
    configure.SetKebabCaseEndpointNameFormatter();
    configure.AddConsumers(typeof(Program).Assembly);
    configure.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri($"amqp://{mqHost}:{mqPort}"), h =>
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

app.MapGet("/api/requests/health", () => Results.Ok("healthy"));

app.MapPost("/api/requests", async (IRequestService requestService, [FromBody] CreateRequestDto request) =>
{
    var createdRequest = await requestService.CreateRequestAsync(request);
    return Results.Ok(createdRequest);
});

app.MapGet("/api/requests/customer/{customerid}/", async (IRequestService requestService, Guid customerid) =>
{
    var request = await requestService.GetCustomerRequestsAsync(customerid);
    return request is null? Results.NotFound() : Results.Ok(request);
});

app.MapGet("/api/requests/{requestId}/", async (IRequestService requestService, Guid requestId) =>
{
    var request = await requestService.GetRequestAsync(requestId);
    return request is null? Results.NotFound() : Results.Ok(request);
});

app.MapPut("/api/requests/", async (IRequestService requestService, [FromBody] UpdateRequestDto request) =>
{
    var updatedRequest = await requestService.UpdateRequestAsync(request);
    return updatedRequest is null ? Results.NotFound() : Results.Ok(updatedRequest);
});

app.MapDelete("/api/requests/{requestId}", async (IRequestService requestService, Guid requestId) =>
{
    var response = await requestService.DeleteRequestAsync(requestId);
    return response>0 ? Results.NoContent() : Results.NotFound();
});

app.Run();