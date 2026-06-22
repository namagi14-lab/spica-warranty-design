using System.Web.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Owin;

namespace MiniPcApiSample
{
    /// <summary>
    /// OWIN セルフホストの起動構成。MiniPC 上に Web API（HTTP サーバー）を立てる。
    /// WebApp.Start&lt;Startup&gt;(url) から呼び出される。
    /// </summary>
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();

            // 属性ルーティング（[RoutePrefix] / [Route]）を有効化
            config.MapHttpAttributeRoutes();

            // JSON は camelCase で入出力（HostPC 側の仕様に合わせる）
            var json = config.Formatters.JsonFormatter;
            json.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            json.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;

            // レスポンスを JSON に統一（XML フォーマッタを外す）
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            app.UseWebApi(config);
        }
    }
}
