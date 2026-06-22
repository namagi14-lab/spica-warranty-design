using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MiniPcApiSample.Server
{
    /// <summary>
    /// 「コールバック受信（サーバー）」と「工程実行ループ（クライアント）」の橋渡し役。
    ///
    /// MANUAL Step では、工程ループ側が <see cref="WaitForResultAsync"/> でコールバックを待ち、
    /// HostPC からの POST /api/instructionResult を受けたコントローラが <see cref="Publish"/> で結果を渡す。
    ///
    /// シングルプロセス内の単純なシグナル機構（in-memory）。
    /// 実運用で複数シリアルを同時に流す場合もキー（シリアル番号 + Step番号）で識別できる。
    /// </summary>
    public sealed class InstructionResultCoordinator
    {
        private static readonly Lazy<InstructionResultCoordinator> _instance =
            new Lazy<InstructionResultCoordinator>(() => new InstructionResultCoordinator());

        public static InstructionResultCoordinator Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _waiters =
            new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        private InstructionResultCoordinator() { }

        private static string Key(string serialNo, int stepKey) => serialNo + "#" + stepKey;

        /// <summary>
        /// 指定シリアル・Step のコールバック結果（"OK"/"NG"）を待つ。
        /// </summary>
        public Task<string> WaitForResultAsync(string serialNo, int stepKey, CancellationToken ct)
        {
            var key = Key(serialNo, stepKey);
            var tcs = _waiters.GetOrAdd(
                key,
                _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

            // キャンセル時はタスクもキャンセルする
            ct.Register(() => tcs.TrySetCanceled());

            // 完了後はエントリを破棄してメモリリークを防ぐ
            tcs.Task.ContinueWith(completed =>
            {
                _waiters.TryRemove(key, out _);
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        /// <summary>
        /// HostPC からのコールバックで受け取った結果を、待機中のループへ通知する。
        /// 待機登録より先に結果が届いた場合も取りこぼさないよう保持する。
        /// </summary>
        public void Publish(string serialNo, int stepKey, string result)
        {
            var tcs = _waiters.GetOrAdd(
                Key(serialNo, stepKey),
                _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

            tcs.TrySetResult(result);
        }
    }
}
