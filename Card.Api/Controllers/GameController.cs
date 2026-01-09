using Card.Api.Domain;
using Card.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Card.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController : ControllerBase
{
    // 서버 연결 확인 코드
    // [HttpGet("ping")]
    // public IActionResult Ping()
    // {
    //     return Ok("Server Alive");
    // }

    private readonly GameRoomService _gameRoomService;

    public GameController(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
    }

    // 방 생성
    [HttpPost("create")]
    public ActionResult<GameRoom> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.PlayerName))
            return BadRequest("PlayerName is required");

        if(string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required");

        var room = _gameRoomService.CreateRoom(
            request.PlayerName,
            request.Title,
            request.Password
        );
        return Ok(room);
    }

    // 방 입장
    [HttpPost("Join")]
    public ActionResult<GameRoom> JoinRoom([FromBody] JoinRoomRequest request)
    {
        var room = _gameRoomService.JoinRoom(request.RoomId, request.PlayerName);

        if (room == null)
            return NotFound("Room not fount or duplicate player name");
        
        return Ok(room);
    }

    // 게임 상태 조회
    [HttpGet("{roomId}")]
    public ActionResult<GameRoom> GetRoom(string roomId)
    {
        var room = _gameRoomService.GetRoom(roomId);

        if(room == null)
            return NotFound("Room not fount");

        return Ok(room);
    }
}