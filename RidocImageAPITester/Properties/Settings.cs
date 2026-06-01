using System.Configuration;

namespace RidocImageAPITester.Properties
{
    // Visual Studio が生成する Settings クラスを手動で定義。
    // ApplicationSettings Provider 経由でユーザー設定を永続化する。
    internal sealed partial class Settings : ApplicationSettingsBase
    {
        private static readonly Settings _default = (Settings)Synchronized(new Settings());

        public static Settings Default => _default;

        [UserScopedSetting]
        [DefaultSettingValue("https://localhost:5088")]
        public string BaseUrl
        {
            get => (string)this[nameof(BaseUrl)];
            set => this[nameof(BaseUrl)] = value;
        }
    }
}
