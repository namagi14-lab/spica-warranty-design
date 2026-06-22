using System;
using System.Web.Http;
using MiniPcApiSample.Models;
using MiniPcApiSample.Server;

namespace MiniPcApiSample.Server.Controllers
{
    /// <summary>
    /// HostPC → MiniPC のコールバック受信エンドポイント。
    ///
    ///   POST /api/instructionResult
    ///
    /// 作業者がタブレットで作業指示を OK/NG した結果がここにプッシュされる。
    /// 受け取った結果は <see cref="InstructionResultCoordinator"/> 経由で、
    /// 待機中の工程実行ループへ渡す。
    ///
    /// 参照: docs/07_system_design.md / docs/04_api_spec.md
    /// </summary>
    [RoutePrefix("api")]
    public class InstructionResultController : ApiController
    {
        [HttpPost]
        [Route("instructionResult")]
        public IHttpActionResult Post([FromBody] InstructionResultRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SerialNo))
            {
                return BadRequest("serialNo は必須です");
            }

            var result = (request.Result ?? string.Empty).Trim().ToUpperInvariant();
            if (result != "OK" && result != "NG")
            {
                return BadRequest("result は OK または NG を指定してください");
            }

            Console.WriteLine(
                $"[Callback] instructionResult 受信: serialNo={request.SerialNo}, stepKey={request.StepKey}, result={result}");

            // 待機中の工程ループへ通知（中の業務処理は別担当のため、ここではシグナル通知のみ）
            InstructionResultCoordinator.Instance.Publish(request.SerialNo, request.StepKey, result);

            return Ok(new ApiAck { Success = true });
        }
    }
}
