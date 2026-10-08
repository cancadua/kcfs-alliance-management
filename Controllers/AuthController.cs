using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("auth")]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens, AllianceAccessService access) : ControllerBase
{
    private readonly PasswordHasher<User> _hasher = new();

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var username = req.Username.Trim();

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Email is already registered." });
        if (await db.Users.AnyAsync(u => u.Username == username))
            return Conflict(new { error = "Username is already taken." });

        var user = new User { Email = email, Username = username };
        user.PasswordHash = _hasher.HashPassword(user, req.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(new AuthResponse(tokens.CreateToken(user)));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "Invalid email or password." });

        return Ok(new AuthResponse(tokens.CreateToken(user)));
    }

    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var uid = access.UserId;
        var me = await db.Users
            .Where(u => u.Id == uid)
            .Select(u => new MeResponse(u.Id, u.Email, u.Username, u.CreatedAt))
            .FirstOrDefaultAsync();
        return me is null ? Unauthorized() : me;
    }
}
