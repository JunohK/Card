using Card.Api.Data;
using Card.Api.Data.Seed;
using Card.Api.Services;
using Card.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

//JWT
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// JWT
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DEV_SECRET_KEY_123456789";
var jwtIssuer = "CardGameServer";

// ---------------------------------
// 서비스 등록 영역 (Build 이전)
// ---------------------------------

// JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey)
        )
    };

    // SignalR 용 토큰 처리
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            // SignalR Hub 경로일 때만 허용
            if(!string.IsNullOrEmpty(accessToken) &&
                context.HttpContext.Request.Path.StartsWithSegments("/gamehub"))
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

// DB (옵션 포함 → 딱 1번만)
builder.Services.AddDbContext<GameDbContext>(options =>
{
    options.UseSqlite("Data Source=cardgame.db");
});

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

// Game Room Service
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<GameRoomService>();
builder.Services.AddScoped<PasswordHashService>();
// ---------------------------------
// 앱 빌드 (여기서부터 app)
// ---------------------------------
var app = builder.Build();

// ---------------------------------
// DB Seed (Build 이후, Run 이전)
// ---------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    DbInitializer.Seed(db);
}

// ---------------------------------
// 미들웨어
// ---------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------
// 엔드포인트
// ---------------------------------
app.MapControllers();
app.MapHub<GameHub>("/gamehub");

// ---------------------------------
// 실행 (항상 맨 마지막)
// ---------------------------------
app.Run();