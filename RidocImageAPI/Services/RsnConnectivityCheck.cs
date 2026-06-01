using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jp.co.ricoh.ridoc.smartnavi;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RidocImageAPI.Models;

namespace RidocImageAPI.Services
{
    /// <summary>
    /// RSN サーバーへの疎通確認ヘルスチェック。
    /// Connect → 即 Disconnect を行い接続可否を確認する。
    /// /healthz/ready で呼び出される。
    /// </summary>
    // プライマリコンストラクターを使用（警告対応）
    public sealed class RsnConnectivityCheck(
        IOptions<RsnServerSettings> settings,
        ILogger<RsnConnectivityCheck> logger) : IHealthCheck
    {
        private readonly RsnServerSettings _settings = settings.Value;

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var rsnSystem = new RsnSystem();
                try
                {
                    rsnSystem.Connect(_settings.Url, _settings.User, _settings.Password);
                    rsnSystem.Disconnect();
                    sw.Stop();

                    logger.LogDebug("[HEALTH] RSN 疎通OK: {Url} elapsed={Elapsed}ms",
                        _settings.Url, sw.ElapsedMilliseconds);

                    return HealthCheckResult.Healthy(
                        $"RSN サーバーへの接続に成功しました。({sw.ElapsedMilliseconds}ms)");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    logger.LogWarning(ex, "[HEALTH] RSN 疎通NG: {Url}", _settings.Url);
                    return HealthCheckResult.Degraded(
                        $"RSN サーバーへの接続に失敗しました。({ex.Message})",
                        ex);
                }
            }, cancellationToken);
        }
    }
}
