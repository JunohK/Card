using Card.Api.Services;
using Card.Api.Domain;
using Card.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Card.Hubs;

// Cards(카드 게임) — 시작·버림·뻥·STOP·뽑기·승리 선언·라운드·기권·전적·턴 타임아웃
public partial class GameHub
{
    // ✅ [새로 추가한 기능] 게임 시작
    public async Task StartGame(string roomId, int maxRounds)
    {
        try
        {
            using (await _roomService.LockRoomAsync(roomId))
            {
                // 1. 서비스에서 게임 데이터 초기화 (패 배분, 첫 턴 설정)
                _roomService.StartGame(roomId, maxRounds);

                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 2. 모든 플레이어에게 "게임 시작"과 "첫 데이터"를 동시에 전송
                // GameStarted와 RoomUpdated를 연달아 보내 모든 클라이언트의 UI를 강제 갱신합니다.
                await BroadcastRoomAsync(room, "GameStarted");
                await BroadcastRoomAsync(room);

                Console.WriteLine($"Game Started in Room {roomId}. First Turn: {room.CurrentTurnPlayerId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StartGame Error: {ex.Message}");
            await Clients.Caller.SendAsync("ErrorMessage", "게임을 시작할 수 없습니다: " + ex.Message);
        }
    }

    // 뻥 대기 시간(ms): 버린 뒤 이 시간 안에만 뻥 가능. 턴은 기다리지 않고 바로 넘어간다
    private const int PungWindowMs = 2000;

    public async Task PlayCard(string roomId, PlayingCard card)
    {
        string cardOwnerId = PlayerKey;
        try
        {
            int myWindowToken;

            // ── 1구간: 카드 버림 + 뻥 대기 시간 시작 + 턴 이동 (락 안에서 원자적으로) ──
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null || !room.IsStarted || room.IsFinished) return;
                // 턴 가드: 내 턴이 아니면 무시(중복/난입 방지)
                if (room.CurrentTurnPlayerId != cardOwnerId) return;

                // 현재 턴인 유저가 선택한 카드 1장을 버림 (서비스는 턴을 넘기지 않음)
                // 손에 없는 카드 등으로 실제 버림이 없으면 아무것도 진행하지 않는다 (턴 건너뛰기·가짜 윈도우 방지)
                if (!_roomService.PlayCard(roomId, cardOwnerId, card))
                {
                    await Clients.Caller.SendAsync("ErrorMessage", "버릴 수 없는 카드입니다.");
                    return;
                }

                // 바가지/STOP 등 즉시 라운드 종료된 경우 — 결과만 전파하고 종료
                if (room.IsRoundEnded || room.IsFinished)
                {
                    await BroadcastRoomAsync(room);
                    await SendResultBoardAsync(room);
                    await RecordGameResultAsync(room);
                    return;
                }

                // 뻥 대기 시간 시작 — 누가 무엇을 버리든 항상 연다.
                // (뻥 가능한 사람이 있을 때만 열면 "열렸다/안 열렸다"로 남의 손패 정보가 전원에게 새기 때문)
                // 대기 시간마다 고유 토큰: InterruptDiscard는 열린 토큰을 0으로 바꾼 한 명만 통과하고,
                // 마감(2구간)은 자기 토큰일 때만 닫아 뒤늦은 마감이 다음 대기 시간을 닫지 못하게 한다
                myWindowToken = Interlocked.Increment(ref room.PungWindowSeq);
                Interlocked.Exchange(ref room.PungWindowFlag, myWindowToken);
                room.PungWindowDraw = null;

                // 턴은 기다리지 않고 바로 넘긴다 — 다음 사람은 즉시 뽑을 수 있고,
                // 대기 시간 안에 뻥이 들어오면 그 사람이 뽑은 카드는 InterruptDiscard가 덱으로 되돌린다
                _roomService.PassToNextPlayer(room, cardOwnerId);
                await BroadcastRoomAsync(room);
            }

            // 다른 유저가 InterruptDiscard를 호출할 시간. 락 밖 — 그래야 InterruptDiscard/DrawCard가 락을 잡고 들어올 수 있다.
            await Task.Delay(PungWindowMs);

            // ── 2구간: 윈도우 마감 (락 안에서) ──
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 마감 시도: 내 토큰→0 성공이면 아무도 뻥하지 않은 것 (0이면 누가 뻥한 것, 다른 토큰이면 이미 다음 대기 시간)
                if (Interlocked.CompareExchange(ref room.PungWindowFlag, 0, myWindowToken) == myWindowToken)
                {
                    room.PungWindowDraw = null;      // 이 대기 시간 중의 뽑기는 확정
                    await BroadcastRoomAsync(room);  // 마감 전파 — 클라이언트 뻥 버튼이 꺼지도록
                }
                // 누가 뻥했으면 InterruptDiscard가 상태를 이미 전파했으므로 여기서는 아무것도 안 함
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PlayCard Hub Error: {ex.Message}");
        }
    }

    // 라운드/게임 종료 결과창 신호 (바가지·STOP·승리선언·타임아웃 공용)
    private Task SendResultBoardAsync(GameRoom room) =>
        Clients.Group(room.RoomId).SendAsync("ShowResultBoard", new
        {
            WinnerPlayerId = room.WinnerPlayerId,
            WinnerName = room.WinnerName,
            WinnerHand = room.WinnerHand,
            WinType = room.LastWinType
        });

    // 🔥 [뻥] 상대가 버린 뒤 대기 시간 안에 누른 사람 — 규칙 판단·상태 변경은 GameRoomService.TryPung
    public async Task InterruptDiscard(string roomId)
    {
        try
        {
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                var pung = _roomService.TryPung(room, PlayerKey);
                if (pung == null) return;

                if (pung.RevertedDraw != null)
                {
                    var drawerName = room.Players.FirstOrDefault(p => p.PlayerId == pung.RevertedDraw.PlayerId)?.Name ?? pung.RevertedDraw.PlayerId;
                    await Clients.Group(roomId).SendAsync("ErrorMessage", $"🔄 {drawerName}님이 방금 뽑은 카드는 뻥으로 덱에 돌아갔습니다 (덱 섞음)");
                }
                await Clients.Group(roomId).SendAsync("ErrorMessage", $"🔥 {pung.Puncher.Name}님이 뻥을 성공했습니다!! 🔥");

                // 상태 전파 (원래 버린 1장 + 뻥한 사람이 낸 2장이 바닥에 보임). 이어서 뻥한 사람이 1장 더 버린다
                await BroadcastRoomAsync(room);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InterruptDiscard Error: {ex.Message}");
        }
    }

    public async Task DeclareStop(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            _roomService.DeclareStop(roomId, PlayerKey);

            // 이 사람의 선언이 받아들여졌을 때만 알림 + 상태 전파
            var room = _roomService.GetRoom(roomId);
            var player = room?.Players.FirstOrDefault(p => p.PlayerId == PlayerKey);
            if (room != null && player != null && room.IsStopDeclared && room.StopCallerId == player.PlayerId)
            {
                await Clients.Group(roomId).SendAsync("ErrorMessage", $"{player.Name}님이 STOP을 선언했습니다! 카드를 버리면 게임이 종료됩니다.");
                await BroadcastRoomAsync(room);
            }
        }
    }

    public async Task DrawCard(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            int pileBefore = _roomService.GetRoom(roomId)?.DiscardPile.Count ?? 0;
            var updatedRoom = _roomService.DrawCard(roomId, PlayerKey);
            if (updatedRoom != null)
            {
                // 덱이 비어 버림더미가 자동으로 덱에 들어갔으면 알림
                if (updatedRoom.DiscardPile.Count < pileBefore)
                    await Clients.Group(roomId).SendAsync("ErrorMessage", "덱이 비어 버려진 카드를 다시 섞어 넣었습니다.");
                await BroadcastRoomAsync(updatedRoom);
            }
        }
    }

    // 방장 수동 셔플 — 자동 보충의 예비 수단 (덱이 비어 있을 때만). 별도 이벤트 없이 알림 + RoomUpdated로 전파
    public async Task ReshuffleDeck(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            if (!_roomService.ReshuffleDiscardPile(roomId, PlayerKey)) return;
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;
            await Clients.Group(roomId).SendAsync("ErrorMessage", "버려진 카드를 다시 섞어 덱에 넣었습니다.");
            await BroadcastRoomAsync(room);
        }
    }

    // 승리 로직 계산
    public async Task DeclareWin(string roomId)
    {
        using (await _roomService.LockRoomAsync(roomId))
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            var player = room.Players
                .FirstOrDefault(p => p.PlayerId == PlayerKey);
            if (player == null) return;

            // ✅ 1. 먼저 선언 가능 여부 검사
            if (!_roomService.CanDeclareWin(room, player))
            {
                await Clients.Caller.SendAsync(
                    "ErrorMessage",
                    "아직 승리 선언을 할 수 없습니다."
                );
                return;
            }

            // ✅ 2. 그 다음 실제 승리 처리
            bool success = _roomService.DeclareWin(
                room,
                player,
                WinReason.ManualDeclare
            );

            if (!success)
            {
                await Clients.Caller.SendAsync(
                    "ErrorMessage",
                    "조건에 맞지 않습니다."
                );
                return;
            }

            // ✅ 3. 결과 전파
            await SendResultBoardAsync(room);

            await BroadcastRoomAsync(room);
            await RecordGameResultAsync(room);
        }
    }

    public async Task RequestNextRound(string roomId)
    {
        try
        {
            using (await _roomService.LockRoomAsync(roomId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room == null) return;

                // 방장만 다음 라운드를 시작할 수 있도록 권한 체크 추가
                if (room.HostPlayerId != PlayerKey)
                {
                    await Clients.Caller.SendAsync("ErrorMessage", "방장만 다음 라운드를 시작할 수 있습니다.");
                    return;
                }

                // 1. 서비스에서 덱 생성, '셔플', 카드 분배, 턴 설정을 모두 수행
                _roomService.StartNextRound(room);

                // 2. 모든 플레이어에게 전광판을 닫으라고 명령
                await Clients.Group(roomId).SendAsync("HideResultBoard");

                // 3. 갱신된 방 상태(새 패, 새로운 턴 등)를 전송 — 다른 핸들러와 동일하게 raw room 전송
                //    (기존엔 GetRoom 프로젝션을 보내 필드 누락으로 다음 라운드 상태가 꼬였음)
                await BroadcastRoomAsync(room);

                Console.WriteLine($"Next Round Started: {room.CurrentRound}. Turn: {room.CurrentTurnPlayerId}");
            }
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "라운드 전환 중 오류: " + ex.Message);
        }
    }

    // 기권
    public async Task GiveUp(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        // 1. 기권 처리 (IsFinished = true 처리됨)
        _roomService.GiveUpGame(roomId, PlayerKey);

        // 2. ⭐ 명시적으로 게임 시작 상태를 해제 (대기실 복귀용)
        room.IsStarted = false; 

        // 3. 최신 데이터 전송
        await BroadcastRoomAsync(room);
        await RecordGameResultAsync(room);
        
        // 4. 전광판을 띄우는 대신 바로 나가게 하고 싶다면 이 신호를 보냄
        // 만약 결과 확인 후 나가게 하고 싶다면 "GameTerminated" 신호를 사용
        await Clients.Group(roomId).SendAsync("GameTerminated", roomId);
    }

    // ✅ 전적 반영 — Cards 게임 종료 시 서버가 1회만 호출 (StatsRecorded 플래그로 중복 집계 방지)
    //    Wins는 게임 전체 승자(총점 최저) 기준, MaxScore/MinScore는 최종 총점 기준
    private Task RecordGameResultAsync(GameRoom room) => RecordGameResultAsync(room, _context, Clients);

    private static async Task RecordGameResultAsync(GameRoom room, GameDbContext db, IHubClients<IClientProxy> clients)
    {
        if (!room.IsFinished || room.StatsRecorded) return;
        room.StatsRecorded = true;

        foreach (var p in room.Players)
        {
            if (p.IsBot) continue;
            // Player의 Name과 User의 Nickname을 비교
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Nickname == p.Name);
            if (user == null) continue;

            user.TotalGames += 1;
            if (room.GameWinnerIds.Contains(p.PlayerId)) user.Wins += 1;
            if (p.TotalScore > user.MaxScore) user.MaxScore = p.TotalScore;
            // 첫 게임이면 그대로 기록, 이후엔 최저값 갱신 (DB 기본값 0을 최저점으로 오인하지 않도록)
            if (user.TotalGames == 1 || p.TotalScore < user.MinScore) user.MinScore = p.TotalScore;
        }

        // SQLite DB에 변경사항 저장
        await db.SaveChangesAsync();

        // 유저 스탯이 갱신되었음을 클라이언트에 알림
        await clients.Group(room.RoomId).SendAsync("UpdateUserStats");
    }

    public async Task ToggleNaturalBagaji(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;

        var player = room.Players
            .FirstOrDefault(p => p.PlayerId == PlayerKey);
        if (player == null) return;

        player.AllowNaturalBagaji = !player.AllowNaturalBagaji;

        // 본인 UI만 갱신
        await Clients.Caller.SendAsync(
            "NaturalBagajiToggled",
            player.AllowNaturalBagaji
        );

        // 룸 상태 동기화 필요하면
        // await BroadcastRoomAsync(room);
    }

    // Cards 턴 타임아웃 — 자동으로 카드 1장 버려 턴 종료 (타임아웃 시 뻥 윈도우는 생략)
    private async Task CardsTurnTimeoutAsync(string roomId, string timedOutPlayerId)
    {
        // 락 안에서 재검증 → 여러 클라가 동시에 호출해도 턴이 중복 진행되지 않음(TOCTOU 방지)
        using (await _roomService.LockRoomAsync(roomId))
        {
            var r = _roomService.GetRoom(roomId);
            if (r == null || !r.IsStarted || r.IsFinished) return;
            if (r.IsRoundEnded) return;
            if (r.CurrentTurnPlayerId != timedOutPlayerId) return;
            if ((DateTime.UtcNow - r.TurnStartedAt).TotalSeconds < r.TurnTimeoutSec - TurnTimeoutGraceSec) return;

            var player = r.Players.FirstOrDefault(p => p.PlayerId == timedOutPlayerId);
            if (player == null) return;

            // 아직 안 뽑았으면(2·5장) 먼저 뽑는다 — 손패 장수 규칙(2/5 → 뽑아 3/6 → 1장 버림)을 지켜 1·4장이 되지 않게
            if (player.Hand.Count == 2 || player.Hand.Count == 5)
                _roomService.DrawCard(roomId, timedOutPlayerId);

            // 버릴 수 있는 상태(3·6장)가 아니면(덱·버림더미 모두 소진 등) 카드 손실 없이 턴만 넘김
            if (player.Hand.Count == 3 || player.Hand.Count == 6)
            {
                // 버림은 서비스 PlayCard로 — 바가지·STOP 정산 포함. 타임아웃 시 뻥 윈도우는 생략
                _roomService.PlayCard(roomId, timedOutPlayerId, player.Hand[^1]);
                if (r.IsRoundEnded || r.IsFinished)
                {
                    await BroadcastRoomAsync(r);
                    await SendResultBoardAsync(r);
                    await RecordGameResultAsync(r);
                    return;
                }
            }
            _roomService.PassToNextPlayer(r, timedOutPlayerId);
            await BroadcastRoomAsync(r);
            await Clients.Group(roomId).SendAsync("ErrorMessage", $"⏱ {player.Name}님 턴 시간 초과 — 자동으로 카드를 버렸습니다.");
        }
    }
}
