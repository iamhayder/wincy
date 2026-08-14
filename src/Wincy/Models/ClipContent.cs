using System.Security.Cryptography;

namespace Wincy.Models;

/// <summary>
/// One clipboard representation of a copy: a format name plus its raw bytes.
///
/// The bytes are loaded lazily. A history of a few hundred screenshots is easily
/// hundreds of megabytes, and almost none of it is needed to draw the list, so blobs
/// stay in the database until something actually asks for them.
/// </summary>
public sealed class ClipContent
{
    private byte[]? _value;
    private bool _loaded = true;

    public long Id { get; set; }

    public long ItemId { get; set; }

    /// <summary>Persisted format name; see <see cref="ClipFormats"/>.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Supplies the bytes on first access. Set by the store when an item is read back
    /// without its blobs; null for freshly captured content, which already has them.
    /// </summary>
    internal Func<long, byte[]?>? Loader { get; set; }

    public byte[]? Value
    {
        get
        {
            if (!_loaded)
            {
                _value = Loader?.Invoke(Id);
                _loaded = true;
            }

            return _value;
        }
        set
        {
            _value = value;
            _loaded = true;
            Hash = value is null ? null : Digest(value);
        }
    }

    /// <summary>
    /// SHA-256 of the bytes, persisted alongside them. Duplicate detection compares
    /// these rather than the blobs, so recognising a repeated copy never needs a read.
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>Number of bytes, as recorded in the database.</summary>
    public int Length { get; set; }

    public bool HasValue => Length > 0;

    public ClipContent()
    {
    }

    public ClipContent(string format, byte[]? value)
    {
        Format = format;
        Value = value;
        Length = value?.Length ?? 0;
    }

    /// <summary>Marks the content as stored-but-unread, to be fetched through <see cref="Loader"/>.</summary>
    internal void DeferValue(Func<long, byte[]?> loader)
    {
        Loader = loader;
        _value = null;
        _loaded = false;
    }

    public bool HasSameValue(ClipContent other)
    {
        if (!string.Equals(Format, other.Format, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Length != other.Length)
        {
            return false;
        }

        // Both sides carry a hash in every path that matters, so this is the fast exit.
        if (Hash is not null && other.Hash is not null)
        {
            return string.Equals(Hash, other.Hash, StringComparison.Ordinal);
        }

        var mine = Value;
        var theirs = other.Value;

        if (mine is null || theirs is null)
        {
            return mine is null && theirs is null;
        }

        return mine.AsSpan().SequenceEqual(theirs);
    }

    public static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
