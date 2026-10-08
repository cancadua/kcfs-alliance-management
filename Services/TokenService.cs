using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AllianceRewards.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace AllianceRewards.Api.Services;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "AllianceRewards";
    public string Audience { get; set; } = "AllianceRewards";
    public int ExpiryMinutes { get; set; } = 60 * 24;
}

public class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public string CreateToken(User user)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _o.Issuer,
            _o.Audience,
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            expires: DateTime.UtcNow.AddMinutes(_o.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
