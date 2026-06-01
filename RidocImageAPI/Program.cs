using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using RidocImageAPI.Models;
using RidocImageAPI.Services;
using System;
using System.IO;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// ── ① 認証情報を IOptions<RsnServerSettings> にバインド ─────────────────────
builder.Services.Configure<RsnServerSettings>(
    builder.Configuration.GetSection(RsnServerSettings.SectionName));

// ── コントローラー登録 ──────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── サービス登録（DI）──────────────────────────────────────────────────────
// Transient: リクエストごとに新インスタンス（RsnSystem は都度接続が前提のため）
builder.Services.AddTransient<IRsnImageService, RsnImageService>();

// ── ⑭ Swagger / OpenAPI 設定（全レスポンスコードを記述）──────────────────
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

    // image/jpeg レスポンスを Swagger UI でバイナリ（string format:binary）として正しく表示する。
    // これがないと byte[] が "string" と表示されてしまう。
    c.MapType<byte[]>(() => new Microsoft.OpenApi.Models.OpenApiSchema
    {
        Type   = "string",
        Format = "binary"
    });

    // XML コメントを Swagger に取り込む（Visual Studio でビルド時に生成）
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// ── ログ設定（appsettings.{env}.json で制御可能）──────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// 開発環境のみデバッグウィンドウへも出力（Visual Studio の出力ウィンドウに表示）
if (builder.Environment.IsDevelopment())
    builder.Logging.AddDebug();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── 開発環境のみ Swagger UI を有効化（デバッグ用）────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RidocImageAPI v1");
        c.RoutePrefix = "swagger"; // https://localhost:5088/swagger でアクセス
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
