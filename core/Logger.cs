using Serilog;
public static class Logger
{
    // private static readonly LoggerConfiguration _logger = new();
    public static void Initialize()
    {
        // Logger setup
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/proxy.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("Logger initialized successfully.");
    }

    public static void _logger(string message, string args)
    {
        if (args == "info")
        {
            Log.Information(message);
        }else if (args == "error")
        {
            Log.Error(message);
        }
        else if (args == "debug")
        {
            Log.Debug(message);
        }
        else if (args == "warning")
        {
            Log.Warning(message);
        }
        else
        {
            Log.Information("Unknown log level: {Args}. Logging as Information.", args);
            Log.Information(message);
        }
    }
}