using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MiniPcApiSample.Client;
using MiniPcApiSample.Models;
using MiniPcApiSample.Server;

namespace MiniPcApiSample.Workflow
{
    /// <summary>
    /// 工程実行ループのスケルトン。MiniPC が HostPC の API を叩きながら 1 台分の工程を流す。
    ///
    /// ★ 通信（API を叩く / コールバックを待つ）の配線のみ実装しています。
    ///   プリンターへの KCFG コマンド送信や工程JSONの解析など「中の業務処理」は
    ///   別担当が実装する想定で、TODO のスタブにしてあります。
    ///
    /// 参照: docs/07_system_design.md / docs/06_process_file_api.md
    /// </summary>
    public class ProcessRunner
    {
        private readonly IHostPcApiClient _api;
        private readonly InstructionResultCoordinator _coordinator;
        private readonly string _serialNo;
        private readonly string _localFileDir;

        public ProcessRunner(
            IHostPcApiClient api,
            string serialNo,
            string localFileDir,
            InstructionResultCoordinator coordinator = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _serialNo = serialNo ?? throw new ArgumentNullException(nameof(serialNo));
            _localFileDir = localFileDir ?? "process_files";
            _coordinator = coordinator ?? InstructionResultCoordinator.Instance;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            Console.WriteLine($"[Runner] 工程開始: serialNo={_serialNo}");

            // ① IP採番（初工程のみ）。必要な工程でのみ呼ぶ。
            // var ip = await _api.AssignIpAsync(_serialNo, ct);
            // TODO(別担当): 取得したIPをプリンターへ設定するコマンドを送信する。

            // ③ 工程JSONファイル実行ループ
            while (!ct.IsCancellationRequested)
            {
                var next = await _api.GetNextProcessFileAsync(_serialNo, ct).ConfigureAwait(false);
                if (next == null || !next.HasNext)
                {
                    Console.WriteLine("[Runner] 全ファイル完了。");
                    break;
                }

                Console.WriteLine($"[Runner] 次ファイル: {next.FileName} (seqId={next.SeqId}, imageInspection={next.IsImageInspection})");

                if (next.IsImageInspection)
                {
                    // TODO(別担当): 画像検査モード。image_inspection_db の ALLResult をポーリングし、
                    //               OK/NG になったら次へ進む（docs/06_process_file_api.md 参照）。
                    Console.WriteLine("[Runner] 画像検査モード（スタブ）。");
                    continue;
                }

                // ハッシュを突き合わせ、不一致ならファイル内容を取得してローカル更新
                var json = await EnsureLocalFileAsync(next, ct).ConfigureAwait(false);

                // 工程JSONを解析して Step 一覧を得る
                var stepKeys = ParseStepKeys(json);

                // 各 Step を順に実行
                foreach (var stepKey in stepKeys)
                {
                    var result = await ExecuteStepAsync(stepKey, ct).ConfigureAwait(false);

                    // Step 完了を記録
                    await _api.RecordStepAsync(_serialNo, stepKey, result, ct).ConfigureAwait(false);

                    if (result == "NG")
                    {
                        // ④ 工程完了（NG）
                        Console.WriteLine($"[Runner] Step {stepKey} が NG。工程を NG で完了します。");
                        await _api.CompleteAsync(_serialNo, "NG", ct).ConfigureAwait(false);
                        return;
                    }
                }
            }

            // ④ 工程完了（OK）
            await _api.CompleteAsync(_serialNo, "OK", ct).ConfigureAwait(false);
            Console.WriteLine("[Runner] 工程を OK で完了しました。");
        }

        /// <summary>
        /// 1 Step の実行。Step 開始を通知し、MANUAL Step ならコールバックを待つ。
        /// 戻り値は "OK" / "NG"。
        /// </summary>
        private async Task<string> ExecuteStepAsync(int stepKey, CancellationToken ct)
        {
            // TODO(別担当): プリンター本体へ KCFG コマンドを送信する（AUTO/MANUAL 問わず）。
            // await ExecuteKcfgCommandAsync(stepKey, ct);

            // Step 開始を HostPC に通知し、作業指示の有無を受け取る
            var upd = await _api.UpdateStepAsync(_serialNo, stepKey, ct).ConfigureAwait(false);

            if (upd != null && upd.HasInstruction)
            {
                // MANUAL Step: HostPC からのコールバック（POST /api/instructionResult）を待つ。
                // ポーリング不要。コールバックはコントローラが Coordinator 経由で渡す。
                Console.WriteLine($"[Runner] Step {stepKey} は作業指示あり。コールバック待機中...");
                var result = await _coordinator.WaitForResultAsync(_serialNo, stepKey, ct).ConfigureAwait(false);
                Console.WriteLine($"[Runner] Step {stepKey} コールバック受信: {result}");
                return result;
            }

            // AUTO Step: 即 OK 扱いで次へ（実際の結果判定は別担当のコマンド実行に従う）
            return "OK";
        }

        /// <summary>
        /// ローカルの工程JSONファイルの SHA-256 を計算し、サーバーのハッシュと突き合わせる。
        /// 不一致（または未取得）なら FileContent を取得して保存する。最新のJSON文字列を返す。
        /// </summary>
        private async Task<string> EnsureLocalFileAsync(ProcessFileNextResponse next, CancellationToken ct)
        {
            Directory.CreateDirectory(_localFileDir);
            var path = Path.Combine(_localFileDir, next.FileName);

            if (File.Exists(path))
            {
                var localHash = ComputeSha256(File.ReadAllBytes(path));
                if (string.Equals(localHash, next.FileHash, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[Runner] ローカルファイルのハッシュ一致。再利用します。");
                    return File.ReadAllText(path, Encoding.UTF8);
                }
            }

            Console.WriteLine("[Runner] ハッシュ不一致または未取得。FileContent を取得します。");
            var json = await _api.GetProcessFileContentAsync(next.SeqId, ct).ConfigureAwait(false);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return json;
        }

        /// <summary>
        /// 工程JSONから Step 番号の一覧を取り出す。
        /// TODO(別担当): 実際のスキーマ（Step_key / Command_Control など）に合わせて解析する。
        ///               ここでは配線確認用の最小スタブとして空一覧を返す。
        /// </summary>
        private static IReadOnlyList<int> ParseStepKeys(string json)
        {
            // 例: [{ "Step_key": 1, "Command_Control": { ... } }, ...]
            // docs/process_file_samples/*.json を参照。
            return Array.Empty<int>();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
