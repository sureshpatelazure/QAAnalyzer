namespace QAAnalyzer.AppErrorLogsAnalyzer
{
    public class LogEntry
    {
        public DateTime? Timestamp { get; set; }
        public string ErrorID { get; set; }
        public string Source { get; set; }
        public string SERVER { get; set; }
        public string DESCRIPTION { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
    }
}
