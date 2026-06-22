namespace MiniPcApiSample.Models
{
    /// <summary>POST /StepApi/UpdateStep のリクエスト。</summary>
    public class UpdateStepRequest
    {
        public string SerialNo { get; set; }
        public int StepKey { get; set; }
    }

    /// <summary>POST /StepApi/RecordStep のリクエスト。</summary>
    public class RecordStepRequest
    {
        public string SerialNo { get; set; }
        public int StepKey { get; set; }

        /// <summary>"OK" または "NG"。</summary>
        public string Result { get; set; }
    }

    /// <summary>POST /MachineApi/Complete のリクエスト。</summary>
    public class CompleteRequest
    {
        public string SerialNo { get; set; }

        /// <summary>"OK" または "NG"。</summary>
        public string Result { get; set; }
    }

    /// <summary>POST /MachineApi/Exit のリクエスト。</summary>
    public class ExitRequest
    {
        public string SerialNo { get; set; }
    }
}
