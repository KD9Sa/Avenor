using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Avenor.Api;

// Invalidations contain no customer or ticket information. Each stream is isolated by tenant.
public sealed class LiveEvents
{
    private readonly ConcurrentDictionary<Guid, (Guid Tenant, Channel<bool> Channel)> subscribers = new();
    public (Guid Id, ChannelReader<bool> Reader) Subscribe(Guid tenant)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        subscribers[id] = (tenant, channel);
        return (id, channel.Reader);
    }
    public void Unsubscribe(Guid id)
    {
        if (subscribers.TryRemove(id, out var s))
            s.Channel.Writer.TryComplete();
    }
    public void Publish(Guid tenant)
    {
        foreach (var s in subscribers.Values.Where(x => x.Tenant == tenant))
            s.Channel.Writer.TryWrite(true);
    }
}
