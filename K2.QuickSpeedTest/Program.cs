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

            Console.WriteLine($"Starting {threadCount * iterations} process instance(s) across {threadCount} thread(s).");
            SpeedTestResult result = RunSpeedTest(threadCount, iterations, batchName, processName, settings);

            Console.WriteLine($"Completed. Successful starts: {result.SuccessCount}. Failed starts: {result.FailureCount}.");
            return result.FailureCount == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
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
        List<Task> tasks = new();

        for (int i = 0; i < threadCount; i++)
        {
            int threadNumber = i + 1;
            tasks.Add(Task.Run(() =>
            {
                using Connection connection = CreateConnection(settings);

                for (int j = 0; j < iterations; j++)
                {
                    try
                    {
                        StartK2Process(batchName, connection, processName);
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failureCount);
                        lock (consoleLock)
                        {
                            Console.Error.WriteLine($"Thread {threadNumber}, iteration {j + 1} failed: {ex.Message}");
                        }
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        return new SpeedTestResult(successCount, failureCount);
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
            throw new FileNotFoundException(
                $"Configuration file '{configFilePath}' was not found. Copy appSettings.example.json to appSettings.json and update it for your K2 environment.");
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
        public SpeedTestResult(int successCount, int failureCount)
        {
            SuccessCount = successCount;
            FailureCount = failureCount;
        }

        public int SuccessCount { get; }

        public int FailureCount { get; }
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
