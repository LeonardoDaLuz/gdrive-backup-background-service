
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using static EmailService;


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
        await CallCommandsBefore();
        gDriveService = new GoogleDriveService(settings.GoogleDrive);
        foreach (var file in settings.Files)
        {
            await BackupFile(file.Origin, file.TargetFolder);
        }
    }
    async Task CallCommandsBefore()
    {
        foreach (var command in settings.CommandsToCallBefore)
        {
            Console.WriteLine("Calling command: " + command);
            // Configuração do processo
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {command}",
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
                process.WaitForExit();

                // Exibe a saída e os erros
                Console.WriteLine("Command execution finished");
            }

        }
    }
    async Task BackupFile(string originPath, string targetPath)
    {
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
        if (uploadedFile is not null)
        {
            var mailService = new EmailService(settings.EmailSettings);
            Console.WriteLine($"Sending backup email");
            await mailService.SendEmailAsync(
                new EmailRequest(
                   toEmail: settings.SendEmailsTo,
                   subject: $"Backup {Path.GetFileName(originPath)} - " + DateTime.UtcNow.AddHours(settings.StartsAt.timezone).ToString("dd/MM/yyyy HH:mm"),
                   body: $@"<h1>Backup do banco de dados realizado com sucesso!</h1>
                             <p>Se está tendo problemas em visualizar este arquivo é porque provavelmente você não tem permissão de acesso à pasta do google drive.</p>
                             <a href='https://drive.google.com/file/d/{uploadedFile.Id}/view?usp=sharing'>Clique aqui para baixar o arquivo</a>
                             ",
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
}