using Card.Api.Domain;
using Card.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Card.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController : ControllerBase
{
    private readonly GameRoomService _gameRoomService;

    public GameController(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
    }

    // 방 생성
    [HttpPost("create")]
    public ActionResult<GameRoom> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.PlayerName) || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("PlayerName and Title are required");

        var room = _gameRoomService.CreateRoom(
            request.PlayerName,
            request.Title,
            request.Password
        );
        return Ok(room);
    }

    // 방 입장 (HTTP 방식)
    [HttpPost("join")]
    public ActionResult<GameRoom> JoinRoom([FromBody] JoinRoomRequest request)
    {
        // Service의 JoinRoom은 이제 닉네임 중복 시 기존 유저를 지우고 새로 추가하므로 
        // 결과가 null이 나오는 경우는 방이 없을 때뿐입니다.
        var room = _gameRoomService.JoinRoom(request.RoomId, "HTTP_CLIENT", request.PlayerName, request.Password);

        if (room == null)
            return NotFound("Room not found");
        
        return Ok(room);
    }

    // 특정 방 상세 조회
    [HttpGet("{roomId}")]
    public ActionResult<GameRoom> GetRoom(string roomId)
    {
        var room = _gameRoomService.GetRoom(roomId);
        if(room == null)
            return NotFound("Room not found");

        return Ok(room);
    }

    // 전체 방 목록 조회
    [HttpGet("rooms")]
    public ActionResult<IEnumerable<GameRoom>> GetRooms()
    {
        return Ok(_gameRoomService.GetRooms());
    }
}