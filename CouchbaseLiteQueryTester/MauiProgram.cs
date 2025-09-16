using Microsoft.Extensions.Logging;

#if WINDOWS || MACCATALYST
using Couchbase.Lite.Support;
#endif

namespace CouchbaseLiteQueryTester
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

#if WINDOWS || MACCATALYST
            NetDesktop.Activate();
#endif

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
