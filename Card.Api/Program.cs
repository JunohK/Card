using Card.Api.Data;
using Card.Api.Data.Seed;
using Card.Api.Services;
using Card.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------
// 서비스 등록 영역 (Build 이전)
// ---------------------------------

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy
                .WithOrigins("http://localhost:5173") // vite
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

// Game Room Service
builder.Services.AddSingleton<GameRoomService>();

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
// ---------------------------------
// 엔드포인트
// ---------------------------------
app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.UseCors("AllowFrontend");
app.UseRouting();
app.UseAuthorization();

// ---------------------------------
// 실행 (항상 맨 마지막)
// ---------------------------------
app.Run();
