using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QAAnalyzer.AppErrorLogsAnalyzer
{
    public class LogAnalyzerPlugin
    {
        private readonly string _logDirectory;
        private readonly int _nooferrorlogsfiles;

        public LogAnalyzerPlugin(IConfiguration configuration)
        {
            _logDirectory = configuration["apperrorlogsdirectory"];
            _nooferrorlogsfiles = int.TryParse(configuration["nooferrorlogsfiles"], out var n) ? n : 2;
        }


        /// <summary>
        /// Reads and parses all multi-line log files in the configured directory.
        /// </summary>
        /// <param name="errorId">Optional ErrorID to filter logs.</param>
        /// <returns>A list of parsed LogEntry models.</returns>
        private List<LogEntry> ReadErrorLogs(string errorId = null)
        {
            var logEntries = new List<LogEntry>();

            // Get all *.log files and Error.Harmony.log.1/2 files
            var logFiles = Directory.GetFiles(_logDirectory, "Error.Harmony.log").ToList();


            var harmonyFiles = Enumerable.Range(1, _nooferrorlogsfiles)
                .Select(i => Path.Combine(_logDirectory, $"Error.Harmony.log.{i}"))
                .Where(File.Exists);

            logFiles.AddRange(harmonyFiles);

            // Order by last write time descending and take only the 3 most recent files
            logFiles = logFiles
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();

            var logEntryPattern = new Regex(
                @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) \[(ERROR)\](.+?)(?=^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3} \[|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline
            );

            foreach (var file in logFiles)
            {
                string content;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }

                var matches = logEntryPattern.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        try
                        {
                            var timestampStr = match.Groups[1].Value;
                            var fullMessage = match.Groups[3].Value.Trim();

                            var entry = ParseLogEntry(timestampStr, fullMessage);

                            // Only add if ErrorID matches (if provided), or if collecting all
                            if (!string.IsNullOrEmpty(errorId))
                            {
                                if (entry.ErrorID != null && entry.ErrorID.Equals(errorId, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Populate Message only for matching ErrorID
                                    entry.Message = ExtractProperty(fullMessage, "Message");
                                    logEntries.Add(entry);
                                    break; // Only one entry per ErrorID
                                }
                            }
                            else
                            {
                                // Populate all, but do not include Message
                                entry.Message = null;
                                if (!string.IsNullOrEmpty(entry.ErrorID))
                                    logEntries.Add(entry);
                                
                            }
                        }
                        catch (FormatException)
                        {
                            continue;
                        }
                    }
                }
            }

            return logEntries.OrderBy(e => e.Timestamp).ToList();
        }

        // Helper to parse log entry properties
        private LogEntry ParseLogEntry(string timestampStr, string fullMessage)
        {
            var entry = new LogEntry();

            if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss,fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                entry.Timestamp = timestamp;

            entry.ErrorID = ExtractProperty(fullMessage, "ErrorID");
            entry.Source = ExtractProperty(fullMessage, "Source");
            entry.SERVER = ExtractProperty(fullMessage, "SERVER");
            entry.DESCRIPTION = ExtractProperty(fullMessage, "DESCRIPTION");
            entry.Type = ExtractProperty(fullMessage, "Type");
            // Message is handled in ReadErrorLogs based on ErrorID presence

            return entry;
        }

        // Extracts a property value from the log message
        private string ExtractProperty(string message, string property)
        {
            //if (property == "Message")
            //{
            //    var match = Regex.Match(message, @"Message=(.*)", RegexOptions.Singleline);
            //    return match.Success ? match.Groups[1].Value.Trim() : null;
            //}
            //else
            //{
                var match = Regex.Match(message, $@"{property}=([^\r\n]+)");
                return match.Success ? match.Groups[1].Value.Trim() : null;
            //}
        }

        /// <summary>
        /// Retrieves a list of error log entries. If ErrorID is provided, returns only the matching log entry with full message; 
        /// if ErrorID is null, returns all log entries without the message property populated.
        /// </summary>
        /// <param name="errorId">Optional ErrorID to filter logs. If null, returns all error logs.</param>
        /// <returns>List of LogEntry models matching the criteria.</returns>
        [KernelFunction("GetErrorSummaryLogs")]
        [Description("Returns a summary of error descriptions and their occurrence counts. If DESCRIPTION is present, groups by DESCRIPTION; otherwise, groups by Type.")]
        public List<ErrorDescriptionSummary> GetErrorSummaryLogs(string errorId = null)
        {
            if (!string.IsNullOrEmpty(errorId) && errorId.Equals("null", StringComparison.OrdinalIgnoreCase))
                errorId = null;

            var logEntries = ReadErrorLogs(errorId);

            var summary = logEntries
                .GroupBy(e => string.IsNullOrWhiteSpace(e.DESCRIPTION) ? e.Type : e.DESCRIPTION)
                .Select(g => new ErrorDescriptionSummary
                {
                    ErrorDescription = g.Key,
                    OccurrenceCount = g.Count()
                })
                .OrderByDescending(s => s.OccurrenceCount)
                .ToList();

            return summary;
        }

        /// <summary>
        /// Returns all error log entries that match the given error description. 
        /// If DESCRIPTION is present, filters by DESCRIPTION; otherwise, filters by Type.
        /// </summary>
        /// <param name="errorDescription">Error description or type to filter log entries.</param>
        /// <returns>List of LogEntry models matching the description or type.</returns>
        [KernelFunction("GetErrorLogsByDescription")]
        [Description("Returns all error log entries that match the given error description. If DESCRIPTION is present, filters by DESCRIPTION; otherwise, filters by Type.")]
        public List<LogEntry> GetErrorLogsByDescription(string errorDescription)
        {
            var logEntries = ReadErrorLogs();

            var filtered = logEntries
                .Where(e =>
                    (!string.IsNullOrWhiteSpace(e.DESCRIPTION) && e.DESCRIPTION.Equals(errorDescription, StringComparison.OrdinalIgnoreCase)) ||
                    (string.IsNullOrWhiteSpace(e.DESCRIPTION) && !string.IsNullOrWhiteSpace(e.Type) && e.Type.Equals(errorDescription, StringComparison.OrdinalIgnoreCase))
                )
                .ToList();

            return filtered;
        }

        /// <summary>
        /// Returns all error log entries that match the given ErrorID (case-insensitive).
        /// </summary>
        /// <param name="errorId">ErrorID to filter log entries.</param>
        /// <returns>List of LogEntry models matching the ErrorID.</returns>
        [KernelFunction("GetErrorLogsByErrorID")]
        [Description("Returns all error log entries that match the given ErrorID (case-insensitive).")]
        public List<LogEntry> GetErrorLogsByErrorID(string errorId)
        {
            if (string.IsNullOrWhiteSpace(errorId))
                return new List<LogEntry>();

           return ReadErrorLogs(errorId);

        }

        /// <summary>
        /// Returns all error log entries within the specified date range, 
        /// including entries 2 minutes before startDate and 2 minutes after endDate.
        /// </summary>
        /// <param name="startDate">Start date (inclusive), mandatory.</param>
        /// <param name="endDate">End date (inclusive), mandatory.</param>
        /// <returns>List of LogEntry models within the buffered date range.</returns>
        [KernelFunction("GetErrorLogsByDateRange")]
        [Description("Returns all error log entries within the specified date range, including a 2-minute buffer before startDate and after endDate.")]
        public List<LogEntry> GetErrorLogsByDateRange(DateTime startDate, DateTime endDate)
        {
            // Apply 2-minute buffer
            var bufferedStart = startDate.AddMinutes(-2);
            var bufferedEnd = endDate.AddMinutes(2);

            var logEntries = ReadErrorLogs();

            var filtered = logEntries
                .Where(e => e.Timestamp.HasValue &&
                            e.Timestamp.Value >= bufferedStart &&
                            e.Timestamp.Value <= bufferedEnd)
                .ToList();

            return filtered;
        }

        /// <summary>
        /// Scans logs for entries with an ErrorID and extracts the error, and root cause.
        /// </summary>
        /// <returns>List of ErrorRootCauseInfo models with ErrorID, Error, and RootCause.</returns>
        [KernelFunction("GetErrorsWithRootCause")]
        [Description("Scans logs for entries with an ErrorID and extracts the error, and root cause.")]
        public List<ErrorRootCauseInfo> GetErrorsWithRootCause()
        {
            var errorInfos = new List<ErrorRootCauseInfo>();
            var logFiles = Directory.GetFiles(_logDirectory, "Error.Harmony.log").ToList();

            var harmonyFiles = Enumerable.Range(1, _nooferrorlogsfiles)
                .Select(i => Path.Combine(_logDirectory, $"Error.Harmony.log.{i}"))
                .Where(File.Exists);

            logFiles.AddRange(harmonyFiles);

            logFiles = logFiles
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();

            var logEntryPattern = new Regex(
                @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) \[(ERROR)\](.+?)(?=^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3} \[|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline
            );

            foreach (var file in logFiles)
            {
                string content;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }

                var matches = logEntryPattern.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        var fullMessage = match.Groups[3].Value.Trim();
                        var errorId = ExtractProperty(fullMessage, "ErrorID");

                        if (!string.IsNullOrEmpty(errorId))
                        {
                            var error = ExtractProperty(fullMessage, "Message");
                            var rootCause = ExtractRootCause(fullMessage);

                            errorInfos.Add(new ErrorRootCauseInfo
                            {
                                ErrorID = errorId,
                                Error = error,
                                RootCause = rootCause
                            });
                        }
                    }
                }
            }

            return errorInfos;
        }

        private string ExtractRootCause(string message)
        {
            var match = Regex.Match(message, @"Root Cause:([\s\S]*)", RegexOptions.Multiline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return null;
        }
    }
}