
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using static AppSettings;
using static EmailService;
using static GoogleDriveService;


public class GdriveBackgroundService : BackgroundService
{
    AppSettings settings;
    GoogleDriveService? _gDriveService;
    GoogleDriveService gDriveService
    {
        get
        {
            _gDriveService = new GoogleDriveService(settings.GoogleDrive!);
            return _gDriveService;
        }
    }
    SemaphoreSlim semaphore = new SemaphoreSlim(1);
    bool initialized = false;
    public GdriveBackgroundService(IOptions<AppSettings> settings)
    {
        this.settings = settings.Value;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken); //Wait for background service default console log;
        if (!initialized)
        {
            initialized = true;
            if (settings.ForceToRunOnStartup)
            {
                await BackupNow();
            }
        }
        Console.WriteLine("Background service is starting.");
        if (settings.BackupIntervalDays < 0.1)
            throw new Exception("Interval not allowed");

        stoppingToken.Register(() =>
            Console.WriteLine("Background service is stopping."));

        var startsAt = new DateTime(
            DateTime.UtcNow.Year,
            DateTime.UtcNow.Month,
            DateTime.UtcNow.Day,
            settings.StartsAt!.Hours,
            settings.StartsAt.Minutes,
            0, DateTimeKind.Utc)
            .AddHours(-settings.StartsAt.timezone);

        if (startsAt < DateTime.UtcNow) //Se o horario for menor do que agora, adicionar mais um dia.
            startsAt = startsAt.AddDays(1);
        var delayTimespan = startsAt - DateTime.UtcNow;
        Console.WriteLine($"Waiting until {settings.StartsAt.Hours.ToString("D2")}:{settings.StartsAt.Minutes.ToString("D2")} ({delayTimespan.TotalMinutes.ToString("#")} minutes remaining)");

        await Task.Delay(delayTimespan, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var workStartTime = DateTime.UtcNow; //stores the work start time, to use in delay calc after work.
            Console.WriteLine("Background service is doing background work.");
            await BackupNow();
            Console.WriteLine($"Waiting for {settings.BackupIntervalDays} days");
            // wait for interval
            await Task.Delay(workStartTime.AddDays(settings.BackupIntervalDays) - DateTime.UtcNow, stoppingToken);
        }

        Console.WriteLine("Background service has stopped.");
    }


    async Task BackupNow()
    {
        await semaphore.WaitAsync();
        try
        {


            foreach (var task in settings.FilesOrDirectory!)
            {
                await RunTask(task);
            }
        }
        finally
        {
            semaphore.Release();
            _gDriveService = null;
        }
    }
    async Task RunTask(FileDirectoryOriginTarget task)
    {
        Console.WriteLine($"Starting task for origin '{task.Origin}' to target '{task.TargetFolder}' on gdrive");
        try
        {
            if (task.CommandsToCallBefore != null && task.CommandsToCallBefore.Count > 0)
                await CallCommandsBefore(task.CommandsToCallBefore);
        }
        catch
        {
            Console.Error.WriteLine("Fail to run commands before");
        }

        if (File.Exists(task.Origin))
        {
            try
            {
                await BackupFile(task);
            }
            catch
            {
                Console.Error.WriteLine("fail to backup files: " + task.Origin);
            }
        }
        else if (Directory.Exists(task.Origin))
        {
            try
            {
                await BackupDirectory(task);
            }
            catch
            {
                Console.Error.WriteLine("fail to backup directory");
            }
        }
    }
    async Task CallCommandsBefore(List<string> CommandsToCallBefore)
    {
        Console.WriteLine(" Call commands before backup...");
        foreach (var command in CommandsToCallBefore)
        {
            Console.WriteLine($"  Calling: {command}");
            // Configuração do processo
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/bash",
                Arguments = Environment.OSVersion.Platform == PlatformID.Win32NT ? $"/C {command}" : $"-c \"{command}\"",
                RedirectStandardOutput = true, // Redireciona a saída padrão
                RedirectStandardError = true,  // Redireciona os erros padrão
                UseShellExecute = false,       // Não usa o shell do sistema para executar
                CreateNoWindow = true          // Não cria uma janela do console
            };

            // Inicia o processo
            using (Process process = new Process())
            {
                process.StartInfo = processStartInfo;
                process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) =>
                {
                    var defaultColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(e.Data);
                    Console.ForegroundColor = defaultColor;
                };
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine(e.Data);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        var defaultColor = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Data);
                        Console.ForegroundColor = defaultColor;
                    }
                };
                process.Start();

                // Inicia a leitura assíncrona das saídas
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Espera o processo terminar
                await process.WaitForExitAsync();

                // Exibe a saída e os erros
                Console.WriteLine("   Finished command.");
            }

        }
    }
    async Task BackupFile(FileDirectoryOriginTarget task)
    {
        if (task.Origin == null)
            throw new Exception("Null origin");
        if (task.TargetFolder == null)
            throw new Exception("Null TargetFolder");

        string originPath = task.Origin;
        string targetPath = task.TargetFolder;
        if (!File.Exists(originPath))
        {
            Console.Write("File not found: " + originPath);
            return;
        }

        int randomNumber = new Random().Next(100, 999);
        string copyFilePath = Path.Combine(Path.GetDirectoryName(originPath)!, Path.GetFileNameWithoutExtension(originPath)!) + $"-backup-{DateTime.UtcNow.AddHours(settings.StartsAt!.timezone).ToString("dd-MM-yyTHH-mm")}-{randomNumber}{Path.GetExtension(originPath)}";

        Console.WriteLine($"Copying file");
        File.Copy(originPath, copyFilePath);
        CompressFile(copyFilePath, copyFilePath + ".zip");
        Console.WriteLine($"Deleting copy file");
        File.Delete(copyFilePath);
        var uploadedFile = await gDriveService.UploadFile(copyFilePath + ".zip", targetPath);
        if (uploadedFile != null)
            await SendEmail(task, uploadedFile, false);
    }

    async Task SendEmail(FileDirectoryOriginTarget task, Google.Apis.Drive.v3.Data.File uploadedFile, bool folder)
    {
        if (uploadedFile is not null)
        {
            Console.WriteLine($"Sending backup email");
            var mailService = new EmailService(settings.EmailSettings!);
            await mailService.SendEmailAsync(
                new EmailRequest(
                   toEmail: settings.SendEmailsTo!,
                   subject: task.EmailTitle ?? "Backup",
                   body: @$"<h1>Backup realizado com sucesso!</h1>
                             <p>Se está tendo problemas em visualizar este arquivo é porque provavelmente você não tem permissão de acesso à pasta do google drive.</p>
                             <a href='https://drive.google.com/{(folder ? "folder" : "file")}/d/{uploadedFile.Id}/view?usp=sharing'>Clique aqui para baixar o arquivo</a>",
                   isHtml: true
                )
            );
        }
    }
    public static void CompressFile(string caminhoArquivo, string caminhoArquivoZipado)
    {
        Console.WriteLine("Compressing file");
        using (var arquivoZip = ZipFile.Open(caminhoArquivoZipado, ZipArchiveMode.Create))
        {
            arquivoZip.CreateEntryFromFile(caminhoArquivo, Path.GetFileName(caminhoArquivo));
        }
    }
    async Task BackupDirectory(FileDirectoryOriginTarget task)
    {
        GoogleDriveService.EnableLog = true;
        //await gDriveService.CacheAllDir(); //muito lento
        var parentFolder = await SyncDirectories(task.TargetFolder!, task.Origin!);
        await SendEmail(task, parentFolder.file, true);
    }
    async Task<GFileInfo> SyncDirectories(string rootDirInGdrive, string rootDirInDisc, int identation = 1)
    {
        Console.WriteLine($"{new string(' ', identation)}Synchronizing files and subdirectories for directory {rootDirInDisc} to gdrive at {rootDirInGdrive}...");
        if (rootDirInGdrive.EndsWith("/")) rootDirInGdrive = rootDirInGdrive.Substring(0, rootDirInGdrive.Length - 1);
        if (rootDirInGdrive.StartsWith("/")) rootDirInGdrive = rootDirInGdrive.Substring(1, rootDirInGdrive.Length - 1);

        var fileList = Directory
            .GetFiles(rootDirInDisc)
            .Select(x => Path.GetFileName(x));
        var dirList = Directory
               .GetDirectories(rootDirInDisc)
               .Select(x => x.Replace(rootDirInDisc, "").Replace("\\", "").Replace("/", ""));

        Console.WriteLine($"{new string(' ', identation)} Found files: " + fileList.Count() + " Found folders: " + dirList.Count());

        GC.Collect();
        var parentFolderResult = await gDriveService.GetOrCreateFolderIfNotExistsByPath(rootDirInGdrive);
        if (parentFolderResult.created)
            Console.WriteLine($"{new string(' ', identation)} Created directory on Gdrive: {rootDirInGdrive}");
        else
            Console.WriteLine($"{new string(' ', identation)} Found directory on Gdrive: {rootDirInGdrive}");

        foreach (var fileName in fileList)
        {
            var filePathInDrive = $"{rootDirInGdrive}/{fileName}";
            var file = await gDriveService.GetFileByPath(filePathInDrive, false);
            if (file is null)
            {
                Console.WriteLine($"{new string(' ', identation)}  File does not exists in gdrive: {filePathInDrive}");
                var filePathInDisc = Path.Combine(rootDirInDisc, fileName);
                Console.WriteLine($"{new string(' ', identation)}  Uploading file: {filePathInDrive}");
                await gDriveService.BatchUploadFile(
                    filePathInDisc,
                    fileName,
                    "",
                    parentFolderResult.fileInfo.Id,
                    rootDirInGdrive,
                    "Backup automático"
                );
                GC.Collect();
            }
            else
            {
                Console.WriteLine($"{new string(' ', identation)}  File exists in gdrive: {filePathInDrive}");
            }
        }

        foreach (var folderName in dirList)
        {
            await SyncDirectories(rootDirInGdrive + "/" + folderName, rootDirInDisc + "/" + folderName, identation + 1);
        }

        return parentFolderResult.fileInfo;
    }
}