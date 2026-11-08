using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Qms.Application.Common;

/// <summary>
/// The <c>version</c> string of the setup API (ADR-008 section 5). A row's version is its PostgreSQL <c>xmin</c>
/// (<c>uint</c>) written as an opaque string; clients echo it back unchanged on <c>PUT</c>. The version of a whole set
/// (a mapping grid) is a hash of the sorted (row id, <c>xmin</c>) pairs, so it does not depend on row order and changes
/// when any row or any <c>xmin</c> changes.
/// </summary>
public static class RowVersionCodec
{
    private const int SetHashBytes = 16;

    /// <summary>Opaque, non-empty text for <paramref name="rowVersion"/> (base64url of the four bytes).</summary>
    public static string Encode(uint rowVersion)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return Base64Url(bytes);
    }

    /// <summary>Reverses <see cref="Encode"/>. False for null, empty, wrong-length or non-base64url text.</summary>
    public static bool TryDecode(string? version, out uint rowVersion)
    {
        rowVersion = 0;
        if (string.IsNullOrEmpty(version) || version.Length != 6)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[sizeof(uint) + 2];
        var base64 = version.Replace('-', '+').Replace('_', '/') + "==";
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != sizeof(uint))
        {
            return false;
        }

        // Only canonical text counts, so "AAAAAB" and "AAAAAA" cannot both mean 0 or 1.
        rowVersion = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return Encode(rowVersion) == version;
    }

    /// <summary>Version of a set of rows: the same for the same (id, xmin) pairs in any order.</summary>
    public static string ForSet(IEnumerable<(Guid Id, uint RowVersion)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var builder = new StringBuilder();
        foreach (var (id, rowVersion) in rows.OrderBy(row => row.Id))
        {
            builder.Append(id.ToString("N")).Append(':').Append(rowVersion).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Base64Url(hash.AsSpan(0, SetHashBytes));
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
