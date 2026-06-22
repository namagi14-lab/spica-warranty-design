namespace MiniPcApiSample.Models
{
    /// <summary>
    /// HostPC の多くの API が返す共通レスポンス（{ "success": true } 等）。
    /// </summary>
    public class ApiAck
    {
        public bool Success { get; set; }

        /// <summary>失敗時のメッセージ（成功時は null）。</summary>
        public string Message { get; set; }
    }
}
