namespace Tharga.Team.Support.Cases;

/// <summary>
/// Reads another stream and refuses to go past a number of bytes, so a file is measured while it is stored
/// rather than trusted for its declared size.
/// </summary>
/// <remarks>
/// <see cref="Exceeded"/> is what the caller checks: the exception thrown here may reach it wrapped by
/// whatever was reading.
/// </remarks>
internal sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    /// <summary>How many bytes have been read so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Whether the content went past the limit.</summary>
    public bool Exceeded { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => Count(await inner.ReadAsync(buffer, cancellationToken));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        BytesRead += read;
        if (BytesRead <= limit) return read;

        Exceeded = true;
        throw new InvalidOperationException($"The content is larger than the {limit} bytes allowed.");
    }
}
