using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Card.Api.Services;

// SignalR UserIdentifier = JWT Name 클레임(닉네임).
// PlayerId가 닉네임 기반 안정 ID로 바뀌면서, Clients.User(닉네임)으로
// 재접속(ConnectionId 변경)과 무관하게 특정 플레이어에게 전송할 수 있다.
public class NameUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?
            .FindFirst(ClaimTypes.Name)?.Value;
    }
}
