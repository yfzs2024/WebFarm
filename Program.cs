using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 注册db服务
builder.Services.AddDbContext<WebFarm.Models.AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

app.MapGet("/", () => "Hello World!");

app.Run();
