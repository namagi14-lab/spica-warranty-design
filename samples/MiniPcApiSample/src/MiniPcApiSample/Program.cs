using System;
using System.Threading;
using Microsoft.Owin.Hosting;
using MiniPcApiSample.Client;
using MiniPcApiSample.Configuration;
using MiniPcApiSample.Server;
using MiniPcApiSample.Workflow;

namespace MiniPcApiSample
{
    /// <summary>
    /// MiniPC（C0L-0161 保証工程制御Program）のサンプル エントリポイント。
    ///
    ///   1) コールバック受信サーバー（HTTP）を OWIN セルフホストで立てる
    ///   2) SerialNo が設定されていれば、HostPC の API を叩いて工程を 1 回流すデモを実行
    ///
    /// 中の業務処理（プリンターへのコマンド送信・JSON解析）は別担当が実装する想定で、
    /// 本サンプルは API の「立てる / 受け取る / 叩く」配線のみを示します。
    /// </summary>
    internal static class Program
    {
        private static void Main()
        {
            var options = MiniPcOptions.Load();

            Console.WriteLine("==== MiniPC API Sample (C0L-0161) ====");
            Console.WriteLine($"HostPC ベースURL : {options.HostPcBaseUrl}");
            Console.WriteLine($"待ち受けURL       : {options.ListenUrl}");

            // 1) コールバック受信サーバーを起動（using を抜けると停止）
            using (WebApp.Start<Startup>(options.ListenUrl))
            {
                var baseDisplay = options.ListenUrl.TrimEnd('/');
                Console.WriteLine($"[Server] 起動しました。受信: POST {baseDisplay}/api/instructionResult");
                Console.WriteLine($"[Server] 疎通確認: GET  {baseDisplay}/api/ping");

                // 2) 工程デモ（SerialNo 指定時のみ）
                if (!string.IsNullOrWhiteSpace(options.SerialNo))
                {
                    using (var api = new HostPcApiClient(options.HostPcBaseUrl))
                    {
                        var runner = new ProcessRunner(
                            api, options.SerialNo, options.LocalFileDir, InstructionResultCoordinator.Instance);
                        try
                        {
                            runner.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Runner] 工程実行中にエラー: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[Server] SerialNo 未設定のため、コールバック受信のみ行います。");
                }

                Console.WriteLine();
                Console.WriteLine("Enter キーで終了します...");
                Console.ReadLine();
            }
        }
    }
}
