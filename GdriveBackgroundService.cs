
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
    GoogleDriveService gDriveService;
    public GdriveBackgroundService(IOptions<AppSettings> settings)
    {
        this.settings = settings.Value;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken); //Wait for background service default console log;
        Console.WriteLine("Background service is starting.");
        if (settings.BackupIntervalDays < 0.1)
            throw new Exception("Interval not allowed");

        stoppingToken.Register(() =>
            Console.WriteLine("Background service is stopping."));


        if (!settings.ForceToRunOnStartup)
        {
            var startsAt = new DateTime(
                DateTime.UtcNow.Year,
                DateTime.UtcNow.Month,
                DateTime.UtcNow.Day,
                settings.StartsAt.Hours,
                settings.StartsAt.Minutes,
                0, DateTimeKind.Utc)
                .AddHours(-settings.StartsAt.timezone);

            if (startsAt < DateTime.UtcNow) //Se o horario for menor do que agora, adicionar mais um dia.
                startsAt = startsAt.AddDays(1);
            var delayTimespan = startsAt - DateTime.UtcNow;
            Console.WriteLine($"Waiting until {settings.StartsAt.Hours.ToString("D2")}:{settings.StartsAt.Minutes.ToString("D2")} ({delayTimespan.TotalMinutes.ToString("#")} minutes remaining)");

            await Task.Delay(delayTimespan, stoppingToken);
        }
        else
        {
            settings.ForceToRunOnStartup = false;
        }

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
        gDriveService = new GoogleDriveService(settings.GoogleDrive);

        foreach (var task in settings.FilesOrDirectory)
        {
            await RunTask(task);
        }
    }
    async Task RunTask(FileDirectoryOriginTarget task)
    {
        try
        {
            if (task.CommandsToCallBefore != null && task.CommandsToCallBefore.Count > 0)
                await CallCommandsBefore(task.CommandsToCallBefore);
        }
        catch
        {
            Console.Error.WriteLine("Fail to run command before");
        }
        if (File.Exists(task.Origin))
        {
            try
            {
                Console.WriteLine("Backup files: "+task.Origin);
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
                Console.WriteLine("Backup directory: " + task.Origin);
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
        foreach (var command in CommandsToCallBefore)
        {
            Console.WriteLine("pwd:" + Directory.GetCurrentDirectory());
            Console.WriteLine("Calling command: " + command);
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

                process.Start();

                // Inicia a leitura assíncrona das saídas
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Espera o processo terminar
                await process.WaitForExitAsync();

                // Exibe a saída e os erros
                Console.WriteLine("Command execution finished");
            }

        }
    }
    async Task BackupFile(FileDirectoryOriginTarget task)
    {
        string originPath = task.Origin;
        string targetPath = task.TargetFolder;
        if (!File.Exists(originPath))
        {
            Console.Write("File not found: " + originPath);
            return;
        }

        int randomNumber = new Random().Next(100, 999);
        string copyFilePath = Path.Combine(Path.GetDirectoryName(originPath), Path.GetFileNameWithoutExtension(originPath)) + $"-backup-{DateTime.UtcNow.AddHours(settings.StartsAt.timezone).ToString("dd-MM-yyTHH-mm")}-{randomNumber}{Path.GetExtension(originPath)}";

        Console.WriteLine($"Copying file");
        File.Copy(originPath, copyFilePath);
        CompressFile(copyFilePath, copyFilePath + ".zip");
        Console.WriteLine($"Deleting copy file");
        File.Delete(copyFilePath);
        var uploadedFile = await gDriveService.UploadFile(copyFilePath + ".zip", targetPath);
        if (uploadedFile != null)
            await SendEmail(task, uploadedFile);
    }

    async Task SendEmail(FileDirectoryOriginTarget task, Google.Apis.Drive.v3.Data.File uploadedFile)
    {
        if (uploadedFile is not null)
        {
            Console.WriteLine($"Sending backup email");
            var mailService = new EmailService(settings.EmailSettings);
            await mailService.SendEmailAsync(
                new EmailRequest(
                   toEmail: settings.SendEmailsTo,
                   subject: task.EmailTitle ?? "Backup",
                   body: (task.EmailBody ?? @"<h1>Backup realizado com sucesso!</h1>
                             <p>Se está tendo problemas em visualizar este arquivo é porque provavelmente você não tem permissão de acesso à pasta do google drive.</p>
                             <a href='https://drive.google.com/file/d/{uploadedFile.Id}/view?usp=sharing'>Clique aqui para baixar o arquivo</a>").Replace("{fileId}", uploadedFile.Id),
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
        var parentFolder = await SyncDirectories(task.TargetFolder, task.Origin);
        await SendEmail(task, parentFolder.file);
    }
    async Task<GFileInfo> SyncDirectories(string rootDirInGdrive, string rootDirInDisc)
    {
        if (rootDirInGdrive.EndsWith("/")) rootDirInGdrive = rootDirInGdrive.Substring(0, rootDirInGdrive.Length - 1);
        if (rootDirInGdrive.StartsWith("/")) rootDirInGdrive = rootDirInGdrive.Substring(1, rootDirInGdrive.Length - 1);
        Console.WriteLine($"Sincronizando diretório: {rootDirInDisc}");
        var fileList = Directory
            .GetFiles(rootDirInDisc)
            .Select(x => Path.GetFileName(x));
        GC.Collect();
        var parentFolder = gDriveService.GetOrCreateFolderIfNotExistsByPath(rootDirInGdrive);
        foreach (var fileName in fileList)
        {
            var filePathInDrive = $"{rootDirInGdrive}/{fileName}";
            Console.WriteLine($"Checando se arquivo existe no drive: {filePathInDrive}");
            var file = gDriveService.GetFileByPath(filePathInDrive, false);
            if (file is null)
            {
                Console.WriteLine($"Arquivo não existe no gdrive: {filePathInDrive}");
                var filePathInDisc = Path.Combine(rootDirInDisc, fileName);



                await gDriveService.BatchUploadFile(
                    filePathInDisc,
                    fileName,
                    "",
                    parentFolder.Id,
                    rootDirInGdrive,
                    "Backup automático"
                );
                GC.Collect();
            }
            else
            {
                Console.WriteLine($"Arquivo já existe no gdrive: {filePathInDrive}");
            }
        }

        var dirList = Directory
            .GetDirectories(rootDirInDisc)
            .Select(x => x.Replace(rootDirInDisc, "").Replace("\\", "").Replace("/", ""));

        foreach (var folderName in dirList)
        {
            var gDriveFolder = gDriveService.GetOrCreateFolderIfNotExistsByPath(rootDirInGdrive + "/" + folderName, false);
            await SyncDirectories(rootDirInGdrive + "/" + folderName, rootDirInDisc + "/" + folderName);
        }
        return parentFolder;
    }
}