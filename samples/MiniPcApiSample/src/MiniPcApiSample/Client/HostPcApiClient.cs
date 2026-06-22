using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MiniPcApiSample.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MiniPcApiSample.Client
{
    /// <summary>
    /// <see cref="IHostPcApiClient"/> の HttpClient 実装。
    /// HostPC のベースURL に対して各 API を叩く。
    /// </summary>
    public class HostPcApiClient : IHostPcApiClient, IDisposable
    {
        private readonly HttpClient _http;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public HostPcApiClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl));

            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<string> AssignIpAsync(string serialNo, CancellationToken ct = default)
        {
            var res = await GetAsync<IpAssignResponse>(
                $"IpApi/Assign?serialNo={Uri.EscapeDataString(serialNo)}", ct).ConfigureAwait(false);
            return res?.IpAddress;
        }

        public Task<ProcessFileNextResponse> GetNextProcessFileAsync(string serialNo, CancellationToken ct = default)
            => GetAsync<ProcessFileNextResponse>(
                $"ProcessFileApi/Next?serialNo={Uri.EscapeDataString(serialNo)}", ct);

        public async Task<string> GetProcessFileContentAsync(int seqId, CancellationToken ct = default)
        {
            using (var res = await _http.GetAsync($"ProcessFileApi/FileContent/{seqId}", ct).ConfigureAwait(false))
            {
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        public Task<UpdateStepResponse> UpdateStepAsync(string serialNo, int stepKey, CancellationToken ct = default)
            => PostAsync<UpdateStepResponse>(
                "StepApi/UpdateStep",
                new UpdateStepRequest { SerialNo = serialNo, StepKey = stepKey }, ct);

        public Task<ApiAck> RecordStepAsync(string serialNo, int stepKey, string result, CancellationToken ct = default)
            => PostAsync<ApiAck>(
                "StepApi/RecordStep",
                new RecordStepRequest { SerialNo = serialNo, StepKey = stepKey, Result = result }, ct);

        public Task<ApiAck> CompleteAsync(string serialNo, string result, CancellationToken ct = default)
            => PostAsync<ApiAck>(
                "MachineApi/Complete",
                new CompleteRequest { SerialNo = serialNo, Result = result }, ct);

        public Task<ApiAck> ExitAsync(string serialNo, CancellationToken ct = default)
            => PostAsync<ApiAck>(
                "MachineApi/Exit",
                new ExitRequest { SerialNo = serialNo }, ct);

        // ---- 内部ヘルパー -------------------------------------------------

        private async Task<T> GetAsync<T>(string path, CancellationToken ct)
        {
            using (var res = await _http.GetAsync(path, ct).ConfigureAwait(false))
            {
                res.EnsureSuccessStatusCode();
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<T>(body);
            }
        }

        private async Task<T> PostAsync<T>(string path, object payload, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(payload, JsonSettings);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var res = await _http.PostAsync(path, content, ct).ConfigureAwait(false))
            {
                res.EnsureSuccessStatusCode();
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<T>(body);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
