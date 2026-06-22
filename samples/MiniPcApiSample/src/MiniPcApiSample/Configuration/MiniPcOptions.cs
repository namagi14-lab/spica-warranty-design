using System.Configuration;

namespace MiniPcApiSample.Configuration
{
    /// <summary>App.config の appSettings から読み込む設定。</summary>
    public class MiniPcOptions
    {
        /// <summary>HostPC（C0L-0160）のベースURL。</summary>
        public string HostPcBaseUrl { get; set; }

        /// <summary>コールバック受信サーバーの待ち受けURL。</summary>
        public string ListenUrl { get; set; }

        /// <summary>デモ実行する製品シリアル番号（空ならサーバー待ち受けのみ）。</summary>
        public string SerialNo { get; set; }

        /// <summary>工程JSONファイルのローカル保存先。</summary>
        public string LocalFileDir { get; set; }

        public static MiniPcOptions Load()
        {
            var s = ConfigurationManager.AppSettings;
            return new MiniPcOptions
            {
                HostPcBaseUrl = s["HostPcBaseUrl"] ?? "http://192.168.1.1/",
                ListenUrl = s["ListenUrl"] ?? "http://localhost:8080/",
                SerialNo = s["SerialNo"] ?? string.Empty,
                LocalFileDir = s["LocalFileDir"] ?? "process_files"
            };
        }
    }
}
