using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EReader_API.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EReader_API.Infra.Identity
{
    public class JwtTokenGenerator(IOptions<JwtOptions> options) : IJwtTokenGenerator
    {
        public (string AccessToken, int ExpiresInSeconds) Generate(Guid userId, string email, string displayName)
        {
            var jwt = options.Value;

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("name", displayName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            var key = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(jwt.AccessTokenMinutes);

            var token = new JwtSecurityToken(
                issuer: jwt.Issuer,
                audience: jwt.Audience,
                claims: claims,
                expires: expires,
                signingCredentials: credentials);

            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
            var expiresInSeconds = jwt.AccessTokenMinutes * 60;

            return (accessToken, expiresInSeconds);
        }
    }
}
