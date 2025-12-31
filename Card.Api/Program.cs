using Card.Api.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.DependencyInjection;
using Card.Hubs;
using Card.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// 컨트롤러 등록 (API용)
builder.Services.AddControllers();

// DB
builder.Services.AddDbContext<GameDbContext>();

// SignalR
builder.Services.AddSignalR();

builder.Services.AddSingleton<GameRoomService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// HTTPS 리다이렉트
// app.UseHttpsRedirection();

app.MapHub<GameHub>("/gamehub");

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.Run();
