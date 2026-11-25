using Card.Api.Domain;
using Card.Api.Services;

namespace Card.Api.Tests;

// 접속 추적(연결 수·지연 퇴장) 테스트
public class PresenceTests
{
    [Fact]
    public async Task 마지막_연결이_끊기고_유예가_지나면_onLeave_호출()
    {
        var svc = new GameRoomService();
        int calls = 0;
        svc.RegisterConnection("p1");
        await svc.UnregisterConnection("p1", () => { calls++; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 유예_중_재접속하면_퇴장_취소()
    {
        var svc = new GameRoomService();
        int calls = 0;
        svc.RegisterConnection("p1");
        var pending = svc.UnregisterConnection("p1", () => { calls++; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(300));
        svc.RegisterConnection("p1");                      // 새로고침으로 돌아옴
        await pending;
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task 다른_탭_연결이_남아있으면_퇴장_예약_안함()
    {
        var svc = new GameRoomService();
        int calls = 0;
        svc.RegisterConnection("p1");
        svc.RegisterConnection("p1");                      // 탭 2개
        await svc.UnregisterConnection("p1", () => { calls++; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50));
        await Task.Delay(120);
        Assert.Equal(0, calls);
    }
}

// 진행 중 게임에서 나갔을 때 서비스의 정리 동작
public class LeaveRoomInGameTests
{
    [Fact]
    public void 진행중_현재턴_플레이어가_나가면_다음_사람에게_턴()
    {
        var (svc, room) = Rooms.Cards(3);
        svc.StartGame(room.RoomId, 3);
        room.CurrentTurnPlayerId = "p2";

        Assert.False(svc.LeaveRoom(room.RoomId, "p2"));

        Assert.Equal("p3", room.CurrentTurnPlayerId);
        Assert.Equal(1, Rooms.P(room, "p3").RoundTurnCount);
        Assert.True(room.IsStarted);
        Assert.False(room.IsFinished);
    }

    [Fact]
    public void 진행중_마지막_순서_플레이어가_나가면_첫_사람에게_턴()
    {
        var (svc, room) = Rooms.Cards(3);
        svc.StartGame(room.RoomId, 3);
        room.CurrentTurnPlayerId = "p3";
        svc.LeaveRoom(room.RoomId, "p3");
        Assert.Equal("p1", room.CurrentTurnPlayerId);
    }

    [Fact]
    public void 진행중_남의턴에_나가면_턴_유지()
    {
        var (svc, room) = Rooms.Cards(3);
        svc.StartGame(room.RoomId, 3);
        svc.LeaveRoom(room.RoomId, "p3");
        Assert.Equal("p1", room.CurrentTurnPlayerId);
        Assert.Equal(0, Rooms.P(room, "p1").RoundTurnCount);
    }

    [Fact]
    public void 진행중_남은_인원이_1명이면_게임종료_승자확정()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);
        Rooms.P(room, "p1").TotalScore = 30;

        Assert.False(svc.LeaveRoom(room.RoomId, "p2"));

        Assert.True(room.IsFinished);
        Assert.False(room.IsStarted);
        Assert.Equal("p2 (퇴장)", room.WinnerName);
        Assert.Equal(new[] { "p1" }, room.GameWinnerIds);
    }

    [Fact]
    public void 시작전_나가기는_방장_승계만()
    {
        var (svc, room) = Rooms.Cards(2);
        Assert.False(svc.LeaveRoom(room.RoomId, "p1"));
        Assert.Equal("p2", room.HostPlayerId);
        Assert.False(room.IsFinished);
        Assert.Null(room.WinnerName);
    }

    [Fact]
    public void GetRoomsOf_플레이어가_속한_방만()
    {
        var svc = new GameRoomService();
        var a = svc.CreateRoom("p1", "a", null); svc.JoinRoom(a.RoomId, "p1", "p1");
        var b = svc.CreateRoom("p2", "b", null); svc.JoinRoom(b.RoomId, "p2", "p2");
        Assert.Equal(new[] { a.RoomId }, svc.GetRoomsOf("p1").Select(r => r.RoomId));
        Assert.Empty(svc.GetRoomsOf("ghost"));
    }
}

// 진행 중인 방 입장 제한
public class JoinStartedRoomTests
{
    [Fact]
    public void 진행중_Cards방에_새_플레이어는_입장_불가_재입장은_가능()
    {
        var (svc, room) = Rooms.Cards(2);
        svc.StartGame(room.RoomId, 3);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.JoinRoom(room.RoomId, "p3", "p3"));
        Assert.Contains("진행 중", ex.Message);
        Assert.Equal(2, room.Players.Count);

        Assert.NotNull(svc.JoinRoom(room.RoomId, "p1", "p1"));   // 새로고침 재입장
        Assert.Equal(2, room.Players.Count);
    }

    [Fact]
    public void 진행중_오목방은_관전_입장_가능()
    {
        var rs = new GameRoomService();
        var room = rs.CreateRoom("p1", "t", null, GameMode.Omok);
        rs.JoinRoom(room.RoomId, "p1", "p1");
        rs.JoinRoom(room.RoomId, "p2", "p2");
        Assert.True(new OmokGameService(rs).StartOmokGame(room.RoomId));

        Assert.NotNull(rs.JoinRoom(room.RoomId, "p3", "p3"));
        Assert.Equal(3, room.Players.Count);
    }
}
