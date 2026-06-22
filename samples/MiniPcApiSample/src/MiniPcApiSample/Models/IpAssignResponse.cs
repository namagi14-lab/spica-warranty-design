namespace MiniPcApiSample.Models
{
    /// <summary>
    /// GET /IpApi/Assign のレスポンス（{ "ipAddress": "192.168.1.10" }）。
    /// 参照: docs/07_system_design.md 「2. IP 採番フロー」
    /// </summary>
    public class IpAssignResponse
    {
        public string IpAddress { get; set; }
    }
}
