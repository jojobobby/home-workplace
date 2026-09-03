namespace HomeWorkplace.ContextApi;

public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    public int MaxMessagesPerRoom { get; set; } = 1000;
    public int MaxRooms { get; set; } = 200;
    public int MaxContentLength { get; set; } = 32768;
    public int MaxAgentIdLength { get; set; } = 128;
    public int MaxNameLength { get; set; } = 128;
    public int MaxGoalLength { get; set; } = 512;
    public int MaxWaitSeconds { get; set; } = 60;
    public int DefaultLimit { get; set; } = 200;
    public int MaxLimit { get; set; } = 500;
    public int FirehoseCapacity { get; set; } = 2000;
    public int MaxFilesPerRoom { get; set; } = 100;
    public int MaxFileBytes { get; set; } = 262144;
}
