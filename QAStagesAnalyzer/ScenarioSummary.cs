namespace QAAnalyzer.QAStagesAnalyzer
{
    public class ScenarioSummary
    {
        public string StageName { get; set; }
        public string Datetime { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
    }
}
