using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebFarm.Midddlewares;
using WebFarm.Models;

namespace WebFarm.Endpoints;

public static class Endpoints
{
    private static readonly PasswordHasher<User> _hasher = new();
    public static void MapAll(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/register", Register);
        app.MapPost("/api/login", Login);
        app.MapPost("/api/cancel", Cancel);
        app.MapPost("/api/changepassword", ChangePassword);
        app.MapPost("/api/logout", Logout);
        app.MapGet("/api/crops", Crops);
        app.MapGet("/api/status", Status);
        app.MapPost("/api/plant", Plant);
    }

    private static async Task<Results<Ok<TokenResponse>, Conflict<string>>> 
        Register(RegisterDto registerDto, AppDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.UserName == registerDto.UserName))
            return TypedResults.Conflict("用户名已存在");
        
        // 创建用户：初始100现金
        var newUser = new User
        {
            UserName = registerDto.UserName,
            Cash = 100
        };
        newUser.PasswordHash = _hasher.HashPassword(newUser, registerDto.Password);
        // 送一块0号田，初始没有作物
        newUser.FarmLands.Add(new FarmLand { Index = 0 });
        // 数据库存库（用户和农田一起插入）
        db.Users.Add(newUser);
        await db.SaveChangesAsync();
        // 签发token
        var token = await IssueToken(newUser, db);

        return TypedResults.Ok(new TokenResponse(token.Token, token.ExpiresAt));
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>>
        Login(LoginDto loginDto, AppDbContext db)
    {
        // 按用户名查用户，顺带带出已有token
        var user = await db.Users
            .Include(u => u.UserToken)
            .FirstOrDefaultAsync(u => u.UserName == loginDto.UserName);
        // 用户不存在或密码错误都返回401，避免暴露用户名是否存在
        if (user is null)
            return TypedResults.Unauthorized();

        switch (_hasher.VerifyHashedPassword(user, user.PasswordHash, loginDto.Password))
        {
            case PasswordVerificationResult.Failed:
                return TypedResults.Unauthorized();
            case PasswordVerificationResult.SuccessRehashNeeded:
                // 哈希算法升级了，重新哈希再存
                user.PasswordHash = _hasher.HashPassword(user, loginDto.Password);
                break;
            case PasswordVerificationResult.Success:
                break;
        }

        // 重新签发token（token与用户一对一，旧token随之作废）
        var token = await IssueToken(user, db);

        return TypedResults.Ok(new TokenResponse(token.Token, token.ExpiresAt));
    }

    private static async Task<UserToken> IssueToken(User user, AppDbContext db)
    {
        var token = user.UserToken;
        if (token is null)
        {
            token = new UserToken { UserId = user.Id };
            db.UserTokens.Add(token);
        }

        token.Token = Guid.NewGuid().ToString("N");
        token.ExpiresAt = DateTime.UtcNow.AddDays(7);

        await db.SaveChangesAsync();
        return token;
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>> 
        Cancel(CancelDto cancelDto, HttpContext httpContext, AppDbContext db)
    {
        // 用户由token鉴权中间件解析后挂在HttpContext上
        var user = httpContext.Items[TokenAuthMiddleware.UserItemKey] as User;
        if (user is null)
            return TypedResults.Unauthorized();

        // 密码效验
        switch (_hasher.VerifyHashedPassword(user, user.PasswordHash, cancelDto.Password))
        {
            case PasswordVerificationResult.Failed:
                return TypedResults.Unauthorized();
            case PasswordVerificationResult.Success:
            case PasswordVerificationResult.SuccessRehashNeeded:
                break;
        }

        // 注销：删除用户（级联删除token和农田）
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>>
        ChangePassword(ChangePasswordDto changePasswordDto, HttpContext httpContext, AppDbContext db)
    {
        // 用户由token鉴权中间件解析后挂在HttpContext上
        var user = httpContext.Items[TokenAuthMiddleware.UserItemKey] as User;
        if (user is null)
            return TypedResults.Unauthorized();

        // 旧密码效验
        switch (_hasher.VerifyHashedPassword(user, user.PasswordHash, changePasswordDto.OldPassword))
        {
            case PasswordVerificationResult.Failed:
                return TypedResults.Unauthorized();
            case PasswordVerificationResult.Success:
            case PasswordVerificationResult.SuccessRehashNeeded:
                break;
        }

        // 用新密码重新哈希后存库
        user.PasswordHash = _hasher.HashPassword(user, changePasswordDto.NewPassword);
        await db.SaveChangesAsync();
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>>
        Logout(HttpContext httpContext, AppDbContext db)
    {
        // 用户由token鉴权中间件解析后挂在HttpContext上
        var user = httpContext.Items[TokenAuthMiddleware.UserItemKey] as User;
        if (user is null)
            return TypedResults.Unauthorized();

        // 删除该用户的token，使其立即失效（下次登录会重新签发）
        var token = await db.UserTokens.FirstOrDefaultAsync(t => t.UserId == user.Id);
        if (token is not null)
        {
            db.UserTokens.Remove(token);
            await db.SaveChangesAsync();
        }

        return TypedResults.Ok();
    }

    private static async Task<Ok<CropListResponse>> Crops(AppDbContext db)
    {
        // 谷物是公共数据，只读查出所有记录
        var crops = await db.Crops
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CropDto(c.Id, c.CropName, c.SeedCost, c.MatureTime))
            .ToListAsync();

        // 顶层用object包一层，前端取 data.crops
        return TypedResults.Ok(new CropListResponse(crops));
    }

    private static async Task<Results<Ok<StatusResponse>, UnauthorizedHttpResult>>
        Status(HttpContext httpContext, AppDbContext db)
    {
        // 用户由token鉴权中间件解析后挂在HttpContext上
        var user = httpContext.Items[TokenAuthMiddleware.UserItemKey] as User;
        if (user is null)
            return TypedResults.Unauthorized();

        return TypedResults.Ok(await BuildStatus(user, db));
    }

    private static async Task<Results<Ok<StatusResponse>, UnauthorizedHttpResult, BadRequest<string>, NotFound<string>, Conflict<string>>>
        Plant(PlantDto plantDto, HttpContext httpContext, AppDbContext db)
    {
        // 用户由token鉴权中间件解析后挂在HttpContext上
        var user = httpContext.Items[TokenAuthMiddleware.UserItemKey] as User;
        if (user is null)
            return TypedResults.Unauthorized();

        // 作物必须存在
        var crop = await db.Crops.FirstOrDefaultAsync(c => c.Id == plantDto.CropId);
        if (crop is null)
            return TypedResults.NotFound("作物不存在");

        // 农田必须属于当前用户
        var land = await db.FarmLands.FirstOrDefaultAsync(f => f.UserId == user.Id && f.Index == plantDto.Index);
        if (land is null)
            return TypedResults.NotFound("农田不存在");

        // 田里已有作物，必须先收获
        if (land.CropId is not null)
            return TypedResults.Conflict("该农田已有作物，请先收获");

        // 现金不足则拒绝
        if (user.Cash < crop.SeedCost)
            return TypedResults.BadRequest("现金不足");

        // 扣款并种植（MatureTime 单位为天，成熟时间在收获/状态接口计算）
        user.Cash -= crop.SeedCost;
        land.CropId = crop.Id;
        land.PlantedDate = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return TypedResults.Ok(await BuildStatus(user, db));
    }

    /// <summary>根据用户和其农田数据组装状态响应（plant 成功后也复用它返回最新状态）。</summary>
    private static async Task<StatusResponse> BuildStatus(User user, AppDbContext db)
    {
        var now = DateTime.UtcNow;

        // 查出该用户的全部农田并带出作物
        var farmLands = await db.FarmLands
            .AsNoTracking()
            .Where(f => f.UserId == user.Id)
            .Include(f => f.Crop)
            .OrderBy(f => f.Index)
            .ToListAsync();

        // 逐块判断是否已成熟可收获（MatureTime 单位为天）
        var lands = farmLands
            .Select(f =>
            {
                // SQLite 读回 DateTime 会丢失 Kind，这里标记为 UTC，让 JSON 带 Z、前端按 UTC 解析
                var plantedDate = f.PlantedDate is null
                    ? (DateTime?)null
                    : DateTime.SpecifyKind(f.PlantedDate.Value, DateTimeKind.Utc);
                var canYield = f.PlantedDate is not null
                    && f.Crop is not null
                    && f.PlantedDate.Value.AddDays(f.Crop.MatureTime) <= now;
                return new FarmLandDto(
                    f.Index,
                    f.Scale,
                    f.CropId,
                    f.Crop?.CropName,
                    plantedDate,
                    f.Crop?.MatureTime,
                    canYield);
            })
            .ToList();

        return new StatusResponse(
            user.UserName,
            user.Cash,
            lands.Any(l => l.CanYield),
            farmLands.Count,
            lands);
    }
}

public record RegisterDto(string UserName, string Password);
public record LoginDto(string UserName, string Password);
public record CancelDto(string Password);
public record ChangePasswordDto(string OldPassword, string NewPassword);
public record PlantDto(int Index, int CropId);
public record TokenResponse(string Token, DateTime ExpiresAt);
public record CropDto(int Id, string CropName, int SeedCost, int MatureTime);
public record CropListResponse(List<CropDto> Crops);
public record FarmLandDto(
    int Index,
    int Scale,
    int? CropId,
    string? CropName,
    DateTime? PlantedDate,
    int? MatureTime,
    bool CanYield);
public record StatusResponse(
    string UserName,
    int Cash,
    bool CanYield,
    int FarmCount,
    List<FarmLandDto> FarmLands);