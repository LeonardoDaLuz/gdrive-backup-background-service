using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using static Google.Apis.Drive.v3.DriveService;

/**
 * Este service envia dados ao gdrive usando a API do Google Drive. Obs: não utitiliza dados do usuário. O artigo usado para fazer isto foi: https://medium.com/geekculture/upload-files-to-google-drive-with-c-c32d5c8a7abc *
 */
public class GoogleDriveService
{
    DateTime expireTime;
    TokenResponse? tokenResponse;
    string client_secret;
    string refresh_token;
    string client_id;
    string applicationName;
    string username;
    public bool justSimulate { get; set; }
    public static bool EnableLog;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new();


    public GoogleDriveService(GoogleDriveConfig config)
    {
        var fileBackupConfig = config;
        if (string.IsNullOrWhiteSpace(fileBackupConfig.ClientSecret))
            throw new FormatException("Client secret not configured");

        if (string.IsNullOrWhiteSpace(fileBackupConfig.RefreshToken))
            throw new FormatException("Client secret not configured");

        if (string.IsNullOrWhiteSpace(fileBackupConfig.ClientId))
            throw new FormatException("Client secret not configured");
        if (string.IsNullOrWhiteSpace(fileBackupConfig.ApplicationName))
            throw new FormatException("Client secret not configured");
        if (string.IsNullOrWhiteSpace(fileBackupConfig.GDriveEmail))
            throw new FormatException("Client secret not configured");

        client_secret = fileBackupConfig.ClientSecret;
        refresh_token = fileBackupConfig.RefreshToken;
        client_id = fileBackupConfig.ClientId;
        applicationName = fileBackupConfig.ApplicationName;
        username = fileBackupConfig.GDriveEmail;
        justSimulate = fileBackupConfig.JustSimulate;
    }

    public async Task<RefreshTokenResponse> GetRefreshedToken()
    {
        using (HttpClient client = new HttpClient())
        {
            var data = new Dictionary<string, string>
            {
                { "client_secret", client_secret },
                { "grant_type", "refresh_token" },
                { "refresh_token", refresh_token },
                { "client_id", client_id }
            };
            HttpContent content = new FormUrlEncodedContent(data);

            HttpResponseMessage response = await client.PostAsync("https://oauth2.googleapis.com/token", content);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<RefreshTokenResponse>(responseBody);
            if (results == null) throw new Exception("Error on refreshtoken");
            expireTime = DateTime.UtcNow.AddSeconds(results.expires_in);
            return results;
        }
    }

    public record RefreshTokenResponse(
        string access_token,
        int expires_in,
        string scope,
        string token_type,
        int? error_code,
        string error_description
    );

    private DriveService? _service;

    private async Task<DriveService> GetService()
    {

        if (_service is not null)
        {
            return _service;
        }
        if (tokenResponse is null || DateTime.UtcNow > expireTime.AddSeconds(-60 * 10))
        {
            var result = await GetRefreshedToken();
            tokenResponse = new TokenResponse { AccessToken = result.access_token, RefreshToken = refresh_token, };
        }

        var apiCodeFlow = new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = client_id, ClientSecret = client_secret },
                Scopes = new[] { Scope.Drive },
                DataStore = new FileDataStore(applicationName)
            }
        );

        var credential = new UserCredential(apiCodeFlow, username, tokenResponse);

        var service = new DriveService(
            new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = applicationName }
        );
        _service = service;

        return service;
    }

    public async Task<Google.Apis.Drive.v3.Data.File> CreateFolder(string parent, string folderName)
    {

        var service = await GetService();
        var driveFolder = new Google.Apis.Drive.v3.Data.File();
        driveFolder.Name = folderName;
        driveFolder.MimeType = "application/vnd.google-apps.folder";
        driveFolder.Parents = new string[] { parent };

        var semaphore = _folderLocks.GetOrAdd($"{parent}{"//\\"}{folderName}", _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            GFileInfo? folderInfo;
            if (fileFolderCache.TryGetValue($"{parent}//\\{folderName}", out folderInfo))
            {
                return folderInfo.file;
            }

            // Primeiro verifica se a pasta já existe
            var command = service.Files.Create(driveFolder);
            var file = await command.ExecuteAsync();

            if (fileFolderCache is not null)
            {
                file.Parents = new List<string>() { parent };
                fileFolderCache.TryAdd($"{parent}//\\{file.Name}", new(file));
            }
            return file;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public string? ___rootFolderId { get; private set; }

    async Task<string> getRootFolderId()
    {
        if (___rootFolderId is not null)
        {
            return ___rootFolderId;
        }
        var service = await GetService();
        FilesResource.ListRequest request = service.Files.List();
        request.Q = "mimeType='application/vnd.google-apps.folder' and 'root' in parents and trashed=false";
        request.Fields = "files(id,name,parents)";

        // Executar a consulta
        var result2 = request.Execute();
        if (result2.Files.Count > 0)
        {
            ___rootFolderId = result2.Files[0].Parents.First();
        }
        else
        {
            ___rootFolderId = "root";
        }
        return ___rootFolderId;
    }

    public async Task<Google.Apis.Drive.v3.Data.File> UploadFile(string origin, string target)
    {
        var targetFolder = await GetOrCreateFolderIfNotExistsByPath(target);
        Google.Apis.Drive.v3.Data.File? uploadedFile = null;
        using (FileStream fileStream = new FileStream(origin, FileMode.Open, FileAccess.Read))
        {
            uploadedFile = await UploadFile(
                fileStream,
                Path.GetFileName(origin),
                "application/zip",
                targetFolder.fileInfo.Id,
                "Backup automático"
            );
            WriteLine("File sended");
        }
        return uploadedFile;
    }
    public async Task<Google.Apis.Drive.v3.Data.File> UploadFile(
        Stream file,
        string fileName,
        string fileMime,
        string folder,
        string fileDescription
    )
    {
        DriveService service = await GetService();

        var driveFile = new Google.Apis.Drive.v3.Data.File();
        driveFile.Name = fileName;
        driveFile.Description = fileDescription;
        driveFile.MimeType = fileMime;
        driveFile.Parents = new string[] { folder };

        var request = service.Files.Create(driveFile, file, fileMime);
        request.Fields = "id";

        var response = request.Upload();
        if (response.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw response.Exception;
        return request.ResponseBody;
    }

    List<Task<Google.Apis.Drive.v3.Data.File>> uploadRequests = new();
    public static int maxConcurrentUploads = 6;

    public async Task BatchUploadFile(
        string filePathInDisc,
        string fileName,
        string fileMime,
        string folderId,
        string gDriveFolderPath,
        string fileDescription
    )
    {
        if (uploadRequests.Count > maxConcurrentUploads)
        {
            await Task.WhenAll(uploadRequests);
        }

        var completedRequests = uploadRequests.Where(x => x.IsCompleted).ToList();
        foreach (var completeRequest in completedRequests)
        {
            //allFileScanned.Add(completeRequest.Result);
        }
        uploadRequests = uploadRequests.Except(completedRequests).ToList();

        var task = Task.Run(async () =>
        {
            using (FileStream fs = new FileStream(filePathInDisc, FileMode.Open, FileAccess.Read))
            {
                if (justSimulate)
                {
                    WriteLine("Finished upload (Fake): " + gDriveFolderPath + "/" + fileName);
                    return new();
                }
                try
                {
                    var result = await UploadFile(fs, fileName, fileMime, folderId, fileDescription);
                    WriteLine("Finished upload: " + gDriveFolderPath + "/" + fileName);
                    return result;
                }
                catch (Exception exception)
                {
                    WriteLine(
                        "Failed to upload: " + gDriveFolderPath + "/" + fileName + " : " + exception.ToString()
                    );
                    return new();
                }
            }
        });

        uploadRequests.Add(task);
    }

    public async Task WaitBatcheUploadFinish()
    {
        await Task.WhenAll(uploadRequests);
        var completedRequests = uploadRequests.Where(x => x.IsCompleted).ToList();
        foreach (var completeRequest in completedRequests)
        {
            //allFileScanned.Add(completeRequest.Result);
        }
        uploadRequests = uploadRequests.Except(completedRequests).ToList();
    }

    public async void DeleteFile(string fileId)
    {
        var service = await GetService();
        var command = service.Files.Delete(fileId);
        var result = command.Execute();
    }

    /// <summary>
    /// Puxa do gdrive toda lista de pastas e arquivos. 
    /// Isto é feito por loops porque o gdrive nao deixa fazer isto de uma vez. Isto é um workaround.
    /// Mas não funciona bem porque geralmente tem muito arquivo pra fazer esse cache pois tem
    /// limitação de 500 por vez, e o google começa a recusar conexão se vc fizer request demais.
    /// </summary>
    public async Task CacheAllDir()
    {
        WriteLine("Getting all file info cache from GDrive");
        var service = await GetService();
        string? pageToken = null;
        int page = 0;
        do
        {
            var request = service.Files.List();
            request.Q = "mimeType='application/vnd.google-apps.folder' and trashed=false";
            request.Fields = "nextPageToken, files(id, name,parents,mimeType)";
            request.PageToken = pageToken;
            request.PageSize = 500;

            // Executa a solicitação e itera pelos resultados
            try
            {
                var response = await request.ExecuteAsync();
                pageToken = response.NextPageToken;
                AddToCache(response.Files);
                page++;
                WriteLine("Loop " + page);
            }
            catch (Google.GoogleApiException ex)
            {
                Console.WriteLine("Error on cache: " + ex.ToString());
            }

        } while (!string.IsNullOrEmpty(pageToken));
        WriteLine("Allfile Infos Obtained");
    }

    public async Task<List<GFileInfo>> Dir(string folderId)
    {
        var service = await GetService();
        var request = service.Files.List();
        request.PageSize = 500;
        if (folderId is null)
        {
            request.Q = $"trashed=false";
        }
        else
        {
            request.Q = $"'{folderId}' in parents and trashed=false";
        }

        // Define os campos que você deseja retornar
        request.Fields = "nextPageToken, files(id, name,parents,mimeType)";

        // Executa a solicitação e itera pelos resultados
        var response = request.Execute();

        AddToCache(response.Files);

        var result = response.Files.Select(x => (new GFileInfo(x))).ToList();
        return result;
    }

    public class GFileInfo
    {
        public string Id;
        public string MimeType;
        public string Name;
        public IList<string> Parents;
        public Google.Apis.Drive.v3.Data.File file;


        public GFileInfo(Google.Apis.Drive.v3.Data.File file)
        {
            this.Id = file.Id;
            this.MimeType = file.MimeType;
            this.Name = file.Name;
            this.file = file;
            if (file.Parents is null)
            {
                this.Parents = new List<string>();
            }
            else
            {
                this.Parents = file.Parents.ToList();
            }
        }
    }

    ConcurrentDictionary<string, GFileInfo> fileFolderCache { get; set; } = new();

    public void AddToCache(GFileInfo file)
    {

        if (file.Parents == null)
        {
            fileFolderCache.TryAdd($"root//\\{file.Name}", file);
        }
        else
        {
            foreach (var parent in file.Parents)
            {
                fileFolderCache.TryAdd($"{parent}//\\{file.Name}", file);
            }
        }
    }

    public void AddToCache(IList<Google.Apis.Drive.v3.Data.File> files)
    {

        foreach (var file in files)
        {
            if (file.Parents == null)
            {
                fileFolderCache.TryAdd($"root//\\{file.Name}", new(file));
            }
            else
            {
                foreach (var parentId in file.Parents)
                {
                    fileFolderCache.TryAdd($"{parentId}//\\{file.Name}", new(file));
                }
            }

        }
    }

    public async Task<(GFileInfo fileInfo, bool created)> GetOrCreateFolderIfNotExistsByPath(string path, bool updateFileTree = false)
    {
        if (path.StartsWith("/"))
            path = path.Substring(1);

        var directories = path.Split("/");

        var parentId = await getRootFolderId();

        GFileInfo? currentCursor = null;
        bool created = false;

        for (int i = 0; i < directories.Length; i++)
        {
            var folderName = directories[i];
            GFileInfo? folderInfo;

            if (fileFolderCache.TryGetValue($"{parentId}//\\{folderName}", out folderInfo))
            {
                currentCursor = folderInfo;
            }
            else
            {
                currentCursor = null;
            }

            if (currentCursor is null)
            {
                var fileList = await Dir(parentId);
                fileList.ForEach(AddToCache);
                currentCursor = fileList.FirstOrDefault(x =>
                    x.Name == folderName
                    && ((x.Parents is not null && x.Parents.Contains(parentId)) || (x.Parents is null && parentId == "root"))
                );
                if (currentCursor is not null)
                {
                    if (i == directories.Length - 1)
                        WriteLine("Folder found in gdrive: " + string.Join("/", directories.Take(i + 1)));
                }
                //remove duplicated dir 
                var duplicatedDirectories = fileList.GroupBy(x => x.Name).Where(x => x.Count() > 1);
                foreach (var equalSiblings in duplicatedDirectories)
                {
                    var _equalSiblings = equalSiblings.ToList();
                    for (int j = 1; j < _equalSiblings.Count(); j++)
                    {
                        var service = await GetService();
                        var request = service.Files.Delete(_equalSiblings[j].Id);
                        request.SupportsAllDrives = true; // Se usar Shared Drives
                        await request.ExecuteAsync();
                    }
                }
            }

            if (currentCursor is null)
            {
                WriteLine("Creating folder in gdrive: " + string.Join("/", directories.Take(i + 1)));
                var folderGFile = await CreateFolder(parentId, folderName);
                folderGFile.Parents = new List<string>() { parentId };
                currentCursor = new(folderGFile);
                AddToCache(currentCursor);
                created = true;
            }

            parentId = currentCursor.Id;
        }
        return (currentCursor!, created);
    }
    public void WriteLine(string msg)
    {
        if (EnableLog)
            Console.WriteLine(msg);
    }
    public async Task<GFileInfo?> GetFileByPath(string pathInGDrive, bool updateFileTree = false)
    {
        var directories = pathInGDrive.Split("/");
        var parentId = await getRootFolderId();
        GFileInfo? currentCursor = null;
        bool foundByGdrive = false;
        // WriteLine(string.Join(" ", allFileThree.Select(x => x.Name)));
        for (int i = 0; i < directories.Length; i++)
        {
            var folderName = directories[i];
            var currentPair = fileFolderCache.FirstOrDefault(x =>
                x.Value.Name == folderName
                && (
                    (x.Value.Parents is not null && x.Value.Parents.Contains(parentId))
                    || (x.Value.Parents is null && parentId == "root")
                )
            );

            if (!currentPair.Equals(default(KeyValuePair<string, GFileInfo>)))
            {
                currentCursor = currentPair.Value;
            }
            else
            {
                currentCursor = null;
            }
            if (currentCursor is null)
            {
                var fileList = await Dir(parentId);
                currentCursor = fileList.FirstOrDefault(x =>
                    x.Name == folderName
                    && ((x.Parents is not null && x.Parents.Contains(parentId)) || (x.Parents is null && parentId == "root"))
                );
                if (currentCursor is not null)
                {
                    foundByGdrive = true;
                    AddToCache(currentCursor);
                }
            }
            if (currentCursor is null)
            {
                return null;
            }
            parentId = currentCursor.Id;
        }
        if (currentCursor is null)
        {
            WriteLine("File not found: " + pathInGDrive);
        }
        else
        {
            if (foundByGdrive)
            {
                WriteLine("File found by gdrive call: " + pathInGDrive);
            }
            else
            {
                WriteLine("File found by cache: " + pathInGDrive);
            }
        }
        return currentCursor;
    }
}


public class GoogleDriveConfig
{
    public bool Enabled { get; set; }
    public bool JustSimulate { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }
    public string? ClientId { get; set; }
    public string? ApplicationName { get; set; }
    public string? GDriveEmail { get; set; }
}