using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using notification_service.dbContext;
using notification_service.DTOs;
using notification_service.Entities;
using notification_service.Interfaces;
using notification_service.Repositories;
using notification_service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        "Host=localhost;Port=5430;Username=admin;Password=admin;Database=notification-service-db"
    )
);

builder.Services.AddScoped<INotificationRepository, PostgresNotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
//builder.Services.AddSingleton<INotificationSender, FirebaseNotificationSender>();

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

app.MapGet("/api/notifications/health", () => "healthy")
    .WithName("Health")
    .WithOpenApi();

app.MapGet("/api/notifications/{id}", async (
    INotificationService notificationService, 
    Guid id) =>
{
    try
    {
        var notification = await notificationService.GetNotificationByIdAsync(id);
        return Results.Ok(notification);
    }
    catch (Exception ex)
    {
        return Results.Problem("An error occurred while retrieving the notification");
    }
});

app.MapGet("/api/notifications/user/{userid}", async (
    INotificationService notificationService, 
    string userId, 
    [FromQuery] bool unreadOnly = false) =>
{
    try
    {
        var notifications = await notificationService.GetUserNotificationsAsync(userId, unreadOnly);
        return Results.Ok(notifications);
    }
    catch (Exception ex)
    {
        return Results.Problem("An error occurred while retrieving the notification");
    }
});

app.MapPost("/api/notifications/", async (
    INotificationService notificationService, 
    [FromBody] CreateNotificationDto notificationDto) =>
{
    var response = await notificationService.CreateNotificationAsync(notificationDto);
    return Results.Ok(response);
});

app.MapPut("/api/notifications/status", async (
    INotificationService notificationService, 
    [FromBody] UpdateNotificationStatusDto updateDto) =>
{
    try
    {
        var result = await notificationService.UpdateNotificationStatusAsync(updateDto);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem("An error occurred while updating the notification status");
    }
});

app.MapDelete("/api/notifications/{id}", async (
    INotificationService notificationService, 
    Guid id) =>
{
    try
    {
        var result = await notificationService.GetNotificationByIdAsync(id);
        if(result == null) return Results.NotFound();
        await notificationService.DeleteNotificationAsync(id);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem("An error occurred while deleting the notification");
    }
});

app.Run();