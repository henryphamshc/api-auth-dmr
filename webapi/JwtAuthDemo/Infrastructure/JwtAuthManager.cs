﻿using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using JwtAuthDemo.Data;
using Microsoft.IdentityModel.Tokens;

namespace JwtAuthDemo.Infrastructure
{
    public interface IJwtAuthManager
    {
        IImmutableDictionary<string, RefreshToken> UsersRefreshTokensReadOnlyDictionary { get; }
        JwtAuthResult GenerateTokens(string username, Claim[] claims, DateTime now);
        JwtAuthResult Refresh(string refreshToken, string accessToken, DateTime now);
        void RemoveExpiredRefreshTokens(DateTime now);
        void RemoveRefreshTokenByUserName(string userName);
        void AddRefreshToken(RefreshToken refreshToken);
        (ClaimsPrincipal, JwtSecurityToken) DecodeJwtToken(string token);
    }

    public class JwtAuthManager : IJwtAuthManager
    {
        public IImmutableDictionary<string, RefreshToken> UsersRefreshTokensReadOnlyDictionary => _usersRefreshTokens.ToImmutableDictionary();
        private readonly ConcurrentDictionary<string, RefreshToken> _usersRefreshTokens;  // can store in a database or a distributed cache
        private readonly JwtTokenConfig _jwtTokenConfig;
        private readonly DataContext _context;
        private readonly byte[] _secret;

        public JwtAuthManager(JwtTokenConfig jwtTokenConfig
            , DataContext context)
        {
            _jwtTokenConfig = jwtTokenConfig;
            _context = context;
            _usersRefreshTokens = new ConcurrentDictionary<string, RefreshToken>();
            _secret = Encoding.ASCII.GetBytes(jwtTokenConfig.Secret);
        }

        // optional: clean up expired refresh tokens
        public void RemoveExpiredRefreshTokens(DateTime now)
        {
            var expiredTokens = _context.RefreshToken.Where(x => x.ExpireAt < now).ToList();
            if (expiredTokens.Count > 0)
            {
                _context.RemoveRange(expiredTokens);
                try
                {
                    _context.SaveChanges();

                }
                catch (Exception ex)
                {
                    throw;
                }
            }
        }

        // can be more specific to ip, user agent, device name, etc.
        public void RemoveRefreshTokenByUserName(string userName)
        {
            var refreshTokens = _context.RefreshToken.Where(x => x.UserName == userName).ToList();
            if (refreshTokens.Count > 0)
            {
                _context.RemoveRange(refreshTokens);
                try
                {
                    _context.SaveChanges();

                }
                catch (Exception ex)
                {
                    throw;
                }
            }

        }

        public JwtAuthResult GenerateTokens(string username, Claim[] claims, DateTime now)
        {
            var shouldAddAudienceClaim = string.IsNullOrWhiteSpace(claims?.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Aud)?.Value);
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = _jwtTokenConfig.Issuer,
                Audience = shouldAddAudienceClaim ? _jwtTokenConfig.Audience : string.Empty,
                Subject = new ClaimsIdentity(claims),
                Expires = now.AddMinutes(_jwtTokenConfig.AccessTokenExpiration),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_secret), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var accessToken = tokenHandler.WriteToken(token);

            var refreshToken = new RefreshToken
            {
                UserName = username,
                TokenString = GenerateRefreshTokenString(),
                ExpireAt = now.AddMinutes(_jwtTokenConfig.RefreshTokenExpiration)
            };


            return new JwtAuthResult
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };
        }

        public JwtAuthResult Refresh(string refreshToken, string accessToken, DateTime now)
        {
            var (principal, jwtToken) = DecodeJwtToken(accessToken);
            if (jwtToken == null || !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256))
            {
                throw new SecurityTokenException("Invalid token");
            }

            var userName = principal.Identity?.Name;
            if (string.IsNullOrEmpty(userName))
            {
                throw new SecurityTokenException("Invalid token");
            }
            var existingRefreshToken = _context.RefreshToken.FirstOrDefault(x => x.TokenString == refreshToken);
            if (existingRefreshToken == null)
            {
                throw new SecurityTokenException("Invalid token");
            }
            if (existingRefreshToken.UserName.ToLower() != userName.ToLower() || existingRefreshToken.ExpireAt < now)
            {
                throw new SecurityTokenException("Invalid token");
            }
            var refreshTokenResult = GenerateTokens(userName.ToLower(), principal.Claims.ToArray(), now);

            existingRefreshToken.TokenString = refreshTokenResult.RefreshToken.TokenString;
            existingRefreshToken.ExpireAt = now.AddMinutes(_jwtTokenConfig.RefreshTokenExpiration);
            _context.RefreshToken.Update(existingRefreshToken);
            _context.SaveChanges();

            return refreshTokenResult; // need to recover the original claims
        }

        public (ClaimsPrincipal, JwtSecurityToken) DecodeJwtToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new SecurityTokenException("Invalid token");
            }
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token,
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = _jwtTokenConfig.Issuer,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(_secret),
                        ValidAudience = _jwtTokenConfig.Audience,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(1)
                    },
                    out var validatedToken);
            return (principal, validatedToken as JwtSecurityToken);
        }

        private static string GenerateRefreshTokenString()
        {
            var randomNumber = new byte[32];
            using var randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        public void AddRefreshToken(RefreshToken refreshToken)
        {
            _context.RefreshToken.Add(refreshToken);
            try
            {
                _context.SaveChanges();
            }
            catch
            {
            }
        }
    }

    public class JwtAuthResult
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public RefreshToken RefreshToken { get; set; }
    }

}
