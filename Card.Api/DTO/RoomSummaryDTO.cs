public class RoomSummaryDTO
{
    public string RoomId { get; set; } = "";
    public string Title { get; set; } = "";
    public int PlayerCount { get; set; }
    public bool IsStarted { get; set; }
    public bool IsLocked { get; set; }
}