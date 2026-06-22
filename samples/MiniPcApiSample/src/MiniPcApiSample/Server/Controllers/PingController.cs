using System.Web.Http;

namespace MiniPcApiSample.Server.Controllers
{
    /// <summary>
    /// 疎通確認用エンドポイント。GET /api/ping → 200 OK。
    /// HostPC 側から MiniPC のコールバックサーバーが生きているか確認する用途。
    /// </summary>
    [RoutePrefix("api")]
    public class PingController : ApiController
    {
        [HttpGet]
        [Route("ping")]
        public IHttpActionResult Get()
        {
            return Ok(new { status = "ok", role = "MiniPC", component = "C0L-0161" });
        }
    }
}
