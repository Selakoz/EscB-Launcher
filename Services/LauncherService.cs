using System.Diagnostics;
using System.Windows.Forms;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Version;
using EscapeBook.Models;

namespace EscapeBook.Services;

internal sealed class LauncherService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly ConfigService _configService = new(Http);
    private readonly ClientUpdater _clientUpdater = new(Http);
    private readonly BannerService _bannerService = new();
    private readonly TelemetryService _telemetryService = new();

    public Task<LauncherConfig> LoadConfigAsync() =>
        _configService.LoadAsync(new Progress<LaunchProgress>(), CancellationToken.None);

    /// <summary>
    /// Downloads/updates the selected build (client ZIP + Minecraft files) without
    /// starting the game. Used by the "ОБНОВИТЬ"/"УСТАНОВИТЬ" button state.
    /// </summary>
    public async Task UpdateBuildAsync(
        LauncherConfig config,
        BuildInfo build,
        UserSettings settings,
        IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        await PrepareBuildAsync(config, build, settings, progress, cancellationToken);
        progress.Report(new LaunchProgress(100, "Сборка обновлена"));
        AppLog.Write($"Build prepared without launch: build={build.Id}, version={build.Version}");
    }

    public async Task<LaunchResult?> LaunchAsync(
        LauncherConfig config,
        BuildInfo build,
        UserSettings settings,
        IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Nickname))
            throw new InvalidOperationException("Не указан никнейм.");

        var (launcher, version) = await PrepareBuildAsync(
            config,
            build,
            settings,
            progress,
            cancellationToken);

        int ramMb = Math.Max(1024, settings.RamMb);
        int minRamMb = Math.Min(1024, ramMb);

        progress.Report(new LaunchProgress(88, "Поиск Java..."));
        string javaPath = ResolveJavaPath(launcher, version, settings);

        progress.Report(new LaunchProgress(90, "Настройка дискретной видеокарты..."));
        if (!GpuPreferenceService.TryApply(javaPath, out string? gpuError) && gpuError != null)
            AppLog.Write("GPU preference warning: " + gpuError);

        string[] customJvmArgs;
        try
        {
            customJvmArgs = JvmArgumentUtil.ParseAndMerge(
                config.DefaultJvmArgs,
                settings.CustomJvmArgs);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось разобрать JVM-аргументы: " + ex.Message, ex);
        }

        string finalJvmArgs = JvmArgumentUtil.FormatForTelemetry(customJvmArgs, ramMb, minRamMb);

        progress.Report(new LaunchProgress(93, "Сбор характеристик ПК..."));
        HardwareInfo hardware = await Task.Run(HardwareInfoService.Collect, cancellationToken);

        if (!string.IsNullOrWhiteSpace(config.DiscordWebhookUrl))
        {
            progress.Report(new LaunchProgress(95, "Отправка телеметрии..."));
            try
            {
                using var telemetryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                telemetryCts.CancelAfter(TimeSpan.FromSeconds(10));
                await _telemetryService.SendAsync(
                    config,
                    settings.Nickname,
                    hardware,
                    ramMb,
                    javaPath,
                    finalJvmArgs,
                    telemetryCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error("Telemetry", ex);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new LaunchProgress(97, "Сборка команды запуска..."));

        var extraJvmArguments = MLaunchOption.DefaultExtraJvmArguments
            .Concat(new[] { new MArgument(customJvmArgs) })
            .ToArray();

        var launchOption = new MLaunchOption
        {
            Session = MSession.CreateOfflineSession(settings.Nickname),
            MaximumRamMb = ramMb,
            MinimumRamMb = minRamMb,
            JavaPath = javaPath,
            ExtraJvmArguments = extraJvmArguments,
            GameLauncherName = "EscapeBook",
            GameLauncherVersion = "2.0.0",
            VersionType = "EscapeBook"
        };

        Process process = launcher.BuildProcess(version, launchOption);
        process.StartInfo.WorkingDirectory = LauncherPaths.GetInstancePath(build.Id);
        process.StartInfo.UseShellExecute = false;
        process.EnableRaisingEvents = true;

        // Low-RAM guard: fast (GlobalMemoryStatusEx) and cancellable by the user.
        if (!ConfirmEnoughMemory(ramMb))
        {
            AppLog.Write($"Launch cancelled by user (low memory): allocated={ramMb}MB");
            progress.Report(new LaunchProgress(0, "Запуск отменён"));
            return null;
        }

        progress.Report(new LaunchProgress(99, "Запуск Minecraft..."));
        process.Start();
        progress.Report(new LaunchProgress(100, "Minecraft запущен"));

        AppLog.Write(
            $"Minecraft started: nick={settings.Nickname}, build={build.Id}, " +
            $"mc={build.McVersion}, ram={ramMb}MB, java={javaPath}, args={finalJvmArgs}");

        return new LaunchResult(process, javaPath, finalJvmArgs, LauncherPaths.GetGameLogFile(build.Id));
    }

    /// <summary>
    /// Shared preparation step: banner, client ZIP and Minecraft files.
    /// </summary>
    private async Task<(MinecraftLauncher Launcher, IVersion Version)> PrepareBuildAsync(
        LauncherConfig config,
        BuildInfo build,
        UserSettings settings,
        IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        LauncherPaths.EnsureRoot();
        LauncherPaths.EnsureInstance(build.Id);

        await _bannerService.TryUpdateAsync(config, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(new LaunchProgress(40, "Обновление сборки..."));
        await _clientUpdater.UpdateAsync(
            build,
            config.OptionalMods,
            settings.OptionalModStates,
            progress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(new LaunchProgress(50, $"Подготовка Minecraft {build.McVersion}..."));

        var minecraftPath = new MinecraftPath(LauncherPaths.GetInstancePath(build.Id));
        var launcher = new MinecraftLauncher(minecraftPath);

        var fileProgress = new Progress<InstallerProgressChangedEventArgs>(args =>
        {
            double ratio = args.TotalTasks > 0
                ? Math.Clamp((double)args.ProgressedTasks / args.TotalTasks, 0d, 1d)
                : 0d;
            string name = string.IsNullOrWhiteSpace(args.Name) ? "файлы игры" : args.Name!;
            progress.Report(new LaunchProgress(
                50 + (ratio * 36),
                $"Установка: {args.ProgressedTasks}/{args.TotalTasks} · {name}"));
        });

        progress.Report(new LaunchProgress(50, $"Получение манифеста {build.McVersion}..."));
        IVersion version = await launcher.GetVersionAsync(build.McVersion, cancellationToken);

        progress.Report(new LaunchProgress(50, $"Проверка файлов Minecraft {build.McVersion}..."));
        await launcher.InstallAsync(version, fileProgress, null, cancellationToken);

        return (launcher, version);
    }

    /// <summary>
    /// Shows an OK/Cancel prompt when the allocated heap exceeds the free physical RAM.
    /// Returns true when the launch should proceed.
    /// </summary>
    private static bool ConfirmEnoughMemory(int allocatedRamMb)
    {
        try
        {
            int availableMb = SystemMemoryInfo.GetAvailablePhysicalMemoryMb();
            if (availableMb <= 0 || availableMb >= allocatedRamMb)
                return true;

            AppLog.Write(
                $"Low memory warning: allocated={allocatedRamMb}MB, available={availableMb}MB");

            DialogResult result = MessageBox.Show(
                $"Выделено памяти: {allocatedRamMb} МБ, свободно: {availableMb} МБ.{Environment.NewLine}{Environment.NewLine}" +
                "Возможны вылеты и проблемы с производительностью.",
                "Недостаточно свободной памяти",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            return result == DialogResult.OK;
        }
        catch (Exception ex)
        {
            AppLog.Error("Memory check", ex);
            return true;
        }
    }

    private static string ResolveJavaPath(
        MinecraftLauncher launcher,
        IVersion version,
        UserSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.JavaPath))
        {
            string customPath = Environment.ExpandEnvironmentVariables(settings.JavaPath.Trim().Trim('"'));
            if (!File.Exists(customPath))
                throw new FileNotFoundException($"Указанный javaw.exe не найден: {customPath}");
            return customPath;
        }

        try
        {
            string? bundledJava = launcher.GetJavaPath(version);
            if (!string.IsNullOrWhiteSpace(bundledJava) && File.Exists(bundledJava))
                return bundledJava;
        }
        catch (Exception ex) { AppLog.Error("Resolve version Java", ex); }

        try
        {
            string? defaultJava = launcher.GetDefaultJavaPath();
            if (!string.IsNullOrWhiteSpace(defaultJava) && File.Exists(defaultJava))
                return defaultJava;
        }
        catch (Exception ex) { AppLog.Error("Resolve default Java", ex); }

        string? systemJava = JavaLocator.FindSystemJavaw();
        if (systemJava != null)
            return systemJava;

        throw new InvalidOperationException(
            "Не удалось найти javaw.exe. Установите Java или укажите путь " +
            "к javaw.exe в разделе «Настройки».");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EscapeBook/2.0");
        return client;
    }
}