using System.Text.Json;
using SourceCode.Hosting.Client.BaseAPI;
using SourceCode.Workflow.Client;

namespace SpeedTestThroughput;

internal static class Program
{
    private const string DefaultConfigFilePath = "appSettings.json";
    private const string DefaultFolderName = "QuickSpeedTest";

    private static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            AppSettings settings = LoadSettings(options.ConfigFilePath);

            int threadCount = options.Threads ?? ReadPositiveInt("Enter number of threads: ");
            int iterations = options.Iterations ?? ReadPositiveInt("Enter number of iterations per thread: ");
            string batchName = options.BatchName ?? ReadBatchName(threadCount, iterations);
            string processName = options.ProcessName ?? ReadProcessName();

            while (true)
            {
                Console.WriteLine($"Starting {threadCount * iterations} process instance(s) across {threadCount} thread(s).");
                Console.WriteLine($"Target server: {settings.Host}:{settings.Port}. Authentication: {(settings.Integrated ? "integrated" : settings.UserID)}.");
                Console.WriteLine($"Process: {processName}. Batch: {batchName}.");

                SpeedTestResult result = RunSpeedTest(threadCount, iterations, batchName, processName, settings);
                WriteResultSummary(result);

                if (result.HasAuthenticationFailure && CanPromptForAuthentication())
                {
                    Console.WriteLine("The failure looks authentication-related. Re-enter connection settings to try again.");
                    settings = ReadSettings();
                    continue;
                }

                return result.FailureCount == 0 ? 0 : 1;
            }
        }
        catch (Exception ex)
        {
            WriteException(ex);
            return 1;
        }
    }

    private static SpeedTestResult RunSpeedTest(
        int threadCount,
        int iterations,
        string batchName,
        string processName,
        AppSettings settings)
    {
        int successCount = 0;
        int failureCount = 0;
        object consoleLock = new();
        List<Exception> errors = new();
        List<Task> tasks = new();

        for (int i = 0; i < threadCount; i++)
        {
            int threadNumber = i + 1;
            tasks.Add(Task.Run(() =>
            {
                Connection? connection = null;

                try
                {
                    lock (consoleLock)
                    {
                        Console.WriteLine($"Thread {threadNumber}: opening K2 connection...");
                    }

                    connection = CreateConnection(settings);

                    lock (consoleLock)
                    {
                        Console.WriteLine($"Thread {threadNumber}: connected.");
                    }

                    for (int j = 0; j < iterations; j++)
                    {
                        try
                        {
                            StartK2Process(batchName, connection, processName);
                            int started = Interlocked.Increment(ref successCount);

                            if (started == 1 || started % 25 == 0 || started == threadCount * iterations)
                            {
                                lock (consoleLock)
                                {
                                    Console.WriteLine($"Started {started} of {threadCount * iterations} process instance(s)...");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failureCount);
                            lock (consoleLock)
                            {
                                errors.Add(ex);
                                Console.Error.WriteLine($"Thread {threadNumber}, iteration {j + 1} failed: {GetInnermostMessage(ex)}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Add(ref failureCount, iterations);
                    lock (consoleLock)
                    {
                        errors.Add(ex);
                        Console.Error.WriteLine($"Thread {threadNumber}: connection failed: {GetInnermostMessage(ex)}");
                    }
                }
                finally
                {
                    connection?.Dispose();
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        return new SpeedTestResult(successCount, failureCount, errors);
    }

    private static Connection CreateConnection(AppSettings settings)
    {
        SCConnectionStringBuilder connectionString = new()
        {
            Host = settings.Host,
            Port = settings.Port,
            Integrated = settings.Integrated,
            IsPrimaryLogin = true
        };

        if (!settings.Integrated)
        {
            connectionString.UserID = settings.UserID;
            connectionString.Password = settings.Password;
        }

        Connection connection = new();
        if (settings.Integrated)
        {
            connection.Open(settings.Host);
        }
        else
        {
            connection.Open(settings.Host, connectionString.ToString());
        }

        return connection;
    }

    private static void StartK2Process(string batchName, Connection connection, string processName)
    {
        long startTimeTicks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ProcessInstance process = connection.CreateProcessInstance(processName);

        process.DataFields["starttime"].Value = startTimeTicks;
        process.DataFields["batchname"].Value = batchName;

        connection.StartProcessInstance(process, true);
    }

    private static AppSettings LoadSettings(string configFilePath)
    {
        if (!File.Exists(configFilePath))
        {
            Console.WriteLine($"Configuration file '{configFilePath}' was not found. Enter connection settings for this run.");
            return ReadSettings();
        }

        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(configFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (settings is null)
        {
            throw new InvalidOperationException($"Configuration file '{configFilePath}' could not be read.");
        }

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new InvalidOperationException("Configuration value 'Host' is required.");
        }

        if (!settings.Integrated && string.IsNullOrWhiteSpace(settings.UserID))
        {
            throw new InvalidOperationException("Configuration value 'UserID' is required when Integrated is false.");
        }

        return settings;
    }

    private static void WriteResultSummary(SpeedTestResult result)
    {
        Console.WriteLine($"Completed. Successful starts: {result.SuccessCount}. Failed starts: {result.FailureCount}.");

        if (result.Errors.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine("Error summary:");
        foreach (IGrouping<string, Exception> group in result.Errors.GroupBy(GetInnermostMessage).Take(5))
        {
            Console.Error.WriteLine($"- {group.Count()}x {group.Key}");
        }
    }

    private static void WriteException(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            foreach (Exception innerException in aggregateException.Flatten().InnerExceptions)
            {
                Console.Error.WriteLine(GetInnermostMessage(innerException));
            }

            return;
        }

        Console.Error.WriteLine(GetInnermostMessage(exception));
    }

    private static string GetInnermostMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static bool CanPromptForAuthentication()
    {
        return !Console.IsInputRedirected;
    }

    private static bool LooksLikeAuthenticationFailure(Exception exception)
    {
        string message = exception.ToString();
        return ContainsIgnoreCase(message, "auth")
            || ContainsIgnoreCase(message, "login")
            || ContainsIgnoreCase(message, "logon")
            || ContainsIgnoreCase(message, "credential")
            || ContainsIgnoreCase(message, "password")
            || ContainsIgnoreCase(message, "unauthorized")
            || ContainsIgnoreCase(message, "access denied")
            || ContainsIgnoreCase(message, "401");
    }

    private static bool ContainsIgnoreCase(string value, string search)
    {
        return value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static AppSettings ReadSettings()
    {
        string host = ReadString("Enter K2 server", "localhost");
        string userId = ReadString("Enter username", "administrator");
        string password = ReadPassword("Enter password");
        bool integrated = ReadBool("Use integrated authentication", true);

        return new AppSettings
        {
            Host = host,
            Port = 5252,
            Integrated = integrated,
            UserID = userId,
            Password = password
        };
    }

    private static int ReadPositiveInt(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string? value = Console.ReadLine();

            if (int.TryParse(value, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            Console.WriteLine("Enter a whole number greater than zero.");
        }
    }

    private static string ReadString(string prompt, string defaultValue)
    {
        Console.Write($"{prompt} [{defaultValue}]: ");
        string? value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static bool ReadBool(string prompt, bool defaultValue)
    {
        string defaultText = defaultValue ? "Y/n" : "y/N";

        while (true)
        {
            Console.Write($"{prompt} [{defaultText}]: ");
            string? value = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "y":
                case "yes":
                case "true":
                case "1":
                    return true;
                case "n":
                case "no":
                case "false":
                case "0":
                    return false;
                default:
                    Console.WriteLine("Enter yes or no.");
                    break;
            }
        }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write($"{prompt}: ");

        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        string password = string.Empty;

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password += key.KeyChar;
            }
        }
    }

    private static string ReadBatchName(int threadCount, int iterations)
    {
        string defaultBatchName = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {threadCount}Thrd {iterations}Iter";

        Console.Write("Enter a Batch Name (press Enter to use default): ");
        string? batchName = Console.ReadLine();

        return string.IsNullOrWhiteSpace(batchName) ? defaultBatchName : batchName;
    }

    private static string ReadProcessName()
    {
        Console.WriteLine("Choose a process:");
        Console.WriteLine("1. Throughput-SQL");
        Console.WriteLine("2. Throughput-Task");
        Console.WriteLine("3. Custom");

        while (true)
        {
            Console.Write("Enter your choice (1-3): ");
            string? choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    return $"{DefaultFolderName}\\Throughput-SQL";
                case "2":
                    return $"{DefaultFolderName}\\Throughput-Task";
                case "3":
                    Console.Write("Enter the custom process name: ");
                    string customProcess = Console.ReadLine() ?? string.Empty;
                    return customProcess.Contains('\\') ? customProcess : $"{DefaultFolderName}\\{customProcess}";
                default:
                    Console.WriteLine("Invalid choice. Please enter 1, 2, or 3.");
                    break;
            }
        }
    }

    private sealed class SpeedTestResult
    {
        public SpeedTestResult(int successCount, int failureCount, IReadOnlyList<Exception> errors)
        {
            SuccessCount = successCount;
            FailureCount = failureCount;
            Errors = errors;
        }

        public int SuccessCount { get; }

        public int FailureCount { get; }

        public IReadOnlyList<Exception> Errors { get; }

        public bool HasAuthenticationFailure => Errors.Any(LooksLikeAuthenticationFailure);
    }

    private sealed class Options
    {
        private Options(string configFilePath, int? threads, int? iterations, string? batchName, string? processName)
        {
            ConfigFilePath = configFilePath;
            Threads = threads;
            Iterations = iterations;
            BatchName = batchName;
            ProcessName = processName;
        }

        public string ConfigFilePath { get; }

        public int? Threads { get; }

        public int? Iterations { get; }

        public string? BatchName { get; }

        public string? ProcessName { get; }

        public static Options Parse(string[] args)
        {
            string configFilePath = DefaultConfigFilePath;
            int? threads = null;
            int? iterations = null;
            string? batchName = null;
            string? processName = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = ReadOptionValue(args, ref i);

                switch (arg)
                {
                    case "--config":
                    case "-c":
                        configFilePath = value;
                        break;
                    case "--threads":
                    case "-t":
                        threads = ParsePositiveOption(value, arg);
                        break;
                    case "--iterations":
                    case "-i":
                        iterations = ParsePositiveOption(value, arg);
                        break;
                    case "--batch":
                    case "-b":
                        batchName = value;
                        break;
                    case "--process":
                    case "-p":
                        processName = value.Contains('\\') ? value : $"{DefaultFolderName}\\{value}";
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{arg}'.");
                }
            }

            return new Options(configFilePath, threads, iterations, batchName, processName);
        }

        private static string ReadOptionValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{args[index]}' requires a value.");
            }

            index++;
            return args[index];
        }

        private static int ParsePositiveOption(string value, string optionName)
        {
            if (int.TryParse(value, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            throw new ArgumentException($"Option '{optionName}' must be a whole number greater than zero.");
        }
    }
}
