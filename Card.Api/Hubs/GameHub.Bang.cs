using Card.Api.Services;
using Card.Api.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Card.Hubs;

public partial class GameHub
{
    // ===== 뱅(Bang!) 모드 =====

    public async Task StartBangGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 게임을 시작할 수 있습니다.");
            return;
        }
        bool ok = _bangService.StartBangGame(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "뱅 게임을 시작할 수 없습니다 (4~7명 필요).");
            return;
        }
        await BroadcastRoomAsync(room, "GameStarted");
        await BroadcastRoomAsync(room);

        // 사람이 손패 확인할 시간 5초 대기 (보안관이 봇이라도)
        await Clients.Group(roomId).SendAsync("BangLog", "⏳ 게임 시작! 손패를 확인할 시간 5초...");
        await Task.Delay(5000);

        // 보안관이 봇이거나 첫 응답자 봇이면 자동 진행
        await TriggerBotActions(roomId);
    }

    // 뱅 — 봇 1명 추가. 방장만, 게임 시작 전, 최대 7명까지
    public async Task AddBangBot(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 봇을 추가할 수 있습니다.");
            return;
        }
        if (room.IsStarted) return;
        if (room.GameMode != GameMode.Bang) return;
        var ok = _roomService.AddBot(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "봇을 추가할 수 없습니다 (정원 초과 또는 게임 시작됨).");
            return;
        }
        await BroadcastRoomAsync(room);
    }

    // 뱅 — Dodge City 확장팩 사용 토글. 방장만, 게임 시작 전에만
    public async Task UpdateBangExpansion(string roomId, bool useDodgeCity)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey) return;
        if (room.IsStarted) return;
        if (room.GameMode != GameMode.Bang) return;
        room.BangUseDodgeCity = useDodgeCity;
        await BroadcastRoomAsync(room);
    }

    // 뱅 — 응답 타이머 한도 설정. 방장만, 언제든 변경 가능 (전체 인원에게 통일 적용)
    public async Task UpdateBangResponseTimer(string roomId, int seconds)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey) return;
        if (room.GameMode != GameMode.Bang) return;
        if (!new[] { 10, 20, 30, 60 }.Contains(seconds)) return;
        room.BangResponseTimerSec = seconds;
        await BroadcastRoomAsync(room);
    }

    // 재대국 — 같은 방에서 다시 시작. 방장만
    public async Task RestartBangGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        if (room.HostPlayerId != PlayerKey)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "방장만 재대국할 수 있습니다.");
            return;
        }
        if (room.GameMode != GameMode.Bang) return;
        // 게임 종료 상태에서만
        if (!room.IsFinished && room.IsStarted) return;
        // 상태 초기화
        room.Bang = null;
        room.IsStarted = false;
        room.IsFinished = false;

        bool ok = _bangService.StartBangGame(roomId);
        if (!ok)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "재대국에 실패했습니다 (4~7명 필요).");
            return;
        }
        await BroadcastRoomAsync(room, "GameStarted");
        await BroadcastRoomAsync(room);
        await Clients.Group(roomId).SendAsync("BangLog", "🔄 재대국 시작!");
        await Clients.Group(roomId).SendAsync("BangLog", "⏳ 손패를 확인할 시간 5초...");
        await Task.Delay(5000);

        await TriggerBotActions(roomId);
    }

    // 봇 자동 액션 트리거 — 한 번에 1액션씩 + delay로 사용자 가시성 확보
    private async Task TriggerBotActions(string roomId)
    {
        const int maxIterations = 30;
        const int delayMs = 500;
        for (int i = 0; i < maxIterations; i++)
        {
            var (didAction, log) = _bangService.ProcessOneBotAction(roomId);
            if (!didAction)
            {
                // 봇이 멈춘 이유 진단 로그 (디버깅용)
                var reason = _bangService.GetBotStallReason(roomId);
                if (!string.IsNullOrEmpty(reason))
                    await Clients.Group(roomId).SendAsync("BangLog", $"[봇 대기] {reason}");
                break;
            }

            var updatedRoom = _roomService.GetRoom(roomId);
            if (updatedRoom != null) await BroadcastRoomAsync(updatedRoom);
            if (!string.IsNullOrEmpty(log)) await Clients.Group(roomId).SendAsync("BangLog", log);

            if (updatedRoom?.Bang?.IsFinished == true)
            {
                await Clients.Group(roomId).SendAsync("BangGameEnded", new
                {
                    WinnerRole = updatedRoom.Bang.WinnerRole,
                    WinnerPlayerIds = updatedRoom.Bang.WinnerPlayerIds,
                    Reason = updatedRoom.Bang.EndReason
                });
                break;
            }

            await Task.Delay(delayMs);
        }
    }

    // 카드 사용 (BANG/Beer/Stagecoach 등). targetPlayerId는 BANG처럼 대상이 필요한 경우만
    public async Task PlayBangCard(string roomId, int cardId, string? targetPlayerId)
    {
        var result = _bangService.PlayCard(roomId, PlayerKey, cardId, targetPlayerId);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "카드를 사용할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("BangGameEnded", new
            {
                WinnerRole = room.Bang?.WinnerRole,
                WinnerPlayerIds = room.Bang?.WinnerPlayerIds,
                Reason = room.Bang?.EndReason
            });
        }
    }

    // General Store — 시계방향으로 카드 선택
    public async Task PickGeneralStoreCard(string roomId, int cardId)
    {
        var result = _bangService.PickFromGeneralStore(roomId, PlayerKey, cardId);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "카드를 선택할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
    }

    // Indians 응답 — useBang=true면 BANG 카드로 응답, false면 데미지 수용
    public async Task RespondIndians(string roomId, bool useBang)
    {
        var result = _bangService.RespondIndians(roomId, PlayerKey, useBang);
        await DispatchBangResult(roomId, result);
    }

    // Gatling 응답 — useMissed=true면 Missed로 회피, false면 데미지 수용
    public async Task RespondGatling(string roomId, bool useMissed)
    {
        var result = _bangService.RespondGatling(roomId, PlayerKey, useMissed);
        await DispatchBangResult(roomId, result);
    }

    // Duel 응답 — useBang=true면 BANG 던지고 상대 차례, false면 1 데미지 + 종료
    public async Task RespondDuel(string roomId, bool useBang)
    {
        var result = _bangService.RespondDuel(roomId, PlayerKey, useBang);
        await DispatchBangResult(roomId, result);
    }

    // Sid Ketchum 능력 — 손패 2장 버려 HP 1 회복 (언제든)
    public async Task UseSidKetchum(string roomId, List<int> discardCardIds)
    {
        var result = _bangService.UseSidKetchum(roomId, PlayerKey, discardCardIds);
        await DispatchBangResult(roomId, result);
    }

    // Lucky Duke — 펴진 2장 중 1장 직접 선택
    public async Task ChooseLuckyDuke(string roomId, int chosenCardId)
    {
        var result = _bangService.ChooseLuckyDuke(roomId, PlayerKey, chosenCardId);
        await DispatchBangResult(roomId, result);
    }

    // Chuck Wengam 능력 — 본인 턴에 HP 1 잃고 카드 2장
    public async Task UseChuckWengam(string roomId)
    {
        var result = _bangService.UseChuckWengam(roomId, PlayerKey);
        await DispatchBangResult(roomId, result);
    }

    // Doc Holyday 능력 — 본인 턴에 손패 2장 버려서 BANG 효과 (턴당 1회)
    public async Task UseDocHolyday(string roomId, List<int> discardCardIds, string targetPlayerId)
    {
        var result = _bangService.UseDocHolyday(roomId, PlayerKey, discardCardIds, targetPlayerId);
        await DispatchBangResult(roomId, result);
    }

    // Kit Carlson — 펴진 3장 중 덱으로 돌릴 1장 선택
    public async Task ChooseKitCarlson(string roomId, int returnedCardId)
    {
        var result = _bangService.KitCarlsonChoice(roomId, PlayerKey, returnedCardId);
        await DispatchBangResult(roomId, result);
    }

    // Jesse Jones — fromDeck=true면 일반 드로우, false면 fromPlayerId 손에서 1장
    public async Task ChooseJesseJones(string roomId, bool fromDeck, string? fromPlayerId)
    {
        var result = _bangService.JesseJonesChoice(roomId, PlayerKey, fromDeck, fromPlayerId);
        await DispatchBangResult(roomId, result);
    }

    // Pedro Ramirez — fromDeck=true면 일반 드로우, false면 버린 카드 top 1장
    public async Task ChoosePedroRamirez(string roomId, bool fromDeck)
    {
        var result = _bangService.PedroRamirezChoice(roomId, PlayerKey, fromDeck);
        await DispatchBangResult(roomId, result);
    }

    // 응답 결과 공통 broadcast 헬퍼
    private async Task DispatchBangResult(string roomId, BangActionResult result)
    {
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "응답할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);

        // Lucky Duke 펴기 결과 별도 로그 + 클리어
        if (room.Bang?.LastLuckyDukeReveal != null && room.Bang.LastLuckyDukeReveal.Count > 0)
        {
            var picks = string.Join(" / ", room.Bang.LastLuckyDukeReveal.Select(c =>
            {
                var s = c.Suit switch
                {
                    Card.Api.Domain.CardSuit.Hearts => "♥",
                    Card.Api.Domain.CardSuit.Diamonds => "♦",
                    Card.Api.Domain.CardSuit.Clubs => "♣",
                    Card.Api.Domain.CardSuit.Spades => "♠",
                    _ => "?"
                };
                var rank = c.Rank switch
                {
                    1 => "A", 11 => "J", 12 => "Q", 13 => "K",
                    _ => c.Rank.ToString()
                };
                return $"{s}{rank}";
            }));
            await Clients.Group(roomId).SendAsync("BangLog", $"🍀 Lucky Duke 펴기 [{picks}]");
            room.Bang.LastLuckyDukeReveal = null;
        }

        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("BangGameEnded", new
            {
                WinnerRole = room.Bang?.WinnerRole,
                WinnerPlayerIds = room.Bang?.WinnerPlayerIds,
                Reason = room.Bang?.EndReason
            });
            return;
        }

        // 봇 자동 액션 처리 — 1액션씩 delay 루프
        await TriggerBotActions(roomId);
    }

    // CatBalou/Panic 사용자가 대상 손패의 카드 ID를 직접 선택
    public async Task PickBangCardFromTarget(string roomId, int targetCardId)
    {
        var result = _bangService.PickCardFromTarget(roomId, PlayerKey, targetCardId);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "카드를 선택할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
    }

    // BANG 응답 — useMissed=true면 Missed로 회피, false면 데미지 수용
    public async Task RespondBang(string roomId, bool useMissed)
    {
        var result = _bangService.RespondBang(roomId, PlayerKey, useMissed);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "응답할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
        if (result.GameEnded)
        {
            await Clients.Group(roomId).SendAsync("BangGameEnded", new
            {
                WinnerRole = room.Bang?.WinnerRole,
                WinnerPlayerIds = room.Bang?.WinnerPlayerIds,
                Reason = room.Bang?.EndReason
            });
        }
    }

    // 턴 종료. discardCardIds는 손패 초과 시 버릴 카드 ID 배열
    public async Task EndBangTurn(string roomId, List<int>? discardCardIds)
    {
        var result = _bangService.EndTurn(roomId, PlayerKey, discardCardIds);
        if (!result.Success)
        {
            await Clients.Caller.SendAsync("ErrorMessage", result.Error ?? "턴을 종료할 수 없습니다.");
            return;
        }
        var room = _roomService.GetRoom(roomId);
        if (room == null) return;
        await BroadcastRoomAsync(room);
        if (!string.IsNullOrEmpty(result.Message))
            await Clients.Group(roomId).SendAsync("BangLog", result.Message);
    }

    // 뱅 턴 타임아웃 — 자동 턴 종료(초과 손패 자동 버림). 응답 대기 중은 응답 타이머가 처리
    private async Task BangTurnTimeoutAsync(string roomId, GameRoom room, string timedOutPlayerId)
    {
        var bang = room.Bang;
        if (bang == null || bang.IsFinished) return;
        if (bang.Pending != null) return;
        if (bang.CurrentTurnPlayerId != timedOutPlayerId) return;
        if ((DateTime.UtcNow - bang.TurnStartedAt).TotalSeconds < room.TurnTimeoutSec - TurnTimeoutGraceSec) return;

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == timedOutPlayerId);
        if (me == null) return;
        int excess = me.Hand.Count - me.Health;
        List<int>? discardIds = excess > 0 ? me.Hand.Take(excess).Select(c => c.Id).ToList() : null;

        var result = _bangService.EndTurn(roomId, timedOutPlayerId, discardIds);
        if (!result.Success) return;
        var r = _roomService.GetRoom(roomId);
        if (r != null) await BroadcastRoomAsync(r);
        await Clients.Group(roomId).SendAsync("BangLog", $"⏱ {me.Name} 턴 시간 초과 — 자동 종료");
        await TriggerBotActions(roomId);
    }
}
