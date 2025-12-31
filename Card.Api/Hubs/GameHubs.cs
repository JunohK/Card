using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;

namespace Card.Hubs
{
    public class GameHub : Hub
    {
        // 각 룸별 플레이어 리스트
        private static Dictionary<string, List<string>> RoomPlayers = new();

        // 플레이어가 룸에 입장할 때 호출되는 메서드
        public async Task JoinRoom(string roomId, string playerName)
        {
            // 룸이 없다면 새로 생성
            if(!RoomPlayers.ContainsKey(roomId))
                RoomPlayers[roomId] = new List<string>();

            // 중복 플레이어 이름이 없으면 리스트에 추가
            if(!RoomPlayers[roomId].Contains(playerName))
                RoomPlayers[roomId].Add(playerName);

            // signalR 그룹에 연결 추가(같은 룸에 있는 사람끼리 메시지 공유)
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            // 그룹 전체에 최신 플레이어 리스트 전송
            await Clients.Group(roomId).SendAsync("PlayerJoined",playerName);
        }

        // 플레이어가 룸을 떠날 때 호출되는 메서드
        public async Task LeaveRoom(string roomId, string playerName)
        {
            // 룸이 존재하면 플레이어 리스트에서 제거
            if (RoomPlayers.ContainsKey(roomId))
            {
                RoomPlayers[roomId].Remove(playerName);
            }

            // signalR 그룹에서 연결 제거
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

            // 그룹 전체에 최신 플레이어 리스트 전송(룸이 없다면 빈 리스트) 
            await Clients.Group(roomId).SendAsync(
                "UpdatePlayerList",
                RoomPlayers.ContainsKey(roomId) ? RoomPlayers[roomId] : new List<string>()
            );
        }
    }
}