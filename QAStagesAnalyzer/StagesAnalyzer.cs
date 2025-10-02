using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace QAAnalyzer.QAStagesAnalyzer
{
    public class StagesAnalyzer
    {
        private readonly string _logDirectory;
        private readonly int _takeLast;
        public StagesAnalyzer(IConfiguration configuration)
        {

            _logDirectory = configuration["stageslogsdirectory"];
            _takeLast = int.TryParse(configuration["nooflastruns"], out var n) ? n : 3;
        }

        /// <summary>
        /// Extracts scenario execution summaries from all log files in the configured directory.
        /// </summary>
        [KernelFunction("ExtractScenarioSummaries")]
        [Description("Scans all log files and returns a structured summary of scenario execution results, including stage name, datetime, passed, failed, and total scenarios.")]
        public List<ScenarioSummary> ExtractScenarioSummaries()
        {

            var summaries = new List<ScenarioSummary>();
            try
            {
                List<(string FilePath, string StageName, string HHMM)> logFiles = GetLastLogFilesByDate(_logDirectory);

                // var pattern = @"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\s+(?<total>\d+)\s+scenarios\s+\((?:(?<failed>\d+)\s+failed,\s+)?(?<passed>\d+)\s+passed\)";
               // var pattern = @"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\s+(?<total>\d+)\s+scenarios\s+\((?:(?<failed>\d+)\s+failed(?:,\s*)?)?(?:(?<passed>\d+)\s+passed)?\)";
                var pattern = @"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\s+(?<total>\d+)\s+scenario[s]?\s+\((?:(?<failed>\d+)\s+failed(?:,\s*)?)?(?:(?<passed>\d+)\s+passed)?\)";

                var regex = new Regex(pattern);

                foreach (var lfile in logFiles)
                {
                    foreach (var line in File.ReadLines(lfile.FilePath))
                    {
                        var match = regex.Match(line);
                        if (match.Success)
                        {
                            summaries.Add(new ScenarioSummary
                            {
                                StageName = lfile.StageName,
                                Datetime = match.Groups["datetime"].Value,
                                Passed = match.Groups["passed"].Success ? int.Parse(match.Groups["passed"].Value) : 0,
                                Failed = match.Groups["failed"].Success ? int.Parse(match.Groups["failed"].Value) : 0,
                                Total = int.Parse(match.Groups["total"].Value)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
                var errorMessage = $"{DateTime.UtcNow:O} - Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                 File.AppendAllText(errorLogPath, errorMessage);
                throw new Exception();
               
            }
            return summaries;
        }

        /// <summary>
        /// Returns the last N files from the directory, sorted by the date in the filename (ascending).
        /// Filename format: Playwright (stagename@yyymmdd) EVV Regression Suite@@20250917
        /// </summary>
        /// <param name="directory">Directory to search for files.</param>
        /// <param name="takeLast">Number of files to return.</param>
        /// <returns>List of file paths for the last N files in ascending order by date.</returns>
        private List<(string FilePath, string StageName, string HHMM)> GetLastLogFilesByDate(string directory)
        {
            var pattern = @"^(?<stageName>.+)@@(?<date>\d{8})(?<hhmm>\d{4})(?<ss>\d{2})$";
            var regex = new Regex(pattern);

            var files = Directory.GetFiles(directory);
            var datedFiles = new List<(string FilePath, string StageName, DateTime Date, string HHMM, string SS)>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var match = regex.Match(fileName);
                if (match.Success)
                {
                    var dateStr = match.Groups["date"].Value;
                    var hhmm = match.Groups["hhmm"].Value;
                    var ss = match.Groups["ss"].Value;
                    var stageName = match.Groups["stageName"].Value;
                    if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        datedFiles.Add((file, stageName, date, hhmm, ss));
                    }
                }
            }

            // Sort by date, hhmm, and ss descending, then take last N per stage
            var lastFiles = datedFiles
                .GroupBy(f => f.StageName)
                .SelectMany(g => g
                    .OrderByDescending(f => f.Date)
                    .ThenByDescending(f => f.HHMM)
                    .ThenByDescending(f => f.SS)
                    .Take(_takeLast))
                .Select(f => (f.FilePath, f.StageName, $"{f.HHMM}{f.SS}"))
                .ToList();

            return lastFiles;
        }

        /// <summary>
        /// Scans the last log file for the specified stage and returns a list of failed scenario descriptions.
        /// </summary>
        [KernelFunction("GetFailedScenarios")]
        [Description("Scans the last log file for the given stage name and returns a list of failed scenario descriptions.")]
        public List<FailedScenarioInfo> GetFailedScenarios(string stageName)
        {
            var failedScenarios = new List<FailedScenarioInfo>();
            try
            {
                var logFiles = GetLastLogFilesByDate(_logDirectory)
                    .Where(f => f.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!logFiles.Any())
                    return failedScenarios;

                var lastLogFile = logFiles.First().FilePath;
                string currentDatetime = null;
                string currentScenario = null;
                bool inFailureSection = false;

                var failureRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s+Failures:");
                var warningRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\s+Warnings:");
                var scenarioRegex = new Regex(@"^(?<datetime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z).*\bScenario:\s+(?<scenario>.+)$");

                var lines = File.ReadLines(lastLogFile).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];

                    if (warningRegex.IsMatch(line))
                        break;

                    if (failureRegex.IsMatch(line))
                    {
                        inFailureSection = true;
                        continue;
                    }

                    if (!inFailureSection)
                        continue;

                    var scenarioMatch = scenarioRegex.Match(line);
                    if (scenarioMatch.Success)
                    {
                        currentDatetime = scenarioMatch.Groups["datetime"].Value;
                        currentScenario = scenarioMatch.Groups["scenario"].Value;
                        continue;
                    }

                    if (line.Contains("??") && currentScenario != null)
                    {
                        var errorDescription = new StringBuilder();
                        errorDescription.AppendLine(line.Trim());

                        // Read subsequent lines until warningRegex or next scenarioMatch
                        int j = i + 1;
                        for (; j < lines.Count; j++)
                        {
                            var nextLine = lines[j];
                            if (warningRegex.IsMatch(nextLine) || scenarioRegex.IsMatch(nextLine))
                                break;
                            if(string.IsNullOrEmpty(errorDescription.ToString()))
                                errorDescription.AppendLine(nextLine.Trim());
                        }
                        failedScenarios.Add(new FailedScenarioInfo
                        {
                            Datetime = currentDatetime,
                            ScenarioName = currentScenario,
                            ErrorDescription = errorDescription.ToString().TrimEnd()
                        });
                        currentScenario = null;
                        currentDatetime = null;
                        i = j - 1; // Continue from where we stopped
                    }
                }
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
                var errorMessage = $"{DateTime.UtcNow:O} - Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(errorLogPath, errorMessage);
                throw;
            }
            return failedScenarios;
        }

    }
}
