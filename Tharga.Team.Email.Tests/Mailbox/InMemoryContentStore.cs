namespace Tharga.Team.Email.Tests.Mailbox;

internal sealed class InMemoryContentStore : IInboundMailContentStore
{
    private readonly Dictionary<string, byte[]> _content = [];

    public List<string> Saved { get; } = [];

    public bool FailNextSave { get; set; }

    /// <summary>Fails the save with this number, counting from one, once.</summary>
    public int FailSaveNumber { get; set; }

    private int _saves;

    public Task SaveAsync(string mailId, byte[] raw, CancellationToken cancellationToken = default)
    {
        _saves++;
        if (FailNextSave || _saves == FailSaveNumber)
        {
            FailNextSave = false;
            FailSaveNumber = 0;
            throw new IOException("The content store is down.");
        }

        _content[mailId] = raw;
        Saved.Add(mailId);
        return Task.CompletedTask;
    }

    public Task<byte[]> GetAsync(string mailId, CancellationToken cancellationToken = default) => Task.FromResult(_content.GetValueOrDefault(mailId));

    public Task DeleteAsync(string mailId, CancellationToken cancellationToken = default)
    {
        _content.Remove(mailId);
        return Task.CompletedTask;
    }
}
