using System.Text.Json.Serialization;

namespace FlowzaMobile.Models;

/// <summary>
/// Read-only mirror of Admin.cs's AdminData. The phone app only ever reads control.json from the
/// same public GitHub repo the desktop app uses - editing (the Owner/Founder panel) stays desktop-only,
/// since that needs the GitHub write token, which has no business being on a phone that can be lost or stolen.
/// </summary>
public class AdminData
{
    public bool Maintenance { get; set; }
    public string MaintenanceText { get; set; } = "";
    public string Announcement { get; set; } = "";
    public string AnnouncementTitle { get; set; } = "";
    public string AnnouncementUntilUtc { get; set; } = "";
    public string BackAtUtc { get; set; } = "";
    public string MinVersion { get; set; } = "";
    public int PollMinutes { get; set; }
    public List<BanEntry> Banned { get; set; } = new();

    [JsonIgnore]
    public bool AnnouncementLive => Announcement.Length > 0 &&
        !(DateTimeOffset.TryParse(AnnouncementUntilUtc, out var t) && t <= DateTimeOffset.UtcNow);
}

public class BanEntry
{
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
    public string UntilUtc { get; set; } = "";
    public bool IsActive => !(DateTimeOffset.TryParse(UntilUtc, out var t) && t <= DateTimeOffset.UtcNow);
}
