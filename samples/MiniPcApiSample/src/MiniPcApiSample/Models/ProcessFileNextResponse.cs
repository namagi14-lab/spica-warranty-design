namespace MiniPcApiSample.Models
{
    /// <summary>
    /// GET /ProcessFileApi/Next のレスポンス。
    /// 参照: docs/06_process_file_api.md
    /// </summary>
    public class ProcessFileNextResponse
    {
        /// <summary>次に実行すべきファイルがあるか。false なら工程完了。</summary>
        public bool HasNext { get; set; }

        /// <summary>process_file_sequence.SeqId。FileContent 取得時のキー。</summary>
        public int SeqId { get; set; }

        /// <summary>実行順。</summary>
        public int StepOrder { get; set; }

        /// <summary>工程JSONファイル名。</summary>
        public string FileName { get; set; }

        /// <summary>サーバー側ファイルの SHA-256。ローカルと突き合わせる。</summary>
        public string FileHash { get; set; }

        /// <summary>ファイルバージョン。</summary>
        public int FileVersion { get; set; }

        /// <summary>true の場合 MiniPC は画像検査モードへ移行する（通常のTCPコマンド実行を行わない）。</summary>
        public bool IsImageInspection { get; set; }

        /// <summary>エラーメッセージ（正常時は null）。</summary>
        public string Error { get; set; }
    }
}
