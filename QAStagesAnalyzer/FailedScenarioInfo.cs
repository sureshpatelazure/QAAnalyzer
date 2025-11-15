namespace QAAnalyzer.QAStagesAnalyzer
{
   public class FailedScenarioInfo
    {
        public string Datetime { get; set; }
        public string ScenarioName { get; set; }
        public string ErrorDescription { get; set; }
        public string RootCause { get; set; }
        public string ErrorCategory { get; set; }
        public string StackTrace { get; set; }
        public string FilePath { get; set; }
        public int? LineNumber { get; set; }
    }
}
