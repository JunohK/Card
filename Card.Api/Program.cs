using Card.Api.Data;
using Card.Api.Data.Seed;
using Card.Api.Services;
using Card.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// =================================
// JWT 설정 (Issuer / Key 통일)
// =================================
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? "DEV_SECRET_KEY_13579";

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
                ?? "CardGameServer";

// ---------------------------------
// Services
// ---------------------------------

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.SaveToken = true; 
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),
            NameClaimType = ClaimTypes.NameIdentifier
        };

        // ⭐⭐⭐ SignalR JWT 처리 핵심
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                Console.WriteLine($"[SignalR] Path: {path}");
                Console.WriteLine($"[SignalR] Token: {accessToken}");

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/gamehub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

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
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// DB
builder.Services.AddDbContext<GameDbContext>(options =>
{
    options.UseSqlite("Data Source=cardgame.db");
});

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

// Services
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<PasswordHashService>();
builder.Services.AddSingleton<GameRoomService>();
builder.Services.AddSingleton<PlayerConnectionService>();

// ---------------------------------
// Build
// ---------------------------------
var app = builder.Build();

// ---------------------------------
// DB Seed
// ---------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    DbInitializer.Seed(db);
}

// ---------------------------------
// Middleware
// ---------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------
// Endpoints
// ---------------------------------
app.MapControllers();
app.MapHub<GameHub>("/gamehub");

// ---------------------------------
// Run
// ---------------------------------
app.Run();
