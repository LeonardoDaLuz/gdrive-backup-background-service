using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Text.Json;
using static Google.Apis.Drive.v3.DriveService;

/**
 * Este service envia dados ao gdrive usando a API do Google Drive. Obs: não utitiliza dados do usuário. O artigo usado para fazer isto foi: https://medium.com/geekculture/upload-files-to-google-drive-with-c-c32d5c8a7abc *
 */
public class GoogleDriveService
{
    DateTime expireTime;
    TokenResponse tokenResponse;
    string client_secret;
    string refresh_token;
    string client_id;
    string applicationName;
    string username;
    public bool justSimulate { get; set; }


    public GoogleDriveService(GoogleDriveConfig config)
    {
        var fileBackupConfig = config;

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

    private DriveService service_;

    private async Task<DriveService> GetService()
    {

        if (service_ is not null)
        {
            return service_;
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
        service_ = service;

        return service;
    }

    public async Task<Google.Apis.Drive.v3.Data.File> CreateFolder(string parent, string folderName)
    {
        var service = await GetService();
        var driveFolder = new Google.Apis.Drive.v3.Data.File();
        driveFolder.Name = folderName;
        driveFolder.MimeType = "application/vnd.google-apps.folder";
        driveFolder.Parents = new string[] { parent };
        var command = service.Files.Create(driveFolder);
        var file = command.Execute();

        if (allFileScanned is not null)
        {
            file.Parents = new List<string>() { parent };
            allFileScanned.Add(file.Id, new(file));
        }
        return file;
    }

    public string ___rootFolderId { get; private set; }

    string getRootFolderId()
    {
        if (___rootFolderId is not null)
        {
            return ___rootFolderId;
        }
        var service = GetService().GetAwaiter().GetResult();
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
        var targetFolder = GetOrCreateFolderIfNotExistsByPath(target);
        Google.Apis.Drive.v3.Data.File uploadedFile = null;
        using (FileStream fileStream = new FileStream(origin, FileMode.Open, FileAccess.Read))
        {
            uploadedFile = await UploadFile(
                fileStream,
                Path.GetFileName(origin),
                "application/zip",
                targetFolder.Id,
                "Backup automático"
            );
            Console.WriteLine("File sended");
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
                    Console.WriteLine("Finished upload (Fake): " + gDriveFolderPath + "/" + fileName);
                    return new();
                }
                try
                {
                    var result = await UploadFile(fs, fileName, fileMime, folderId, fileDescription);
                    Console.WriteLine("Finished upload: " + gDriveFolderPath + "/" + fileName);
                    return result;
                }
                catch (Exception exception)
                {
                    Console.WriteLine(
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
    /// Puxa do gdrive toda lista de pastas e arquivos. Isto é feito por loops porque o gdrive nao deixa fazer isto de uma vez. Isto é um workaround.
    /// </summary>
    public void CacheAllDir()
    {
        Console.WriteLine("Getting all file info cache from GDrive");
        var service = GetService().GetAwaiter().GetResult();
        string pageToken = null;
        int page = 0;
        do
        {
            var request = service.Files.List();
            request.Q = $"trashed=false";
            request.Fields = "nextPageToken, files(id, name,parents,mimeType)";
            request.PageToken = pageToken;
            request.PageSize = 500;

            // Executa a solicitação e itera pelos resultados
            var response = request.Execute();

            pageToken = response.NextPageToken;
            AddToCache(response.Files);
            page++;
            Console.WriteLine("Loop " + page);
        } while (!string.IsNullOrEmpty(pageToken));
        Console.WriteLine("Allfile Infos Obtained");
    }

    public List<GFileInfo> Dir(string folderId)
    {
        Console.WriteLine("Dir");
        var service = GetService().GetAwaiter().GetResult();
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

        return response.Files.Select(x => (new GFileInfo(x))).ToList();
    }

    public class GFileInfo
    {
        public string Id;
        public string MimeType;
        public string Name;
        public IList<string> Parents;

        public GFileInfo() { }

        public GFileInfo(Google.Apis.Drive.v3.Data.File file)
        {
            this.Id = file.Id;
            this.MimeType = file.MimeType;
            this.Name = file.Name;
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

    Dictionary<string, GFileInfo> allFileScanned { get; set; } = new();

    public void AddToCache(GFileInfo file)
    {
        if (file.Parents is null)
        {
            throw new Exception("parent cannot be null");
        }
        if (allFileScanned.Count > 100)
        {
            //allFileScanned.RemoveAt(0);
        }
        if (file is null)
        {
            throw new Exception();
        }
        if (!allFileScanned.ContainsKey(file.Id))
            allFileScanned.Add(file.Id, file);
    }

    public void AddToCache(IList<Google.Apis.Drive.v3.Data.File> files)
    {
        var converted = files.Select(x => new GFileInfo()
        {
            Id = x.Id,
            Name = x.Name,
            Parents = x.Parents,
            MimeType = x.MimeType
        });
        if (files.Contains(null))
        {
            throw new Exception();
        }
        foreach (var file in files)
        {
            if (!allFileScanned.ContainsKey(file.Id))
                allFileScanned.Add(file.Id, new(file));
        }
    }

    public GFileInfo GetOrCreateFolderIfNotExistsByPath(string path, bool updateFileTree = false)
    {
        if (path.StartsWith("/"))
            path = path.Substring(1);
        
        var directories = path.Split("/");
        Console.WriteLine("path ->" + path);
        var parentId = getRootFolderId();
        ;
        GFileInfo currentCursor = null;
        bool created = false;
        bool foundByGdrive = false;
        // Console.WriteLine(string.Join(" ", allFileThree.Select(x => x.Name)));
        for (int i = 0; i < directories.Length; i++)
        {
            var folderName = directories[i];
            var currentPair = allFileScanned.FirstOrDefault(x =>
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
                var fileList = Dir(parentId);
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
                var folderGFile = CreateFolder(parentId, folderName).GetAwaiter().GetResult();
                Console.WriteLine("Directory created in gdrive: " + string.Join("/", directories.Take(i + 1)));
                folderGFile.Parents = new List<string>() { parentId };
                currentCursor = new(folderGFile);
                AddToCache(currentCursor);
                created = true;
            }

            parentId = currentCursor.Id;
        }

        if (created)
        {
            Console.WriteLine("Folder created in gdrive: " + path);
        }
        else if (foundByGdrive)
        {
            Console.WriteLine("Folder found by gdrive call: " + path);
        }
        else
        {
            Console.WriteLine("Folder found by cache: " + path);
        }

        return currentCursor;
    }

    public GFileInfo GetFileByPath(string pathInGDrive, bool updateFileTree = false)
    {
        var directories = pathInGDrive.Split("/");
        var parentId = getRootFolderId();
        GFileInfo currentCursor = null;
        bool foundByGdrive = false;
        // Console.WriteLine(string.Join(" ", allFileThree.Select(x => x.Name)));
        for (int i = 0; i < directories.Length; i++)
        {
            var folderName = directories[i];
            var currentPair = allFileScanned.FirstOrDefault(x =>
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
                var fileList = Dir(parentId);
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
            Console.WriteLine("File not found: " + pathInGDrive);
        }
        else
        {
            if (foundByGdrive)
            {
                Console.WriteLine("File found by gdrive call: " + pathInGDrive);
            }
            else
            {
                Console.WriteLine("File found by cache: " + pathInGDrive);
            }
        }
        return currentCursor;
    }
}


public class GoogleDriveConfig
{
    public bool Enabled { get; set; }
    public bool JustSimulate { get; set; }
    public string ClientSecret { get; set; }
    public string RefreshToken { get; set; }
    public string ClientId { get; set; }
    public string ApplicationName { get; set; }
    public string GDriveEmail { get; set; }
}