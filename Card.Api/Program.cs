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
var jwtKey = builder.Configuration["Jwt:Key"] ?? "Junoh_Card_Key";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "CardGameServer";

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = ClaimTypes.Name
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/gamehub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ⭐ [CORS 설정 수정] ngrok 주소 대응을 위해 모든 오리진 허용
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true) // 어떤 도메인(ngrok 등)에서 와도 허용
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // SignalR 인증을 위해 필수
    });
});

// DB / SignalR / Services 동일
builder.Services.AddDbContext<GameDbContext>(options => options.UseSqlite("Data Source=cardgame.db"));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<PasswordHashService>();
builder.Services.AddSingleton<GameRoomService>();
builder.Services.AddSingleton<PlayerConnectionService>();

var app = builder.Build();

// DB Seed 동일
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    DbInitializer.Seed(db);
}

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");

app.Run();