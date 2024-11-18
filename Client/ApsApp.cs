namespace ClientV3
{
    using Autodesk.Authentication;
    using Autodesk.Authentication.Model;
    using Autodesk.Forge.Core;
    using Autodesk.Forge.DesignAutomation;
    using Autodesk.Forge.DesignAutomation.Model;
    using Autodesk.Oss;
    using Autodesk.Oss.Model;
    using Autodesk.SDKManager;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Main namespace for the APS Application.
    /// Provides services for authentication, storage management, file downloading, 
    /// and interaction with Autodesk Forge Design Automation APIs.
    /// </summary>

    #region Configuration and Models

    /// <summary>
    /// Configuration model for the APS Application.
    /// Stores necessary details like activity name, bucket key, and file paths.
    /// </summary>
    public class ApsAppConfiguration
    {
        public string ActivityName { get; set; } = "AutoCAD.PlotToPDF+prod";
        public string Owner { get; set; } = "dasplottingmad";
        public string BucketKey { get; set; }
        public string InputFilePath { get; set; }
        public string OutputFolderPath { get; set; }
    }

    /// <summary>
    /// Model to store the Forge authentication token.
    /// Includes properties for access token and expiration check.
    /// </summary>
    public record AuthToken(string AccessToken, DateTime ExpiresAt)
    {
        public bool IsExpired => ExpiresAt < DateTime.UtcNow;
    }

    #endregion

    #region Service Interfaces

    /// <summary>
    /// Interface for APS Authentication Service.
    /// Manages token retrieval for Autodesk services.
    /// </summary>
    public interface IAPSAuthenticationService
    {
        Task<AuthToken> GetToken(List<Scopes> scopes);
        Task<AuthToken> GetInternalToken();
    }

    /// <summary>
    /// Interface for APS Storage Service.
    /// Manages bucket creation, file uploads, and signed URL generation.
    /// </summary>
    public interface IAPSStorageService
    {
        Task EnsureBucketExists(string bucketKey);
        Task<ObjectDetails> UploadModel(string objectName, string bucketKey, Stream stream);
        Task<string> GetSignedS3DownloadUrl(string bucketKey, string objectKey);
    }

    /// <summary>
    /// Interface for File Download Service.
    /// Handles downloading files from a URL to a local path.
    /// </summary>
    public interface IFileDownloadService
    {
        Task<string> DownloadFile(string url, string localFilePath);
    }

    /// <summary>
    /// Interface for SDK Manager Factory.
    /// Provides instances of the SDKManager for managing Autodesk SDKs.
    /// </summary>
    public interface ISDKManagerFactory
    {
        SDKManager Create();
    }

    #endregion

    #region Service Implementations

    /// <summary>
    /// Factory for creating SDKManager instances.
    /// </summary>
    public class SDKManagerFactory : ISDKManagerFactory
    {
        public SDKManager Create()
        {
            return SdkManagerBuilder.Create().Build();
        }
    }

    /// <summary>
    /// Implementation of APS Authentication Service.
    /// Handles the retrieval of Forge tokens with various scopes.
    /// </summary>
    public class APSAuthenticationService : IAPSAuthenticationService
    {
        private readonly ForgeConfiguration _config;
        private AuthToken _tokenCache;

        public APSAuthenticationService(IOptions<ForgeConfiguration> config)
        {
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        }

        public async Task<AuthToken> GetToken(List<Scopes> scopes)
        {
            var authClient = new AuthenticationClient();
            var auth = await authClient.GetTwoLeggedTokenAsync(_config.ClientId, _config.ClientSecret, scopes);
            return new AuthToken(auth.AccessToken, DateTime.UtcNow.AddSeconds((double)auth.ExpiresIn));
        }

        public async Task<AuthToken> GetInternalToken()
        {
            if (_tokenCache == null || _tokenCache.IsExpired)
            {
                _tokenCache = await GetToken(new List<Scopes>
                {
                    Scopes.BucketCreate,
                    Scopes.BucketRead,
                    Scopes.DataRead,
                    Scopes.DataWrite,
                    Scopes.DataCreate,
                    Scopes.CodeAll
                });
            }
            return _tokenCache;
        }
    }

    /// <summary>
    /// Implementation of APS Storage Service.
    /// Handles bucket operations, file uploads, and signed URL generation.
    /// </summary>
    public class APSStorageService : IAPSStorageService
    {
        private readonly IAPSAuthenticationService _authService;
        private readonly OssClient _ossClient;

        public APSStorageService(IAPSAuthenticationService authService, ISDKManagerFactory sdkManagerFactory)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _ossClient = new OssClient(sdkManagerFactory.Create());
        }

        public async Task EnsureBucketExists(string bucketKey)
        {
            var auth = await _authService.GetInternalToken();

            try
            {
                await _ossClient.GetBucketDetailsAsync(auth.AccessToken, bucketKey);
            }
            catch (OssApiException ex) when (ex.HttpResponseMessage.StatusCode == HttpStatusCode.NotFound)
            {
                var payload = new CreateBucketsPayload
                {
                    BucketKey = bucketKey,
                    PolicyKey = PolicyKey.Persistent
                };
                await _ossClient.CreateBucketAsync(auth.AccessToken, Region.US, payload);
            }
        }

        public async Task<ObjectDetails> UploadModel(string objectName, string bucketKey, Stream stream)
        {
            var auth = await _authService.GetInternalToken();
            return await _ossClient.Upload(bucketKey, objectName, stream,
                accessToken: auth.AccessToken,
                new CancellationTokenSource().Token);
        }

        public async Task<string> GetSignedS3DownloadUrl(string bucketKey, string objectKey)
        {
            var auth = await _authService.GetInternalToken();
            var signedUrl = await _ossClient.SignedS3DownloadAsync(auth.AccessToken, bucketKey, objectKey);
            return signedUrl.Url;
        }
    }

    /// <summary>
    /// Service for downloading files from a given URL.
    /// </summary>
    public class FileDownloadService : IFileDownloadService
    {
        private readonly HttpClient _httpClient;

        public FileDownloadService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> DownloadFile(string url, string localFilePath)
        {
            if (File.Exists(localFilePath))
                File.Delete(localFilePath);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using var fs = new FileStream(localFilePath, FileMode.CreateNew);
            await response.Content.CopyToAsync(fs);

            return localFilePath;
        }
    }

    #endregion

    /// <summary>
    /// Main APS Application class.
    /// Handles the overall flow of Forge Design Automation tasks, including bucket initialization, 
    /// work item creation, status monitoring, and result downloading.
    /// </summary>

    public class ApsApp
    {
        private readonly DesignAutomationClient _designAutomationClient;
        private readonly IAPSAuthenticationService _authService;
        private readonly IAPSStorageService _storageService;
        private readonly IFileDownloadService _fileDownloadService;
        private readonly ApsAppConfiguration _config;

        public ApsApp(
            DesignAutomationClient designAutomationClient,
            IAPSAuthenticationService authService,
            IAPSStorageService storageService,
            IFileDownloadService fileDownloadService,
            ApsAppConfiguration config)
        {
            _designAutomationClient = designAutomationClient ?? throw new ArgumentNullException(nameof(designAutomationClient));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _fileDownloadService = fileDownloadService ?? throw new ArgumentNullException(nameof(fileDownloadService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public async Task RunAsync()
        {
            if (string.IsNullOrEmpty(_config.Owner))
            {
                throw new InvalidOperationException("Owner must not be empty.");
            }

            await InitializeBucketAsync();
            if (!await SetupOwnerAsync())
            {
                throw new InvalidOperationException("Failed to setup owner.");
            }

            await ProcessWorkItemAsync();
        }

        private async Task InitializeBucketAsync()
        {
            if (string.IsNullOrEmpty(_config.BucketKey))
            {
                var auth = await _authService.GetInternalToken();
                _config.BucketKey = $"{_config.Owner.ToLower()}_{Guid.NewGuid():N}";
                Console.WriteLine($"Creating Bucket {_config.BucketKey}....");
                await _storageService.EnsureBucketExists(_config.BucketKey);
            }
        }

        private async Task<bool> SetupOwnerAsync()
        {
            Console.WriteLine("Setting up owner...");
            var nickname = await _designAutomationClient.GetNicknameAsync("me");

            if (nickname == _config.Owner)
                return true;

            var response = await _designAutomationClient.ForgeAppsApi.CreateNicknameAsync(
                "me",
                new NicknameRecord { Nickname = _config.Owner },
                throwOnError: false);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                Console.WriteLine("Resources already exist for this clientId or nickname is in use.");
                return false;
            }

            response.EnsureSuccessStatusCode();
            return true;
        }

        private async Task ProcessWorkItemAsync()
        {
            Console.WriteLine("Processing work item...");
            var auth = await _authService.GetInternalToken();

            var workItem = await CreateWorkItem(auth);
            var ws = await MonitorWorkItemStatus(workItem);
            await DownloadResults(ws);
        }

        private async Task<WorkItemStatus> CreateWorkItem(AuthToken auth)
        {
            var inputFileOss = GenerateFileName("input", Path.GetFileName(_config.InputFilePath));
            var outputFileOss = GenerateFileName("output", "result.pdf");
            ResultPdf = outputFileOss;

            return await _designAutomationClient.CreateWorkItemAsync(new WorkItem
            {
                ActivityId = _config.ActivityName,
                Arguments = CreateWorkItemArguments(auth, inputFileOss, outputFileOss),
                LimitProcessingTimeSec = 900

            });
        }

        private Dictionary<string, IArgument> CreateWorkItemArguments(AuthToken auth, string inputFileOss, string outputFileOss)
        {
            return new Dictionary<string, IArgument>
            {
                { "HostDwg", CreateFileArgument(Verb.Get, inputFileOss, auth) },
                { "Result", CreateFileArgument(Verb.Put, outputFileOss, auth) }
            };
        }

        private XrefTreeArgument CreateFileArgument(Verb verb, string fileName, AuthToken auth)
        {
            return new XrefTreeArgument
            {
                Verb = verb,
                Url = GetObjectId(_config.BucketKey, fileName, _config.InputFilePath),
                Headers = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {auth.AccessToken}" }
                }
            };
        }

        private async Task <WorkItemStatus> MonitorWorkItemStatus(WorkItemStatus workItemStatus)
        {
            Console.Write("\tPolling status");
            while (!workItemStatus.Status.IsDone())
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                workItemStatus = await _designAutomationClient.GetWorkitemStatusAsync(workItemStatus.Id);
                Console.Write(".");
            }
            Console.WriteLine($"{workItemStatus.Status}.");
            return workItemStatus;
        }

        private async Task DownloadResults(WorkItemStatus workItemStatus)
        {
            var reportPath = await _fileDownloadService.DownloadFile(
                workItemStatus.ReportUrl,
                Path.Combine(_config.OutputFolderPath, "Das-report.txt"));
            Console.WriteLine($"Downloaded report: {reportPath}");
          
            var signedUrl = await _storageService.GetSignedS3DownloadUrl(_config.BucketKey, ResultPdf);

            var resultPath = await _fileDownloadService.DownloadFile(
                signedUrl,
                Path.Combine(_config.OutputFolderPath, ResultPdf));
            Console.WriteLine($"Downloaded result: {resultPath}");
        }

        private static string GenerateFileName(string prefix, string fileName)
            => $"{DateTime.Now:yyyyMMddhhmmss}_{prefix}_{fileName}";

        private static string ResultPdf = string.Empty;

        private string GetObjectId(string bucketKey, string objectKey, string filePath)
        {
            var objectDetails = _storageService.UploadModel(objectKey, bucketKey, File.OpenRead(filePath)).Result;
            return objectDetails.ObjectId;
        }
            
    }
}