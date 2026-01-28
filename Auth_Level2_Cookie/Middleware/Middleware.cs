using Auth_Level2_Cookie.Data;
using Auth_Level2_Cookie.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;

namespace Auth_Level2_Cookie.Middleware;


public class Middleware
{
    private readonly RequestDelegate _next;

    public Middleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            await Unauthorized(context, "Missing Authorization header");
            return;
        }

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(
                context.Request.Headers["Authorization"]
            );

            if (!authHeader.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            {
                await Unauthorized(context, "Invalid auth scheme");
                return;
            }

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter!);
            var credentials = Encoding.UTF8
                .GetString(credentialBytes)
                .Split(':', 2);

            if (credentials.Length != 2)
            {
                await Unauthorized(context, "Invalid credentials format");
                return;
            }

            var username = credentials[0];
            var password = credentials[1];

            if (username != "admin" || password != "123")
            {
                await Unauthorized(context, "Invalid username or password");
                return;
            }

            context.Items["User"] = new User
            {
                Username = username
            };

            await _next(context);
        }
        catch
        {
            await Unauthorized(context, "Authorization parsing failed");
        }
    }

    private static async Task Unauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync($$"""
        {
            "error": "{{message}}"
        }
        """);
    }
}
