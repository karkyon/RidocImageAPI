using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using RidocImageAPI.Models;
using RidocImageAPI.Services;
using Serilog;
using Serilog.Events;
using System.Text.Json;

// ═══════════════════════════════════════════════════════════════════════════════
// 0. ログ出力先（exe と同じディレクトリ配下の logs\ に出力）
// ═══════════════════════════════════════════════════════════════════════════════
string baseDir = AppContext.BaseDirectory;
string logDir  = Path.Combine(baseDir, "logs");
Directory.CreateDirectory(logDir);
string logPath = Path.Combine(logDir, "ridocapi-.log");

// ═══════════════════════════════════════════════════════════════════════════════
// 1. 起動前ロガー（Program.cs 内のクラッシュもキャプチャ）
// ═══════════════════════════════════════════════════════════════════════════════
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft",            LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    // RollingInterval は Serilog 名前空間に存在する（Serilog.Sinks.File ではない）
    .WriteTo.File(
        path                  : logPath,
        rollingInterval       : RollingInterval.Day,   // ← Serilog.RollingInterval
        fileSizeLimitBytes    : 100L * 1024 * 1024,
        retainedFileCountLimit: 31,
        outputTemplate        :
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
            "[pid:{ProcessId} tid:{ThreadId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.EventLog(
        source                  : "RidocImageAPI",
        manageEventSource       : false,
        restrictedToMinimumLevel: LogEventLevel.Warning)
    .CreateBootstrapLogger();

// ═══════════════════════════════════════════════════════════════════════════════
// 2. グローバル未処理例外ハンドラー（フォールトトレランス）
// ═══════════════════════════════════════════════════════════════════════════════
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal(e.ExceptionObject as Exception,
        "[FATAL] ハンドルされていない例外が発生しました。IsTerminating={IsTerminating}",
        e.IsTerminating);
    Log.CloseAndFlush();
};

// TaskScheduler は System.Threading.Tasks.TaskScheduler
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "[ERROR] UnobservedTaskException が発生しました。");
    e.SetObserved(); // プロセスクラッシュを防ぐ
};

try
{
    Log.Information("=== RidocImageAPI 起動中 === BaseDir={BaseDir}", baseDir);

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. ホストビルダー
    // ═══════════════════════════════════════════════════════════════════════════
    var builder = WebApplication.CreateBuilder(args);

    // Windows サービス対応（コンソール実行時はフォールバック）
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "RidocImageAPI";
    });

    // Serilog をメインロガーに
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        bool isDev = context.HostingEnvironment.IsDevelopment();
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .MinimumLevel.Is(isDev ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft",            LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path                  : logPath,
                rollingInterval       : RollingInterval.Day,
                fileSizeLimitBytes    : 100L * 1024 * 1024,
                retainedFileCountLimit: 31,
                outputTemplate        :
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
                    "[pid:{ProcessId} tid:{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.EventLog(
                source                  : "RidocImageAPI",
                manageEventSource       : false,
                restrictedToMinimumLevel: LogEventLevel.Warning);
    });

    // ── DI 登録 ──────────────────────────────────────────────────────────
    builder.Services.Configure<RsnServerSettings>(
        builder.Configuration.GetSection(RsnServerSettings.SectionName));

    builder.Services.AddControllers();
    builder.Services.AddTransient<IRsnImageService, RsnImageService>();

    // ── ヘルスチェック ────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("API プロセスは正常です。"),
            tags: ["live"])
        .AddCheck<RsnConnectivityCheck>("rsn-connectivity",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready"]);

    // ── Swagger ───────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "RidocImageAPI",
            Version     = "v1",
            Description = "Ridoc Smart Navigator から図面画像を取得する API。\n\n" +
                          "**imgType**: `TN`（サムネイル）または `ORG`（オリジナルデータ）"
        });
        c.MapType<byte[]>(() => new OpenApiSchema { Type = "string", Format = "binary" });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            c.IncludeXmlComments(xmlPath);
    });

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. ミドルウェアパイプライン
    // ═══════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    // グローバル例外ハンドラー（コントローラー未キャッチ → 500 + ログ）
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var feature = context.Features
                .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            if (feature?.Error is { } ex)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "[MIDDLEWARE] 未処理例外: {Path}", context.Request.Path);
            }

            context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                message = "内部エラーが発生しました。管理者に連絡してください。"
            }));
        });
    });

    // ヘルスチェックエンドポイント
    app.MapHealthChecks("/healthz", new HealthCheckOptions
    {
        Predicate      = check => check.Tags.Contains("live"),
        ResponseWriter = WriteHealthResponse
    });
    app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
    {
        Predicate      = check => check.Tags.Contains("ready"),
        ResponseWriter = WriteHealthResponse
    });

    // Swagger（開発環境のみ）
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RidocImageAPI v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Serilog アクセスログ
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "[HTTP] {RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.000}ms)";
        opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
            diagCtx.Set("RemoteIpAddress",
                httpCtx.Connection.RemoteIpAddress?.ToString() ?? "-");
    });

    app.UseHttpsRedirection();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("=== RidocImageAPI 起動完了 ===");
    app.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException
                           && ex.GetType().Name != "HostAbortedException")
{
    Log.Fatal(ex, "[FATAL] アプリケーションの起動に失敗しました。");
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("=== RidocImageAPI シャットダウン ===");
    Log.CloseAndFlush();
}

// ─────────────────────────────────────────────────────────────────────────────
// ヘルスチェックレスポンスライター（JSON）
// ─────────────────────────────────────────────────────────────────────────────
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    // IReadOnlyDictionary → LINQ の ToDictionary で変換（using System.Linq が必要）
    var entries = report.Entries.ToDictionary(
        e => e.Key,
        e => (object)new
        {
            status      = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration    = e.Value.Duration.TotalMilliseconds,
            exception   = e.Value.Exception?.Message
        });

    var result = JsonSerializer.Serialize(
        new
        {
            status  = report.Status.ToString(),
            elapsed = report.TotalDuration.TotalMilliseconds,
            entries
        },
        new() { WriteIndented = true });

    return context.Response.WriteAsync(result);
}
