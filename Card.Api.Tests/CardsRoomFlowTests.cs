using Card.Api.Domain;
using Card.Api.Services;
using static Card.Api.Tests.Cards;

namespace Card.Api.Tests;

// GameRoomService 방/라운드/턴 흐름 특성화 테스트 (허브 없이 서비스만)
public class CardsRoomFlowTests
{
    [Fact]
    public void CreateRoom_8자리ID와_모드_저장()
    {
        var svc = new GameRoomService();
        var room = svc.CreateRoom("p1", "title", "pw", GameMode.Omok);
        Assert.Equal(8, room.RoomId.Length);
        Assert.Equal(GameMode.Omok, room.GameMode);
        Assert.Equal("pw", room.Password);
        Assert.Empty(room.Players);
        Assert.Same(room, svc.GetRoom(room.RoomId));
    }

    [Fact]
    public void JoinRoom_첫입장자가_방장_8번째는_예외()
    {
        var (svc, room) = Rooms.Cards(7);
        Assert.Equal("p1", room.HostPlayerId);
        Assert.Equal(7, room.Players.Count);
        var ex = Assert.Throws<Exception>(() => { svc.JoinRoom(room.RoomId, "p8", "p8"); });
        Assert.Contains("가득", ex.Message);
    }

    [Fact]
    public void JoinRoom_비밀번호_검증()
    {
        var svc = new GameRoomService();
        var room = svc.CreateRoom("p1", "t", "pw");
        Assert.Throws<UnauthorizedAccessException>(() => { svc.JoinRoom(room.RoomId, "p1", "p1", "wrong"); });
        Assert.Throws<UnauthorizedAccessException>(() => { svc.JoinRoom(room.RoomId, "p1", "p1", null); });
        Assert.NotNull(svc.JoinRoom(room.RoomId, "p1", "p1", "pw"));
        Assert.Null(svc.JoinRoom("nope", "p1", "p1"));
    }

    [Fact]
    public void JoinRoom_같은닉네임_재입장시_ID와_방장_턴_재매핑()
    {
        var svc = new GameRoomService();
        var room = svc.CreateRoom("p1", "t", null);
        svc.JoinRoom(room.RoomId, "conn1", "p1");
        room.CurrentTurnPlayerId = "conn1";

        svc.JoinRoom(room.RoomId, "conn2", "p1");

        var only = Assert.Single(room.Players);
        Assert.Equal("conn2", only.PlayerId);
        Assert.Equal("conn2", room.HostPlayerId);
        Assert.Equal("conn2", room.CurrentTurnPlayerId);
    }

    [Theory]
    [InlineData(CardMode.Trump, 1, 2, 43)]  // 52 + 1 - 10
    [InlineData(CardMode.Trump, 0, 3, 37)]  // 52 - 15
    [InlineData(CardMode.Trump, 2, 2, 44)]  // 52 + 2 - 10
    [InlineData(CardMode.Hwatu, 2, 2, 40)]  // 48 + 2 - 10
    public void StartGame_덱크기는_모드와_조커수를_따른다(CardMode mode, int jokers, int players, int expectedDeck)
    {
        var (svc, room) = Rooms.Cards(players, jokers, mode);
        svc.StartGame(room.RoomId, 3);
        Assert.Equal(expectedDeck, room.DeckCount);
        Assert.All(room.Players, p => Assert.Equal(5, p.Hand.Count));
    }

    [Fact]
    public void StartGame_첫턴은_첫플레이어_라운드1_상태초기화()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        Assert.True(room.IsStarted);
        Assert.False(room.IsFinished);
        Assert.Equal(1, room.CurrentRound);
        Assert.Equal(3, room.MaxRounds);
        Assert.Equal("p1", room.CurrentTurnPlayerId);
        Assert.Empty(room.DiscardPile);
        Assert.Null(room.LastDiscardedCard);
        Assert.Empty(room.RoundScoreHistory);
        // 이미 시작된 방은 다시 시작하지 않음
        room.MaxRounds = 99;
        svc.StartGame(room.RoomId, 3);
        Assert.Equal(99, room.MaxRounds);
    }

    [Fact]
    public void DrawCard_내턴만_6장까지_턴카운트_증가()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");

        svc.DrawCard(room.RoomId, "p2");           // 남의 턴 → 무시
        Assert.Equal(5, p2.Hand.Count);
        Assert.Equal(0, p2.RoundTurnCount);

        svc.DrawCard(room.RoomId, "p1");
        Assert.Equal(6, p1.Hand.Count);
        Assert.Equal(1, p1.RoundTurnCount);
        Assert.Equal(42, room.DeckCount);

        svc.DrawCard(room.RoomId, "p1");           // 6장이면 더 못 뽑음
        Assert.Equal(6, p1.Hand.Count);
        Assert.Equal(1, p1.RoundTurnCount);
        Assert.Equal("p1", room.CurrentTurnPlayerId);
    }

    [Fact]
    public void DrawCard_덱이_비면_버림더미를_자동으로_섞어_넣고_바닥카드는_남긴다()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        room.Deck.Clear();
        room.DiscardPile.AddRange(Hand("2", "3", "4"));
        room.LastDiscardedCard = room.DiscardPile[^1];

        svc.DrawCard(room.RoomId, "p1");

        Assert.Equal(6, p1.Hand.Count);
        Assert.Equal(1, room.DeckCount);                       // 2,3 중 1장 뽑고 1장 남음
        Assert.Single(room.DiscardPile);
        Assert.Equal("4", room.LastDiscardedCard!.Rank);       // 바닥 카드 유지
        Assert.Contains(p1.Hand, c => c.Rank == "2" || c.Rank == "3");
    }

    [Fact]
    public void DrawCard_되돌릴_카드가_없으면_뽑지_못함()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        room.Deck.Clear();
        room.DiscardPile.Add(C("4"));                          // 바닥 1장뿐 → 되돌릴 게 없음
        svc.DrawCard(room.RoomId, "p1");
        Assert.Equal(5, p1.Hand.Count);
        Assert.Equal(0, p1.RoundTurnCount);
    }

    [Fact]
    public void DrawCard_뻥대기시간_중에도_뽑을수있고_그_뽑기는_기록된다()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        room.PungWindowFlag = 7;                               // 허브가 연 대기 시간 토큰
        svc.DrawCard(room.RoomId, "p1");
        Assert.Equal(6, p1.Hand.Count);
        Assert.NotNull(room.PungWindowDraw);
        Assert.Equal(7, room.PungWindowDraw!.Token);
        Assert.Equal("p1", room.PungWindowDraw.PlayerId);
        Assert.Same(p1.Hand[^1], room.PungWindowDraw.Card);
    }

    [Fact]
    public void DrawCard_뻥대기시간_밖의_뽑기는_기록하지_않음()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        svc.DrawCard(room.RoomId, "p1");
        Assert.Null(room.PungWindowDraw);
    }

    [Fact]
    public void RevertPungWindowDraw_뽑았던_카드를_손에서_빼_덱에_섞어_넣는다()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        int turnsBefore = p1.RoundTurnCount;
        int deckBefore = room.Deck.Count;
        room.PungWindowFlag = 7;
        svc.DrawCard(room.RoomId, "p1");
        var drawn = room.PungWindowDraw!.Card;

        var reverted = svc.RevertPungWindowDraw(room, 7);

        Assert.NotNull(reverted);
        Assert.Same(drawn, reverted!.Card);
        Assert.Equal(5, p1.Hand.Count);
        Assert.DoesNotContain(drawn, p1.Hand);
        Assert.Equal(deckBefore, room.Deck.Count);
        Assert.Contains(drawn, room.Deck);
        Assert.Equal(turnsBefore, p1.RoundTurnCount);
        Assert.Null(room.PungWindowDraw);
    }

    [Fact]
    public void RevertPungWindowDraw_다른_대기시간의_기록이면_되돌리지_않음()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        room.PungWindowFlag = 7;
        svc.DrawCard(room.RoomId, "p1");

        Assert.Null(svc.RevertPungWindowDraw(room, 8));
        Assert.Equal(6, p1.Hand.Count);
        Assert.NotNull(room.PungWindowDraw);                   // 기록은 그대로
    }

    [Fact]
    public void PlayCard_버림처리만_하고_턴은_넘기지_않는다()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p1.Hand = Hand("K", "Q", "J", "10", "9", "8");
        p2.Hand = Hand("2", "3", "4", "5", "6");   // 바가지 불가 손패

        Assert.False(svc.PlayCard(room.RoomId, "p1", C("A")));   // 손에 없는 카드 → 거부, 상태 변경 없음
        Assert.Equal(6, p1.Hand.Count);
        Assert.Empty(room.DiscardPile);
        Assert.False(svc.PlayCard(room.RoomId, "p2", C("2")));   // 남의 턴 → 거부
        Assert.True(svc.PlayCard(room.RoomId, "p1", C("K")));

        Assert.Equal(5, p1.Hand.Count);
        Assert.DoesNotContain(p1.Hand, c => c.Rank == "K");
        Assert.Single(room.DiscardPile);
        Assert.Equal("K", room.LastDiscardedCard!.Rank);
        Assert.Equal("p1", room.LastActorPlayerId);
        Assert.Equal("p1", room.CurrentTurnPlayerId);  // 턴 이동은 허브 책임
        Assert.False(room.IsRoundEnded);
    }

    [Fact]
    public void PlayCard_남의턴이거나_없는카드면_무시()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        p1.Hand = Hand("K", "Q", "J", "10", "9", "8");
        svc.PlayCard(room.RoomId, "p2", C("K"));
        svc.PlayCard(room.RoomId, "p1", C("A"));
        Assert.Equal(6, p1.Hand.Count);
        Assert.Empty(room.DiscardPile);
    }

    [Fact]
    public void PlayCard_바가지면_즉시_라운드종료_점수반영()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p1.Hand = Hand("8", "Q", "J", "10", "9", "7");
        p2.Hand = Hand("8", "8");

        svc.PlayCard(room.RoomId, "p1", C("8"));

        Assert.True(room.IsRoundEnded);
        Assert.Equal("p2", room.WinnerPlayerId);
        Assert.Contains("바가지", room.WinnerName);
        Assert.Equal(0, p2.Score);
        Assert.Equal(12 + 11 + 10 + 9 + 7 + 30, p1.Score);  // 남은 패 합 + 독박 30
        Assert.Equal(p1.Score, p1.TotalScore);
        Assert.Single(room.RoundScoreHistory);
    }

    [Fact]
    public void DeclareStop_조건과_STOP성공_정산()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p1.Hand = Hand("2", "3", "K", "Q", "J");
        p2.Hand = Hand("9", "10", "J", "Q", "K");

        svc.DeclareStop(room.RoomId, "p1");      // 5장 → 불가
        Assert.False(room.IsStopDeclared);

        p1.Hand = Hand("2", "3", "K");             // 뽑아서 3장
        svc.DeclareStop(room.RoomId, "p2");      // 남의 턴 → 불가
        Assert.False(room.IsStopDeclared);
        svc.DeclareStop(room.RoomId, "p1");      // 2장인 상대가 없음 → 불가
        Assert.False(room.IsStopDeclared);

        p2.Hand = Hand("9", "10");                 // 2장인 상대 생김
        p1.Hand = Hand("2", "3", "K", "Q", "J", "10");
        svc.DeclareStop(room.RoomId, "p1");      // 6장 → 불가 (3장일 때만)
        Assert.False(room.IsStopDeclared);

        p1.Hand = Hand("2", "3", "K");
        svc.DeclareStop(room.RoomId, "p1");
        Assert.True(room.IsStopDeclared);
        Assert.Equal("p1", room.StopCallerId);

        svc.PlayCard(room.RoomId, "p1", C("K"));   // 남은 2,3 = 5 < 상대 19 → 성공

        Assert.True(room.IsRoundEnded);
        Assert.Equal("p1", room.WinnerPlayerId);
        Assert.Contains("STOP 성공", room.WinnerName);
        Assert.Equal("STOP 성공 (0점)", room.LastWinType);
        Assert.Equal(0, p1.Score);
        Assert.Equal(19, p2.Score);
        Assert.False(room.IsStopDeclared);
        Assert.False(room.IsFinished);           // 1/3 라운드
    }

    [Fact]
    public void DeclareStop_2장_상대보다_점수가_높으면_독박()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 1);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p1.Hand = Hand("K", "Q", "J");            // 버린 뒤 K,Q = 25
        p2.Hand = Hand("2", "3");                 // 5

        svc.DeclareStop(room.RoomId, "p1");
        Assert.True(room.IsStopDeclared);
        svc.PlayCard(room.RoomId, "p1", C("J"));

        Assert.Contains("STOP 실패", room.WinnerName);
        Assert.Equal(25 + 50, p1.Score);
        Assert.Equal(5, p2.Score);

        // 마지막 라운드 → 게임 종료 + 총점 최저(p2)가 게임 승자
        Assert.True(room.IsFinished);
        Assert.Equal(new[] { "p2" }, room.GameWinnerIds);
        Assert.Equal("p2", room.GameWinnerName);
    }

    [Fact]
    public void StartNextRound_라운드증가_승자가_선()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p1.Hand = Hand("8", "Q", "J", "10", "9", "7");
        p2.Hand = Hand("8", "8");
        svc.PlayCard(room.RoomId, "p1", C("8"));   // p2 바가지 승
        int p1Total = p1.TotalScore;

        svc.StartNextRound(room);

        Assert.Equal(2, room.CurrentRound);
        Assert.False(room.IsRoundEnded);
        Assert.Equal("p2", room.CurrentTurnPlayerId);
        Assert.All(room.Players, p => Assert.Equal(5, p.Hand.Count));
        Assert.All(room.Players, p => Assert.Equal(0, p.Score));
        Assert.Equal(p1Total, p1.TotalScore);        // 누적은 유지
        Assert.Equal(43, room.DeckCount);
        Assert.Empty(room.DiscardPile);
    }

    [Fact]
    public void StartNextRound_라운드가_안끝났으면_무시()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        svc.StartNextRound(room);
        Assert.Equal(1, room.CurrentRound);
    }

    [Fact]
    public void StartNextRound_승자가_결과창_중에_나갔으면_방장이_선()
    {
        var (svc, room) = Rooms.Cards(3);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        p1.Hand = Hand("5", "5", "5", "9", "9", "9");
        Assert.True(svc.DeclareWin(room, p1, WinReason.ManualDeclare));   // p1 승 → 다음 라운드 선 후보
        svc.LeaveRoom(room.RoomId, "p1");                                  // 결과창 중 퇴장 → 방장 p2 승계

        svc.StartNextRound(room);

        Assert.Equal(2, room.CurrentRound);
        Assert.Equal("p2", room.HostPlayerId);
        Assert.Equal("p2", room.CurrentTurnPlayerId);                      // 유령 ID가 아닌 방장이 선
        Assert.Contains(room.Players, p => p.PlayerId == room.CurrentTurnPlayerId);
    }

    [Fact]
    public void DeclareWin_족보없으면_거부_있으면_정산()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p2.Hand = Hand("K", "Q", "2", "3", "4");  // 34

        p1.Hand = Hand("2", "5", "7", "9", "J", "K");
        Assert.False(svc.DeclareWin(room, p1, WinReason.ManualDeclare));
        Assert.False(room.IsRoundEnded);

        p1.Hand = Hand("5", "5", "5", "9", "9", "9");
        Assert.True(svc.DeclareWin(room, p1, WinReason.ManualDeclare));
        Assert.True(room.IsRoundEnded);
        Assert.Equal("p1", room.WinnerPlayerId);
        Assert.Equal("3 + 3", room.LastWinType);
        Assert.Equal(6, room.WinnerHand.Count);
        Assert.Equal(0, p1.Score);
        Assert.Equal(34, p2.Score);
        Assert.Single(room.RoundScoreHistory);
        Assert.Equal(1, room.RoundScoreHistory[0].Round);
    }

    [Fact]
    public void DeclareWin_마지막라운드면_게임종료()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 1);
        var p1 = Rooms.P(room, "p1");
        p1.Hand = Hand("5", "5", "5", "9", "9", "9");
        Assert.True(svc.DeclareWin(room, p1, WinReason.ManualDeclare));
        Assert.True(room.IsFinished);
        Assert.False(room.IsStarted);
    }

    [Fact]
    public void CanDeclareWin_내턴이고_턴카운트_2이상()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        Assert.False(svc.CanDeclareWin(room, p1));
        svc.DrawCard(room.RoomId, "p1");            // 턴카운트 1 → 아직 불가
        Assert.False(svc.CanDeclareWin(room, p1));
        p1.RoundTurnCount = 2;
        Assert.True(svc.CanDeclareWin(room, p1));
        p2.RoundTurnCount = 2;
        Assert.False(svc.CanDeclareWin(room, p2));  // 남의 턴
    }

    [Fact]
    public void StartGame_1명이면_예외()
    {
        var (svc, room) = Rooms.Cards(1);
        var ex = Assert.Throws<InvalidOperationException>(() => svc.StartGame(room.RoomId, 3));
        Assert.Contains("2명", ex.Message);
        Assert.False(room.IsStarted);
    }

    [Fact]
    public void StartGame_조커는_Type이_Joker()
    {
        var (svc, room) = Rooms.Cards(2, jokerCount: 2);
        svc.StartGame(room.RoomId, 3);
        var all = room.Deck.Concat(room.Players.SelectMany(p => p.Hand)).ToList();
        Assert.Equal(2, all.Count(c => c.IsJoker));
        Assert.All(all.Where(c => c.IsJoker), c => Assert.Equal("Joker", c.Rank));
        Assert.All(all.Where(c => !c.IsJoker), c => Assert.NotEqual("Joker", c.Rank));
    }

    [Fact]
    public void 게임종료시_총점최저가_최종승자_라운드승자와_다를수있음()
    {
        var (svc, room) = Rooms.Cards(3);
        svc.StartGame(room.RoomId, 1);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        var p3 = Rooms.P(room, "p3");
        p1.Hand = Hand("5", "5", "5", "9", "9", "9");   // 3+3 → 0점
        p2.Hand = Hand("K", "Q", "2", "3", "4");        // 34
        p3.Hand = Hand("A", "2", "3", "4", "5");        // 15
        p1.TotalScore = 40;                             // 이전 누적(조작) → 최종 40 / 34 / 15

        Assert.True(svc.DeclareWin(room, p1, WinReason.ManualDeclare));

        Assert.True(room.IsFinished);
        Assert.Equal("p1", room.WinnerPlayerId);            // 라운드 승자
        Assert.Equal(new[] { "p3" }, room.GameWinnerIds);   // 게임 승자 = 총점 최저
        Assert.Equal("p3", room.GameWinnerName);

        // 새 게임 시작 시 초기화
        room.StatsRecorded = true;
        svc.StartGame(room.RoomId, 1);
        Assert.Empty(room.GameWinnerIds);
        Assert.Null(room.GameWinnerName);
        Assert.False(room.StatsRecorded);
    }

    [Fact]
    public void 게임종료시_동점이면_전원_최종승자()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 1);
        var p1 = Rooms.P(room, "p1");
        var p2 = Rooms.P(room, "p2");
        p1.Hand = Hand("5", "5", "5", "9", "9", "9");   // 0점
        p2.Hand = Hand("K", "Q", "2", "3", "4");        // 34
        p2.TotalScore = -34;                            // 이전 누적(조작) → 둘 다 0점

        Assert.True(svc.DeclareWin(room, p1, WinReason.ManualDeclare));

        Assert.Equal(new[] { "p1", "p2" }, room.GameWinnerIds);
        Assert.Equal("p1, p2", room.GameWinnerName);
    }

    [Fact]
    public void GiveUpGame_기권자_제외하고_총점최저가_최종승자()
    {
        var (svc, room) = Rooms.Cards(3);
        svc.StartGame(room.RoomId, 3);
        Rooms.P(room, "p1").TotalScore = 10;
        Rooms.P(room, "p2").TotalScore = 0;
        Rooms.P(room, "p3").TotalScore = 20;

        svc.GiveUpGame(room.RoomId, "p2");              // 최저점이지만 기권

        Assert.True(room.IsFinished);
        Assert.Equal(new[] { "p1" }, room.GameWinnerIds);
        Assert.Equal("p1", room.GameWinnerName);
    }

    [Fact]
    public void LeaveRoom_방장승계와_빈방삭제()
    {
        var (svc, room) = Rooms.Cards(2);
        Assert.False(svc.LeaveRoom(room.RoomId, "p1"));
        Assert.Equal("p2", room.HostPlayerId);
        Assert.True(svc.LeaveRoom(room.RoomId, "p2"));
        Assert.Null(svc.GetRoom(room.RoomId));
    }

    [Fact]
    public void GiveUpGame_종료플래그와_승자명()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        svc.GiveUpGame(room.RoomId, "p2");
        Assert.True(room.IsFinished);
        Assert.Equal("p2 (기권)", room.WinnerName);
    }

    [Fact]
    public void ReshuffleDiscardPile_방장만_덱이_비었을때만_바닥카드는_남김()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        room.DiscardPile.AddRange(Hand("2", "3", "4"));
        room.LastDiscardedCard = room.DiscardPile[^1];
        Assert.False(svc.ReshuffleDiscardPile(room.RoomId, "p1"));   // 덱이 아직 있음
        room.Deck.Clear();
        Assert.False(svc.ReshuffleDiscardPile(room.RoomId, "p2"));   // 방장 아님
        Assert.True(svc.ReshuffleDiscardPile(room.RoomId, "p1"));
        Assert.Equal(2, room.DeckCount);                              // 2,3 → 덱
        Assert.Single(room.DiscardPile);                              // 4는 바닥에 남음
        Assert.Equal("4", room.LastDiscardedCard!.Rank);
        Assert.False(svc.ReshuffleDiscardPile(room.RoomId, "p1"));   // 되돌릴 게 없음
    }

    [Fact]
    public void AddBot_시작전_7명까지()
    {
        var (svc, room) = Rooms.Cards(6);
        Assert.True(svc.AddBot(room.RoomId));
        Assert.False(svc.AddBot(room.RoomId));
        Assert.True(room.Players[^1].IsBot);
        Assert.StartsWith("bot:", room.Players[^1].PlayerId);
        var (svc2, room2) = Rooms.Cards(2);
        svc2.StartGame(room2.RoomId, 1);
        Assert.False(svc2.AddBot(room2.RoomId));
    }
}
