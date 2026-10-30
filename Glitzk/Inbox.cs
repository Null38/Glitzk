using System.Collections.Concurrent;

namespace Glitzk;

sealed class Inbox
{
    private readonly ConcurrentQueue<Action> queue = new();

    public void Post(Action work) => queue.Enqueue(work);

    public void Drain()
    {
        while (queue.TryDequeue(out var work))
        {
            work();
        }
    }
}
