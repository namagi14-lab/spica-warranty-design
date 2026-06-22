namespace MiniPcApiSample.Models
{
    /// <summary>
    /// POST /StepApi/UpdateStep のレスポンス。
    /// hasInstruction が true の場合、その Step は MANUAL（作業者確認が必要）であり、
    /// MiniPC は HostPC からのコールバック（POST /api/instructionResult）を待つ。
    /// 参照: docs/04_api_spec.md / docs/07_system_design.md
    /// </summary>
    public class UpdateStepResponse
    {
        public bool Success { get; set; }

        /// <summary>true: 作業指示あり（コールバック待ち）。false: AUTO Step（即次へ）。</summary>
        public bool HasInstruction { get; set; }

        public int ZoneId { get; set; }
    }
}
