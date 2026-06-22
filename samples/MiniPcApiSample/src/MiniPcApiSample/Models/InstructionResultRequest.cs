namespace MiniPcApiSample.Models
{
    /// <summary>
    /// HostPC → MiniPC コールバック（POST /api/instructionResult）のリクエストボディ。
    /// 作業者がタブレットで作業指示を OK/NG した結果がプッシュ通知される。
    /// 参照: docs/07_system_design.md 「5. Step 実行〜作業指示フロー（プッシュ型）」
    /// </summary>
    public class InstructionResultRequest
    {
        /// <summary>マシンシリアル番号。</summary>
        public string SerialNo { get; set; }

        /// <summary>対象の Step 番号。</summary>
        public int StepKey { get; set; }

        /// <summary>作業者の判定結果。"OK" または "NG"。</summary>
        public string Result { get; set; }
    }
}
