using System.Security.Cryptography;

namespace AskMyData.Application.Prompting;

/// <summary>Production <see cref="INonceSource"/> backed by <see cref="RandomNumberGenerator"/>.</summary>
public sealed class RandomNonceSource : INonceSource
{
    public string Next() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
