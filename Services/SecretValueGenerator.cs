using System;
using System.Security.Cryptography;

namespace RauskuClaw.Services
{
    public interface ISecretValueGenerator
    {
        string GenerateHex(int bytes = 32);
    }

    public sealed class SecretValueGenerator : ISecretValueGenerator
    {
        public string GenerateHex(int bytes = 32)
        {
            var length = Math.Max(16, bytes);
            var data = new byte[length];
            RandomNumberGenerator.Fill(data);
            return Convert.ToHexString(data).ToLowerInvariant();
        }
    }
}
