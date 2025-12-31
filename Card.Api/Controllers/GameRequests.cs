namespace Card.Api.Controllers;

public class CreateRoomRequest
{
    public string PlayerName { get; set; } = "";
}

public class JoinRoomRequest
{
    public string RoomId { get; set; } = "";
    public string PlayerName { get; set; } = "";
}