
// using Microsoft.Extensions.Options;
// using System.IO.Compression;
// namespace Klinny.Backup;


// public class BackupService
// {
//     IServiceProvider sp;
//     GoogleDriveService gDriveService;

//     public BackupService(
//         IServiceProvider sp,
//         GoogleDriveService gDriveService,
//         IOptions<AppSettings> appSettings
//     )
//     {
//         this.sp = sp;
//         this.gDriveService = gDriveService;
//     }

//     public static void ZiparArquivo(string caminhoArquivo, string caminhoArquivoZipado)
//     {
//         using (var arquivoZip = ZipFile.Open(caminhoArquivoZipado, ZipArchiveMode.Create))
//         {
//             arquivoZip.CreateEntryFromFile(caminhoArquivo, Path.GetFileName(caminhoArquivo));
//         }
//     }

//     public async Task BackupAllComaniesFilesToGDrive(string targetFolderInGDrive)
//     {
//         Console.WriteLine("Backup files Start");
//         gDriveService.CacheAllDir();
//         gDriveService.GetOrCreateFolderIfNotExistsByPath(targetFolderInGDrive, true);
//         gDriveService.GetOrCreateFolderIfNotExistsByPath(targetFolderInGDrive + "/Files", false);
//         gDriveService.GetOrCreateFolderIfNotExistsByPath(targetFolderInGDrive + "/Files/PublicFiles", false); //
//         await SyncDirectories(targetFolderInGDrive + "/Files/PublicFiles", "./wwwroot");
//         gDriveService.GetOrCreateFolderIfNotExistsByPath(targetFolderInGDrive + "/Files/PrivateFiles", false); //
//         await SyncDirectories(targetFolderInGDrive + "/Files/PrivateFiles", "./PrivateFiles");
//         await gDriveService.WaitBatcheUploadFinish();
//         Console.WriteLine("Backup files finished");
//     }

//     async Task SyncDirectories(string rootDirInGdrive, string rootDirInDisc)
//     {
//         var fileList = Directory
//             .GetFiles(rootDirInDisc)
//             .Select(x => x.Replace(rootDirInDisc, "").Replace("\\", "").Replace("/", ""));
//         GC.Collect();
//         foreach (var fileName in fileList)
//         {
//             var file = gDriveService.GetFileByPath(rootDirInGdrive + "/" + fileName, false);
//             if (file is null)
//             {
//                 var parentFolder = gDriveService.GetOrCreateFolderIfNotExistsByPath(rootDirInGdrive);
//                 var filePathInDisc = rootDirInDisc + "/" + fileName;

//                 await gDriveService.BatchUploadFile(
//                     filePathInDisc,
//                     fileName,
//                     "",
//                     parentFolder.fileInfo.Id,
//                     rootDirInGdrive,
//                     "Backup automático"
//                 );
//                 GC.Collect();
//             }
//             else { }
//         }

//         var dirList = Directory
//             .GetDirectories(rootDirInDisc)
//             .Select(x => x.Replace(rootDirInDisc, "").Replace("\\", "").Replace("/", ""));

//         foreach (var folderName in dirList)
//         {
//             var gDriveFolder = gDriveService.GetOrCreateFolderIfNotExistsByPath(rootDirInGdrive + "/" + folderName, false);
//             await SyncDirectories(rootDirInGdrive + "/" + folderName, rootDirInDisc + "/" + folderName);
//         }
//     }


// }
