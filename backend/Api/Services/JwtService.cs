using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Api.Services;

public interface IJwtService
{
    string GenerateToken(string userId, string username, string email, IEnumerable<string> roles, IDictionary<string, string>? customClaims = null);
}

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(string userId, string username, string email, IEnumerable<string> roles, IDictionary<string, string>? customClaims = null)
    {
        var jwtSection = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (customClaims != null)
        {
            foreach (var kvp in customClaims)
            {
                claims.Add(new Claim(kvp.Key, kvp.Value));
            }
        }

        var expiryMinutes = int.TryParse(jwtSection["ExpiryMinutes"], out var mins) ? mins : 120;

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
