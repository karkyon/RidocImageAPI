using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RidocImageAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ログの設定 (コンソールログを有効化)
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(); 
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // サービスを追加
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // エラーハンドリング
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage(); // 詳細エラーを表示
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
            {
                app.UseExceptionHandler("/error");
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
