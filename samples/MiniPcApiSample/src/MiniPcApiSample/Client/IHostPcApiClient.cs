using System.Threading;
using System.Threading.Tasks;
using MiniPcApiSample.Models;

namespace MiniPcApiSample.Client
{
    /// <summary>
    /// MiniPC（C0L-0161）→ HostPC（C0L-0160）へ発行する API のクライアント。
    /// 早見表: docs/07_system_design.md 「API 早見表 / MiniPC → HostPCProgram」
    /// </summary>
    public interface IHostPcApiClient
    {
        /// <summary>GET /IpApi/Assign — 空きIPを採番する（初工程のみ）。割り当てられたIPを返す。</summary>
        Task<string> AssignIpAsync(string serialNo, CancellationToken ct = default);

        /// <summary>GET /ProcessFileApi/Next — 次に実行すべき工程JSONファイルを問い合わせる。</summary>
        Task<ProcessFileNextResponse> GetNextProcessFileAsync(string serialNo, CancellationToken ct = default);

        /// <summary>GET /ProcessFileApi/FileContent/{seqId} — 工程JSONの内容（生JSON文字列）を取得する。</summary>
        Task<string> GetProcessFileContentAsync(int seqId, CancellationToken ct = default);

        /// <summary>POST /StepApi/UpdateStep — Step開始を通知し、作業指示の有無を受け取る。</summary>
        Task<UpdateStepResponse> UpdateStepAsync(string serialNo, int stepKey, CancellationToken ct = default);

        /// <summary>POST /StepApi/RecordStep — Step完了（OK/NG）を記録する。</summary>
        Task<ApiAck> RecordStepAsync(string serialNo, int stepKey, string result, CancellationToken ct = default);

        /// <summary>POST /MachineApi/Complete — 工程完了（OK/NG）を通知する。</summary>
        Task<ApiAck> CompleteAsync(string serialNo, string result, CancellationToken ct = default);

        /// <summary>POST /MachineApi/Exit — 異常退室を通知する（保険用）。</summary>
        Task<ApiAck> ExitAsync(string serialNo, CancellationToken ct = default);
    }
}
