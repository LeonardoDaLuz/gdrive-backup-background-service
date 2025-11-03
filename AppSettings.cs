public class AppSettings
{
    public bool Enabled { get; set; }
    public List<FileDirectoryOriginTarget>? FilesOrDirectory { get; set; }
    public GoogleDriveConfig? GoogleDrive { get; set; }
    public bool ForceToRunOnStartup { get; set; }
    public TimeConfig? StartsAt { get; set; }
    public int BackupIntervalDays { get; set; }
    public List<string>? SendEmailsTo { get; set; }
    public EmailService.EmailSettings? EmailSettings { get; set; }
    public class FileDirectoryOriginTarget
    {
        public string? Origin { get; set; }
        public string? TargetFolder { get; set; }
        public List<string> CommandsToCallBefore { get; set; } = new();
        public string? EmailBody { get; set; }
        public string? EmailTitle { get; set; }
    }
    public class TimeConfig
    {
        public int Hours { get; set; }
        public int Minutes { get; set; }
        public float timezone { get; set; }
    }


}
