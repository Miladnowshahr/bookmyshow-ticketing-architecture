using Microsoft.AspNetCore.SignalR;

namespace BookingApi.Hubs;

/// <summary>
/// Pushes incremental seat-state changes to everyone viewing a given
/// auditorium (article §5). Clients join a per-show group on connect so a
/// change to Auditorium 1's seat map never fans out to viewers of
/// Auditorium 2 — this is what keeps broadcast volume manageable when
/// 20k–50k people are watching the same show at once.
/// </summary>
public class SeatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var showId = Context.GetHttpContext()?.Request.Query["showId"];
        if (!string.IsNullOrEmpty(showId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showId!));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var showId = Context.GetHttpContext()?.Request.Query["showId"];
        if (!string.IsNullOrEmpty(showId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(showId!));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(string showId) => $"auditorium:{showId}";
}

public record SeatUpdate(string SeatId, string ShowId, string Status);

/// <summary>
/// Batches seat-state changes and flushes them together every 500ms
/// (article §5.2.3). A single click that locks 4 adjacent seats — or a
/// timeout that releases a dozen — produces one broadcast instead of a
/// flood of individually-tiny SignalR messages.
/// </summary>
public class SeatUpdateBuffer
{
    private readonly IHubContext<SeatHub> _hubContext;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SeatUpdate> _latest = new();

    public SeatUpdateBuffer(IHubContext<SeatHub> hubContext)
    {
        _hubContext = hubContext;
        _ = ProcessAsync();
    }

    public void AddUpdate(SeatUpdate update) => _latest[update.SeatId] = update;

    private async Task ProcessAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            if (_latest.IsEmpty)
                continue;

            var updates = _latest.Values.ToList();
            _latest.Clear();

            foreach (var group in updates.GroupBy(u => u.ShowId))
            {
                await _hubContext.Clients
                    .Group(SeatHub.GroupName(group.Key))
                    .SendAsync("SeatBatchUpdate", group.ToList());
            }
        }
    }
}
