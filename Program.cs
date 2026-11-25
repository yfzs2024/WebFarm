using Microsoft.EntityFrameworkCore;
using WebFarm.Endpoints;
using WebFarm.Midddlewares;

var builder = WebApplication.CreateBuilder(args);
// 注册db服务
builder.Services.AddDbContext<WebFarm.Models.AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
var app = builder.Build();

// token鉴权中间件
app.UseMiddleware<TokenAuthMiddleware>();

Endpoints.MapAll(app);

app.Run();
