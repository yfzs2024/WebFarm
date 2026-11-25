using Microsoft.EntityFrameworkCore;
using WebFarm.Models;

namespace WebFarm.Midddlewares;

public class TokenAuthMiddleware
{
    /// <summary>HttpContext.Items 中存放当前登录用户的键。</summary>
    public const string UserItemKey = "User";

    private readonly RequestDelegate _next;
    private readonly string[] _exemptUrls = [
        "/api/register",
        "/api/login",
    ];

    public TokenAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext httpContext, AppDbContext db)
    {
        // 白名单内的接口不需要鉴权
        if (IsExempt(httpContext.Request.Path))
        {
            await _next(httpContext);
            return;
        }

        var token = GetToken(httpContext.Request);
        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteUnauthorized(httpContext, "缺少Token");
            return;
        }

        // 查询token并带出用户
        var userToken = await db.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut => ut.Token == token);

        // token不存在或已过期
        if (userToken is null || userToken.ExpiresAt <= DateTime.UtcNow)
        {
            await WriteUnauthorized(httpContext, "Token无效或已过期");
            return;
        }

        // 把用户挂到HttpContext上，后续接口通过 Items["User"] 取用
        httpContext.Items[UserItemKey] = userToken.User;

        await _next(httpContext);
    }

    private bool IsExempt(PathString path) =>
        _exemptUrls.Any(url =>
            path.StartsWithSegments(url, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 从请求头取token，支持 "Bearer xxx" 和直接写 "xxx" 两种形式。
    /// </summary>
    private static string? GetToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return null;

        const string bearer = "Bearer ";
        return header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            ? header[bearer.Length..].Trim()
            : header.Trim();
    }

    private static async Task WriteUnauthorized(HttpContext httpContext, string message)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await httpContext.Response.WriteAsJsonAsync(new { message });
    }
}
