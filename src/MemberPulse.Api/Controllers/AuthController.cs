using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MemberPulse.Api.Auth;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Api.Controllers;

[ApiController]
public sealed class AuthController(
    MemberPulseDbContext db,
    IPasswordHasher<StaffUser> hasher,
    TokenService tokens,
    TimeProvider time) : ControllerBase
{
    // Hash verified against when the email is unknown, so response time doesn't reveal which emails exist.
    private static readonly StaffUser Decoy = StaffUser.Create("decoy@invalid", "x", StaffRole.Staff, DateTimeOffset.UnixEpoch);

    [AllowAnonymous]
    [HttpPost("api/auth/login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var result = hasher.VerifyHashedPassword(user ?? Decoy, user?.PasswordHash ?? DecoyHash, request.Password);
        if (user is null || result == PasswordVerificationResult.Failed)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");
        }

        var issued = tokens.Issue(user);
        return new LoginResponse(issued.AccessToken, "Bearer", issued.ExpiresAt);
    }

    [HttpGet("api/auth/me")]
    public async Task<ActionResult<StaffUserDto>> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id))
        {
            return Unauthorized();
        }

        var user = await db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? Unauthorized() : new StaffUserDto(user.Id, user.Email, user.Role, user.CreatedAt);
    }

    [Authorize(Policy = AuthSetup.AdminPolicy)]
    [HttpPost("api/users")]
    public async Task<ActionResult<StaffUserDto>> CreateUser(CreateStaffUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.StaffUsers.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered",
                detail: $"A staff user with email '{email}' already exists.");
        }

        var placeholder = StaffUser.Create(email, "pending", request.Role!.Value, time.GetUtcNow());
        var user = StaffUser.Create(email, hasher.HashPassword(placeholder, request.Password), request.Role.Value, time.GetUtcNow());
        db.StaffUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/users/{user.Id}", new StaffUserDto(user.Id, user.Email, user.Role, user.CreatedAt));
    }

    private string DecoyHash => _decoyHash ??= hasher.HashPassword(Decoy, "decoy-password");
    private static string? _decoyHash;
}
