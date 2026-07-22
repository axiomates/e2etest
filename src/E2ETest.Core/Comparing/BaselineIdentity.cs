using System.Security.Cryptography;

namespace E2ETest.Core.Comparing;

public static class BaselineIdentity
{
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static bool Matches(string path, string expectedSha256) =>
        string.Equals(ComputeSha256(path), expectedSha256, StringComparison.OrdinalIgnoreCase);
}
