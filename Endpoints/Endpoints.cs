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
    }

    private static async Task<Results<Ok<TokenResponse>, Conflict<string>>> 
        Register(RegisterDto registerDto, AppDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.UserName == registerDto.UserName))
            return TypedResults.Conflict("用户名已存在");
        
        // 创建用户
        var newUser = new User{ UserName = registerDto.UserName };
        newUser.PasswordHash = _hasher.HashPassword(newUser, registerDto.Password);
        // 数据库存库
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
}

public record RegisterDto(string UserName, string Password);
public record LoginDto(string UserName, string Password);
public record CancelDto(string Password);
public record ChangePasswordDto(string OldPassword, string NewPassword);

public record TokenResponse(string Token, DateTime ExpiresAt);