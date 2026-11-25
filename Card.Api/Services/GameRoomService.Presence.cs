using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    // ── 접속 추적 (유령 플레이어·유령 방 방지) ──
    // 닉네임별 활성 SignalR 연결 수. 탭을 여러 개 열어도 마지막 연결이 끊길 때만 퇴장을 예약한다.
    private readonly ConcurrentDictionary<string, int> _connectionCounts = new();
    // 닉네임별 지연 퇴장 취소 토큰. 유예 안에 재접속(새로고침·순간 끊김)하면 취소된다.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingLeaves = new();

    /// <summary>연결이 끊긴 뒤 방에서 제거되기까지의 유예 시간.</summary>
    public static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(30);

    /// <summary>연결 등록. 대기 중인 지연 퇴장이 있으면 취소한다.</summary>
    public void RegisterConnection(string playerKey)
    {
        _connectionCounts.AddOrUpdate(playerKey, 1, (_, n) => n + 1);
        if (_pendingLeaves.TryRemove(playerKey, out var cts))
            cts.Cancel();
    }

    /// <summary>연결 해제. 마지막 연결이면 유예 시간 뒤 onLeave를 실행하는 지연 퇴장을 예약한다.
    /// 유예 중 RegisterConnection이 오면 취소된다. 반환 Task는 예약된 작업(대기·테스트용).</summary>
    public Task UnregisterConnection(string playerKey, Func<Task> onLeave, TimeSpan? grace = null)
    {
        int remaining = _connectionCounts.AddOrUpdate(playerKey, 0, (_, n) => Math.Max(0, n - 1));
        if (remaining > 0) return Task.CompletedTask;
        _connectionCounts.TryRemove(new KeyValuePair<string, int>(playerKey, 0));

        var cts = new CancellationTokenSource();
        _pendingLeaves.AddOrUpdate(playerKey, cts, (_, prev) => { prev.Cancel(); return cts; });
        var token = cts.Token;

        return Task.Run(async () =>
        {
            try { await Task.Delay(grace ?? DisconnectGrace, token); }
            catch (OperationCanceledException) { return; }
            if (token.IsCancellationRequested) return;
            _pendingLeaves.TryRemove(new KeyValuePair<string, CancellationTokenSource>(playerKey, cts));
            await onLeave();
        });
    }

    /// <summary>플레이어가 들어 있는 방 목록.</summary>
    public List<GameRoom> GetRoomsOf(string playerKey) =>
        _rooms.Values.Where(r => r.Players.Any(p => p.PlayerId == playerKey)).ToList();
}
